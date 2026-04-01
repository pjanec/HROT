using System.Numerics;

namespace FDP.Toolkit.ImGui.Icons;

/// <summary>
/// Wraps a texture-atlas sprite sheet and provides UV coordinate lookup for icon cells.
/// Icons are addressed by a string coordinate such as <c>"b12"</c>:
/// the letter identifies the row ('a'=0, 'b'=1, …) and the number (1-based) identifies the column.
/// </summary>
/// <remarks>
/// Design note: <c>Raylib_cs</c> is not referenced by <c>FDP.Toolkit.ImGui</c>.
/// The caller (integration layer) is responsible for loading the texture and supplying the GPU
/// handle as <paramref name="textureId"/>. This makes the class GPU-framework-agnostic and
/// fully testable without a GPU context.
/// </remarks>
public class IconAtlas : IDisposable
{
    private readonly float _atlasWidth;
    private readonly float _atlasHeight;
    private readonly float _iconSize;
    private bool _disposed;

    /// <summary>The GPU texture handle (framework-agnostic opaque pointer).</summary>
    public IntPtr TextureId { get; }

    /// <summary>Icon cell size as a <see cref="Vector2"/> for convenient passing to ImGui APIs.</summary>
    public Vector2 IconSizeVec { get; }

    /// <summary>
    /// Creates an <see cref="IconAtlas"/> from a pre-loaded texture handle.
    /// No GPU calls are made inside this constructor.
    /// </summary>
    /// <param name="textureId">Opaque GPU texture handle provided by the caller.</param>
    /// <param name="atlasWidth">Total atlas texture width in pixels.</param>
    /// <param name="atlasHeight">Total atlas texture height in pixels.</param>
    /// <param name="iconSize">Width and height of a single icon cell in pixels (square cells assumed).</param>
    public IconAtlas(IntPtr textureId, float atlasWidth, float atlasHeight, float iconSize = 16f)
    {
        TextureId = textureId;
        _atlasWidth = atlasWidth;
        _atlasHeight = atlasHeight;
        _iconSize = iconSize;
        IconSizeVec = new Vector2(iconSize, iconSize);
    }

    /// <summary>
    /// Parses a string coordinate (e.g. <c>"b12"</c>) and returns the UV pair
    /// (<c>uv0</c> top-left, <c>uv1</c> bottom-right) for that icon cell.
    /// Returns <c>(Vector2.Zero, Vector2.One)</c> for any malformed or null input.
    /// </summary>
    public (Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coordinate)
    {
        if (string.IsNullOrEmpty(coordinate) || coordinate.Length < 2)
            return (Vector2.Zero, Vector2.One);

        var rowChar = char.ToLowerInvariant(coordinate[0]);
        if (rowChar < 'a' || rowChar > 'z')
            return (Vector2.Zero, Vector2.One);

        var numericPart = coordinate.Substring(1);
        if (!int.TryParse(numericPart, out var column) || column < 1)
            return (Vector2.Zero, Vector2.One);

        int rowIndex = rowChar - 'a';
        int colIndex = column - 1; // 1-based input → 0-based index

        var uv0 = new Vector2(
            colIndex * _iconSize / _atlasWidth,
            rowIndex * _iconSize / _atlasHeight);
        var uv1 = uv0 + new Vector2(_iconSize / _atlasWidth, _iconSize / _atlasHeight);

        return (uv0, uv1);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // No GPU resources to unload: Raylib_cs is not referenced.
        // The integration caller owns and manages the texture lifetime.
    }
}
