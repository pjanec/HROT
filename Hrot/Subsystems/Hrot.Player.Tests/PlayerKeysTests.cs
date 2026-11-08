using System.Numerics;
using Fdp.Toolkit.Input;
using Hrot.Player;
using Hrot.Player.UI;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Player.Tests;

/// <summary>
/// ⭐⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — the Player's map keys and its debug F-key are ROWS of the keymap: the arrow keys pan (Axis, in the map scope's view), <c>=</c> / <c>+</c> / <c>−</c> zoom (Press),
/// F3 shows the performance overlay (the Global scope's debug rows) — today's keys through the keymap, and a rebind moves each.
/// ⛔ The Player's Ctrl+Z / Ctrl+Y is NOT among them: nothing pushes onto its undo stack and R-4120 says no undo in Live / Preview (design §6g, STOPPED).
/// </summary>
public sealed class PlayerKeysTests
{
    /// <summary>
    /// The arrows pan in WORLD axes (Right east, Up NORTH — CE-1040), opposite arrows cancel, and the same rows are the map scope's "view" in the Keyboard shortcuts window.
    /// 🔴 Red-proof (compiles): the Up row's scale put to −1 ⇒ the view pans south ⇒ red; <c>HandleCameraInput</c> reading the raw arrows ⇒ the rebound keys do not pan (rail below).
    /// </summary>
    [Fact]
    public void ThePanArrows_AreAxisRowsOfTheMapView_NorthUp_AndOppositesCancel()
    {
        var map = new Keymap();
        var hand = new ManualChordInput();
        var keys = PlayerKeys.Reader(map, hand);
        Assert.Empty(map.Validate());
        Assert.Equal(Vector2.Zero, PlayerKeys.Pan(keys));
        hand.Hold(EditorKey.Right);
        Assert.Equal(new Vector2(1f, 0f), PlayerKeys.Pan(keys));
        hand.Hold(EditorKey.Up);
        Assert.Equal(new Vector2(1f, 1f), PlayerKeys.Pan(keys));
        hand.Hold(EditorKey.Left);                                      // east + west: nothing
        Assert.Equal(new Vector2(0f, 1f), PlayerKeys.Pan(keys));
        hand.Release(EditorKey.Up).Hold(EditorKey.Down);
        Assert.Equal(new Vector2(0f, -1f), PlayerKeys.Pan(keys));

        // the rows live in the map scope, qualifier "view", labelled
        Assert.True(map.IsDeclared(InputScope.Map, PlayerKeys.Qualifier, PlayerKeys.PanX));
        Assert.Equal("Pan the map east / west", map.LabelOf(PlayerKeys.PanX));
    }

    /// <summary>A rebind moves the pan: the arrows no longer pan, WASD does; and one zoom action answers both the main-row and the keypad chord.</summary>
    [Fact]
    public void ARebind_MovesThePan_AndZoomHasTwoChords()
    {
        var map = new Keymap();
        var hand = new ManualChordInput();
        var keys = PlayerKeys.Reader(map, hand);
        Assert.Equal(1, map.Unbind(InputScope.Map, PlayerKeys.Qualifier, PlayerKeys.PanY) / 2);   // up + down rows
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.W, KeyModifiers.None), InputScope.Map, PlayerKeys.Qualifier, PlayerKeys.PanY, KeyTrigger.Axis, 1f));
        hand.Hold(EditorKey.Up);
        Assert.Equal(0f, PlayerKeys.Pan(keys).Y);
        hand.Hold(EditorKey.W);
        Assert.Equal(1f, PlayerKeys.Pan(keys).Y);

        hand.Press(EditorKey.Equals);
        Assert.True(keys.Pressed(PlayerKeys.ZoomIn));
        hand.NextFrame();
        hand.Press(EditorKey.KpAdd);
        Assert.True(keys.Pressed(PlayerKeys.ZoomIn));
        Assert.False(keys.Pressed(PlayerKeys.ZoomOut));
        hand.NextFrame();
        hand.Press(EditorKey.KpSubtract);
        Assert.True(keys.Pressed(PlayerKeys.ZoomOut));
    }

    /// <summary>
    /// F3 toggles the performance overlay through the keymap's debug row, and a rebind (F3 → F4) moves it.
    /// 🔴 Red-proof (compiles): <c>HandleKeys</c> reading <c>Raylib.IsKeyPressed(F3)</c> ⇒ the rail's hand is not read ⇒ red.
    /// </summary>
    [Fact]
    public void F3TogglesThePerformanceOverlay_ThroughTheDebugRow_AndARebindMovesIt()
    {
        var map = new Keymap();
        var hand = new ManualChordInput();
        var overlay = new PerformanceOverlay(new PerformanceMetrics(), map, hand);
        Assert.True(overlay.IsVisible);
        hand.Press(EditorKey.F3);
        overlay.HandleKeys();
        Assert.False(overlay.IsVisible);
        hand.NextFrame(); hand.Release(EditorKey.F3);

        Assert.Equal(1, map.Unbind(InputScope.Global, Hrot.UI.Common.DebugKeys.Qualifier, Hrot.UI.Common.DebugKeys.PerformanceOverlay));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.F4, KeyModifiers.None), InputScope.Global, Hrot.UI.Common.DebugKeys.Qualifier, Hrot.UI.Common.DebugKeys.PerformanceOverlay));
        hand.Press(EditorKey.F3);
        overlay.HandleKeys();
        Assert.False(overlay.IsVisible);                                // F3 does nothing now
        hand.NextFrame(); hand.Release(EditorKey.F3);
        hand.Press(EditorKey.F4);
        overlay.HandleKeys();
        Assert.True(overlay.IsVisible);                                 // F4 shows it again
    }
}
