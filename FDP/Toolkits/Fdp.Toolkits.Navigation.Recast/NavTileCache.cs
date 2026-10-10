#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Navigation.Recast;

/// <summary>
/// One baked navmesh tile's identity: the layer, its ABSOLUTE tile coordinates (the grid is anchored at the world origin, so a
/// tile's coordinates mean the same place in every bake — Q71 R7 "keyed geographically") and the hash of everything its bake read
/// (its own triangles, the doorway volumes over it, the bake settings — <see cref="RecastNavmeshBaker"/>).
/// </summary>
public readonly record struct NavTileKey(NavLayerMask Layer, int TileX, int TileZ, string Hash)
{
    /// <summary>The file name of this tile in a disk cache (geographic, then content).</summary>
    public string FileName => $"{TileX}_{TileZ}_{Hash}.navtile";
}

/// <summary>
/// ⭐⭐ CE-1029 + R-218 P2 (📄 docs/designs/navig-2/Navigation_Design_v2_0.md §14 "P2 as built") — the per-node cache of baked
/// navmesh tiles: in memory, and (optionally) in a local folder that survives the process. Keyed by <see cref="NavTileKey"/>, so a
/// tile whose inputs did not change is never baked twice — a second load of a terrain bakes nothing, and a re-bake after a
/// geometry change bakes only the tiles the change touched (the touched-tile rebuild IS a bake through this cache).
/// <para>⭐ It stores BYTES, never a <see cref="DtMeshData"/>: Detour writes each polygon's link list into the data when a tile is
/// added to a mesh (<c>DtPoly.firstLink</c>), so one data object can never sit in two meshes (two snapshots — R-218). Every hit
/// is a fresh copy.</para>
/// <para>⚠ Thread-safe: the baker reads and writes it from its parallel tile loop. A disk write goes through a temp file and a
/// rename, so two processes baking the same tile never leave a torn file; an unreadable file is a miss (the tile re-bakes).</para>
/// </summary>
public sealed class NavTileCache
{
    /// <summary>Environment variable naming the disk folder; <c>off</c> keeps the cache in memory only.</summary>
    public const string FolderVariable = "HROT_NAVTILE_CACHE";

    /// <summary>The memory part is dropped whole when it grows past this (a bound, not a policy: one terrain is far below it).</summary>
    public const long MaxMemoryBytes = 512L * 1024 * 1024;

    private const int MaxVertsPerPoly = 6;

    private readonly ConcurrentDictionary<NavTileKey, byte[]> _memory = new();
    private long _memoryBytes;
    private int _diskWarned;

    /// <summary>The disk folder, or null for memory only.</summary>
    public string? Folder { get; }

    public NavTileCache(string? folder = null) => Folder = string.IsNullOrWhiteSpace(folder) ? null : folder;

    /// <summary>
    /// The process-wide cache every <see cref="RecastNavmeshFactory"/> uses unless told otherwise: memory + the folder in
    /// <see cref="FolderVariable"/>, else <c>&lt;local app data&gt;/Hrot/navtiles</c> (per node, per machine — Q71 R7).
    /// </summary>
    public static NavTileCache Default => _default.Value;
    private static readonly Lazy<NavTileCache> _default = new(() => new NavTileCache(DefaultFolder()), LazyThreadSafetyMode.ExecutionAndPublication);

    private static string? DefaultFolder()
    {
        var env = Environment.GetEnvironmentVariable(FolderVariable);
        if (!string.IsNullOrWhiteSpace(env)) return string.Equals(env, "off", StringComparison.OrdinalIgnoreCase) ? null : env;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
        return Path.Combine(root, "Hrot", "navtiles");
    }

    /// <summary>
    /// A hit: <paramref name="data"/> is a fresh copy of the cached tile — or null when the tile is cached as EMPTY (it has geometry
    /// but no walkable polygon: a roof, a cliff — measured: 102 of basic-desert's 1352 tiles re-baked on every load before empties
    /// were cached). False = a miss.
    /// </summary>
    public bool TryGet(in NavTileKey key, out DtMeshData? data)
    {
        data = null;
        if (!_memory.TryGetValue(key, out var bytes))
        {
            bytes = ReadDisk(key);
            if (bytes == null) return false;
            Remember(key, bytes);
        }
        if (bytes.Length == 0) return true;   // cached as empty
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes, writable: false));
            data = new DtMeshDataReader().Read(reader, MaxVertsPerPoly);
            return true;
        }
        catch (Exception)
        {
            _memory.TryRemove(key, out _);
            return false;   // a damaged entry re-bakes
        }
    }

    /// <summary>Stores a freshly baked tile — null = EMPTY (the caller keeps using its own object; the cache keeps bytes).</summary>
    public void Put(in NavTileKey key, DtMeshData? data)
    {
        byte[] bytes = Array.Empty<byte>();
        if (data != null)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                new DtMeshDataWriter().Write(w, data, RcByteOrder.LITTLE_ENDIAN, false);
            bytes = ms.ToArray();
        }
        Remember(key, bytes);
        WriteDisk(key, bytes);
    }

    /// <summary>How many tiles the memory part holds (a rail reads it).</summary>
    public int MemoryCount => _memory.Count;

    /// <summary>Drops the memory part (the disk part stays) — a rail uses it to prove a load comes from disk.</summary>
    public void ClearMemory()
    {
        _memory.Clear();
        Interlocked.Exchange(ref _memoryBytes, 0);
    }

    private void Remember(in NavTileKey key, byte[] bytes)
    {
        if (Interlocked.Add(ref _memoryBytes, bytes.Length) > MaxMemoryBytes) ClearMemory();
        _memory[key] = bytes;
    }

    private string PathOf(in NavTileKey key) => Path.Combine(Folder!, key.Layer.ToString(), key.FileName);

    private byte[]? ReadDisk(in NavTileKey key)
    {
        if (Folder == null) return null;
        try
        {
            var path = PathOf(key);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception) { return null; }
    }

    private void WriteDisk(in NavTileKey key, byte[] bytes)
    {
        if (Folder == null) return;
        try
        {
            var path = PathOf(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e)
        {
            // ⭐ A cache that cannot write is a slower load, never a failed one — said once per process.
            if (Interlocked.Exchange(ref _diskWarned, 1) == 0)
                Fdp.Core.Logging.FdpLog<NavTileCache>.Warn($"[Navmesh] tile cache folder '{Folder}' is not writable ({e.Message}); tiles stay in memory.");
        }
    }
}
