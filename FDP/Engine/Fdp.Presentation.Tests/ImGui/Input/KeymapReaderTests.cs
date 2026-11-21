using System;
using System.Numerics;
using Fdp.Presentation.Input;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.Vis2D.Abstractions;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Input;

/// <summary>
/// ⭐⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — the keymap's Hold and Axis rows are READ through <see cref="KeymapReader"/>: Press fires once on its chord, Hold is state, Axis is a value
/// from keys (any positive − any negative) and the mouse (× scale); a row fires with the modifiers it names held, and the row naming the MOST modifiers wins; a rebind (<see cref="Keymap.Unbind"/>) moves it;
/// the device adapters speak the map's Raylib codes; a steady read allocates nothing.
/// </summary>
public sealed class KeymapReaderTests
{
    private const string Q = "t";
    private static ActionRef A(string id) => ActionRef.Command(id);
    private static KeymapEntry Row(EditorKey k, string id, KeyTrigger t, KeyModifiers m = KeyModifiers.None, float scale = 1f)
        => new(new KeyBinding(k, m), InputScope.Hat, Q, A(id), t, scale);

    private static (Keymap Map, KeymapReader Reader, ManualChordInput Hand) Rig(params KeymapEntry[] rows)
    {
        var map = new Keymap();
        foreach (var r in rows) Assert.True(map.Declare(r));
        var hand = new ManualChordInput();
        return (map, new KeymapReader(map, InputScope.Hat, Q) { Input = hand }, hand);
    }

    /// <summary>🔴 Red-proof (compiles): <c>Pressed</c> reading <c>IsDown</c> ⇒ a held key fires every frame ⇒ red.</summary>
    [Fact]
    public void Press_FiresOnTheFrameTheChordGoesDown_OnlyThere()
    {
        var (_, r, hand) = Rig(Row(EditorKey.M, "map", KeyTrigger.Press));
        Assert.False(r.Pressed(A("map")));
        hand.Press(EditorKey.M);
        Assert.True(r.Pressed(A("map")));
        hand.NextFrame();                                           // still held, no longer a press
        Assert.False(r.Pressed(A("map")));
    }

    /// <summary>🔴 Red-proof (compiles): <c>Held</c> not checking the modifier ⇒ Ctrl alone is never held / a plain key is held with no key.</summary>
    [Fact]
    public void Hold_IsStateWhileTheChordIsHeld_AModifierAloneIsAChord()
    {
        var (_, r, hand) = Rig(Row(EditorKey.Unknown, "run", KeyTrigger.Hold, KeyModifiers.Shift), Row(EditorKey.MouseRight, "zoom", KeyTrigger.Hold), Row(EditorKey.B, "bino", KeyTrigger.Hold));
        Assert.False(r.Held(A("run")));
        hand.Modifiers = KeyModifiers.Shift;
        Assert.True(r.Held(A("run")));
        hand.Modifiers = KeyModifiers.Ctrl;
        Assert.False(r.Held(A("run")));
        hand.Hold(EditorKey.MouseRight);
        Assert.True(r.Held(A("zoom")));
        Assert.False(r.Held(A("bino")));
        hand.Hold(EditorKey.B);
        Assert.True(r.Held(A("bino")));
        hand.Release(EditorKey.B);
        Assert.False(r.Held(A("bino")));
        Assert.False(r.Pressed(A("run")));                          // a Hold row is never a press
    }

    /// <summary>
    /// A key pair is ONE value — any positive minus any negative, so both climb keys down still climb once — and a mouse value is multiplied by its scale (a negative one inverts).
    /// 🔴 Red-proof (compiles): summing the key scales instead of the any-minus-any ⇒ Q + Space reads 2.
    /// </summary>
    [Fact]
    public void Axis_IsTheKeysPair_PlusTheMouseTimesItsScale()
    {
        var (_, r, hand) = Rig(Row(EditorKey.W, "fwd", KeyTrigger.Axis, scale: 1f), Row(EditorKey.S, "fwd", KeyTrigger.Axis, scale: -1f),
                               Row(EditorKey.Q, "up", KeyTrigger.Axis, scale: 1f), Row(EditorKey.Space, "up", KeyTrigger.Axis, scale: 1f),
                               Row(EditorKey.MouseDeltaX, "look", KeyTrigger.Axis, scale: -1f), Row(EditorKey.MouseWheel, "wheel", KeyTrigger.Axis));
        Assert.Equal(0f, r.Axis(A("fwd")));
        hand.Hold(EditorKey.W);
        Assert.Equal(1f, r.Axis(A("fwd")));
        hand.Hold(EditorKey.S);                                      // both: cancel
        Assert.Equal(0f, r.Axis(A("fwd")));
        hand.Release(EditorKey.W);
        Assert.Equal(-1f, r.Axis(A("fwd")));
        hand.Hold(EditorKey.Q).Hold(EditorKey.Space);
        Assert.Equal(1f, r.Axis(A("up")));
        hand.DeltaX = 7f; hand.Wheel = 2f;
        Assert.Equal(-7f, r.Axis(A("look")));                        // inverted by the scale
        Assert.Equal(2f, r.Axis(A("wheel")));
        hand.NextFrame();
        Assert.Equal(0f, r.Axis(A("look")));
    }

    /// <summary>A row names the modifiers it needs, extra ones do not stop it (Shift+W walks), and a row naming MORE modifiers for the same key owns it (J / Shift+J; wheel / Shift+wheel).</summary>
    [Fact]
    public void ARow_FiresWithItsModifiersHeld_AndTheMostSpecificRowWins()
    {
        var (_, r, hand) = Rig(Row(EditorKey.W, "fwd", KeyTrigger.Axis), Row(EditorKey.MouseWheel, "zoom", KeyTrigger.Axis), Row(EditorKey.MouseWheel, "pan", KeyTrigger.Axis, KeyModifiers.Shift),
                               Row(EditorKey.Z, "undo", KeyTrigger.Press, KeyModifiers.Ctrl), Row(EditorKey.Z, "redo", KeyTrigger.Press, KeyModifiers.Ctrl | KeyModifiers.Shift));
        hand.Modifiers = KeyModifiers.Shift; hand.Hold(EditorKey.W);
        Assert.Equal(1f, r.Axis(A("fwd")));                          // Shift+W still walks
        hand.Wheel = 3f;
        Assert.Equal(3f, r.Axis(A("pan")));                          // Shift+wheel is the pan …
        Assert.Equal(0f, r.Axis(A("zoom")));                         // … never also the zoom
        hand.Modifiers = KeyModifiers.None;
        Assert.Equal(3f, r.Axis(A("zoom")));
        Assert.Equal(0f, r.Axis(A("pan")));

        hand.Modifiers = KeyModifiers.Ctrl; hand.Press(EditorKey.Z);
        Assert.True(r.Pressed(A("undo")));
        hand.Modifiers = KeyModifiers.Ctrl | KeyModifiers.Shift;
        Assert.True(r.Pressed(A("redo")));
        Assert.False(r.Pressed(A("undo")));
    }

    /// <summary>A row belongs to ONE place of the scope chain: the same chord in another hat, or another scope, is not this reader's.</summary>
    [Fact]
    public void AReader_SeesOnlyItsOwnScopeAndQualifier()
    {
        var map = new Keymap();
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.W, KeyModifiers.None), InputScope.Hat, "Other", A("fwd"), KeyTrigger.Axis, 1f));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.W, KeyModifiers.None), InputScope.Map, Q, A("fwd"), KeyTrigger.Axis, 1f));
        var hand = new ManualChordInput(); hand.Hold(EditorKey.W);
        var reader = new KeymapReader(map, InputScope.Hat, Q) { Input = hand };
        Assert.Equal(0f, reader.Axis(A("fwd")));
        Assert.Equal(1f, new KeymapReader(map, InputScope.Map, Q) { Input = hand }.Axis(A("fwd")));
        Assert.Equal(0f, new KeymapReader(map, InputScope.Hat, Q).Axis(A("fwd")));   // no device: nothing
    }

    /// <summary>A rebind is "unbind, declare": the new chord works, the old one does not, the reader follows the keymap's version, and a declarer that guards on <c>IsDeclared</c> does not bring the default back.</summary>
    [Fact]
    public void ARebind_MovesTheRow_AndTheReaderFollowsTheVersion()
    {
        var (map, r, hand) = Rig(Row(EditorKey.W, "fwd", KeyTrigger.Axis), Row(EditorKey.S, "fwd", KeyTrigger.Axis, scale: -1f));
        hand.Hold(EditorKey.W);
        Assert.Equal(1f, r.Axis(A("fwd")));
        int v = map.Version;
        Assert.Equal(2, map.Unbind(InputScope.Hat, Q, A("fwd")));
        Assert.True(map.Version > v);
        Assert.Equal(0f, r.Axis(A("fwd")));
        Assert.True(map.IsDeclared(InputScope.Hat, Q, A("fwd")));        // still the action's place — no default comes back
        map.Declare(Row(EditorKey.I, "fwd", KeyTrigger.Axis));
        hand.Hold(EditorKey.I);
        Assert.Equal(1f, r.Axis(A("fwd")));
        var d = new RowDeclarer(map, InputScope.Hat, Q);
        d.Axis(EditorKey.W, A("fwd"), "Forward", 1f);                    // a surface declaring its defaults on the next start …
        hand.Release(EditorKey.I);
        hand.Hold(EditorKey.W);
        Assert.Equal(0f, r.Axis(A("fwd")));                              // … changes nothing: W (held) does not walk, the released I does not either
    }

    /// <summary>One action may have several chords (the keypad + and = both zoom) and the same chord as a Press and a Hold of two actions is no conflict; a chord on two actions of one kind is.</summary>
    [Fact]
    public void SeveralChordsPerAction_AndConflictsPerKind()
    {
        var map = new Keymap();
        Assert.True(map.Declare(Row(EditorKey.Equals, "zin", KeyTrigger.Press)));
        Assert.True(map.Declare(Row(EditorKey.KpAdd, "zin", KeyTrigger.Press)));
        Assert.False(map.Declare(Row(EditorKey.KpAdd, "zin", KeyTrigger.Press)));          // the same row twice: no-op
        Assert.True(map.Declare(Row(EditorKey.F, "use", KeyTrigger.Press)));
        Assert.True(map.Declare(Row(EditorKey.F, "use.held", KeyTrigger.Hold)));
        Assert.Empty(map.Validate());
        map.Declare(Row(EditorKey.F, "other", KeyTrigger.Hold));
        Assert.Single(map.Validate());
    }

    private sealed class Fake : IInputProvider
    {
        public Vector2 MousePosition { get; set; }
        public Vector2 MouseDelta { get; set; }
        public float MouseWheelMove { get; set; }
        public System.Collections.Generic.HashSet<int> Down { get; } = new();
        public System.Collections.Generic.HashSet<int> Pressed { get; } = new();
        public bool LeftDown, RightDown, MiddleDown, LeftPressed;
        public bool IsMouseButtonPressed(MapMouseButton b) => b == MapMouseButton.Left && LeftPressed;
        public bool IsMouseButtonDown(MapMouseButton b) => b == MapMouseButton.Left ? LeftDown : b == MapMouseButton.Right ? RightDown : MiddleDown;
        public bool IsMouseButtonReleased(MapMouseButton b) => false;
        public bool IsKeyPressed(MapKeyboardKey k) => Pressed.Contains((int)k);
        public bool IsKeyDown(MapKeyboardKey k) => Down.Contains((int)k);
        public bool IsKeyReleased(MapKeyboardKey k) => false;
        public int GetKeyPressed() => 0;
        public bool IsMouseCaptured => false;
        public bool IsKeyboardCaptured => false;
    }

    /// <summary>The map's device adapter speaks Raylib's codes (W = 87, Space = 32, F3 = 292, the keypad +, either Shift), the mouse buttons and the mouse values. 🔴 Red-proof: a wrong code in <c>RaylibKeyCodes</c> ⇒ red.</summary>
    [Fact]
    public void TheProviderAdapter_MapsKeysButtonsModifiersAndValues()
    {
        Assert.Equal(87, RaylibKeyCodes.Of(EditorKey.W));
        Assert.Equal(65, RaylibKeyCodes.Of(EditorKey.A));
        Assert.Equal(32, RaylibKeyCodes.Of(EditorKey.Space));
        Assert.Equal(49, RaylibKeyCodes.Of(EditorKey.D1));
        Assert.Equal(292, RaylibKeyCodes.Of(EditorKey.F3));
        Assert.Equal(334, RaylibKeyCodes.Of(EditorKey.KpAdd));
        Assert.Equal(262, RaylibKeyCodes.Of(EditorKey.Right));
        Assert.Equal(61, RaylibKeyCodes.Of(EditorKey.Equals));
        Assert.Equal(0, RaylibKeyCodes.Of(EditorKey.MouseLeft));

        var f = new Fake { MouseDelta = new Vector2(3f, -2f), MouseWheelMove = 1.5f, RightDown = true };
        f.Down.Add(87); f.Down.Add(344);       // W + the RIGHT Shift
        f.Pressed.Add(292);
        var c = new ProviderChordInput(f);
        Assert.True(c.IsDown(EditorKey.W));
        Assert.False(c.IsDown(EditorKey.S));
        Assert.True(c.IsPressed(EditorKey.F3));
        Assert.True(c.IsDown(EditorKey.MouseRight));
        Assert.False(c.IsDown(EditorKey.MouseLeft));
        Assert.Equal(KeyModifiers.Shift, c.Modifiers);
        Assert.Equal((3f, -2f, 1.5f), (c.Value(EditorKey.MouseDeltaX), c.Value(EditorKey.MouseDeltaY), c.Value(EditorKey.MouseWheel)));
    }

    /// <summary>⛔ R-2084 — a steady read of every kind allocates nothing. 🔴 Red-proof: the per-version cache dropped ⇒ bytes &gt; 0.</summary>
    [Fact]
    public void ASteadyRead_AllocatesNothing()
    {
        var (_, r, hand) = Rig(Row(EditorKey.W, "fwd", KeyTrigger.Axis), Row(EditorKey.Unknown, "run", KeyTrigger.Hold, KeyModifiers.Shift), Row(EditorKey.M, "map", KeyTrigger.Press), Row(EditorKey.MouseWheel, "wheel", KeyTrigger.Axis));
        hand.Hold(EditorKey.W); hand.Modifiers = KeyModifiers.Shift; hand.Press(EditorKey.M); hand.Wheel = 1f;
        float sink = 0;
        void Pass() { sink += r.Axis(A("fwd")) + r.Axis(A("wheel")); if (r.Held(A("run"))) sink++; if (r.Pressed(A("map"))) sink++; }
        for (int i = 0; i < 3; i++) Pass();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) Pass();
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sink > 0);
    }
}
