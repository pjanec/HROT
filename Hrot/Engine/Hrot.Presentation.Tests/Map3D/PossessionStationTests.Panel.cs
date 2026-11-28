using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐⭐ Simulated devices SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-122 – D-141) — the station's VIRTUAL PANEL on a real station bound to a
/// device: the HUD run's PanelButtons (drawn by <c>HudPanel.Button</c>, as a device's projector does) are hit-tested under the FREE cursor (POINT); a click, the
/// type's hotkey and the pad's button all bump the SAME counter of the device (one press, every source — D-125); RMB held LOOKS (the cursor captured, given back
/// where it was); a source that owns the look (a tracker) stops RMB-look; an attached adapter drives the device exactly as the virtual one (D-131); a latching
/// chip beside a replica is refused (D-135).
/// </summary>
public sealed partial class PossessionStationTests
{
    private const int PanelNetId = 7002, PanelType = 78;

    /// <summary>A test PRIMARY source (a replica / a tracker stand-in): its own encoder, flushed by the station's binding.</summary>
    private sealed class FakePanel : IDevicePanelIo
    {
        public FakePanel(DeviceInputSource source, bool ownsLook = false, bool ownsLatching = false)
        { Source = source; Encoder = new DeviceInputEncoder(source) { Clock = () => 0d }; OwnsLook = ownsLook; OwnsLatching = ownsLatching; }
        public DeviceInputSource Source { get; }
        public DeviceInputEncoder Encoder { get; }
        public bool OwnsLook { get; }
        public bool OwnsLatching { get; }
    }

    private sealed class PanelRig : IDisposable
    {
        public EntityRepository W = null!;
        public PossessionStation Station = null!;
        public MapViewSwitch View = null!;
        public Entity Device;
        public DebugPrimitiveBuffer Buffer = new(256);
        public Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer Layer = null!;
        public DeviceInputProbe Probe = new();
        public FpsCameraDriver Driver => (FpsCameraDriver)View.Camera3D.Driver!;
        public void Dispose() => W.Dispose();

        /// <summary>One input frame: the body's frame and the device's merged input after it.</summary>
        public OperatorControlFrameEvent Frame(Input input)
        {
            Station.Update(0.016f);
            View.Camera3D.HandleInput(input);
            W.Bus.SwapBuffers();
            var body = Assert.Single(W.Bus.Read<OperatorControlFrameEvent>().ToArray());
            Probe.Read(W);
            return body;
        }

        public DeviceControl Input => Probe.Of(Device);

        /// <summary>The device's HUD run as its projector emits it: two chips through HudPanel — RANGE (button 0, key R) and POWER (switch 0, key K).</summary>
        public void DrawHud()
        {
            Buffer.Clear();
            Buffer.BeginScope(PanelNetId);
            HudPanel.Button(Buffer, DeviceElementKind.Button, 0, new Vector2(1000f, 300f), new Vector2(200f, 50f), new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("RANGE"), HudPanel.Badge("R"),
                            PanelButtonState.Normal, HudPanel.HotkeyCode("R"), DebugTraceLayers.DeviceHud);
            HudPanel.Button(Buffer, DeviceElementKind.Switch, 0, new Vector2(1000f, 400f), new Vector2(200f, 50f), new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("POWER"), HudPanel.Badge("K"),
                            PanelButtonState.Normal, HudPanel.HotkeyCode("K"), DebugTraceLayers.DeviceHud);
            Buffer.EndScope();
        }

        public static Vector2 Range => new(1100f, 325f);   // the RANGE chip's centre (the 1280 × 720 view IS the canvas)
        public static Vector2 Power => new(1100f, 425f);
    }

    private static PanelRig BoundToADevice()
    {
        var r = new PanelRig();
        var (w, unit, map) = World();
        r.W = w;
        w.AddComponent(unit, new PossessedBy { StationNodeId = (StationId)0, Mode = PossessionMode.Fps, Epoch = 1 });
        w.RegisterComponent<DeviceInfo>();
        w.RegisterComponent<DevicePose>();
        w.RegisterComponent<DeviceInput>();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        w.Bus.Register<DeviceInputEvent>();
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        var type = new Fdp.Interfaces.TkbTemplate("Test rangefinder", PanelType);
        type.AddDescriptor(new DeviceDefDto
        {
            Behavior = Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceRangefinder,
            Elements = { DeviceElementDto.Button("RANGE", "R"), DeviceElementDto.Switch("POWER", 2, "K"), DeviceElementDto.Button("ZOOM", "", DeviceWheelRole.Up) },
        });
        db.Register(type);
        w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        var d = w.CreateEntity();
        w.AddComponent(d, new SimTransform { Position = new Vector3(100f, 50f, 1.5f), Rotation = Quaternion.Identity });
        w.AddComponent(d, new DeviceInfo { DeviceType = PanelType, Behavior = new FixedString64(Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceRangefinder) });
        w.AddComponent(d, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = PanelNetId });
        w.AddComponent(d, new PossessedBy { StationNodeId = (StationId)0, Mode = PossessionMode.Operated, Epoch = 1 });
        w.AddComponent(d, new DevicePose { Orientation = Quaternion.Identity, FovDegrees = 7f });
        map.Register(PanelNetId, d);
        r.Device = d;
        var (_, sw, _) = Map();
        r.View = sw;
        r.Layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, r.Buffer, new FdpEventBus());
        r.Station = new PossessionStation(sw, () => w) { ViewLayer = r.Layer, Keymap = new Keymap(), OverShoulder = false };
        r.Station.Update(0.016f);
        sw.Camera3D.Update(0.016f);
        r.DrawHud();
        var b = new Input(); b.Pressed.Add((int)Raylib_cs.KeyboardKey.B);
        r.Frame(b);                                       // B: the view's source is the device
        Assert.Equal(d, r.Station.BoundDevice);
        Assert.Equal(PanelNetId, r.Layer.BoundSource);
        return r;
    }

    /// <summary>
    /// ⭐⭐ D-122 / D-123 / D-125 — POINT: the cursor is FREE; the chip under it is the hover; LMB on it presses ITS element — the same counter the type's HOTKEY
    /// (R, shadowing the body's next-round key) and the pad's button 0 bump; LMB on no chip presses nothing and fires no shot; the wheel presses the element its
    /// type names for it. 🔴 Red-proof (compiles): the click's hit test skipped (<c>DevicePanelClick.Click</c> returns −1) ⇒ the counter stays 0 ⇒ red.
    /// </summary>
    [Fact]
    public void SDS5v_AClick_AHotkey_AndThePad_AreTheSamePress_OnTheDevice()
    {
        using var r = BoundToADevice();
        var point = new Input { MousePosition = PanelRig.Range };
        r.Frame(point);
        Assert.False(point.Locked);                                            // POINT: the cursor is free
        Assert.Equal((byte)0, r.Driver.Panel.Hovered!.Value.PanelElement);     // the hover: RANGE's chip

        var click = r.Frame(new Input { MousePosition = PanelRig.Range, LeftPressed = true, LeftDown = true });
        Assert.Equal((byte)1, r.Input.Presses[0]);                             // the click pressed RANGE …
        Assert.Equal((byte)0, click.ShotSeq);                                  // … and fired nothing

        r.Frame(new Input { MousePosition = new Vector2(200f, 200f), LeftPressed = true, LeftDown = true });
        Assert.Equal((byte)1, r.Input.Presses[0]);                             // LMB on no chip: nothing

        var key = new Input { MousePosition = new Vector2(200f, 200f) }; key.Pressed.Add((int)Raylib_cs.KeyboardKey.R);
        var keyed = r.Frame(key);
        Assert.Equal((byte)2, r.Input.Presses[0]);                             // the hotkey: the same counter
        Assert.Equal((byte)0, keyed.AmmoSeq);                                  // R is RANGE here, not the next round (the type's place shadows it)

        var pad = new Input { PadConnected = true }; pad.PadPressed.Add(MapGamepadButton.RightFaceDown);
        r.Frame(pad);
        Assert.Equal((byte)3, r.Input.Presses[0]);                             // the pad's A: button 0 too

        r.Frame(new Input { MouseWheelMove = 1f });
        Assert.Equal((byte)1, r.Input.Presses[1]);                             // a wheel notch up: the element whose role it is (ZOOM, button 1)
        Assert.Equal((byte)0, r.Input.Switches[0]);
    }

    /// <summary>
    /// ⭐ D-123 — LOOK while RMB is held: the cursor captured and the drag turning the look; released, the cursor comes back where it was and the mouse no longer
    /// looks (POINT). The arrows turn it finely. 🔴 Red-proof (compiles): no <c>SetMousePosition</c> on release ⇒ red.
    /// </summary>
    [Fact]
    public void SDS5v_RmbLook_CapturesTheCursor_TurnsTheLook_AndGivesTheCursorBack()
    {
        using var r = BoundToADevice();
        float yaw0 = r.Driver.Yaw;
        var drag = new Input { MousePosition = new Vector2(300f, 200f), RightDown = true, MouseDelta = new Vector2(80f, 0f) };
        r.Frame(drag);
        Assert.True(drag.Locked);                                              // captured
        Assert.True(r.Driver.Panel.Looking);
        Assert.True(r.Driver.Yaw < yaw0);                                      // a drag right turns the look right
        float yaw1 = r.Driver.Yaw;

        var release = new Input { MousePosition = new Vector2(640f, 360f) };
        r.Frame(release);
        Assert.False(release.Locked);
        Assert.Equal(new Vector2(300f, 200f), release.MovedTo);                // back where it was
        r.Frame(new Input { MouseDelta = new Vector2(80f, 0f) });
        Assert.Equal(yaw1, r.Driver.Yaw);                                      // POINT: moving the mouse does not look

        var arrow = new Input(); arrow.Keys.Add((int)Raylib_cs.KeyboardKey.Right);
        r.Frame(arrow);
        Assert.True(r.Driver.Yaw < yaw1);                                      // the arrow turns it finely
        Assert.True(yaw1 - r.Driver.Yaw < (yaw0 - yaw1) / 4f);
    }

    /// <summary>
    /// ⭐ D-126 / D-131 — a TRACKER attached as the device's primary owns its look: RMB-look does nothing (no capture, no turn) and the station sends no look for
    /// that device — while the tracker's own encoder, flushed by the station, drives the device exactly as the virtual panel would (its press counts on the same
    /// counter, beside a click). 🔴 Red-proof (compiles): <c>LookOwnedElsewhere</c> always false ⇒ the drag turns the look ⇒ red.
    /// </summary>
    [Fact]
    public void SDS5v_ATrackerOwnedDevice_IgnoresRmbLook_AndAnAttachedAdapter_DrivesTheDevice()
    {
        using var r = BoundToADevice();
        var tracker = new FakePanel(DeviceInputSource.Tracker, ownsLook: true);
        r.Station.PanelIo.Attach(r.Device, tracker);
        float yaw0 = r.Driver.Yaw;
        var drag = new Input { RightDown = true, MouseDelta = new Vector2(80f, 0f) };
        r.Frame(drag);
        Assert.Equal(yaw0, r.Driver.Yaw);
        Assert.False(drag.Locked);
        Assert.False(r.Driver.Panel.Looking);

        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        tracker.Encoder.SetOrientation(r.Device, q);
        tracker.Encoder.Press(r.Device, 0);                                    // the adapter's own press …
        r.Frame(new Input { MousePosition = PanelRig.Range, LeftPressed = true, LeftDown = true });   // … and a HUD click beside it (D-141)
        Assert.Equal((byte)2, r.Input.Presses[0]);
        Assert.Equal(q, r.Input.Orientation);                                  // the tracker's look — the station sent none

        r.Station.PanelIo.Detach(r.Device);                                    // swapped back: the virtual panel is primary again
        r.Frame(new Input { RightDown = true, MouseDelta = new Vector2(80f, 0f) });
        Assert.True(r.Driver.Yaw < yaw0);
    }

    /// <summary>
    /// ⭐ D-135 — a LATCHING control's chip steps the switch from the position the device holds; beside a replica that owns the latching controls it is hovered but
    /// REFUSED (a click would put the device where the physical switch is not) — while a momentary chip beside it still presses. 🔴 Red-proof (compiles): the refusal
    /// dropped from <c>DevicePanelClick.Click</c> ⇒ the switch steps ⇒ red.
    /// </summary>
    [Fact]
    public void SDS5v_ALatchingChip_Steps_ButIsRefusedBesideAReplica()
    {
        using var r = BoundToADevice();
        r.Frame(new Input { MousePosition = PanelRig.Power, LeftPressed = true, LeftDown = true });
        Assert.Equal((byte)1, r.Input.Switches[0]);                            // OFF → ON
        r.W.AddComponent(r.Device, new DeviceInput { Control = r.Input });     // the device holds it (as its tick's merge would)

        r.Station.PanelIo.Attach(r.Device, new FakePanel(DeviceInputSource.Replica, ownsLatching: true));
        r.Frame(new Input { MousePosition = PanelRig.Power });
        Assert.True(r.Driver.Panel.HoverRefused);
        r.Frame(new Input { MousePosition = PanelRig.Power, LeftPressed = true, LeftDown = true });
        var k = new Input(); k.Pressed.Add((int)Raylib_cs.KeyboardKey.K);
        r.Frame(k);                                                            // its hotkey is refused the same way
        Assert.Equal((byte)1, r.Input.Switches[0]);
        r.Frame(new Input { MousePosition = PanelRig.Range, LeftPressed = true, LeftDown = true });
        Assert.Equal((byte)1, r.Input.Presses[0]);                             // a momentary chip beside the replica: still a press
    }

    /// <summary>⭐ R-2084 — the virtual panel's steady POINT frame (hover over a chip, no click) allocates nothing.</summary>
    [Fact]
    public void SDS5v_TheVirtualPanel_SteadyFrame_AllocatesNothing()
    {
        using var r = BoundToADevice();
        var input = new Input { MousePosition = PanelRig.Range };
        r.Frame(input);
        for (int i = 0; i < 50; i++) r.Driver.Panel.Frame(r.W, input, r.Device, r.Station.PanelIo, r.Layer.ViewFrame(), Viewport);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) r.Driver.Panel.Frame(r.W, input, r.Device, r.Station.PanelIo, r.Layer.ViewFrame(), Viewport);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.NotNull(r.Driver.Panel.Hovered);
    }
}
