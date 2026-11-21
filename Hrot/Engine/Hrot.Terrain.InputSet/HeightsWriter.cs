using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hrot.Terrain.InputSet;

/// <summary>
/// Writes <c>heights.f32</c> (float32 little-endian, NORTH row first, ESRI BIL), <c>heights.hdr</c> and <c>heights.json</c>, one row at a time,
/// so a producer can stream a grid of any size. Usage: create, <see cref="WriteRow"/> every row north to south, <see cref="Dispose"/>.
/// </summary>
public sealed class HeightsWriter : IDisposable
{
    private readonly string _dir;
    private readonly HeightsInfo _info;
    private readonly FileStream _fs;
    private readonly byte[] _bytes;
    private int _rowsWritten;
    private long _nodata;
    private float _min = float.PositiveInfinity, _max = float.NegativeInfinity;
    private bool _closed;

    public HeightsWriter(string heightsDir, HeightsInfo info)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("big-endian host");
        _dir = heightsDir; _info = info;
        Directory.CreateDirectory(heightsDir);
        _fs = new FileStream(Path.Combine(heightsDir, "heights.f32"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        _bytes = new byte[info.Cols * 4];
    }

    /// <summary>The next row, north to south; <see cref="HeightsInfo.NoData"/> marks no ground.</summary>
    public void WriteRow(ReadOnlySpan<float> row)
    {
        if (row.Length != _info.Cols) throw new ArgumentException($"a row has {_info.Cols} values, got {row.Length}");
        if (_rowsWritten >= _info.Rows) throw new InvalidOperationException("more rows than the grid has");
        for (int i = 0; i < row.Length; i++)
        {
            float v = row[i];
            if (v == HeightsInfo.NoData) _nodata++;
            else { if (v < _min) _min = v; if (v > _max) _max = v; }
        }
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(row).CopyTo(_bytes);
        _fs.Write(_bytes, 0, _bytes.Length);
        _rowsWritten++;
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _fs.Dispose();
        if (_rowsWritten != _info.Rows) throw new InvalidOperationException($"{_rowsWritten} of {_info.Rows} rows written");
        WriteHeader(Path.Combine(_dir, "heights.hdr"));
        WriteJson(Path.Combine(_dir, "heights.json"));
    }

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private void WriteHeader(string path)
    {
        long rowBytes = (long)_info.Cols * 4;
        var sb = new StringBuilder();
        sb.Append("BYTEORDER I\n");
        sb.Append("LAYOUT BIL\n");
        sb.Append("NROWS ").Append(N(_info.Rows)).Append("\n");
        sb.Append("NCOLS ").Append(N(_info.Cols)).Append("\n");
        sb.Append("NBANDS 1\n");
        sb.Append("NBITS 32\n");
        sb.Append("PIXELTYPE FLOAT\n");
        sb.Append("BANDROWBYTES ").Append(N(rowBytes)).Append("\n");
        sb.Append("TOTALROWBYTES ").Append(N(rowBytes)).Append("\n");
        sb.Append("BANDGAPBYTES 0\n");
        sb.Append("NODATA -9999\n");
        sb.Append("ULXMAP ").Append(N(_info.OriginX)).Append("\n");
        sb.Append("ULYMAP ").Append(N(_info.OriginY + (_info.Rows - 1) * _info.CellSize)).Append("\n");
        sb.Append("XDIM ").Append(N(_info.CellSize)).Append("\n");
        sb.Append("YDIM ").Append(N(_info.CellSize)).Append("\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private void WriteJson(string path)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("originX", _info.OriginX);
            w.WriteNumber("originY", _info.OriginY);
            w.WriteString("originIs", "south-west cell centre");
            w.WriteNumber("cellSize", _info.CellSize);
            w.WriteNumber("cols", _info.Cols);
            w.WriteNumber("rows", _info.Rows);
            w.WriteString("rowOrder", "north first");
            w.WriteNumber("nodata", -9999.0);
            w.WriteString("units", "m");
            Manifest.WriteFrame(w, _info.Frame);
            if (float.IsFinite(_min)) w.WriteNumber("minZ", _min); else w.WriteNull("minZ");
            if (float.IsFinite(_max)) w.WriteNumber("maxZ", _max); else w.WriteNull("maxZ");
            w.WriteNumber("nodataCells", _nodata);
            if (_info.SourceResolution is double sr) w.WriteNumber("sourceResolution", sr);
            w.WriteEndObject();
        }
        // LF line endings on every platform, so the bytes do not depend on the host
        File.WriteAllText(path, Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
    }
}
