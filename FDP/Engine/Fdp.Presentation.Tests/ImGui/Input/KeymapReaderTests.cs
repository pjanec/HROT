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

    // ── ⭐⭐ SD-S2a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-102) — the GAMEPAD as value sources of the keymap ─────────────────────────

    /// <summary>A provider with a pad (pad 0): the fake's own sticks and buttons.</summary>
    private sealed class PadFake : IInputProvider
    {
        public bool Connected = true;
        public readonly float[] Axes = new float[6];
        public readonly System.Collections.Generic.HashSet<MapGamepadButton> Down = new(), Pressed = new();
        public Vector2 MousePosition => default;
        public Vector2 MouseDelta => default;
        public float MouseWheelMove => 0f;
        public bool IsMouseButtonPressed(MapMouseButton b) => false;
        public bool IsMouseButtonDown(MapMouseButton b) => false;
        public bool IsMouseButtonReleased(MapMouseButton b) => false;
        public bool IsKeyPressed(MapKeyboardKey k) => false;
        public bool IsKeyDown(MapKeyboardKey k) => false;
        public bool IsKeyReleased(MapKeyboardKey k) => false;
        public int GetKeyPressed() => 0;
        public bool IsMouseCaptured => false;
        public bool IsKeyboardCaptured => false;
        public bool GamepadAvailable => Connected;
        public float GamepadAxis(MapGamepadAxis axis) => Axes[(int)axis];
        public bool GamepadButtonDown(MapGamepadButton button) => Down.Contains(button);
        public bool GamepadButtonPressed(MapGamepadButton button) => Pressed.Contains(button);
    }

    /// <summary>
    /// ⭐⭐ SD-S2a (D-102) — a pad stick is a VALUE source: an Axis row multiplies it by its scale (a negative scale inverts), through ONE deadzone (0.15): a stick resting within it reads 0,
    /// just outside it ~0, full deflection 1 — no jump at the edge.
    /// 🔴 Red-proof (compiles): the deadzone dropped from <c>Axis</c> ⇒ a stick resting at 0.1 moves him ⇒ red.
    /// </summary>
    [Fact]
    public void SDS2a_APadStick_IsAValueSource_ThroughTheOneDeadzone()
    {
        var (_, r, hand) = Rig(Row(EditorKey.PadLeftY, "fwd", KeyTrigger.Axis, scale: -1f), Row(EditorKey.PadRightX, "look", KeyTrigger.Axis, scale: 12f), Row(EditorKey.W, "fwd", KeyTrigger.Axis));
        Assert.Equal(0.15f, KeymapReader.PadDeadzone);
        hand.Pad(EditorKey.PadLeftY, -0.10f);                       // resting, a little off centre
        Assert.Equal(0f, r.Axis(A("fwd")));
        hand.Pad(EditorKey.PadLeftY, 0.14f);
        Assert.Equal(0f, r.Axis(A("fwd")));
        hand.Pad(EditorKey.PadLeftY, -1f);                          // full up = forward (Y is −1 up, the row's scale −1)
        Assert.Equal(1f, r.Axis(A("fwd")), 5);
        hand.Pad(EditorKey.PadLeftY, 0.575f);                       // half way out of the deadzone, down = back
        Assert.Equal(-0.5f, r.Axis(A("fwd")), 4);
        hand.Pad(EditorKey.PadLeftY, -0.16f);                       // just past the edge: ~0, never a jump to 0.16
        Assert.True(r.Axis(A("fwd")) is > 0f and < 0.02f);
        hand.Pad(EditorKey.PadRightX, 1f);
        Assert.Equal(12f, r.Axis(A("look")), 4);                    // the row's scale
        hand.Pad(EditorKey.PadLeftY, 0f);
        hand.Hold(EditorKey.W);                                     // and the keys of the same action still add
        Assert.Equal(1f, r.Axis(A("fwd")));
        Assert.Equal(0f, KeymapReader.ApplyDeadzone(0.15f));
        Assert.Equal(-1f, KeymapReader.ApplyDeadzone(-1.3f));
    }

    /// <summary>
    /// ⭐ SD-S2a (D-102) — the pad's buttons are KEYS: a Press row fires on the frame it goes down, a Hold row while it is held; a trigger is both an axis (its value) and a button (held).
    /// 🔴 Red-proof (compiles): the pad buttons in <c>ProviderChordInput.IsPressed</c> read <c>GamepadButtonDown</c> ⇒ a held A presses every frame ⇒ red.
    /// </summary>
    [Fact]
    public void SDS2a_PadButtons_AreKeys_PressAndHold_ThroughTheProviderAdapter()
    {
        var map = new Keymap();
        Assert.True(map.Declare(Row(EditorKey.PadA, "use", KeyTrigger.Press)));
        Assert.True(map.Declare(Row(EditorKey.PadA, "list", KeyTrigger.Hold)));
        Assert.True(map.Declare(Row(EditorKey.PadTriggerL, "aim", KeyTrigger.Hold)));
        Assert.True(map.Declare(Row(EditorKey.PadTriggerR, "gas", KeyTrigger.Axis)));
        Assert.Empty(map.Validate());
        var pad = new PadFake();
        var r = new KeymapReader(map, InputScope.Hat, Q) { Input = new ProviderChordInput(pad) };

        pad.Down.Add(MapGamepadButton.RightFaceDown); pad.Pressed.Add(MapGamepadButton.RightFaceDown);   // A goes down
        Assert.True(r.Pressed(A("use")));
        Assert.True(r.Held(A("list")));
        pad.Pressed.Clear();                                                                            // still held, no longer a press
        Assert.False(r.Pressed(A("use")));
        Assert.True(r.Held(A("list")));

        pad.Axes[(int)MapGamepadAxis.RightTrigger] = -1f;                                             // released (GLFW: −1)
        Assert.Equal(0f, r.Axis(A("gas")));
        pad.Axes[(int)MapGamepadAxis.RightTrigger] = 1f;                                              // full: 1
        Assert.Equal(1f, r.Axis(A("gas")), 5);
        pad.Down.Add(MapGamepadButton.LeftTrigger2);
        Assert.True(r.Held(A("aim")));

        pad.Connected = false;                                                                          // unplugged: nothing (an unplugged trigger would read ½)
        pad.Axes[(int)MapGamepadAxis.RightTrigger] = 0f;
        Assert.Equal(0f, r.Axis(A("gas")));
        Assert.False(r.Held(A("list")));
    }

    /// <summary>The adapter's pad map: the face buttons by position (A = bottom), the bumpers, the triggers as buttons, a stick axis no button.</summary>
    [Fact]
    public void SDS2a_TheProviderAdapter_MapsThePadsButtonsAndAxes()
    {
        Assert.Equal(MapGamepadButton.RightFaceDown, ProviderChordInput.PadButtonOf(EditorKey.PadA));
        Assert.Equal(MapGamepadButton.RightFaceRight, ProviderChordInput.PadButtonOf(EditorKey.PadB));
        Assert.Equal(MapGamepadButton.RightFaceLeft, ProviderChordInput.PadButtonOf(EditorKey.PadX));
        Assert.Equal(MapGamepadButton.RightFaceUp, ProviderChordInput.PadButtonOf(EditorKey.PadY));
        Assert.Equal(MapGamepadButton.LeftTrigger1, ProviderChordInput.PadButtonOf(EditorKey.PadLB));
        Assert.Equal(MapGamepadButton.RightTrigger1, ProviderChordInput.PadButtonOf(EditorKey.PadRB));
        Assert.Equal(MapGamepadButton.RightTrigger2, ProviderChordInput.PadButtonOf(EditorKey.PadTriggerR));
        Assert.Equal(MapGamepadButton.Unknown, ProviderChordInput.PadButtonOf(EditorKey.PadLeftX));
        Assert.Equal((int)Raylib_cs.GamepadButton.RightFaceDown, (int)MapGamepadButton.RightFaceDown);   // a direct cast to Raylib's codes
        Assert.Equal((int)Raylib_cs.GamepadAxis.RightTrigger, (int)MapGamepadAxis.RightTrigger);
        Assert.Equal((int)Raylib_cs.GamepadButton.RightThumb, (int)MapGamepadButton.RightThumb);
        var pad = new PadFake();
        pad.Axes[(int)MapGamepadAxis.LeftX] = 0.7f;
        var c = new ProviderChordInput(pad);
        Assert.Equal(0.7f, c.Value(EditorKey.PadLeftX));                                              // raw: the reader applies the deadzone
        Assert.Equal(0f, c.Value(EditorKey.PadRightX));
    }

    /// <summary>
    /// ⭐⭐ SD-S2a (D-102 / D-38's device scope) — an INNER place shadows the outer one's rows on the same input, any trigger: while a view shows a device the left stick flies the DEVICE and does not
    /// also walk the body; with no inner place (no device shown) the body's row reads it again. A key the inner place does not bind still works.
    /// 🔴 Red-proof (compiles): <c>InnerOwns</c> returning false ⇒ the body walks while the device is flown ⇒ red.
    /// </summary>
    [Fact]
    public void SDS2a_AnInnerPlace_ShadowsTheOuterRowsOnTheSameInput()
    {
        var map = new Keymap();
        map.Declare(Row(EditorKey.PadLeftY, "walk", KeyTrigger.Axis, scale: -1f));
        map.Declare(Row(EditorKey.PadA, "use", KeyTrigger.Press));
        map.Declare(Row(EditorKey.W, "walk", KeyTrigger.Axis));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.PadLeftY, KeyModifiers.None), InputScope.Hat, "t device", A("axis1"), KeyTrigger.Axis, -1f));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.PadA, KeyModifiers.None), InputScope.Hat, "t device", A("ch0"), KeyTrigger.Press));
        Assert.Empty(map.Validate());                                           // two places: no conflict
        var hand = new ManualChordInput();
        var body = new KeymapReader(map, InputScope.Hat, Q) { Input = hand };
        var device = new KeymapReader(map, InputScope.Hat, "t device") { Input = hand };
        hand.Pad(EditorKey.PadLeftY, -1f).Press(EditorKey.PadA);

        Assert.Equal(1f, body.Axis(A("walk")), 5);                            // no device shown: the stick walks him
        Assert.True(body.Pressed(A("use")));
        body.Inner = device;                                                    // a device shown
        Assert.Equal(0f, body.Axis(A("walk")));
        Assert.False(body.Pressed(A("use")));
        Assert.Equal(1f, device.Axis(A("axis1")), 5);
        Assert.True(device.Pressed(A("ch0")));
        hand.Hold(EditorKey.W);
        Assert.Equal(1f, body.Axis(A("walk")));                               // W is not the device's: it still walks
        body.Inner = null;
        hand.Release(EditorKey.W);
        Assert.Equal(1f, body.Axis(A("walk")), 5);
    }

    /// <summary>⛔ R-2084 — a steady read of the pad (an axis through the deadzone, a button, an inner place) allocates nothing. 🔴 Red-proof: <c>Binds</c> building a list ⇒ bytes &gt; 0.</summary>
    [Fact]
    public void SDS2a_ASteadyPadRead_AllocatesNothing()
    {
        var map = new Keymap();
        map.Declare(Row(EditorKey.PadLeftY, "walk", KeyTrigger.Axis, scale: -1f));
        map.Declare(Row(EditorKey.PadA, "use", KeyTrigger.Press));
        map.Declare(Row(EditorKey.PadTriggerL, "aim", KeyTrigger.Hold));
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.PadRightX, KeyModifiers.None), InputScope.Hat, "t device", A("axis2"), KeyTrigger.Axis, 1f));
        var pad = new PadFake();
        pad.Axes[(int)MapGamepadAxis.LeftY] = -0.8f; pad.Axes[(int)MapGamepadAxis.RightX] = 0.5f;
        pad.Down.Add(MapGamepadButton.LeftTrigger2); pad.Pressed.Add(MapGamepadButton.RightFaceDown);
        var chords = new ProviderChordInput(pad);
        var body = new KeymapReader(map, InputScope.Hat, Q) { Input = chords };
        var device = new KeymapReader(map, InputScope.Hat, "t device") { Input = chords };
        float sink = 0;
        void Pass()
        {
            body.Inner = device;
            sink += body.Axis(A("walk")) + device.Axis(A("axis2"));
            if (body.Pressed(A("use"))) sink++;
            if (body.Held(A("aim"))) sink++;
            body.Inner = null;
            sink += body.Axis(A("walk"));
        }
        for (int i = 0; i < 3; i++) Pass();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) Pass();
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sink > 0);
    }
}
