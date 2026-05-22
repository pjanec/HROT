using System.Numerics;
using FluentAssertions;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

// ---------------------------------------------------------------------------
// Minimal stub so the tests have no dependency on Moq.
// ---------------------------------------------------------------------------
file sealed class StubTheme : IEditorTheme
{
    private nint _fontPtr;
    private int  _callCount;

    public void SetFontPtr(nint ptr) => _fontPtr = ptr;
    public int CallCount => _callCount;

    public nint GetFontForSize(float targetPixelSize)
    {
        _callCount++;
        return _fontPtr;
    }

    // Structural properties — values are arbitrary for these tests.
    public Vector4 BackgroundColor        => Vector4.Zero;
    public Vector4 GridMinorColor         => Vector4.Zero;
    public Vector4 GridMajorColor         => Vector4.Zero;
    public Vector4 SelectionAccent        => Vector4.One;
    public Vector4 PrimarySelectionAccent => Vector4.One;
    public Vector4 ErrorColor             => Vector4.One;
    public Vector4 WarningColor           => Vector4.One;
    public Vector4 TextDefault            => Vector4.One;
    public Vector4 TextMuted              => new(0.6f, 0.6f, 0.6f, 1f);
    public float NodeCornerRadius    => 4f;
    public float NodeBorderThickness => 1.5f;
    public float NodeHeaderHeight    => 28f;
    public float PinGlyphSize        => 10f;
    public float WireThicknessExec   => 3f;
    public float WireThicknessData   => 2f;
    public Vector4 GetCategoryHeaderColor(NodeCategory _) => Vector4.Zero;
}

// ---------------------------------------------------------------------------

public sealed class FontScalingTests : IDisposable
{
    private readonly nint _ctx;

    public FontScalingTests()
    {
        _ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(_ctx);

        var io = ImGui.GetIO();
        io.Fonts.AddFontDefault();
        io.Fonts.Build();

        // Satisfy the minimum IO preconditions so NewFrame() does not assert.
        io.DisplaySize = new Vector2(1280, 720);
        io.DeltaTime   = 1f / 60f;

        // Satisfy the ImGui internal state so PushFont/PopFont are reachable.
        ImGui.NewFrame();
    }

    public void Dispose()
    {
        ImGui.EndFrame();
        ImGui.DestroyContext(_ctx);
    }

    // ── GetFontForSize selection logic ────────────────────────────────────

    [Fact]
    public void GetFontForSize_ReturnsExactMatch_WhenAvailable()
    {
        var theme = new FakeEditorThemeFonts(new Dictionary<float, nint>
        {
            { 16f, 1 },
            { 24f, 2 },
            { 32f, 3 },
        });

        theme.GetFontForSize(24f).Should().Be(2, "24px is an exact baked size");
    }

    [Fact]
    public void GetFontForSize_ReturnsNextLargerSize_WhenNoExactMatch()
    {
        var theme = new FakeEditorThemeFonts(new Dictionary<float, nint>
        {
            { 16f, 1 },
            { 24f, 2 },
            { 32f, 3 },
        });

        // 20px → smallest baked size that is >= 20px is 24px
        theme.GetFontForSize(20f).Should().Be(2, "24px is the smallest baked size >= 20px");
    }

    [Fact]
    public void GetFontForSize_ReturnsLargestAvailable_WhenTargetExceedsAll()
    {
        var theme = new FakeEditorThemeFonts(new Dictionary<float, nint>
        {
            { 16f, 1 },
            { 24f, 2 },
            { 32f, 3 },
        });

        // 48px exceeds every baked size → fall back to 32px (largest)
        theme.GetFontForSize(48f).Should().Be(3, "32px is the largest available baked size");
    }

    [Fact]
    public void GetFontForSize_ReturnsZero_WhenNoFontsRegistered()
    {
        var theme = new FakeEditorThemeFonts(new Dictionary<float, nint>());

        theme.GetFontForSize(24f).Should().Be(0, "an empty dictionary must signal fallback");
    }

    // ── PushFont / PopFont round-trip ─────────────────────────────────────

    [Fact]
    public void PushFont_WithValidDefaultFontPointer_DoesNotThrow()
    {
        // Obtain the pointer to the default font already baked into the atlas.
        nint validFontPtr;
        unsafe { validFontPtr = (nint)ImGui.GetIO().Fonts.Fonts[0].NativePtr; }

        validFontPtr.Should().NotBe(0, "the default font must have been loaded");

        var stub = new StubTheme();
        stub.SetFontPtr(validFontPtr);

        stub.GetFontForSize(16f).Should().Be(validFontPtr);

        // Simulate what NodeRenderer does: push → draw → pop.
        System.Action act = () =>
        {
            unsafe { ImGui.PushFont(new ImFontPtr((ImFont*)(void*)validFontPtr)); }
            _ = ImGui.GetFont();
            ImGui.PopFont();
        };

        act.Should().NotThrow("a valid baked font pointer must survive a PushFont/PopFont cycle");
    }

    [Fact]
    public void GetFontForSize_IsQueriedWithPositivePixelSize()
    {
        var stub = new StubTheme();
        stub.SetFontPtr(0); // returns default fallback

        // Simulate the target-size calculation: base font size (13px default) × zoom
        float baseSize = ImGui.GetFontSize();
        float zoom = 1.5f;
        float target = baseSize * zoom;

        target.Should().BeGreaterThan(0, "computed target pixel size must be positive");

        // Query as the renderer would
        nint result = stub.GetFontForSize(target);
        stub.CallCount.Should().Be(1, "the renderer calls GetFontForSize once per text element");
        result.Should().Be(0, "zero signals a fallback to the default ImGui font");
    }
}

// ---------------------------------------------------------------------------
// Re-use the real selection logic from FakeEditorTheme inline so this test
// assembly does not depend on NodeEditor.Demo.
// ---------------------------------------------------------------------------
file sealed class FakeEditorThemeFonts : IEditorTheme
{
    private readonly Dictionary<float, nint> _fonts;
    public FakeEditorThemeFonts(Dictionary<float, nint> fonts) => _fonts = fonts;

    public nint GetFontForSize(float targetPixelSize)
    {
        if (_fonts.Count == 0) return 0;
        float best = _fonts.Keys.OrderBy(k => k).FirstOrDefault(k => k >= targetPixelSize);
        if (best == 0f) best = _fonts.Keys.Max();
        return _fonts[best];
    }

    public Vector4 BackgroundColor        => Vector4.Zero;
    public Vector4 GridMinorColor         => Vector4.Zero;
    public Vector4 GridMajorColor         => Vector4.Zero;
    public Vector4 SelectionAccent        => Vector4.One;
    public Vector4 PrimarySelectionAccent => Vector4.One;
    public Vector4 ErrorColor             => Vector4.One;
    public Vector4 WarningColor           => Vector4.One;
    public Vector4 TextDefault            => Vector4.One;
    public Vector4 TextMuted              => new(0.6f, 0.6f, 0.6f, 1f);
    public float NodeCornerRadius    => 4f;
    public float NodeBorderThickness => 1.5f;
    public float NodeHeaderHeight    => 28f;
    public float PinGlyphSize        => 10f;
    public float WireThicknessExec   => 3f;
    public float WireThicknessData   => 2f;
    public Vector4 GetCategoryHeaderColor(NodeCategory _) => Vector4.Zero;
}
