using System.Numerics;
using Fdp.Presentation.Icons;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ CE-4271 (docs/DESIGN_Add_Entity_Picker.md §2j, R-4117) — the ENTITY THUMBNAIL ATLAS: the pictures of the entity types, 320 × 180 cells on a few JPEG pages that are EMBEDDED in this assembly with their index
/// (<c>Thumbnails/entities.atlas.json</c>, written only by <c>scripts/thumbnails.py</c>; nothing is read from disk or the NAS at run time). A picture is addressed by the KEY <c>atlas:entities/&lt;stem&gt;</c>, the stem being
/// <see cref="ThumbnailMap.GeneratedFileName"/> of the type's name — the same stem the loose <c>generated/&lt;stem&gt;.png</c> has.
/// <para>Two halves: <see cref="Contains"/> / <see cref="KeyOf"/> are PURE (the <see cref="AtlasIndex"/>; no GPU, any thread) — <see cref="ThumbnailMap"/> asks them when it builds the catalogue; <see cref="TryGetImage"/> is the picker
/// image cache's RESIDENT lookup (render thread): the first ask for a page decodes and uploads it ONCE (<paramref name="uploadPage"/>; a failure is remembered, not retried), after which every ask is a dictionary hit that allocates
/// nothing (R-2084) — and a page is never unloaded, because it is one texture shared by up to 66 pictures.</para>
/// ⛔ <see cref="Shared"/> is the one instance of a process; a host that builds a <see cref="ThumbnailMap"/> or a picker image cache must PASS it (the silent-default rule — a map built without it resolves nothing from the atlas and looks fine).
/// </summary>
public sealed class ThumbnailAtlas
{
    /// <summary>The key prefix of an atlas picture; a file picture's key is its path.</summary>
    public const string KeyPrefix = "atlas:entities/";

    /// <summary>The embedded index's resource name.</summary>
    public const string IndexResourceName = "Thumbnails/entities.atlas.json";

    private sealed class Cell
    {
        public int Page, X, Y, W, H;
        public PickerImage? Image;
    }

    private readonly AtlasIndex _index;
    private readonly Func<string, (IntPtr tex, int w, int h)?> _uploadPage;
    private readonly Dictionary<string, Cell> _cells;          // key → cell, built once
    private readonly IconAtlas?[] _pages;
    private readonly bool[] _pageFailed;

    /// <param name="index">The parsed index.</param>
    /// <param name="uploadPage">Decodes and uploads the page file <c>name</c> (texture id, width, height); null ⇒ it could not be loaded. Called at most once per page, on the render thread.</param>
    public ThumbnailAtlas(AtlasIndex index, Func<string, (IntPtr tex, int w, int h)?> uploadPage)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _uploadPage = uploadPage ?? throw new ArgumentNullException(nameof(uploadPage));
        _pages = new IconAtlas?[index.Pages.Count];
        _pageFailed = new bool[index.Pages.Count];
        _cells = new Dictionary<string, Cell>(index.Count, StringComparer.Ordinal);
        foreach (var name in index.Names)
            if (index.TryGet(name, out int page, out int x, out int y, out int w, out int h))
                _cells[KeyPrefix + name] = new Cell { Page = page, X = x, Y = y, W = w, H = h };
    }

    /// <summary>An atlas with no pictures.</summary>
    public static ThumbnailAtlas Empty { get; } = new(AtlasIndex.Empty, _ => null);

    private static readonly Lazy<ThumbnailAtlas> SharedAtlas = new(LoadShipped);

    /// <summary>⭐ The process's atlas: the EMBEDDED index and pages, uploaded to the GPU on first use. An embedded index that is missing or malformed gives <see cref="Empty"/> (every tile then draws its glyph) — never a throw.</summary>
    public static ThumbnailAtlas Shared => SharedAtlas.Value;

    private static ThumbnailAtlas LoadShipped()
    {
        var asm = typeof(ThumbnailAtlas).Assembly;
        var bytes = EmbeddedAtlasResources.TryRead(asm, IndexResourceName);
        if (bytes is null) return Empty;
        try { return new ThumbnailAtlas(AtlasIndex.Parse(System.Text.Encoding.UTF8.GetString(bytes)), UploadEmbeddedPage); }
        catch (FormatException) { return Empty; }
    }

    /// <summary>The production page loader: the embedded JPEG → a bilinear Raylib texture.</summary>
    private static (IntPtr tex, int w, int h)? UploadEmbeddedPage(string pageName)
    {
        var bytes = EmbeddedAtlasResources.TryRead(typeof(ThumbnailAtlas).Assembly, "Thumbnails/" + pageName);
        if (bytes is null) return null;
        var img = Raylib_cs.Raylib.LoadImageFromMemory(System.IO.Path.GetExtension(pageName), bytes);
        if (img.Width == 0) return null;
        var tex = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);
        if (tex.Id == 0) return null;
        Raylib_cs.Raylib.SetTextureFilter(tex, Raylib_cs.TextureFilter.Bilinear);
        return ((IntPtr)tex.Id, tex.Width, tex.Height);
    }

    /// <summary>The pure index (what <c>scripts/thumbnails.py</c> wrote).</summary>
    public AtlasIndex Index => _index;

    /// <summary>How many pictures the atlas holds.</summary>
    public int Count => _index.Count;

    /// <summary>How many pages have been uploaded so far (a probe for the rails: a scroll over every type costs at most the page count).</summary>
    public int PagesUploaded { get; private set; }

    /// <summary>Is there a picture for <paramref name="stem"/>? Pure — no GPU.</summary>
    public bool Contains(string stem) => _index.Contains(stem);

    /// <summary>The picker key of <paramref name="stem"/>'s picture.</summary>
    public static string KeyOf(string stem) => KeyPrefix + stem;

    /// <summary>
    /// The picker cache's RESIDENT lookup: the image (page texture + the cell's UV rectangle) for an <c>atlas:entities/&lt;stem&gt;</c> key; null for any other key, a name not in the atlas, or a page that would not load.
    /// ⛔ Render thread only (it may upload a page). No allocation after the page's first upload.
    /// </summary>
    public PickerImage? TryGetImage(string key)
    {
        if (!_cells.TryGetValue(key, out var cell)) return null;
        if (cell.Image is { } ready) return ready;
        var page = PageOf(cell.Page);
        if (page is null) return null;
        var (uv0, uv1) = page.GetUvForRect(cell.X, cell.Y, cell.W, cell.H);
        var image = new PickerImage(page.TextureId, cell.W, cell.H, uv0, uv1);
        cell.Image = image;
        return image;
    }

    private IconAtlas? PageOf(int number)
    {
        if (_pages[number] is { } done) return done;
        if (_pageFailed[number]) return null;
        (IntPtr tex, int w, int h)? up = null;
        try { up = _uploadPage(_index.Pages[number]); }
        catch (Exception) { /* a page that will not load must never stop a picker */ }
        if (up is not { } u || u.w <= 0 || u.h <= 0) { _pageFailed[number] = true; return null; }
        PagesUploaded++;
        return _pages[number] = new IconAtlas(u.tex, u.w, u.h, _index.CellW);
    }
}
