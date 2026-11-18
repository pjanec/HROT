using System.Text;
using Microsoft.Win32.SafeHandles;

#nullable enable

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// ⭐⭐ CE-3428 (📄 docs/DESIGN_Terrain_Import_UTM.md §12.4) — a terrain folder's prefilled navmesh tiles PACKED: one file per layer per
/// <see cref="RegionTiles"/> × <see cref="RegionTiles"/> tiles (<c>navtiles/&lt;layer&gt;/&lt;rx&gt;_&lt;rz&gt;.navpack</c>), an index then the tile bytes.
/// 📐 Measured on <c>full-1</c> (5.5 km): the loose layout was 106 260 files in 106 263 folders (142.8 MB of data, 847 MB on disk); staging copies
/// and checks every entry on every node (a stat walk alone 0.94 s, a local copy 4.7 s against 0.1 s for one file of the same bytes).
/// ⭐ The KEYS do not change (<see cref="NavTileKey"/>: layer, tile x / z, content hash) — only where the bytes live. The per-machine cache stays loose
/// (it prunes per square); a seed reads packs first, then loose files (a terrain imported before this keeps working).
/// <para>Format (little-endian): <c>"HNTP"</c>, version 1, entry count; per entry tile x, tile z (int), hash (byte length + UTF-8), offset (long), length (int);
/// then the data. An empty tile has length 0 (as a loose empty file).</para>
/// </summary>
public static class NavTilePack
{
    /// <summary>Tiles per region side: a pack holds up to 32 × 32 tiles of one layer.</summary>
    public const int RegionTiles = 32;

    /// <summary>The pack file extension.</summary>
    public const string Extension = ".navpack";

    private static readonly byte[] Magic = "HNTP"u8.ToArray();
    private const int Version = 1;

    /// <summary>A region coordinate (floor division, so negative tiles group the same way).</summary>
    public static int RegionOf(int tile) => (int)Math.Floor(tile / (double)RegionTiles);

    /// <summary>The pack holding <paramref name="key"/>: <c>&lt;folder&gt;/&lt;layer&gt;/&lt;rx&gt;_&lt;rz&gt;.navpack</c>.</summary>
    public static string PathOf(string folder, in NavTileKey key)
        => Path.Combine(folder, key.Layer.ToString(), $"{RegionOf(key.TileX)}_{RegionOf(key.TileZ)}{Extension}");

    private readonly record struct Entry(int X, int Z, string Hash, byte[] Bytes);

    /// <summary>
    /// Packs every loose tile under <paramref name="folder"/> (and the entries of any pack already there) into one pack per layer and region, then deletes
    /// the loose files and their emptied folders. Each pack goes through a temp file and a rename. Returns the pack count. Cold: once per prefill.
    /// </summary>
    public static int Pack(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        int packs = 0;
        foreach (var layerDir in Directory.GetDirectories(folder))
        {
            var byRegion = new Dictionary<(int, int), List<Entry>>();
            foreach (var pack in Directory.GetFiles(layerDir, "*" + Extension))
                foreach (var e in ReadAll(pack)) Add(byRegion, e);
            var loose = Directory.GetFiles(layerDir, "*.navtile", SearchOption.AllDirectories);
            foreach (var file in loose)
            {
                var name = Path.GetFileNameWithoutExtension(file);   // <x>_<z>_<hash>
                var parts = name.Split('_', 3);
                if (parts.Length != 3 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int z)) continue;
                Add(byRegion, new Entry(x, z, parts[2], File.ReadAllBytes(file)));
            }
            foreach (var ((rx, rz), entries) in byRegion)
            {
                Write(Path.Combine(layerDir, $"{rx}_{rz}{Extension}"), entries);
                packs++;
            }
            foreach (var file in loose) File.Delete(file);
            foreach (var dir in Directory.GetDirectories(layerDir))
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
        }
        return packs;
    }

    private static void Add(Dictionary<(int, int), List<Entry>> byRegion, Entry e)
    {
        var region = (RegionOf(e.X), RegionOf(e.Z));
        if (!byRegion.TryGetValue(region, out var list)) byRegion[region] = list = new List<Entry>();
        for (int i = 0; i < list.Count; i++)
            if (list[i].X == e.X && list[i].Z == e.Z && list[i].Hash == e.Hash) { list[i] = e; return; }   // the newer (loose) wins
        list.Add(e);
    }

    private static void Write(string path, List<Entry> entries)
    {
        entries.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : string.CompareOrdinal(a.Hash, b.Hash));   // deterministic bytes
        long header = 4 + 4 + 4;
        foreach (var e in entries) header += 4 + 4 + 1 + Encoding.UTF8.GetByteCount(e.Hash) + 8 + 4;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(Magic); w.Write(Version); w.Write(entries.Count);
            long offset = header;
            foreach (var e in entries)
            {
                var hash = Encoding.UTF8.GetBytes(e.Hash);
                w.Write(e.X); w.Write(e.Z); w.Write((byte)hash.Length); w.Write(hash); w.Write(offset); w.Write(e.Bytes.Length);
                offset += e.Bytes.Length;
            }
            foreach (var e in entries) w.Write(e.Bytes);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static IEnumerable<Entry> ReadAll(string path)
    {
        var index = ReadIndex(path);
        if (index == null) yield break;
        using var handle = File.OpenHandle(path);
        foreach (var ((x, z, hash), (offset, length)) in index)
        {
            var bytes = new byte[length];
            if (length > 0) RandomAccess.Read(handle, bytes, offset);
            yield return new Entry(x, z, hash, bytes);
        }
    }

    /// <summary>A pack's index (tile x, z, hash → offset, length), or null when the file is not a readable pack (a miss, never a failure).</summary>
    public static Dictionary<(int X, int Z, string Hash), (long Offset, int Length)>? ReadIndex(string path)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            if (!r.ReadBytes(4).AsSpan().SequenceEqual(Magic) || r.ReadInt32() != Version) return null;
            int n = r.ReadInt32();
            var index = new Dictionary<(int, int, string), (long, int)>(n);
            for (int i = 0; i < n; i++)
            {
                int x = r.ReadInt32(), z = r.ReadInt32();
                var hash = Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
                long offset = r.ReadInt64(); int length = r.ReadInt32();
                index[(x, z, hash)] = (offset, length);
            }
            return index;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// ⭐ CE-3428 — reads a seed folder's packs: each pack's index once (lazily, thread-safe — the baker reads in parallel), the tile's bytes by a
/// positional read. Null = not in a pack (the caller falls back to the loose file).
/// </summary>
public sealed class NavTilePackReader
{
    private readonly string _folder;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<(int X, int Z, string Hash), (long Offset, int Length)>?> _indexes = new();

    public NavTilePackReader(string folder) => _folder = folder;

    public byte[]? Read(in NavTileKey key)
    {
        var path = NavTilePack.PathOf(_folder, key);
        var index = _indexes.GetOrAdd(path, p => File.Exists(p) ? NavTilePack.ReadIndex(p) : null);
        if (index == null || !index.TryGetValue((key.TileX, key.TileZ, key.Hash), out var at)) return null;
        try
        {
            var bytes = new byte[at.Length];
            if (at.Length > 0)
            {
                using SafeFileHandle handle = File.OpenHandle(path);
                if (RandomAccess.Read(handle, bytes, at.Offset) != at.Length) return null;
            }
            return bytes;
        }
        catch (Exception) { return null; }
    }
}
