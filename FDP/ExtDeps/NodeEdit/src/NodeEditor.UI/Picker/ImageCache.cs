namespace NodeEditor.UI.Picker;

/// <summary>What a loader answered for one key: the image, "not yet" (asked again on a later frame — a download in flight), or failed (remembered).</summary>
public readonly record struct ImageLoad(ImageLoadState State, PickerImage Image)
{
    public static ImageLoad Loaded(PickerImage image) => new(ImageLoadState.Loaded, image);
    public static readonly ImageLoad Pending = new(ImageLoadState.Pending, default);
    public static readonly ImageLoad Failed = new(ImageLoadState.Failed, default);
}

public enum ImageLoadState { Failed, Pending, Loaded }

/// <summary>
/// ⭐ CE-1468 — THE lazy LRU of host-uploaded textures, generic over its KEY: the picker's thumbnails key it by path (<see cref="PickerImageCache"/>), the OSM wizard's map by
/// tile (a struct key: no allocation per tile per frame, R-2084). It loads on demand through the host's loader within <see cref="MaxLoadsPerFrame"/> loads a frame, remembers
/// a FAILED key until <see cref="Invalidate"/>, asks a PENDING key again on a later frame (an answer that did no work costs no budget), and evicts the least-recently-used
/// image past <see cref="Capacity"/>, calling the host's unloader. ⛔ One concept, one cache: a second texture LRU is a defect (CE-1468).
/// </summary>
public class ImageCache<TKey> where TKey : notnull
{
    private readonly Func<TKey, ImageLoad> _loader;
    private readonly Action<IntPtr>? _unloader;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru = new();   // first = most recently used
    private int _loadsThisFrame;

    private sealed record Entry(TKey Key, PickerImage? Image);

    /// <param name="loader">loads the image for a key, says it is not there yet, or that it failed</param>
    /// <param name="unloader">frees a texture the cache evicts or invalidates; optional</param>
    /// <param name="capacity">most images kept (LRU eviction beyond it)</param>
    /// <param name="maxLoadsPerFrame">most loads per <see cref="BeginFrame"/> window (a pending answer is not charged)</param>
    /// <param name="comparer">the key comparer (null: the default)</param>
    public ImageCache(Func<TKey, ImageLoad> loader, Action<IntPtr>? unloader = null, int capacity = 64, int maxLoadsPerFrame = 2, IEqualityComparer<TKey>? comparer = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _unloader = unloader;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
        Capacity = Math.Max(1, capacity);
        MaxLoadsPerFrame = Math.Max(1, maxLoadsPerFrame);
    }

    /// <summary>Most images kept.</summary>
    public int Capacity { get; }

    /// <summary>Most loader calls per frame.</summary>
    public int MaxLoadsPerFrame { get; }

    /// <summary>Entries held now (loaded images and remembered failures).</summary>
    public int Count => _map.Count;

    /// <summary>Starts a frame: the load budget is full again.</summary>
    public void BeginFrame() => _loadsThisFrame = 0;

    /// <summary>A key answered without the LRU (a subclass's resident images); none by default.</summary>
    protected virtual bool TryResident(TKey key, out PickerImage image) { image = default; return false; }

    /// <summary>False for a key that is never looked up nor loaded (the picker's empty path); every key by default.</summary>
    protected virtual bool IsValidKey(TKey key) => true;

    /// <summary>The image for <paramref name="key"/>, loading it on demand within the per-frame budget.</summary>
    public bool TryGet(TKey key, out PickerImage image)
    {
        image = default;
        if (!IsValidKey(key)) return false;
        if (TryResident(key, out image)) return true;
        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            if (node.Value.Image is { } hit) { image = hit; return true; }
            return false; // remembered failure
        }

        if (_loadsThisFrame >= MaxLoadsPerFrame) return false; // over budget: retry next frame
        _loadsThisFrame++;

        ImageLoad load;
        try { load = _loader(key); }
        catch (Exception) { load = ImageLoad.Failed; }   // a loader must never stop its window: treat as failed
        if (load.State == ImageLoadState.Pending) { _loadsThisFrame--; return false; }   // not yet: ask again later, no budget spent
        PickerImage? loaded = load.State == ImageLoadState.Loaded && load.Image.Width > 0 && load.Image.Height > 0 ? load.Image : null;

        _map[key] = _lru.AddFirst(new Entry(key, loaded));
        while (_map.Count > Capacity) EvictOldest();

        if (loaded is { } img) { image = img; return true; }
        return false;
    }

    /// <summary>Forgets <paramref name="key"/> (unloading its texture) so the next <see cref="TryGet"/> loads it again.</summary>
    public void Invalidate(TKey key)
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
            try { _unloader?.Invoke(img.Texture); } catch (Exception) { /* freeing must never stop a window */ }
        }
    }
}
