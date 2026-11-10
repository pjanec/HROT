using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Terrain.Osm;
using Raylib_cs;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>
/// ⭐ CE-4375 (OSM §6a — "an LRU of tile textures") — the wizard map's XYZ tiles: fetched from <see cref="ITileSource"/> on a worker, decoded and UPLOADED on the frame thread
/// (C-15: only texture uploads on the frame thread) through the same raylib calls the icon atlas uses (<c>LoadImageFromMemory</c> + <c>LoadTextureFromImage</c>), drawn by the map
/// with <c>AddImage</c>. A missing or failed tile is simply not drawn (the light ground shows). ⭐ Bounded: at most <see cref="Capacity"/> textures; the least recently drawn go.
/// </summary>
public sealed class OsmTileTextures : IDisposable
{
    public const int Capacity = 160;
    private const int MaxInFlight = 6;

    private readonly ITileSource _source;
    private readonly Dictionary<(int Z, int X, int Y), (Texture2D Tex, long Used)> _textures = new();
    private readonly HashSet<(int, int, int)> _requested = new();
    private readonly HashSet<(int, int, int)> _missing = new();
    private readonly ConcurrentQueue<((int, int, int) Key, byte[]? Png)> _arrived = new();
    private readonly CancellationTokenSource _cts = new();
    private int _inFlight;
    private long _frame;

    public OsmTileTextures(ITileSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

    public string Attribution => _source.Attribution;

    /// <summary>How many tiles are resident (a rail).</summary>
    public int Count => _textures.Count;

    /// <summary>Once a frame: uploads what arrived (at most a few per frame) and starts the next frame's count.</summary>
    public void Pump(int maxUploads = 8)
    {
        _frame++;
        while (maxUploads-- > 0 && _arrived.TryDequeue(out var a))
        {
            _requested.Remove(a.Key);
            if (a.Png is not { Length: > 0 } png) { _missing.Add(a.Key); continue; }
            var img = Raylib.LoadImageFromMemory(".png", png);
            if (img.Width <= 0) { _missing.Add(a.Key); continue; }
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            _textures[a.Key] = (tex, _frame);
            Evict();
        }
    }

    /// <summary>The texture of a tile when it is resident (marked as used); else asks for it and returns false.</summary>
    public bool TryGet(int z, int x, int y, out Texture2D texture)
    {
        var key = (z, x, y);
        if (_textures.TryGetValue(key, out var t)) { _textures[key] = (t.Tex, _frame); texture = t.Tex; return true; }
        texture = default;
        if (_missing.Contains(key) || _requested.Contains(key) || _inFlight >= MaxInFlight) return false;
        _requested.Add(key);
        Interlocked.Increment(ref _inFlight);
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            byte[]? png = null;
            try { png = await _source.GetTileAsync(z, x, y, ct).ConfigureAwait(false); }
            catch (Exception) { png = null; }   // a failed tile is not drawn; the light ground shows
            finally { Interlocked.Decrement(ref _inFlight); }
            _arrived.Enqueue((key, png));
        }, ct);
        return false;
    }

    private void Evict()
    {
        while (_textures.Count > Capacity)
        {
            (int, int, int) oldest = default; long when = long.MaxValue;
            foreach (var kv in _textures) if (kv.Value.Used < when) { when = kv.Value.Used; oldest = kv.Key; }
            Raylib.UnloadTexture(_textures[oldest].Tex);
            _textures.Remove(oldest);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var t in _textures.Values) Raylib.UnloadTexture(t.Tex);
        _textures.Clear();
    }
}
