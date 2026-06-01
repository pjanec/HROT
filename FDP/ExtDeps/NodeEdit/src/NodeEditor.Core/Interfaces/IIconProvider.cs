using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>Lookup for icons by string key. The host or theme provides icons.</summary>
public interface IIconProvider
{
    /// <summary>Try to resolve an icon key to a renderable handle. Returns false if unknown.</summary>
    bool TryGet(string key, out IconHandle handle);
}

/// <summary>
/// Opaque handle to a renderable icon. Implementation defined by host.
/// <para>
/// <see cref="Uv0"/> and <see cref="Uv1"/> address a sub-rect of the texture atlas.
/// Defaults to <c>(0,0)</c>–<c>(1,1)</c> covering the whole texture so that
/// existing whole-texture constructions are backwards-compatible.
/// </para>
/// </summary>
public readonly struct IconHandle : System.IEquatable<IconHandle>
{
    /// <summary>The GPU texture handle (opaque; framework-agnostic).</summary>
    public nint    TextureId { get; }

    /// <summary>Icon width in pixels.</summary>
    public uint    Width     { get; }

    /// <summary>Icon height in pixels.</summary>
    public uint    Height    { get; }

    /// <summary>Top-left UV coordinate within the atlas. Default: <c>(0, 0)</c>.</summary>
    public Vector2 Uv0       { get; }

    /// <summary>Bottom-right UV coordinate within the atlas. Default: <c>(1, 1)</c>.</summary>
    public Vector2 Uv1       { get; }

    /// <summary>
    /// Full constructor — addresses a sub-rect of a texture atlas via UV coordinates.
    /// </summary>
    public IconHandle(nint textureId, uint width, uint height, Vector2 uv0, Vector2 uv1)
    {
        TextureId = textureId;
        Width     = width;
        Height    = height;
        Uv0       = uv0;
        Uv1       = uv1;
    }

    /// <summary>
    /// Whole-texture constructor. <see cref="Uv0"/> = (0,0), <see cref="Uv1"/> = (1,1).
    /// Existing code that only cares about the texture handle uses this form.
    /// </summary>
    public IconHandle(nint textureId, uint width, uint height)
        : this(textureId, width, height, Vector2.Zero, Vector2.One) { }

    // Equality (needed for record-like semantics in tests)
    public bool Equals(IconHandle other) =>
        TextureId == other.TextureId && Width == other.Width && Height == other.Height
        && Uv0 == other.Uv0 && Uv1 == other.Uv1;

    public override bool Equals(object? obj) => obj is IconHandle h && Equals(h);
    public override int GetHashCode() =>
        System.HashCode.Combine(TextureId, Width, Height, Uv0, Uv1);

    public static bool operator ==(IconHandle left, IconHandle right) => left.Equals(right);
    public static bool operator !=(IconHandle left, IconHandle right) => !left.Equals(right);
}
