using System.Numerics;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ C1 (docs/UX/DESIGN_UI_Conventions.md §5) — the icon of a tree row or a section header: EITHER a Silk-atlas image (texture + uv rectangle) OR a glyph string
/// with its colour, or nothing. A small readonly struct: no allocation to build or pass (the glyph string is the caller's, usually a constant).
/// </summary>
public readonly struct RowIcon
{
    /// <summary>The GPU texture of an image icon (0 for a glyph or no icon).</summary>
    public nint Texture { get; }
    public Vector2 Uv0 { get; }
    public Vector2 Uv1 { get; }
    /// <summary>The glyph of a glyph icon (null for an image or no icon).</summary>
    public string? Glyph { get; }
    /// <summary>The glyph's colour.</summary>
    public Vector4 Color { get; }
    /// <summary>⭐ CE-8611 — an image icon's width over its height (a NATO unit symbol's frame is 1.5 : 1); 1 = the usual square.</summary>
    public float Aspect { get; }

    private RowIcon(nint texture, Vector2 uv0, Vector2 uv1, string? glyph, Vector4 color, float aspect = 1f)
    { Texture = texture; Uv0 = uv0; Uv1 = uv1; Glyph = glyph; Color = color; Aspect = aspect > 0f ? aspect : 1f; }

    public static RowIcon None => default;
    public bool IsNone => Texture == 0 && Glyph is null;
    public bool IsImage => Texture != 0;
    public bool IsGlyph => Glyph is not null;

    /// <summary>A Silk image: a texture and the uv rectangle of the icon in it.</summary>
    public static RowIcon Image(nint texture, Vector2 uv0, Vector2 uv1) => new(texture, uv0, uv1, null, Vector4.One);

    /// <summary>⭐ CE-8611 — an image icon wider (or narrower) than the row's square: drawn the row's height tall and <paramref name="aspect"/> × that wide.</summary>
    public static RowIcon Image(nint texture, Vector2 uv0, Vector2 uv1, float aspect) => new(texture, uv0, uv1, null, Vector4.One, aspect);
    /// <summary>A Silk image from an <see cref="IIconProvider"/> handle.</summary>
    public static RowIcon Image(in IconHandle handle) => new(handle.TextureId, handle.Uv0, handle.Uv1, null, Vector4.One);
    /// <summary>A glyph (a FontAwesome code point string) in a colour.</summary>
    public static RowIcon OfGlyph(string glyph, Vector4 color) => new(0, Vector2.Zero, Vector2.Zero, glyph, color);
    /// <summary>A glyph and its palette colour as one value (<see cref="IconGlyph"/>).</summary>
    public static RowIcon OfGlyph(in IconGlyph glyph) => new(0, Vector2.Zero, Vector2.Zero, glyph.Glyph, glyph.Color);

    public static implicit operator RowIcon(IconGlyph glyph) => OfGlyph(glyph);
}
