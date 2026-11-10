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

    /// <summary>Writes heights.f32 (north row first, little-endian), heights.hdr and heights.json through the shared
    /// <see cref="HeightsWriter"/>, computing one band of rows at a time in parallel.</summary>
    public void Write(string heightsDir)
    {
        var info = new HeightsInfo(OriginX, OriginY, Spacing, Cols, Rows, new UtmFrame(_p.UtmZone, !_p.NorthernHemisphere, _p.E0, _p.N0));
        using var writer = new HeightsWriter(heightsDir, info);
        var band = new float[BandRows][];
        for (int k = 0; k < BandRows; k++) band[k] = new float[Cols];
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
            for (int k = 0; k < n; k++) writer.WriteRow(band[k]);
        }
    }
}
