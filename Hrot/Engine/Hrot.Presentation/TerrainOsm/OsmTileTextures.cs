using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Terrain.Osm;
using NodeEditor.UI.Picker;
using Raylib_cs;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>
/// ⭐ CE-4375 (OSM §6a — "an LRU of tile textures") — the wizard map's XYZ tiles: fetched from <see cref="ITileSource"/> on a worker, decoded and UPLOADED on the frame thread
/// (C-15: only texture uploads on the frame thread) through the same raylib calls the icon atlas uses (<c>LoadImageFromMemory</c> + <c>LoadTextureFromImage</c>), drawn by the map
/// with <c>AddImage</c>. A missing or failed tile is simply not drawn (the light ground shows). ⭐ CE-1468 — the LRU is THE shared one, <see cref="ImageCache{TKey}"/> (the picker's
/// thumbnails use it too), keyed by the tile (a struct: no allocation per tile per frame); this class is only its LOADER — a tile still downloading answers "pending".
/// </summary>
public sealed class OsmTileTextures : IDisposable
{
    public const int Capacity = 160;
    private const int MaxInFlight = 6;
    private const int MaxUploadsPerFrame = 8;

    private readonly ITileSource _source;
    private readonly ImageCache<(int Z, int X, int Y)> _cache;
    private readonly HashSet<(int, int, int)> _requested = new();
    private readonly Dictionary<(int, int, int), byte[]?> _ready = new();
    private readonly ConcurrentQueue<((int, int, int) Key, byte[]? Png)> _arrived = new();
    private readonly CancellationTokenSource _cts = new();
    private int _inFlight;

    public OsmTileTextures(ITileSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = new ImageCache<(int, int, int)>(Load, id => Raylib.UnloadTexture(new Texture2D { Id = (uint)id }), Capacity, MaxUploadsPerFrame);
    }

    public string Attribution => _source.Attribution;

    /// <summary>How many tiles are resident (a rail).</summary>
    public int Count => _cache.Count;

    /// <summary>Once a frame: the upload budget is full again and the tiles that arrived are ready to upload.</summary>
    public void Pump()
    {
        _cache.BeginFrame();
        while (_arrived.TryDequeue(out var a)) { _requested.Remove(a.Key); _ready[a.Key] = a.Png; }
    }

    /// <summary>The texture of a tile when it is resident (marked as used); else asks for it and returns false.</summary>
    public bool TryGet(int z, int x, int y, out Texture2D texture)
    {
        texture = default;
        if (!_cache.TryGet((z, x, y), out var img)) return false;
        texture = new Texture2D { Id = (uint)img.Texture, Width = img.Width, Height = img.Height, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
        return true;
    }

    // the cache's loader: upload a tile that arrived, else start its download and answer "pending"
    private ImageLoad Load((int Z, int X, int Y) key)
    {
        if (_ready.Remove(key, out var png))
        {
            if (png is not { Length: > 0 }) return ImageLoad.Failed;
            var img = Raylib.LoadImageFromMemory(".png", png);
            if (img.Width <= 0) return ImageLoad.Failed;
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            return ImageLoad.Loaded(new PickerImage((IntPtr)tex.Id, tex.Width, tex.Height));
        }
        if (_requested.Contains(key) || _inFlight >= MaxInFlight) return ImageLoad.Pending;
        _requested.Add(key);
        Interlocked.Increment(ref _inFlight);
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            byte[]? bytes = null;
            try { bytes = await _source.GetTileAsync(key.Z, key.X, key.Y, ct).ConfigureAwait(false); }
            catch (Exception) { bytes = null; }   // a failed tile is not drawn; the light ground shows
            finally { Interlocked.Decrement(ref _inFlight); }
            _arrived.Enqueue((key, bytes));
        }, ct);
        return ImageLoad.Pending;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cache.Clear();
    }
}
