using System.Numerics;

namespace Fdp.Presentation.Icons;

/// <summary>
/// Wraps a texture-atlas sprite sheet and provides UV coordinate lookup for icon cells.
/// Icons are addressed by a string coordinate such as <c>"b12"</c>:
/// the letter identifies the row ('a'=0, 'b'=1, …) and the number (1-based) identifies the column.
/// </summary>
/// <remarks>
/// Design note: <c>Raylib_cs</c> is not referenced by <c>FDP.Toolkit_ImGui</c>.
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
    /// Parses a string coordinate (e.g. <c>"b12"</c> or "af32") and returns the UV pair
    /// (<c>uv0</c> top-left, <c>uv1</c> bottom-right) for that icon cell.
    /// Returns <c>(Vector2.Zero, Vector2.One)</c> for any malformed or null input.
    /// </summary>
    public (Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coordinate)
    {
        if (string.IsNullOrEmpty(coordinate) || coordinate.Length < 2)
            return (Vector2.Zero, Vector2.One);

        // 1. Separate the alphabetic row prefix from the numeric column suffix
        int splitIdx = 0;
        while (splitIdx < coordinate.Length && char.IsLetter(coordinate[splitIdx]))
        {
            splitIdx++;
        }

        // Must have at least one letter and at least one number
        if (splitIdx == 0 || splitIdx == coordinate.Length)
            return (Vector2.Zero, Vector2.One);

        string rowStr = coordinate.Substring(0, splitIdx).ToLowerInvariant();
        string numericPart = coordinate.Substring(splitIdx);

        if (!int.TryParse(numericPart, out var column) || column < 1)
            return (Vector2.Zero, Vector2.One);

        // 2. Decode the base-26 alphabetic string to a 0-based row index
        // 'a' -> 0, 'z' -> 25, 'aa' -> 26, 'af' -> 31
        int rowIndex = 0;
        for (int i = 0; i < rowStr.Length; i++)
        {
            char c = rowStr[i];
            if (c < 'a' || c > 'z')
                return (Vector2.Zero, Vector2.One);
            
            rowIndex *= 26;
            rowIndex += (c - 'a' + 1);
        }
        rowIndex -= 1; // Shift to 0-based index

        // 3. Compute existing UV coordinates using the parsed dynamic rowIndex
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
