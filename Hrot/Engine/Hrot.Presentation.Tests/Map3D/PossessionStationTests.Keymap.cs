using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Input;
using Hrot.UI.Common.Map3D;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>A station on a keymap of its own (a rebind must not leak into the process's), first person already on, one frame in.</summary>
    private static (EntityRepository World, PossessionStation Station, Fdp.Toolkit.Vis3D.MapViewSwitch View, FpsCameraDriver Driver, Keymap Keymap) K4Station()
    {
        var (w, unit, _) = World();
        w.AddComponent(unit, new PossessedBy { StationNodeId = (Fdp.Core.NodeId)(0), Mode = PossessionMode.Fps, Epoch = 1 });
        var (_, sw, _) = Map();
        var keymap = new Keymap();
        var station = new PossessionStation(sw, () => w) { Keymap = keymap, OverShoulder = false };
        station.Update(0.016f);
        sw.Camera3D.Update(0.016f);
        return (w, station, sw, Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver), keymap);
    }

    private static OperatorControlFrameEvent K4Frame(EntityRepository w, Fdp.Toolkit.Vis3D.MapViewSwitch sw, Input input)
    {
        sw.Camera3D.HandleInput(input);
        w.Bus.SwapBuffers();
        return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
    }

    private static void Rebind(Keymap map, ActionRef action, params KeymapEntry[] rows)
    {
        Assert.True(map.Unbind(InputScope.Hat, EmbodiedKeys.Hat, action) > 0);
        foreach (var r in rows) Assert.True(map.Declare(r));
    }

    /// <summary>
    /// ⭐⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — the FPS station's keys are ROWS of the Embodied hat's scope: every key it reads is declared as a Press, Hold or Axis row, labelled,
    /// and no two actions share a chord of one kind (the keymap refuses it at start-up).
    /// ✅ Red-proof: an Axis row of the defaults dropped (W) ⇒ the forward row is missing; two Hold rows on one chord ⇒ `Validate` names it.
    /// </summary>
    [Fact]
    public void K4_TheEmbodiedHatDeclaresEveryFpsKey_AsPressHoldAndAxisRows_LabelledAndWithoutAConflict()
    {
        var map = new Keymap();
        EmbodiedKeys.Declare(map);
        EmbodiedKeys.Declare(map);                                                          // idempotent: a second start adds nothing
        int count = map.Count;
        EmbodiedKeys.Declare(map);
        Assert.Equal(count, map.Count);
        Assert.Empty(map.Validate());

        KeymapEntry? Find(ActionRef a, KeyTrigger t, EditorKey key) => Enumerable.Range(0, map.Count).Select(i => map[i])
            .Where(e => e.Scope == InputScope.Hat && e.WindowKind == EmbodiedKeys.Hat && e.Action.Equals(a) && e.Trigger == t && e.Chord.Key == key).Cast<KeymapEntry?>().FirstOrDefault();
        Assert.Equal(1f, Find(EmbodiedKeys.MoveForward, KeyTrigger.Axis, EditorKey.W)!.Value.Scale);
        Assert.Equal(-1f, Find(EmbodiedKeys.MoveForward, KeyTrigger.Axis, EditorKey.S)!.Value.Scale);
        Assert.NotNull(Find(EmbodiedKeys.Sprint, KeyTrigger.Hold, EditorKey.Unknown));         // a modifier alone: Shift
        Assert.Equal(KeyModifiers.Shift, Find(EmbodiedKeys.Sprint, KeyTrigger.Hold, EditorKey.Unknown)!.Value.Chord.Modifiers);
        Assert.NotNull(Find(EmbodiedKeys.Zoom, KeyTrigger.Hold, EditorKey.MouseRight));
        Assert.NotNull(Find(EmbodiedKeys.LookX, KeyTrigger.Axis, EditorKey.MouseDeltaX));
        Assert.NotNull(Find(EmbodiedKeys.TeamNext, KeyTrigger.Press, EditorKey.J));
        Assert.Equal(KeyModifiers.Shift, Find(EmbodiedKeys.TeamPrevious, KeyTrigger.Press, EditorKey.J)!.Value.Chord.Modifiers);

        // every row is labelled for the Keyboard shortcuts window (a label that is just the id means the declarer forgot it)
        for (int i = 0; i < map.Count; i++)
            Assert.NotEqual(map[i].Action.ToString(), map.LabelOf(map[i].Action));
        // and all three kinds are present
        Assert.Contains(Enumerable.Range(0, map.Count), i => map[i].Trigger == KeyTrigger.Press);
        Assert.Contains(Enumerable.Range(0, map.Count), i => map[i].Trigger == KeyTrigger.Hold);
        Assert.Contains(Enumerable.Range(0, map.Count), i => map[i].Trigger == KeyTrigger.Axis);

        // a conflict is refused: a second Hold on Shift for another action
        map.Declare(new KeymapEntry(new KeyBinding(EditorKey.Unknown, KeyModifiers.Shift), InputScope.Hat, EmbodiedKeys.Hat, ActionRef.Command("fps.test"), KeyTrigger.Hold));
        Assert.Single(map.Validate());
    }

    /// <summary>
    /// ⭐⭐ B-10 K4 — the SAME controls, through the keymap, drive the SAME frame as before (W, Shift, the zoom button, the trigger) — and a rebind in the keymap MOVES them: the new key walks / runs / zooms,
    /// the old one does nothing. One Axis, one Hold-with-modifier, one Hold-with-mouse-button, one Press.
    /// ✅ Red-proof (compiling mutations, each reverting ONE read to the provider's raw key): `_keys.Axis(MoveForward)` ⇒ the frame reads W off the provider ⇒ the rebound I does not walk; `_keys.Held(Sprint)` ⇒ raw
    /// LeftShift ⇒ the rebound Alt does not run; `_keys.Held(Zoom)` ⇒ raw right button ⇒ the rebound middle button does not zoom; `_keys.Pressed(Shoulder)` ⇒ raw O ⇒ the rebound P does not toggle.
    /// </summary>
    [Fact]
    public void K4_TheFpsControlsRunThroughTheKeymap_AndARebindMovesEachKind()
    {
        var (w, station, sw, driver, map) = K4Station();
        using (w)
        {
            // today's keys, through the keymap
            Assert.Equal(1f, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.W } }).Forward);
            Assert.Equal(-1f, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.S } }).Forward);
            Assert.Equal((byte)0, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.W, (int)Raylib_cs.KeyboardKey.LeftShift } }).Gait);   // Shift + W: runs and still walks
            Assert.Equal(1f, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.W, (int)Raylib_cs.KeyboardKey.LeftShift } }).Forward);
            Assert.False(driver.Zooming);
            K4Frame(w, sw, new Input { RightDown = true });
            Assert.True(driver.Zooming);

            // an AXIS rebound: forward moves from W to I
            Rebind(map, EmbodiedKeys.MoveForward,
                new KeymapEntry(new KeyBinding(EditorKey.I, KeyModifiers.None), InputScope.Hat, EmbodiedKeys.Hat, EmbodiedKeys.MoveForward, KeyTrigger.Axis, 1f));
            Assert.Equal(1f, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.I } }).Forward);
            Assert.Equal(0f, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.W } }).Forward);

            // a HOLD on a modifier rebound: the run moves from Shift to Alt
            Rebind(map, EmbodiedKeys.Sprint, new KeymapEntry(new KeyBinding(EditorKey.Unknown, KeyModifiers.Alt), InputScope.Hat, EmbodiedKeys.Hat, EmbodiedKeys.Sprint, KeyTrigger.Hold));
            Assert.Equal((byte)0, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.LeftAlt } }).Gait);
            Assert.Equal((byte)1, K4Frame(w, sw, new Input { Keys = { (int)Raylib_cs.KeyboardKey.LeftShift } }).Gait);

            // a HOLD on a mouse button rebound: the zoom moves from the right button to the middle one
            Rebind(map, EmbodiedKeys.Zoom, new KeymapEntry(new KeyBinding(EditorKey.MouseMiddle, KeyModifiers.None), InputScope.Hat, EmbodiedKeys.Hat, EmbodiedKeys.Zoom, KeyTrigger.Hold));
            K4Frame(w, sw, new Input { RightDown = true });
            Assert.False(driver.Zooming);
            K4Frame(w, sw, new Input { MiddleDown = true });
            Assert.True(driver.Zooming);

            // a PRESS rebound: the shoulder view moves from O to P
            Rebind(map, EmbodiedKeys.Shoulder, new KeymapEntry(new KeyBinding(EditorKey.P, KeyModifiers.None), InputScope.Hat, EmbodiedKeys.Hat, EmbodiedKeys.Shoulder));
            bool before = station.OverShoulder;
            K4Frame(w, sw, new Input { Pressed = { (int)Raylib_cs.KeyboardKey.O } });
            Assert.Equal(before, station.OverShoulder);
            K4Frame(w, sw, new Input { Pressed = { (int)Raylib_cs.KeyboardKey.P } });
            Assert.Equal(!before, station.OverShoulder);
        }
    }

    /// <summary>
    /// ⭐ B-10 K4 — an Axis from the MOUSE: the look is two Axis rows (movement X / Y), so a rebind with a negative scale inverts it and the wheel's camera distance is one too.
    /// ✅ Red-proof: `delta = input.MouseDelta` (the raw read back) ⇒ the inverted look does not invert.
    /// </summary>
    [Fact]
    public void K4_TheMouseLookIsAnAxis_AScaleOfMinusOneInvertsIt()
    {
        var (w, _, sw, driver, map) = K4Station();
        using (w)
        {
            float y0 = driver.Yaw;
            K4Frame(w, sw, new Input { MouseDelta = new Vector2(-100f, 0f) });
            float normal = driver.Yaw - y0;
            Assert.True(normal > 0f);                                                       // today: a mouse to the left turns him to his left

            Rebind(map, EmbodiedKeys.LookX, new KeymapEntry(new KeyBinding(EditorKey.MouseDeltaX, KeyModifiers.None), InputScope.Hat, EmbodiedKeys.Hat, EmbodiedKeys.LookX, KeyTrigger.Axis, -1f));
            float y1 = driver.Yaw;
            K4Frame(w, sw, new Input { MouseDelta = new Vector2(-100f, 0f) });
            Assert.Equal(-normal, driver.Yaw - y1, 4);                                      // inverted, by the keymap alone
        }
    }

    /// <summary>
    /// ⭐ B-10 K4 — the shift of a chord decides which action a key is: J takes the next man, Shift+J the previous one, and Shift+J is never also J.
    /// ✅ Red-proof: the shadowing rule of the reader removed ⇒ Shift+J also reads as J.
    /// </summary>
    [Fact]
    public void K4_ShiftJ_IsThePreviousMan_AndNeverAlsoTheNextOne()
    {
        var map = new Keymap();
        EmbodiedKeys.Declare(map);
        var reader = new KeymapReader(map, InputScope.Hat, EmbodiedKeys.Hat);
        var hand = new ManualChordInput();
        reader.Input = hand;
        hand.Press(EditorKey.J);
        Assert.True(reader.Pressed(EmbodiedKeys.TeamNext));
        Assert.False(reader.Pressed(EmbodiedKeys.TeamPrevious));
        hand.NextFrame(); hand.Release(EditorKey.J);
        hand.Modifiers = KeyModifiers.Shift;
        hand.Press(EditorKey.J);
        Assert.True(reader.Pressed(EmbodiedKeys.TeamPrevious));
        Assert.False(reader.Pressed(EmbodiedKeys.TeamNext));
    }

    /// <summary>
    /// ⭐⭐ R-2084 / B-10 K4 — reading the FPS rows allocates nothing in a steady frame: every kind (Press, Hold, Axis, a mouse value) over the real provider adapter.
    /// ✅ Red-proof: the reader's per-version cache removed (a list built per call) ⇒ bytes &gt; 0.
    /// </summary>
    [Fact]
    public void K4_ReadingTheFpsRows_AllocatesNothingInASteadyFrame()
    {
        var (w, _, sw, driver, _) = K4Station();
        using (w)
        {
            var input = new Input { MouseDelta = new Vector2(3f, 1f), Keys = { (int)Raylib_cs.KeyboardKey.W, (int)Raylib_cs.KeyboardKey.LeftShift }, Pressed = { (int)Raylib_cs.KeyboardKey.O } };
            driver.Bind(input);
            var keys = driver.Keys;
            float sink = 0f;
            void Pass()
            {
                sink += keys.Axis(EmbodiedKeys.MoveForward) + keys.Axis(EmbodiedKeys.LookX) + keys.Axis(EmbodiedKeys.Climb) + keys.Axis(EmbodiedKeys.CameraDistance);
                if (keys.Held(EmbodiedKeys.Sprint)) sink++;
                if (keys.Held(EmbodiedKeys.Zoom)) sink++;
                if (keys.Pressed(EmbodiedKeys.Shoulder)) sink++;
                if (keys.Pressed(EmbodiedKeys.TeamNext)) sink++;
            }
            for (int i = 0; i < 3; i++) Pass();   // warm: the rows of each action are found once per keymap version
            // ⚠ measured: inside a full class run the JIT's tiering (call counting / tier-up of the readers) allocates on THIS thread for a few hundred calls
            //   (112 … 4120 B, 0 with DOTNET_TieredCompilation=0). A real per-frame allocation shows in EVERY window; the runtime's settles ⇒ the steady cost is the
            //   smallest of several 200-pass windows, and it must be 0.
            long steady = long.MaxValue;
            for (int round = 0; round < 8 && steady != 0; round++)
            {
                long a = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 200; i++) Pass();
                steady = Math.Min(steady, GC.GetAllocatedBytesForCurrentThread() - a);
            }
            Assert.Equal(0L, steady);
            Assert.True(sink > 0f);
        }
    }
}
