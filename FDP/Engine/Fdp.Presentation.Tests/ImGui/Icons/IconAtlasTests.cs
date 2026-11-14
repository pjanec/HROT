using System;
using System.Numerics;
using Fdp.Presentation.Icons;
using Xunit;

namespace Fdp.Presentation.Tests.Icons;

/// <summary>
/// Unit tests for <see cref="IconAtlas"/> — WM-S101 success conditions.
/// No ImGui context is required; all tests operate on pure UV math and disposal logic.
/// </summary>
public class IconAtlasTests
{
    /// <summary>Creates a standard 256×256 atlas with 16px icons and a dummy texture handle.</summary>
    private static IconAtlas CreateAtlas(
        float atlasWidth = 256f,
        float atlasHeight = 256f,
        float iconSize = 16f)
        => new IconAtlas(new IntPtr(1), atlasWidth, atlasHeight, iconSize);

    // ── WM-S101 condition 1: Row parsing ─────────────────────────────────────

    [Fact]
    public void GetUvCoordinates_RowA_ReturnsZeroY()
    {
        using var atlas = CreateAtlas();
        var (uv0, _) = atlas.GetUvCoordinates("a1");
        Assert.Equal(0f, uv0.Y);
    }

    [Fact]
    public void GetUvCoordinates_RowB_ReturnsIconSizeOverAtlasHeightY()
    {
        using var atlas = CreateAtlas(256f, 256f, 16f);
        var (uv0, _) = atlas.GetUvCoordinates("b1");
        Assert.Equal(16f / 256f, uv0.Y, precision: 5);
    }

    // ── WM-S101 condition 2: Column parsing ───────────────────────────────────

    [Fact]
    public void GetUvCoordinates_Column1_ReturnsZeroX()
    {
        using var atlas = CreateAtlas();
        var (uv0, _) = atlas.GetUvCoordinates("a1");
        Assert.Equal(0f, uv0.X);
    }

    [Fact]
    public void GetUvCoordinates_Column2_ReturnsIconSizeOverAtlasWidthX()
    {
        using var atlas = CreateAtlas(256f, 256f, 16f);
        var (uv0, _) = atlas.GetUvCoordinates("a2");
        Assert.Equal(16f / 256f, uv0.X, precision: 5);
    }

    // ── WM-S101 condition 3: 1-based column index ─────────────────────────────

    [Fact]
    public void GetUvCoordinates_Column1_IndexIsZero_XIsZero()
    {
        using var atlas = CreateAtlas();
        var (uv0, _) = atlas.GetUvCoordinates("a1");
        Assert.Equal(0f, uv0.X);
    }

    [Fact]
    public void GetUvCoordinates_Column12_IndexIs11()
    {
        using var atlas = CreateAtlas(256f, 256f, 16f);
        var (uv0, _) = atlas.GetUvCoordinates("a12");
        Assert.Equal(11f * 16f / 256f, uv0.X, precision: 5);
    }

    // ── WM-S101 condition 4: Case-insensitive row ─────────────────────────────

    [Fact]
    public void GetUvCoordinates_UpperAndLowerCaseRow_ReturnSameUVs()
    {
        using var atlas = CreateAtlas();
        var (uv0Upper, uv1Upper) = atlas.GetUvCoordinates("B12");
        var (uv0Lower, uv1Lower) = atlas.GetUvCoordinates("b12");
        Assert.Equal(uv0Upper, uv0Lower);
        Assert.Equal(uv1Upper, uv1Lower);
    }

    // ── WM-S101 condition 5: UV1 offset equals cell size ─────────────────────

    [Fact]
    public void GetUvCoordinates_UV1MinusUV0_EqualsIconSizeOverAtlasDimensions()
    {
        using var atlas = CreateAtlas(256f, 256f, 16f);
        var (uv0, uv1) = atlas.GetUvCoordinates("b5");
        var diff = uv1 - uv0;
        Assert.Equal(16f / 256f, diff.X, precision: 5);
        Assert.Equal(16f / 256f, diff.Y, precision: 5);
    }

    // ── WM-S101 condition 6: Malformed — empty string ─────────────────────────

    [Fact]
    public void GetUvCoordinates_EmptyString_ReturnsFallback()
    {
        using var atlas = CreateAtlas();
        var (uv0, uv1) = atlas.GetUvCoordinates(string.Empty);
        Assert.Equal(Vector2.Zero, uv0);
        Assert.Equal(Vector2.One, uv1);
    }

    // ── WM-S101 condition 7: Malformed — no numeric part ─────────────────────

    [Fact]
    public void GetUvCoordinates_NoNumericPart_ReturnsFallback()
    {
        using var atlas = CreateAtlas();
        var (uv0, uv1) = atlas.GetUvCoordinates("a");
        Assert.Equal(Vector2.Zero, uv0);
        Assert.Equal(Vector2.One, uv1);
    }

    // ── WM-S101 condition 8: Malformed — null ────────────────────────────────

    [Fact]
    public void GetUvCoordinates_Null_ReturnsFallback()
    {
        using var atlas = CreateAtlas();
        var (uv0, uv1) = atlas.GetUvCoordinates(null!);
        Assert.Equal(Vector2.Zero, uv0);
        Assert.Equal(Vector2.One, uv1);
    }

    // ── WM-S101 condition 9: Double-Dispose safety ───────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var atlas = CreateAtlas();
        atlas.Dispose();
        var ex = Record.Exception(() => atlas.Dispose());
        Assert.Null(ex);
    }

    // ── WM-S101 condition 10: TextureId non-zero after construction ───────────

    [Fact]
    public void TextureId_AfterConstruction_IsNotZero()
    {
        using var atlas = new IconAtlas(new IntPtr(42), 256f, 256f, 16f);
        Assert.NotEqual(IntPtr.Zero, atlas.TextureId);
    }

    // ── ⭐ CE-4271 (docs/DESIGN_Add_Entity_Picker.md §2j) — a RECTANGLE of a page, and the pure index of a fixed-cell atlas ─────────────────

    /// <summary>🔴 Red-proof (compiles): the V of the rectangle taken from its X (or the far corner left at the near one) ⇒ red.</summary>
    [Fact]
    public void CE4271_GetUvForRect_IsThePixelRectangleOverThePage()
    {
        using var atlas = CreateAtlas(2048f, 2048f, 320f);
        var (uv0, uv1) = atlas.GetUvForRect(320f, 360f, 320f, 180f);          // the cell at col 1, row 2 of a 320 x 180 grid
        Assert.Equal(new Vector2(320f / 2048f, 360f / 2048f), uv0);
        Assert.Equal(new Vector2(640f / 2048f, 540f / 2048f), uv1);
        Assert.Equal((Vector2.Zero, Vector2.One), atlas.GetUvForRect(0f, 0f, 0f, 10f));   // a degenerate rectangle is the whole page
    }

    private const string Index = """
        { "version": 1, "cell": [320, 180], "page": [2048, 2048], "pages": ["entities-0.jpg", "entities-1.jpg"],
          "items": { "Rifleman": [0, 0, 0], "T-72": [0, 1, 0], "BoxTruck": [1, 5, 10] } }
        """;

    [Fact]
    public void CE4271_AtlasIndex_AnswersWhereANamesCellIs_AndWhatItDoesNotHold()
    {
        var index = AtlasIndex.Parse(Index);
        Assert.Equal((320, 180, 2048, 2048, 6, 11), (index.CellW, index.CellH, index.PageW, index.PageH, index.Columns, index.Rows));
        Assert.Equal(new[] { "entities-0.jpg", "entities-1.jpg" }, index.Pages);
        Assert.Equal(3, index.Count);
        Assert.True(index.TryGet("BoxTruck", out int page, out int x, out int y, out int w, out int h));
        Assert.Equal((1, 5 * 320, 10 * 180, 320, 180), (page, x, y, w, h));
        Assert.True(index.Contains("T-72"));
        Assert.False(index.Contains("Tank"));
        Assert.False(index.TryGet("Tank", out _, out _, out _, out _, out _));
        Assert.Equal(0, AtlasIndex.Empty.Count);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "version": 2, "cell": [320,180], "page": [2048,2048], "pages": [], "items": {} }""")]
    [InlineData("""{ "version": 1, "cell": [320], "page": [2048,2048], "pages": [], "items": {} }""")]
    [InlineData("""{ "version": 1, "cell": [4000,180], "page": [2048,2048], "pages": [], "items": {} }""")]
    [InlineData("""{ "version": 1, "cell": [320,180], "page": [2048,2048], "pages": ["a.jpg"], "items": { "X": [1, 0, 0] } }""")]       // page 1 does not exist
    [InlineData("""{ "version": 1, "cell": [320,180], "page": [2048,2048], "pages": ["a.jpg"], "items": { "X": [0, 6, 0] } }""")]       // col 6 is off the page
    [InlineData("""{ "version": 1, "cell": [320,180], "page": [2048,2048], "pages": ["a.jpg"], "items": { "X": [0, 0, 0], "Y": [0, 0, 0] } }""")]   // two names, one cell
    [InlineData("""{ "version": 1, "cell": [320,180], "page": [2048,2048], "pages": ["a.jpg"] }""")]
    public void CE4271_AtlasIndex_RefusesAMalformedIndex(string json)
        => Assert.Throws<FormatException>(() => AtlasIndex.Parse(json));
}
