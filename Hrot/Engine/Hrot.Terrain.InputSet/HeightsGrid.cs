using System.Text.Json;

namespace Hrot.Terrain.InputSet;

/// <summary>Reads the input set's height grid: the description (<c>heights.json</c>) and the floats (<c>heights.f32</c>, north row first).</summary>
public static class HeightsGrid
{
    public static HeightsInfo ReadInfo(string setFolder)
    {
        string path = Path.Combine(setFolder, InputSetLayout.HeightsJson);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var r = doc.RootElement;
        var info = new HeightsInfo(r.GetProperty("originX").GetDouble(), r.GetProperty("originY").GetDouble(), r.GetProperty("cellSize").GetDouble(),
                                   r.GetProperty("cols").GetInt32(), r.GetProperty("rows").GetInt32(), FrameOf(r, path, setFolder));
        return r.TryGetProperty("sourceResolution", out var sr) ? info with { SourceResolution = sr.GetDouble() } : info;
    }

    // §10 does not list a frame in heights.json: when it carries no frame OBJECT (absent, or a producer's text note) the manifest's frame is the grid's
    private static UtmFrame FrameOf(JsonElement r, string path, string setFolder)
    {
        if (r.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.Object) return InputSetReader.ReadFrame(r, path);
        string manifest = Path.Combine(setFolder, InputSetLayout.Manifest);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
        return InputSetReader.ReadFrame(doc.RootElement, manifest);
    }

    /// <summary>Calls <paramref name="row"/> for every row, NORTH first (file order), with a reused buffer — for any size of grid.</summary>
    public static void ReadRows(string setFolder, Action<int, float[]> row)
    {
        var info = ReadInfo(setFolder);
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("big-endian host");
        string path = Path.Combine(setFolder, InputSetLayout.HeightsF32);
        long expected = (long)info.Cols * info.Rows * 4;
        long actual = new FileInfo(path).Length;
        if (actual != expected) throw new InvalidDataException($"{path}: {actual} bytes, the grid needs {expected} ({info.Cols} × {info.Rows} floats)");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var buf = new float[info.Cols];
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(buf.AsSpan());
        for (int r = 0; r < info.Rows; r++)
        {
            fs.ReadExactly(bytes);
            row(r, buf);
        }
    }

    /// <summary>The whole grid in one array, row-major SOUTH row first (<c>z[row * cols + col]</c>, row 0 at <c>OriginY</c>) — the order
    /// <c>TerrainHeightGrid</c> uses. ⚠ a 0.5 m grid of 5.5 km is ≈ 500 MB; stream with <see cref="ReadRows"/> where that matters.</summary>
    public static (HeightsInfo Info, float[] Z) ReadAll(string setFolder)
    {
        var info = ReadInfo(setFolder);
        var z = new float[(long)info.Cols * info.Rows];
        ReadRows(setFolder, (r, buf) => Array.Copy(buf, 0, z, (long)(info.Rows - 1 - r) * info.Cols, info.Cols));
        return (info, z);
    }
}
