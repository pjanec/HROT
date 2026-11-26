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
        return WriteFolder(folder, ReadFolder(folder));
    }

    /// <summary>
    /// ⭐ CE-3428 (the T-6 rule of CE-3435, kept) — lays <paramref name="overlay"/>'s tiles OVER <paramref name="target"/>'s: where the overlay holds a tile of a
    /// layer + square, the target's tiles of that square are dropped (their hash can no longer match), then the overlay's are added; the result is written to
    /// <paramref name="target"/> as packs. Either side may be packed or loose. The terrain save (<c>TerrainBake</c>) is its caller. Returns the pack count.
    /// </summary>
    public static int MergeOver(string target, string overlay)
    {
        var merged = ReadFolder(target);
        foreach (var (layer, entries) in ReadFolder(overlay))
        {
            var squares = new HashSet<(int, int)>();
            foreach (var e in entries) squares.Add((e.X, e.Z));
            if (!merged.TryGetValue(layer, out var list)) merged[layer] = list = new List<Entry>();
            list.RemoveAll(e => squares.Contains((e.X, e.Z)));
            list.AddRange(entries);
        }
        Directory.CreateDirectory(target);
        return WriteFolder(target, merged);
    }

    /// <summary>Every tile key under <paramref name="folder"/> — packed or loose — as (layer folder name, tile x, tile z, hash). Cold (a tool, a rail).</summary>
    public static List<(string Layer, int X, int Z, string Hash)> ListTiles(string folder)
    {
        var list = new List<(string, int, int, string)>();
        foreach (var (layer, entries) in ReadFolder(folder))
            foreach (var e in entries) list.Add((layer, e.X, e.Z, e.Hash));
        return list;
    }

    /// <summary>Every tile under <paramref name="folder"/> by layer folder name: packs first, then loose files (a loose tile of the same key wins).</summary>
    private static Dictionary<string, List<Entry>> ReadFolder(string folder)
    {
        var layers = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        if (!Directory.Exists(folder)) return layers;
        foreach (var layerDir in Directory.GetDirectories(folder))
        {
            var byKey = new Dictionary<(int, int, string), Entry>();
            foreach (var pack in Directory.GetFiles(layerDir, "*" + Extension))
                foreach (var e in ReadAll(pack)) byKey[(e.X, e.Z, e.Hash)] = e;
            var loose = Directory.GetFiles(layerDir, "*.navtile", SearchOption.AllDirectories);
            for (int li = 0; li < loose.Length; li++)
            {
                var file = loose[li];
                // ⭐ CE-5060 — packing a prefilled terrain reads ~60 000 tiles (16 s on Stechovice, measured): it says how far it is, so a node importing on first use keeps its heartbeat (free with no load)
                Fdp.Core.LoadProgress.ReportDetail("navmesh packing", li, loose.Length);
                var parts = Path.GetFileNameWithoutExtension(file).Split('_', 3);   // <x>_<z>_<hash>
                if (parts.Length != 3 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int z)) continue;
                byKey[(x, z, parts[2])] = new Entry(x, z, parts[2], File.ReadAllBytes(file));
            }
            layers[Path.GetFileName(layerDir)] = new List<Entry>(byKey.Values);
        }
        return layers;
    }

    /// <summary>Writes <paramref name="layers"/> under <paramref name="folder"/> as packs, replacing every pack and loose tile there. Returns the pack count.</summary>
    private static int WriteFolder(string folder, Dictionary<string, List<Entry>> layers)
    {
        int packs = 0;
        foreach (var (layer, entries) in layers)
        {
            string layerDir = Path.Combine(folder, layer);
            Directory.CreateDirectory(layerDir);
            var byRegion = new Dictionary<(int, int), List<Entry>>();
            foreach (var e in entries) Add(byRegion, e);
            var old = new HashSet<string>(Directory.GetFiles(layerDir, "*" + Extension), StringComparer.Ordinal);
            int region = 0;
            foreach (var ((rx, rz), list) in byRegion)
            {
                string path = Path.Combine(layerDir, $"{rx}_{rz}{Extension}");
                Fdp.Core.LoadProgress.ReportDetail("navmesh packing", region++, byRegion.Count);   // ⭐ CE-5060 — as the read above
                Write(path, list);
                old.Remove(path);
                packs++;
            }
            foreach (var stale in old) File.Delete(stale);   // a region with no tile left
            var written = Directory.GetFiles(layerDir, "*.navtile", SearchOption.AllDirectories);
            for (int di = 0; di < written.Length; di++)
            {
                Fdp.Core.LoadProgress.ReportDetail("navmesh packing: removing loose tiles", di, written.Length);   // ⭐ CE-5060 — ~60 000 deletes
                File.Delete(written[di]);
            }
            var dirs = Directory.GetDirectories(layerDir);
            for (int si = 0; si < dirs.Length; si++)
            {
                Fdp.Core.LoadProgress.ReportDetail("navmesh packing: removing tile folders", si, dirs.Length);   // ⭐ CE-5060 — measured 3.6–4.3 s silent here on Stechovice
                if (Directory.GetFileSystemEntries(dirs[si], "*", SearchOption.AllDirectories).All(e => Directory.Exists(e))) Directory.Delete(dirs[si], recursive: true);
            }
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

    private static List<Entry> ReadAll(string path)
    {
        var list = new List<Entry>();
        var index = ReadIndex(path);
        if (index == null) return list;
        using var handle = File.OpenHandle(path);
        foreach (var ((x, z, hash), (offset, length)) in index)
        {
            var bytes = new byte[length];
            if (length > 0) RandomAccess.Read(handle, bytes, offset);
            list.Add(new Entry(x, z, hash, bytes));
        }
        return list;
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
