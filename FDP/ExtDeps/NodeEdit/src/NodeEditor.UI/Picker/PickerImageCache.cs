using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>
/// A loaded picker image: the host texture handle and its pixel size. ⭐ CE-4271 — <see cref="Uv0"/>/<see cref="Uv1"/> are the part of the texture the image IS: a whole file is (0,0)–(1,1), a picture in an
/// atlas page is its cell (then <see cref="Width"/>/<see cref="Height"/> are the cell's size, not the page's).
/// </summary>
public readonly record struct PickerImage(IntPtr Texture, int Width, int Height, Vector2 Uv0, Vector2 Uv1)
{
    /// <summary>A whole-texture image.</summary>
    public PickerImage(IntPtr texture, int width, int height) : this(texture, width, height, Vector2.Zero, Vector2.One) { }
}

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P5) — THE shared lazy image cache of every picker: a thumbnail grid and a side pane ask it for
/// an image by KEY (a path), it loads on demand through the host's <c>loader</c> (NodeEditor.UI has no Raylib — the host owns
/// the GPU upload), remembers the result, and evicts the least-recently-used image past <see cref="ImageCache{TKey}.Capacity"/> (calling the
/// host's <c>unloader</c> so the texture is freed). ⭐ At most <see cref="ImageCache{TKey}.MaxLoadsPerFrame"/> loads happen per frame
/// (<see cref="ImageCache{TKey}.BeginFrame"/> resets the budget), so a fast scroll over 500 thumbnails never stalls a frame — an image over the
/// budget answers <see langword="null"/> and is retried next frame. A key the loader could not load (null) is remembered as
/// failed and never retried until <see cref="ImageCache{TKey}.Invalidate"/>, so a missing file costs one attempt, not one per frame.
/// </summary>
public sealed class PickerImageCache : ImageCache<string>
{
    /// <summary>Default cap on cached images.</summary>
    public const int DefaultCapacity = 64;

    /// <summary>
    /// ⭐ CE-4271 (DESIGN_Add_Entity_Picker §2j) — the RESIDENT lookup, asked FIRST by <see cref="ImageCache{TKey}.TryGet"/>: a key it answers is served with no load budget, is never entered into the LRU, never evicted and never unloaded
    /// (<see cref="ImageCache{TKey}.Invalidate"/>/<see cref="ImageCache{TKey}.Clear"/> leave it alone) — an atlas page is one texture shared by many pictures, so an eviction of one picture would free the other 65's. It must not allocate per call
    /// (the render thread asks per tile per frame). Null: no resident images.
    /// </summary>
    public Func<string, PickerImage?>? Resident { get; init; }

    /// <summary>A cache that loads nothing (a host without images): every <see cref="Get"/> is null.</summary>
    public PickerImageCache() : this(_ => null) { }

    /// <param name="loader">Loads the image for a key (texture + pixel size); null ⇒ it cannot be loaded.</param>
    /// <param name="unloader">Frees a texture the cache evicts or invalidates; optional.</param>
    /// <param name="capacity">Most images kept (LRU eviction beyond it).</param>
    /// <param name="maxLoadsPerFrame">Most loader calls per <see cref="ImageCache{TKey}.BeginFrame"/> window.</param>
    /// <param name="resident">⭐ CE-4271 — the <see cref="Resident"/> lookup (null: none).</param>
    public PickerImageCache(Func<string, (IntPtr tex, int w, int h)?> loader, Action<IntPtr>? unloader = null,
                            int capacity = DefaultCapacity, int maxLoadsPerFrame = 2, Func<string, PickerImage?>? resident = null)
        : base(Adapt(loader ?? throw new ArgumentNullException(nameof(loader))), unloader, capacity, maxLoadsPerFrame, StringComparer.Ordinal)
        => Resident = resident;

    // the picker's loader is synchronous: an image or null (failed) — never pending
    private static Func<string, ImageLoad> Adapt(Func<string, (IntPtr tex, int w, int h)?> loader)
        => key => loader(key) is { } r && r.w > 0 && r.h > 0 ? ImageLoad.Loaded(new PickerImage(r.tex, r.w, r.h)) : ImageLoad.Failed;

    protected override bool IsValidKey(string key) => !string.IsNullOrEmpty(key);

    protected override bool TryResident(string key, out PickerImage image)
    {
        image = default;
        if (Resident is { } resident && resident(key) is { } resolved) { image = resolved; return true; }   // ⭐ CE-4271 — shared page textures: no budget, no LRU, never unloaded
        return false;
    }

    /// <summary>The texture for <paramref name="key"/>; null when not loaded (yet, over this frame's budget, or it failed).</summary>
    public IntPtr? Get(string key) => TryGet(key, out var image) ? image.Texture : null;

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
