using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>A loaded picker image: the host texture handle and its pixel size.</summary>
public readonly record struct PickerImage(IntPtr Texture, int Width, int Height);

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P5) — THE shared lazy image cache of every picker: a thumbnail grid and a side pane ask it for
/// an image by KEY (a path), it loads on demand through the host's <c>loader</c> (NodeEditor.UI has no Raylib — the host owns
/// the GPU upload), remembers the result, and evicts the least-recently-used image past <see cref="Capacity"/> (calling the
/// host's <c>unloader</c> so the texture is freed). ⭐ At most <see cref="MaxLoadsPerFrame"/> loads happen per frame
/// (<see cref="BeginFrame"/> resets the budget), so a fast scroll over 500 thumbnails never stalls a frame — an image over the
/// budget answers <see langword="null"/> and is retried next frame. A key the loader could not load (null) is remembered as
/// failed and never retried until <see cref="Invalidate"/>, so a missing file costs one attempt, not one per frame.
/// </summary>
public sealed class PickerImageCache
{
    /// <summary>Default cap on cached images.</summary>
    public const int DefaultCapacity = 64;

    private readonly Func<string, (IntPtr tex, int w, int h)?> _loader;
    private readonly Action<IntPtr>? _unloader;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _lru = new();   // first = most recently used
    private int _loadsThisFrame;

    private sealed record Entry(string Key, PickerImage? Image);

    /// <summary>A cache that loads nothing (a host without images): every <see cref="Get"/> is null.</summary>
    public PickerImageCache() : this(_ => null) { }

    /// <param name="loader">Loads the image for a key (texture + pixel size); null ⇒ it cannot be loaded.</param>
    /// <param name="unloader">Frees a texture the cache evicts or invalidates; optional.</param>
    /// <param name="capacity">Most images kept (LRU eviction beyond it).</param>
    /// <param name="maxLoadsPerFrame">Most loader calls per <see cref="BeginFrame"/> window.</param>
    public PickerImageCache(Func<string, (IntPtr tex, int w, int h)?> loader, Action<IntPtr>? unloader = null,
                            int capacity = DefaultCapacity, int maxLoadsPerFrame = 2)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _unloader = unloader;
        Capacity = Math.Max(1, capacity);
        MaxLoadsPerFrame = Math.Max(1, maxLoadsPerFrame);
    }

    /// <summary>Most images kept.</summary>
    public int Capacity { get; }

    /// <summary>Most loader calls per frame.</summary>
    public int MaxLoadsPerFrame { get; }

    /// <summary>Entries held now (loaded images and remembered failures).</summary>
    public int Count => _map.Count;

    /// <summary>Starts a frame: the load budget is full again. The picker window calls it once per draw.</summary>
    public void BeginFrame() => _loadsThisFrame = 0;

    /// <summary>The texture for <paramref name="key"/>; null when not loaded (yet, over this frame's budget, or it failed).</summary>
    public IntPtr? Get(string key) => TryGet(key, out var image) ? image.Texture : null;

    /// <summary>The image for <paramref name="key"/>, loading it on demand within the per-frame budget.</summary>
    public bool TryGet(string key, out PickerImage image)
    {
        image = default;
        if (string.IsNullOrEmpty(key)) return false;

        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            if (node.Value.Image is { } hit) { image = hit; return true; }
            return false; // remembered failure
        }

        if (_loadsThisFrame >= MaxLoadsPerFrame) return false; // over budget: retry next frame
        _loadsThisFrame++;

        PickerImage? loaded = null;
        try
        {
            if (_loader(key) is { } r && r.w > 0 && r.h > 0)
                loaded = new PickerImage(r.tex, r.w, r.h);
        }
        catch (Exception) { /* a loader must never stop a picker: treat as failed */ }

        _map[key] = _lru.AddFirst(new Entry(key, loaded));
        while (_map.Count > Capacity) EvictOldest();

        if (loaded is { } img) { image = img; return true; }
        return false;
    }

    /// <summary>Forgets <paramref name="key"/> (unloading its texture) so the next <see cref="Get"/> loads it again — a file that changed.</summary>
    public void Invalidate(string key)
    {
        if (!_map.Remove(key, out var node)) return;
        _lru.Remove(node);
        Unload(node.Value);
    }

    /// <summary>Drops everything (unloading each texture).</summary>
    public void Clear()
    {
        foreach (var e in _lru) Unload(e);
        _lru.Clear();
        _map.Clear();
    }

    private void EvictOldest()
    {
        var last = _lru.Last!;
        _lru.RemoveLast();
        _map.Remove(last.Value.Key);
        Unload(last.Value);
    }

    private void Unload(Entry e)
    {
        if (e.Image is { } img)
        {
            try { _unloader?.Invoke(img.Texture); } catch (Exception) { /* freeing must never stop a picker */ }
        }
    }

    /// <summary>
    /// The size to draw an image of <paramref name="imageW"/>×<paramref name="imageH"/> at, scaled to
    /// <paramref name="availW"/> keeping its aspect (never above <paramref name="maxH"/>, and not enlarged past its own width).
    /// </summary>
    public static Vector2 FitToWidth(int imageW, int imageH, float availW, float maxH = float.MaxValue)
    {
        if (imageW <= 0 || imageH <= 0 || availW <= 0f) return Vector2.Zero;
        float scale = Math.Min(availW / imageW, 1f);
        if (imageH * scale > maxH) scale = maxH / imageH;
        return new Vector2(imageW * scale, imageH * scale);
    }
}
