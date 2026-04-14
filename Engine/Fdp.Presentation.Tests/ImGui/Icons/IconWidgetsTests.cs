using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.ImGui.Icons;
using Xunit;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Toolkit.ImGui.Tests.Icons;

/// <summary>
/// Integration tests for <see cref="IconWidgets"/> — WM-S102 through WM-S105.
/// All tests run inside a headless ImGui context provided by <see cref="ImGuiTestFixture"/>.
/// Methods that access the window draw list are called inside a Begin/End block.
/// </summary>
[Collection("ImGui Sequential")]
public class IconWidgetsTests
{
    private static IconAtlas CreateAtlas() =>
        new IconAtlas(new IntPtr(1), 256f, 256f, 16f);

    // ─── WM-S102: InlineIcon ──────────────────────────────────────────────────

    [Fact]
    public void InlineIcon_ValidCoordinate_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.InlineIcon(atlas, "a1"));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void InlineIcon_NullCoordinate_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.InlineIcon(atlas, null!));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void InlineIcon_EmptyCoordinate_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.InlineIcon(atlas, string.Empty));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    // ─── WM-S102: AbsoluteIcon ────────────────────────────────────────────────

    [Fact]
    public void AbsoluteIcon_ValidCoordinate_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.AbsoluteIcon(atlas, "a1", new Vector2(100f, 100f)));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void AbsoluteIcon_NullCoordinate_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.AbsoluteIcon(atlas, null!, new Vector2(100f, 100f)));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    // ─── WM-S103: IconButton ─────────────────────────────────────────────────

    [Fact]
    public void IconButton_ValidArgs_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.IconButton(atlas, "btn1", "a1"));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void IconButton_WhenNotClicked_ReturnsFalse()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var result = IconWidgets.IconButton(atlas, "btn2", "a1");
        ImGuiApi.End();
        fixture.Render();
        Assert.False(result);
    }

    // ─── WM-S103: ToggleIcon ─────────────────────────────────────────────────

    [Fact]
    public void ToggleIcon_WhenNotClicked_ReturnsFalse()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var result = IconWidgets.ToggleIcon(atlas, "tgl1", "a1", ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.False(result);
    }

    [Fact]
    public void ToggleIcon_WhenNotClicked_StateIsUnchanged()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = true;
        IconWidgets.ToggleIcon(atlas, "tgl2", "a1", ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.True(toggled); // No click occurred, so isToggled must remain true
    }

    [Fact]
    public void ToggleIcon_WhenToggledTrue_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = true;
        var ex = Record.Exception(() => IconWidgets.ToggleIcon(atlas, "tgl3", "a1", ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void ToggleIcon_WhenToggledFalse_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var ex = Record.Exception(() => IconWidgets.ToggleIcon(atlas, "tgl4", "a1", ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    // ─── WM-S104: AlternatingFaceToggleIcon ───────────────────────────────────

    [Fact]
    public void AlternatingFaceToggleIcon_WhenToggledTrue_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = true;
        var ex = Record.Exception(() =>
            IconWidgets.AlternatingFaceToggleIcon(atlas, "aft1", "a1", "b1", ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void AlternatingFaceToggleIcon_WhenToggledFalse_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var ex = Record.Exception(() =>
            IconWidgets.AlternatingFaceToggleIcon(atlas, "aft2", "a1", "b1", ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void AlternatingFaceToggleIcon_WhenNotClicked_ReturnsFalse()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var result = IconWidgets.AlternatingFaceToggleIcon(atlas, "aft3", "a1", "b1", ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.False(result);
    }

    [Fact]
    public void AlternatingFaceToggleIcon_WhenNotClicked_StateIsUnchanged()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = true;
        IconWidgets.AlternatingFaceToggleIcon(atlas, "aft4", "a1", "b1", ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.True(toggled); // No click, state must be unchanged
    }

    [Fact]
    public void AlternatingFaceToggleIcon_DifferentCoordinates_UseDifferentUVs()
    {
        // Verify atlas returns distinct UV pairs for the two face coordinates —
        // confirming atlas lookup is coordinate-dependent.
        using var atlas = CreateAtlas();
        var (uv0_true, _) = atlas.GetUvCoordinates("a1");
        var (uv0_false, _) = atlas.GetUvCoordinates("b1");
        Assert.NotEqual(uv0_true, uv0_false);
    }

    // ─── WM-S105: DropdownFaceIcon ────────────────────────────────────────────

    [Fact]
    public void DropdownFaceIcon_ValidArgs_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        IReadOnlyList<string> coords = new[] { "a1", "a2", "b1", "b2" };
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        int selectedIndex = 0;
        var ex = Record.Exception(() => IconWidgets.DropdownFaceIcon(atlas, "ddi1", coords, ref selectedIndex));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void DropdownFaceIcon_WhenNotClicked_ReturnsFalse()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        IReadOnlyList<string> coords = new[] { "a1", "a2", "b1", "b2" };
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        int selectedIndex = 0;
        var result = IconWidgets.DropdownFaceIcon(atlas, "ddi2", coords, ref selectedIndex);
        ImGuiApi.End();
        fixture.Render();
        Assert.False(result);
    }

    [Fact]
    public void DropdownFaceIcon_OutOfBoundsNegative_ClampsToZero()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        IReadOnlyList<string> coords = new[] { "a1", "a2", "b1", "b2" };
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        int selectedIndex = -1;
        IconWidgets.DropdownFaceIcon(atlas, "ddi3", coords, ref selectedIndex);
        ImGuiApi.End();
        fixture.Render();
        Assert.Equal(0, selectedIndex);
    }

    [Fact]
    public void DropdownFaceIcon_OutOfBoundsOverCount_ClampsToZero()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        IReadOnlyList<string> coords = new[] { "a1", "a2" };
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        int selectedIndex = 99;
        IconWidgets.DropdownFaceIcon(atlas, "ddi4", coords, ref selectedIndex);
        ImGuiApi.End();
        fixture.Render();
        Assert.Equal(0, selectedIndex);
    }

    [Fact]
    public void DropdownFaceIcon_EmptyList_ReturnsFalseWithoutThrowing()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        IReadOnlyList<string> coords = Array.Empty<string>();
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        int selectedIndex = 0;
        bool result = false;
        var ex = Record.Exception(() =>
            result = IconWidgets.DropdownFaceIcon(atlas, "ddi5", coords, ref selectedIndex));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
        Assert.False(result);
    }

    [Fact]
    public void DropdownFaceIcon_ValidIndex_IsPreserved()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        IReadOnlyList<string> coords = new[] { "a1", "a2", "b1", "b2" };
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        int selectedIndex = 2; // valid, should not be clamped
        IconWidgets.DropdownFaceIcon(atlas, "ddi6", coords, ref selectedIndex);
        ImGuiApi.End();
        fixture.Render();
        Assert.Equal(2, selectedIndex);
    }
}
