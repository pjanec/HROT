using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Hrot.Terrain.Osm.Dem;

/// <summary>
/// ⭐ A minimal reader of a single-band float32 cloud-optimised GeoTIFF — exactly what Copernicus GLO-30 publishes (measured, §14.1:
/// tiled 1 024², deflate, predictor 3, little-endian). Reads the first IFD (full resolution) and only the internal tiles asked for, by range.
/// ⛔ Not a general TIFF reader: strips, other sample types, BigTIFF and other compressions are refused with a message.
/// </summary>
public sealed class CogReader
{
    private const int HeadBytes = 64 * 1024;
    private readonly IRangeSource _src;
    private readonly string _name;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int TileWidth { get; private set; }
    public int TileHeight { get; private set; }
    /// <summary>The geographic position (lon, lat in degrees) of pixel (0, 0)'s CENTRE.</summary>
    public double OriginLon { get; private set; }
    public double OriginLat { get; private set; }
    /// <summary>Degrees per pixel (both positive; rows go SOUTH).</summary>
    public double StepLon { get; private set; }
    public double StepLat { get; private set; }
    public float? NoData { get; private set; }

    private int _compression, _predictor;
    private long[] _tileOffsets = Array.Empty<long>();
    private long[] _tileBytes = Array.Empty<long>();
    private readonly Dictionary<int, float[]> _tiles = new();

    private CogReader(IRangeSource src, string name) { _src = src; _name = name; }

    /// <summary>Null when the file does not exist (open sea).</summary>
    public static async Task<CogReader?> OpenAsync(IRangeSource src, string name, CancellationToken ct)
    {
        var head = await src.ReadAsync(name, 0, HeadBytes, ct);
        if (head == null) return null;
        var r = new CogReader(src, name);
        await r.ParseAsync(head, ct);
        return r;
    }

    private async Task ParseAsync(byte[] head, CancellationToken ct)
    {
        if (head.Length < 8 || head[0] != (byte)'I' || head[1] != (byte)'I') throw Bad("only little-endian TIFF is supported");
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2));
        if (magic != 42) throw Bad(magic == 43 ? "BigTIFF is not supported" : $"not a TIFF (magic {magic})");
        long ifd = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
        byte[] buf = head;
        async Task<byte[]> At(long off, int len)
        {
            if (off + len <= buf.Length) return buf.AsSpan((int)off, len).ToArray();
            return await _src.ReadAsync(_name, off, len, ct) ?? throw Bad("truncated");
        }
        var countBytes = await At(ifd, 2);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(countBytes);
        var entries = await At(ifd + 2, n * 12);
        int bits = 0, format = 1, spp = 1;
        double[]? scale = null, tie = null;
        ushort[]? geoKeys = null;
        var raw = new List<(ushort Tag, ushort Type, uint Count, int Size, byte[] Inline, long Offset)>(n);
        for (int i = 0; i < n; i++)
        {
            int at = i * 12;
            ushort tag0 = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(at));
            ushort type0 = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(at + 2));
            uint count0 = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(at + 4));
            int size0 = type0 switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 or 16 => 8, _ => 1 } * (int)count0;
            raw.Add((tag0, type0, count0, size0, entries.AsSpan(at + 8, 4).ToArray(), BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(at + 8))));
        }
        foreach (var (tag, type, count, size, inline, offset) in raw)
        {
            byte[] val = size <= 4 ? inline : await At(offset, size);
            long U(int k) => type == 3 ? BinaryPrimitives.ReadUInt16LittleEndian(val.AsSpan(k * 2)) : BinaryPrimitives.ReadUInt32LittleEndian(val.AsSpan(k * 4));
            switch (tag)
            {
                case 256: Width = (int)U(0); break;
                case 257: Height = (int)U(0); break;
                case 258: bits = (int)U(0); break;
                case 259: _compression = (int)U(0); break;
                case 277: spp = (int)U(0); break;
                case 317: _predictor = (int)U(0); break;
                case 322: TileWidth = (int)U(0); break;
                case 323: TileHeight = (int)U(0); break;
                case 324: _tileOffsets = Enumerable.Range(0, (int)count).Select(k => U(k)).ToArray(); break;
                case 325: _tileBytes = Enumerable.Range(0, (int)count).Select(k => U(k)).ToArray(); break;
                case 339: format = (int)U(0); break;
                case 33550: scale = Doubles(val, (int)count); break;
                case 33922: tie = Doubles(val, (int)count); break;
                case 34735: geoKeys = Enumerable.Range(0, (int)count).Select(k => BinaryPrimitives.ReadUInt16LittleEndian(val.AsSpan(k * 2))).ToArray(); break;
                case 42113: if (float.TryParse(Encoding.ASCII.GetString(val).TrimEnd('\0', ' '), System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out var nd)) NoData = nd; break;
            }
        }
        if (bits != 32 || format != 3 || spp != 1) throw Bad($"only single-band float32 is supported (bits {bits}, format {format}, samples {spp})");
        if (TileWidth <= 0 || TileHeight <= 0 || _tileOffsets.Length == 0) throw Bad("only TILED TIFF is supported");
        if (_compression is not (1 or 8 or 32946)) throw Bad($"compression {_compression} is not supported (none or deflate)");
        if (_predictor is not (0 or 1 or 3)) throw Bad($"predictor {_predictor} is not supported (none or floating point)");
        if (scale == null || tie == null) throw Bad("no georeference (ModelPixelScale / ModelTiepoint)");
        StepLon = scale[0]; StepLat = scale[1];
        // GTRasterTypeGeoKey (1025): 1 = PixelIsArea (the tie point is the pixel's CORNER), 2 = PixelIsPoint (its CENTRE)
        bool isPoint = false;
        if (geoKeys != null)
            for (int k = 4; k + 3 < geoKeys.Length; k += 4)
                if (geoKeys[k] == 1025) isPoint = geoKeys[k + 3] == 2;
        double half = isPoint ? 0 : 0.5;
        OriginLon = tie[3] + (half - tie[0]) * StepLon;
        OriginLat = tie[4] - (half - tie[1]) * StepLat;
    }

    private static double[] Doubles(byte[] v, int count)
        => Enumerable.Range(0, count).Select(k => BinaryPrimitives.ReadDoubleLittleEndian(v.AsSpan(k * 8))).ToArray();

    private InvalidDataException Bad(string why) => new($"{_name}: {why}");

    private int TilesAcross => (Width + TileWidth - 1) / TileWidth;

    /// <summary>Fetches and decodes the internal tiles covering pixel columns [c0, c1] and rows [r0, r1] (clamped).</summary>
    public async Task LoadWindowAsync(int c0, int r0, int c1, int r1, CancellationToken ct)
    {
        c0 = Math.Clamp(c0, 0, Width - 1); c1 = Math.Clamp(c1, 0, Width - 1);
        r0 = Math.Clamp(r0, 0, Height - 1); r1 = Math.Clamp(r1, 0, Height - 1);
        for (int ty = r0 / TileHeight; ty <= r1 / TileHeight; ty++)
            for (int tx = c0 / TileWidth; tx <= c1 / TileWidth; tx++)
            {
                int index = ty * TilesAcross + tx;
                if (_tiles.ContainsKey(index)) continue;
                var raw = await _src.ReadAsync(_name, _tileOffsets[index], (int)_tileBytes[index], ct) ?? throw Bad("a tile is missing");
                _tiles[index] = Decode(raw);
            }
    }

    /// <summary>The value at pixel (col, row); the tile must have been loaded. ⚠ edge tiles hold padding past Width / Height.</summary>
    public float Pixel(int col, int row)
    {
        int index = (row / TileHeight) * TilesAcross + col / TileWidth;
        if (!_tiles.TryGetValue(index, out var t)) throw new InvalidOperationException($"{_name}: tile {index} not loaded (LoadWindowAsync first)");
        return t[(row % TileHeight) * TileWidth + col % TileWidth];
    }

    internal float[] Decode(byte[] raw)
    {
        int rowBytes = TileWidth * 4;
        byte[] data;
        if (_compression == 1) data = raw;
        else
        {
            data = new byte[rowBytes * TileHeight];
            using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            z.ReadExactly(data);
        }
        var result = new float[TileWidth * TileHeight];
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(result.AsSpan());
        if (_predictor == 3)
        {
            // floating-point predictor (TIFF Technical Note 3): each row is byte-differenced, then its bytes are planes — the most
            // significant byte of every sample first. Undo the difference, then gather each sample's 4 bytes (libtiff fpAcc).
            var row = new byte[rowBytes];
            for (int y = 0; y < TileHeight; y++)
            {
                data.AsSpan(y * rowBytes, rowBytes).CopyTo(row);
                for (int i = 1; i < rowBytes; i++) row[i] = unchecked((byte)(row[i] + row[i - 1]));
                int o = y * rowBytes;
                for (int i = 0; i < TileWidth; i++)
                    for (int k = 0; k < 4; k++)
                        bytes[o + i * 4 + k] = row[(3 - k) * TileWidth + i];   // little-endian host
            }
        }
        else data.AsSpan(0, bytes.Length).CopyTo(bytes);
        return result;
    }
}
