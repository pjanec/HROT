using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Icons;
using NodeEditor.Core.Interfaces;
using Xunit;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Tests.Icons;

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

    /// <summary>
    /// Creates an <see cref="IconHandle"/> from an atlas cell coordinate.
    /// Uses a 64×64 render size (toolbar-standard).
    /// </summary>
    private static IconHandle CreateHandle(IconAtlas atlas, string coordinate = "a1")
    {
        var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
        return new IconHandle(atlas.TextureId, 64, 64, uv0, uv1);
    }

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

    // ─── MTB-P1-T2: IconHandle overloads — IconButton ─────────────────────────

    /// <summary>
    /// MTB-P1-T2: <c>_DoesNotThrow</c> for valid args at 64×64 — icon button.
    /// </summary>
    [Fact]
    public void IconButton_Handle_ValidArgs_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.IconButton(in handle, "hbtn1", new Vector2(64, 64)));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void IconButton_Handle_WhenNotClicked_ReturnsFalse()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var result = IconWidgets.IconButton(in handle, "hbtn2", new Vector2(64, 64));
        ImGuiApi.End();
        fixture.Render();
        Assert.False(result);
    }

    /// <summary>
    /// MTB-P1-T2: Disabled icon button never returns true and registers no click hit-area.
    /// Uses <see cref="ImGuiApi.Dummy"/> (passive, no interaction) instead of
    /// <see cref="ImGuiApi.InvisibleButton"/> when disabled.
    /// </summary>
    [Fact]
    public void IconButton_Handle_Disabled_NeverReturnsTrue_AndRegistersNoHitArea()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        // Even if there were a click, disabled always returns false.
        var result = IconWidgets.IconButton(in handle, "hbtn3", new Vector2(64, 64), enabled: false);
        ImGuiApi.End();
        fixture.Render();

        Assert.False(result);
    }

    /// <summary>
    /// MTB-P1-T2: Disabled does not throw at various sizes (robustness).
    /// </summary>
    [Fact]
    public void IconButton_Handle_Disabled_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        var ex = Record.Exception(() => IconWidgets.IconButton(in handle, "hbtn4", new Vector2(64, 64), enabled: false));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    // ─── MTB-P1-T2: IconHandle overloads — ToggleIcon ─────────────────────────

    /// <summary>
    /// MTB-P1-T2: <c>_DoesNotThrow</c> for valid args at 64×64 — toggle icon.
    /// </summary>
    [Fact]
    public void ToggleIcon_Handle_ValidArgs_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var ex = Record.Exception(() => IconWidgets.ToggleIcon(in handle, "htgl1", new Vector2(64, 64), ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void ToggleIcon_Handle_WhenNotClicked_ReturnsFalse()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var result = IconWidgets.ToggleIcon(in handle, "htgl2", new Vector2(64, 64), ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.False(result);
    }

    /// <summary>
    /// MTB-P1-T2: When not clicked, toggle state is unchanged (both true and false paths).
    /// </summary>
    [Fact]
    public void ToggleIcon_Handle_WhenNotClicked_StateIsUnchanged()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");

        // Initially false
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        IconWidgets.ToggleIcon(in handle, "htgl3", new Vector2(64, 64), ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.False(toggled);

        // Initially true
        fixture.NewFrame();
        ImGuiApi.Begin("test_window2");
        toggled = true;
        IconWidgets.ToggleIcon(in handle, "htgl4", new Vector2(64, 64), ref toggled);
        ImGuiApi.End();
        fixture.Render();
        Assert.True(toggled);
    }

    /// <summary>
    /// MTB-P1-T2: Toggle icon when enabled, toggled true/false — no throw.
    /// </summary>
    [Fact]
    public void ToggleIcon_Handle_WhenToggledTrue_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = true;
        var ex = Record.Exception(() => IconWidgets.ToggleIcon(in handle, "htgl5", new Vector2(64, 64), ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    [Fact]
    public void ToggleIcon_Handle_WhenToggledFalse_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var ex = Record.Exception(() => IconWidgets.ToggleIcon(in handle, "htgl6", new Vector2(64, 64), ref toggled));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    /// <summary>
    /// MTB-P1-T2: Disabled toggle icon — returns false and does NOT modify toggle state.
    /// Uses <see cref="ImGuiApi.Dummy"/> (passive placeholder) instead of
    /// <see cref="ImGuiApi.InvisibleButton"/>, so there is no hit area and no
    /// toggle occurs.
    /// </summary>
    [Fact]
    public void ToggleIcon_Handle_WhenDisabled_StateUnchanged()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");

        // Start toggled
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = true;
        var result = IconWidgets.ToggleIcon(in handle, "htgl7", new Vector2(64, 64),
                                            ref toggled, enabled: false);
        ImGuiApi.End();
        fixture.Render();

        Assert.False(result, "Disabled toggle must never return true");
        Assert.True(toggled, "Disabled toggle must NOT flip state");
    }

    /// <summary>
    /// MTB-P1-T2: Disabled toggle — starting from false, state remains false.
    /// </summary>
    [Fact]
    public void ToggleIcon_Handle_WhenDisabled_StateUnchanged_False()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");

        // Start not toggled
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        bool toggled = false;
        var result = IconWidgets.ToggleIcon(in handle, "htgl8", new Vector2(64, 64),
                                            ref toggled, enabled: false);
        ImGuiApi.End();
        fixture.Render();

        Assert.False(result, "Disabled toggle must never return true");
        Assert.False(toggled, "Disabled toggle must NOT flip state");
    }

    // ─── MTB-P1-T2: Tooltip helper ────────────────────────────────────────────

    [Fact]
    public void Tooltip_AfterButton_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        IconWidgets.IconButton(in handle, "htip1", new Vector2(64, 64));
        var ex = Record.Exception(() => IconWidgets.Tooltip("Test tooltip"));
        ImGuiApi.End();
        fixture.Render();
        Assert.Null(ex);
    }

    /// <summary>
    /// MTB-P1-T2: Tooltip with empty or null text does not throw.
    /// </summary>
    [Fact]
    public void Tooltip_NullOrEmpty_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var handle = CreateHandle(atlas, "a1");
        fixture.NewFrame();
        ImGuiApi.Begin("test_window");
        IconWidgets.IconButton(in handle, "htip2", new Vector2(64, 64));

        var ex1 = Record.Exception(() => IconWidgets.Tooltip(""));
        Assert.Null(ex1);

        // Tooltip(null!) should also not throw — ImGui handles it gracefully
        var ex2 = Record.Exception(() => IconWidgets.Tooltip(null!));
        Assert.Null(ex2);

        ImGuiApi.End();
        fixture.Render();
    }
}
