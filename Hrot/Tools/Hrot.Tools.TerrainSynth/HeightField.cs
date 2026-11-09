using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hrot.Tools.TerrainSynth;

/// <summary>
/// Procedural height field. Posts lie on a regular grid from the south-west corner of the bounds; the value at a post
/// is a pure function of its coordinates, so other generators can query Sample without the grid in memory.
/// </summary>
public sealed class HeightField
{
    public const float NoDataValue = -9999f;
    private const int BandRows = 64;

    private readonly SynthProfile _p;
    private readonly ulong _seed;

    public int Cols { get; }
    public int Rows { get; }
    public double Spacing { get; }
    public double OriginX { get; }
    public double OriginY { get; }

    public HeightField(SynthProfile p, Rng rng)
    {
        _p = p;
        _seed = rng.Fork("heights").NextU64();
        Spacing = p.HeightSpacing;
        OriginX = p.MinX;
        OriginY = p.MinY;
        Cols = (int)Math.Round(p.Width / Spacing) + 1;
        Rows = (int)Math.Round(p.Height / Spacing) + 1;
    }

    public bool IsNoData(double x, double y)
    {
        foreach (var r in _p.NodataRects)
            if (x >= r.MinX && x <= r.MaxX && y >= r.MinY && y <= r.MaxY) return true;
        return false;
    }

    /// <summary>Ground height at a point (bilinear between posts); NaN inside a nodata rectangle.</summary>
    public float Sample(double x, double y)
    {
        if (IsNoData(x, y)) return float.NaN;
        double gx = Math.Clamp((x - OriginX) / Spacing, 0, Cols - 1);
        double gy = Math.Clamp((y - OriginY) / Spacing, 0, Rows - 1);
        int i = Math.Min((int)gx, Math.Max(Cols - 2, 0));
        int j = Math.Min((int)gy, Math.Max(Rows - 2, 0));
        double fx = gx - i, fy = gy - j;
        int i1 = Math.Min(i + 1, Cols - 1), j1 = Math.Min(j + 1, Rows - 1);
        double a = Post(i, j), b = Post(i1, j), c = Post(i, j1), d = Post(i1, j1);
        return (float)(a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy);
    }

    // Height at grid post (i, j), j counted from the south; defined everywhere, nodata or not.
    private double Post(int i, int j) => Raw(OriginX + i * Spacing, OriginY + j * Spacing);

    private double Raw(double x, double y)
    {
        double v = Noise.Fbm(_seed, x, y, 1200.0, 6);
        double r1 = Noise.Ridge(_seed ^ 0xA5A5A5A5A5A5A5A5UL, x + 0.6 * y, y - 0.6 * x, 2400.0);
        double r2 = Noise.Ridge(_seed ^ 0x5A5A5A5A5A5A5A5AUL, x - 0.4 * y, y + 0.4 * x, 700.0);
        double h = 0.75 * v + 0.25 * (0.6 * r1 + 0.4 * r2);
        double t = Math.Clamp((h - 0.32) / 0.42, 0.0, 1.0);
        t = Math.Pow(t, 1.9);
        return Math.Clamp(_p.ZMin + (_p.ZMax - _p.ZMin) * t, _p.ZMin, _p.ZMax);
    }

    /// <summary>Writes heights.f32 (north row first, little-endian), heights.hdr and heights.json, one band of rows at a time.</summary>
    public void Write(string heightsDir)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("big-endian host");
        Directory.CreateDirectory(heightsDir);
        long nodataCells = 0;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

        using (var fs = new FileStream(Path.Combine(heightsDir, "heights.f32"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            var band = new float[BandRows][];
            for (int k = 0; k < BandRows; k++) band[k] = new float[Cols];
            var bytes = new byte[Cols * 4];

            for (int fileRow0 = 0; fileRow0 < Rows; fileRow0 += BandRows)
            {
                int n = Math.Min(BandRows, Rows - fileRow0);
                int r0 = fileRow0;
                Parallel.For(0, n, k =>
                {
                    int j = Rows - 1 - (r0 + k); // file row 0 is the northernmost row
                    double y = OriginY + j * Spacing;
                    var row = band[k];
                    for (int i = 0; i < Cols; i++)
                    {
                        double x = OriginX + i * Spacing;
                        row[i] = IsNoData(x, y) ? NoDataValue : (float)Raw(x, y);
                    }
                });
                for (int k = 0; k < n; k++)
                {
                    var row = band[k];
                    for (int i = 0; i < Cols; i++)
                    {
                        float v = row[i];
                        if (v == NoDataValue) nodataCells++;
                        else
                        {
                            if (v < minZ) minZ = v;
                            if (v > maxZ) maxZ = v;
                        }
                    }
                    Buffer.BlockCopy(row, 0, bytes, 0, bytes.Length);
                    fs.Write(bytes, 0, bytes.Length);
                }
            }
        }

        WriteHeader(Path.Combine(heightsDir, "heights.hdr"));
        WriteJson(Path.Combine(heightsDir, "heights.json"), nodataCells, minZ, maxZ);
    }

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private void WriteHeader(string path)
    {
        long rowBytes = (long)Cols * 4;
        var sb = new StringBuilder();
        sb.Append("BYTEORDER I\n");
        sb.Append("LAYOUT BIL\n");
        sb.Append("NROWS ").Append(N(Rows)).Append("\n");
        sb.Append("NCOLS ").Append(N(Cols)).Append("\n");
        sb.Append("NBANDS 1\n");
        sb.Append("NBITS 32\n");
        sb.Append("PIXELTYPE FLOAT\n");
        sb.Append("BANDROWBYTES ").Append(N(rowBytes)).Append("\n");
        sb.Append("TOTALROWBYTES ").Append(N(rowBytes)).Append("\n");
        sb.Append("BANDGAPBYTES 0\n");
        sb.Append("NODATA -9999\n");
        sb.Append("ULXMAP ").Append(N(OriginX)).Append("\n");
        sb.Append("ULYMAP ").Append(N(OriginY + (Rows - 1) * Spacing)).Append("\n");
        sb.Append("XDIM ").Append(N(Spacing)).Append("\n");
        sb.Append("YDIM ").Append(N(Spacing)).Append("\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private void WriteJson(string path, long nodataCells, float minZ, float maxZ)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
        w.WriteStartObject();
        w.WriteNumber("originX", OriginX);
        w.WriteNumber("originY", OriginY);
        w.WriteString("originIs", "south-west cell centre");
        w.WriteNumber("cellSize", Spacing);
        w.WriteNumber("cols", Cols);
        w.WriteNumber("rows", Rows);
        w.WriteString("rowOrder", "north first");
        w.WriteNumber("nodata", -9999.0);
        w.WriteString("units", "m");
        w.WriteStartObject("frame");
        w.WriteStartObject("utm");
        w.WriteNumber("zone", _p.UtmZone);
        w.WriteString("hemisphere", _p.NorthernHemisphere ? "N" : "S");
        w.WriteNumber("e0", _p.E0);
        w.WriteNumber("n0", _p.N0);
        w.WriteEndObject();
        w.WriteEndObject();
        if (float.IsFinite(minZ)) w.WriteNumber("minZ", minZ); else w.WriteNull("minZ");
        if (float.IsFinite(maxZ)) w.WriteNumber("maxZ", maxZ); else w.WriteNull("maxZ");
        w.WriteNumber("nodataCells", nodataCells);
        w.WriteEndObject();
        }
        // LF line endings on every platform, so the bytes do not depend on the host
        File.WriteAllText(path, Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
    }
}
