using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ CE-3174 (docs/DESIGN_Entity_Control.md §8d, M-9) — the FPS station, window-free: the camera driver seam, the station that FOLLOWS
/// the replicated <see cref="PossessedBy"/> into and out of first person (the operator's view restored, never saved), the controls
/// becoming control frames on the bus, and the HUD's text.
/// </summary>
public sealed partial class PossessionStationTests
{
    private static readonly Vector2 Viewport = new(1280, 720);

    private sealed class Input : IInputProvider
    {
        public Vector2 MousePosition { get; set; } = new(640, 360);
        public Vector2 MouseDelta { get; set; }
        public float MouseWheelMove { get; set; }
        public HashSet<int> Keys { get; } = new();
        public HashSet<int> Pressed { get; } = new();
        public bool LeftDown { get; set; }
        public bool LeftPressed { get; set; }    // ⭐ CE-1078
        public bool RightPressed { get; set; }
        public bool RightDown { get; set; }       // ⭐ CE-1211 — the zoom is held
        public bool? Locked { get; private set; }
        public bool IsMouseButtonPressed(MapMouseButton b) => b == MapMouseButton.Left ? LeftPressed : b == MapMouseButton.Right && RightPressed;
        public bool IsMouseButtonDown(MapMouseButton b) => b == MapMouseButton.Left ? LeftDown : b == MapMouseButton.Right && RightDown;
        public bool IsMouseButtonReleased(MapMouseButton b) => false;
        public bool IsKeyPressed(MapKeyboardKey k) => Pressed.Contains((int)k);
        public bool IsKeyDown(MapKeyboardKey k) => Keys.Contains((int)k);
        public bool IsKeyReleased(MapKeyboardKey k) => false;
        public int GetKeyPressed() => 0;
        public bool IsMouseCaptured => false;
        public bool IsKeyboardCaptured => false;
        public void SetCursorLocked(bool locked) => Locked = locked;
    }

    private sealed class CountingDriver : ICameraDriver
    {
        public int Updates, Inputs, Detaches;
        public void Update(MapCamera3D camera, float dt) { Updates++; camera.LookAt = new Vector3(7f, 8f, 9f); }
        public bool HandleInput(MapCamera3D camera, IInputProvider input) { Inputs++; return true; }
        public void Detached(MapCamera3D camera) => Detaches++;
    }

    private static (Fdp.Toolkit.Vis2D.MapCanvas Canvas, MapViewSwitch Switch, MapCamera Two) Map()
    {
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var two = new MapCamera();
        var target = new Vector2(-Viewport.X / 2f, Viewport.Y / 2f);
        two.ApplyCameraView(new MapCameraView { Target = target, Zoom = 1f, SmoothTarget = target, SmoothZoom = 1f });
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = Viewport });
        return (canvas, sw, two);
    }

    private static (EntityRepository World, Entity Unit, NetworkEntityMap Map) World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>();
        w.RegisterComponent<PossessedBy>();
        w.RegisterComponent<WeaponAmmo>();
        w.RegisterComponent<Health>();
        var unit = w.CreateEntity();
        w.AddComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(unit, new Health { Current = 80f, Max = 100f });
        var map = new NetworkEntityMap();
        map.Register(4242L, unit);
        w.SetSingletonManaged(map);
        return (w, unit, map);
    }

    [Fact]
    public void CE3174_ADriver_ReplacesTheCamerasOwnUpdateAndInput_AndIsToldWhenRemoved()
    {
        var cam = new MapCamera3D { ViewportOverride = Viewport };
        var driver = new CountingDriver();
        cam.Driver = driver;
        cam.Update(0.016f);
        Assert.True(cam.HandleInput(new Input()));   // consumed: no layer gets the click
        Assert.Equal((1, 1), (driver.Updates, driver.Inputs));
        Assert.Equal(new Vector3(7f, 8f, 9f), cam.LookAt);
        cam.Driver = null;
        Assert.Equal(1, driver.Detaches);
    }

    [Fact]
    public void CE3174_TheStation_FollowsPossessedBy_IntoFirstPerson_AndBackToTheOperatorsView_WhichIsNeverSaved()
    {
        var (w, unit, _) = World();
        using (w)
        {
            var (canvas, sw, two) = Map();
            var cameraLayer = new MapCameraLayer(sw, () => w);
            var station = new PossessionStation(sw, () => w);
            var before = sw.CurrentPose;
            Assert.False(sw.Is3D);

            station.Update(0.016f);
            Assert.False(station.InFps);                                    // nobody holds the unit

            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });   // this node (no authority data ⇒ 0)
            station.Update(0.016f);
            Assert.True(station.InFps);
            Assert.True(sw.Is3D);
            Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            sw.Camera3D.Update(0.016f);
            Assert.InRange(Vector2.Distance(new Vector2(sw.Camera3D.Eye.X, sw.Camera3D.Eye.Y), new Vector2(100f, 50f)), 0f, 0.01f);   // the eye is on the unit
            Assert.True(sw.Camera3D.Eye.Z > 1f, $"at eye height: {sw.Camera3D.Eye}");
            cameraLayer.Update(5f);                                          // a driven view is never saved
            Assert.Null(cameraLayer.Camera);

            w.SetComponent(unit, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 2 });   // another station took it
            station.Update(0.016f);
            Assert.False(station.InFps);
            Assert.Null(sw.Camera3D.Driver);
            Assert.False(sw.Is3D);                                          // back to the 2-D map it was in
            Assert.InRange(Vector3.Distance(sw.CurrentPose.LookAt, before.LookAt), 0f, 0.5f);
            _ = canvas; _ = two;
        }
    }

    /// <summary>
    /// ⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — the weather's sky in the 3-D map: ⭐ since 2026-10-19 in the FREE camera too, read at
    /// what it looks at, its fog starting there (what is looked at stays clear); in a unit read at the eye with the fog from the eye; the 2-D
    /// map gets none. 🔒 User, 2026-10-19: "I'm not asking for much more than just showing the same weather as in FPS as the first slice"
    /// (supersedes 2026-10-16's "not in the usual free camera").
    /// </summary>
    [Fact]
    public void CE1146_TheWeathersSky_InTheFreeCameraAtWhatItLooksAt_AndInFirstPersonAtTheEye()
    {
        var (w, unit, _) = World();
        using (w)
        {
            var (_, sw, _) = Map();
            var sky = new WeatherSkyLayer(sw, () => w);
            var station = new PossessionStation(sw, () => w);
            sky.Update(0.016f);
            Assert.Null(sw.Camera3D.Atmosphere);                              // the 2-D map: none
            sw.Set(true, animate: false);
            sky.Update(0.016f);
            Assert.NotNull(sw.Camera3D.Atmosphere);                           // the free camera: the weather where it looks
            Assert.Equal(sw.Camera3D.Distance * MapCamera3D.FreeFogStartShare, sw.Camera3D.FogStart, 3);   // the fog starts there

            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            sky.Update(0.016f);
            Assert.NotNull(sw.Camera3D.Atmosphere);                           // in the unit: the weather's
            Assert.Equal(0f, sw.Camera3D.FogStart);                           // first person: the fog from the eye

            w.SetComponent(unit, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 2 });
            station.Update(0.016f);
            sky.Update(0.016f);
            Assert.NotNull(sw.Camera3D.Atmosphere);                           // released: back to the free camera's weather
            Assert.True(sw.Camera3D.FogStart > 0f);
        }
    }

    [Fact]
    public void CE3174_TheControls_BecomeControlFrames_TheCursorLocks_AndEscReleases()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            sw.Camera3D.Update(0.016f);
            float yaw0 = driver.Yaw;

            var input = new Input { MouseDelta = new Vector2(40f, 0f), LeftDown = true };
            input.Keys.Add((int)Raylib_cs.KeyboardKey.W);
            input.Keys.Add((int)Raylib_cs.KeyboardKey.A);
            Assert.True(sw.Camera3D.HandleInput(input));
            Assert.True(input.Locked);
            w.Bus.SwapBuffers();
            var frame = Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            Assert.Equal(unit, frame.Unit);
            Assert.Equal((1f, 1f, (byte)1, (byte)1), (frame.Forward, frame.Lateral, frame.Trigger, frame.Gait));   // walk unless Shift
            Assert.True(frame.Yaw < yaw0, "mouse right turns right (yaw decreases)");
            Assert.Equal(driver.Yaw, frame.Yaw);
            Assert.Equal(1L, station.FramesSent);

            var esc = new Input();
            esc.Pressed.Add((int)MapKeyboardKey.Escape);
            sw.Camera3D.HandleInput(esc);
            w.Bus.SwapBuffers();
            Assert.Single(w.Bus.Read<ReleasePossessionEvent>().ToArray());

            sw.Camera3D.Driver = null;
            Assert.False(input.Locked);   // detaching unlocks the cursor it locked
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1073 (docs/DESIGN_Map_3D_Mode.md §6l) — the eye is the DRAWN head's, on the unit's current position and heading, and the held
    /// unit's head is not drawn. 🔒 User, 2026-10-14: "When moving forward the character leans a bit forward basically putting his head
    /// in front of the FPS camera so i see nothing — the camera should rather be bound to character's head/eyes". A look moved by the
    /// mouse shows in the SAME frame (the driver used to apply it on the next Update).
    /// </summary>
    [Fact]
    public void CE1073_TheEye_IsTheDrawnHeads_OnTheUnitsCurrentPose_TheHeadIsHidden_AndTheLookIsSameFrame()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var bodies = new EntityBodyLayer3D(() => w, () => null);
            var station = new PossessionStation(sw, () => w, bodies);
            Assert.Same(bodies, station.Bodies);
            station.Update(0.016f);
            Assert.Equal(unit, bodies.FirstPerson);                           // its head is not drawn: the camera is in it

            var leaning = new Vector3(0.45f, 0.02f, 1.48f);                     // a running man's head: forward of, and lower than, upright
            bodies.RememberEyeLocal(unit, leaning);
            var yawed = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);   // he faces north
            w.SetComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = yawed });
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            var expected = new Vector3(100f, 50f, 0f) + Vector3.Transform(leaning, yawed);
            Assert.InRange(Vector3.Distance(sw.Camera3D.Eye, expected), 0f, 0.01f);
            Assert.InRange(Vector3.Distance(driver.Eye, expected), 0f, 0.01f);

            float yawBefore = sw.Camera3D.Yaw;
            sw.Camera3D.HandleInput(new Input { MouseDelta = new Vector2(100f, 0f) });
            Assert.NotEqual(yawBefore, sw.Camera3D.Yaw, 3);                      // this frame's look, this frame
            Assert.InRange(Vector3.Distance(sw.Camera3D.Eye, expected), 0f, 0.01f); // turning the head keeps the eye in it

            w.SetComponent(unit, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 2 });
            station.Update(0.016f);
            Assert.Equal(Entity.Null, bodies.FirstPerson);                    // released: his head is drawn again
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1143 (supersedes CE-1074's C cycle) — a shooter's stance keys: C toggles crouched, Z toggles prone, HOLDING Ctrl crouches
    /// and letting go stands him back up; every frame carries the stance (StanceId + 1) and the HUD says it (with RUN while Shift is held).
    /// 🔒 User, 2026-10-16: "CTRL when pressed should make him crouch (and releasing ctrl should make him stand again), special key toggle
    /// for prone mode would be better then the cycling today".
    /// </summary>
    [Fact]
    public void CE1143_CTogglesCrouch_ZTogglesProne_HoldingCtrlCrouches_InEveryFrame_AndTheHudSaysIt()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);

            byte Frame(Raylib_cs.KeyboardKey? press = null, bool ctrl = false, bool shift = false)
            {
                var input = new Input();
                if (press is { } k) input.Pressed.Add((int)k);
                if (ctrl) input.Keys.Add((int)MapKeyboardKey.LeftControl);
                if (shift) input.Keys.Add((int)MapKeyboardKey.LeftShift);
                sw.Camera3D.HandleInput(input);
                w.Bus.SwapBuffers();
                return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray()).Stance;
            }

            Assert.Equal(1, Frame());                                   // standing (no intent ⇒ standing), held every frame
            Assert.Equal(2, Frame(Raylib_cs.KeyboardKey.C));            // C: crouched
            Assert.Equal(2, Frame());                                   // a frame without the key keeps it
            Assert.Equal(1, Frame(Raylib_cs.KeyboardKey.C));            // C again: standing
            Assert.Equal(2, Frame(ctrl: true));                         // holding Ctrl: crouched…
            Assert.Equal(1, Frame());                                   // …letting go: standing again
            Assert.Equal(3, Frame(Raylib_cs.KeyboardKey.Z));            // Z: prone
            Assert.Equal(3, Frame(ctrl: true));                         // Ctrl does not lift him out of prone
            Assert.Equal(1, Frame(Raylib_cs.KeyboardKey.Z));            // Z again: standing
            Frame(shift: true);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.Contains("STANCE STANDING  RUN", PossessionStation.Hud.Lines(w, unit, 0, false, false, driver.Stance, running: driver.Running));
            Assert.Contains(PossessionStation.Hud.Lines(w, unit, 0, false, false, driver.Stance), l => l.Contains("Z prone"));
        }
    }

    [Fact]
    public void CE3174_TheHud_ShowsHealth_AndAmmoPerMount_TheSelectedOneMarked()
    {
        var (w, unit, _) = World();
        using (w)
        {
            var ammo = new WeaponAmmo { Count = 2 };
            ammo.Set(0, new WeaponState { Ammo = 27, MaxAmmo = 30, MagazineSize = 10, MagazineRounds = 7 });
            ammo.Set(1, new WeaponState { Ammo = 2, MaxAmmo = 4 });
            w.AddComponent(unit, ammo);
            var lines = PossessionStation.Hud.Lines(w, unit, mount: 1, vehicle: false, hullLocked: false);
            Assert.Equal("HEALTH 80/100", lines[0]);
            Assert.Equal("  1 WEAPON  27/30  [7]", lines[1]);   // ⭐ CE-1079 — numbered as its key; no TKB here, so no kind or round name
            Assert.Equal("> 2 WEAPON  2/4", lines[2]);
            Assert.Contains("Esc release", lines[3]);
        }
    }

    /// <summary>A picker that answers a unit 30 m ahead (network id 77) — the crosshair resting on it.</summary>
    private sealed class UnitAhead : IPicker3D
    {
        public PickFilter Kind => PickFilter.EntityParts;   // ⭐ CE-1126 — the crosshair asks for the parts
        public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
        {
            hit = new PickResult(origin + direction * 30f + new Vector3(0f, 0f, -1f), PickKind.Entity, 30f, 77);
            return true;
        }
    }

    /// <summary>
    /// ⭐ CE-1212 (docs/DESIGN_Entity_Control.md §8j L-1, L-2) — a person leans while Q / E is held (both or neither: upright), the frame
    /// carrying it as a level; R is the next round now (F before) and F the use key (E before).
    /// </summary>
    [Fact]
    public void CE1212_HoldingQOrE_LeansThePerson_AndTheFrameCarriesIt()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            OperatorControlFrameEvent Frame(params Raylib_cs.KeyboardKey[] down)
            {
                var input = new Input();
                foreach (var k in down) input.Keys.Add((int)k);
                sw.Camera3D.HandleInput(input);
                w.Bus.SwapBuffers();
                return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            }
            Assert.Equal((byte)Fdp.Toolkit.Combat.LeanSide.LeanLeft, Frame(Raylib_cs.KeyboardKey.Q).Lean);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanLeft, driver.Leaning);
            Assert.Equal((byte)Fdp.Toolkit.Combat.LeanSide.LeanRight, Frame(Raylib_cs.KeyboardKey.E).Lean);
            Assert.Equal((byte)Fdp.Toolkit.Combat.LeanSide.LeanNone, Frame(Raylib_cs.KeyboardKey.Q, Raylib_cs.KeyboardKey.E).Lean);
            Assert.Equal((byte)Fdp.Toolkit.Combat.LeanSide.LeanNone, Frame().Lean);                       // let go: upright
            var e = Frame(Raylib_cs.KeyboardKey.E);
            Assert.Equal(0, e.UseSeq);                                                                    // E no longer uses
            Assert.Contains(PossessionStation.Hud.Lines(w, unit, 0, true, false, null, false), l => l.Contains("Q/E lean") && l.Contains("R ammo") && l.Contains("F use"));
        }
    }

    /// <summary>
    /// ⭐ CE-1211 (docs/DESIGN_Entity_Control.md §8j Z-1) — holding RMB zooms: a scoped weapon to its scope's field of view, an unscoped one
    /// to the iron sights' 30°; the mouse turns the view slower by the same ratio; letting go, and leaving the unit, give the map its 45° back.
    /// </summary>
    [Fact]
    public void CE1211_HoldingRmb_ZoomsToTheScopeOrTheIronSights_AndSlowsTheLook()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var ammo = new WeaponAmmo { Count = 2 };
            ammo.Set(0, new WeaponState { Ammo = 40, MaxAmmo = 42 });
            ammo.Set(1, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            w.AddComponent(unit, ammo);
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var type = new Fdp.Interfaces.TkbTemplate("Marksman", 4712);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, TurretIndex = -1 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, TurretIndex = -1,
                                                            ScopeMagnification = 4f, ScopeFovDegrees = 8f },
            } });
            db.Register(type);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            w.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 4712 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            var camera = sw.Camera3D;

            float Turn(Input input)
            {
                float before = camera.Yaw;
                input.MouseDelta = new Vector2(100f, 0f);
                sw.Camera3D.HandleInput(input);
                return MathF.Abs(camera.Yaw - before);
            }

            float normal = Turn(new Input());
            Assert.Equal(45f, camera.FovyDegrees);
            float iron = Turn(new Input { RightDown = true });                          // the rifle: no scope
            Assert.True(driver.Zooming);
            Assert.Equal(FpsZoom.IronSightsFovDegrees, camera.FovyDegrees);
            Assert.InRange(iron / normal, 30f / 45f - 0.01f, 30f / 45f + 0.01f);

            var two = new Input { RightDown = true };
            two.Pressed.Add((int)Raylib_cs.KeyboardKey.Two);                            // the scoped rifle
            sw.Camera3D.HandleInput(two);
            float scoped = Turn(new Input { RightDown = true });
            Assert.Equal(8f, camera.FovyDegrees);
            Assert.InRange(scoped / normal, 8f / 45f - 0.01f, 8f / 45f + 0.01f);

            Turn(new Input());                                                          // let go
            Assert.Equal(45f, camera.FovyDegrees);
            Turn(new Input { RightDown = true });
            sw.Camera3D.Driver = null;                                                  // left the unit while zoomed
            Assert.Equal(45f, camera.FovyDegrees);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1078 / CE-1079 / CE-1080 (docs/DESIGN_Entity_Control.md §8e) — the station's side: a LMB PRESS counts a shot and locks the
    /// unit under the crosshair (its id, and the aim point is where the ray meets its box — not its feet); X (RMB before CE-1211) counts a
    /// cancel and drops the lock; F counts an ammo press; 1-4 pick a weapon that exists; the lock clears when the selected weapon's count drops. Counters,
    /// not levels: every frame carries them, so a coalesced or lost frame never loses a press.
    /// </summary>
    [Fact]
    public void CE1078_APress_CountsAShot_LocksTheUnderCrosshairUnit_XCancels_FCountsAmmo_NumbersPickAWeapon()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var ammo = new WeaponAmmo { Count = 2 };
            ammo.Set(0, new WeaponState { Ammo = 40, MaxAmmo = 42 });
            ammo.Set(1, new WeaponState { Ammo = 200, MaxAmmo = 200 });
            w.AddComponent(unit, ammo);
            // ⭐ CE-1127 — weapon 1 a grenade launcher (LOCK), weapon 2 a rifle (SIGHT)
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var type = new Fdp.Interfaces.TkbTemplate("Grenadier", 4711);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade, TurretIndex = -1 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, TurretIndex = -1 },
            } });
            db.Register(type);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            w.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 4711 });
            var (_, sw, _) = Map();
            sw.Camera3D.Pickers.Add(new UnitAhead());
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);

            OperatorControlFrameEvent Frame(Input input)
            {
                sw.Camera3D.HandleInput(input);
                w.Bus.SwapBuffers();
                return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            }

            var f = Frame(new Input { LeftPressed = true, LeftDown = true });
            Assert.Equal((1, 77L, 0), (f.ShotSeq, f.ShotTarget, f.CancelSeq));
            Assert.InRange(Vector3.Distance(f.AimPoint, sw.Camera3D.Eye + sw.Camera3D.Forward * 30f), 0f, 0.01f);   // the box hit, not the picker's feet point
            Assert.True(driver.ShotPending);
            Assert.Contains(PossessionStation.Hud.Lines(w, unit, 0, true, false, null, driver.ShotPending), l => l.StartsWith("LOCKED"));

            f = Frame(new Input());                               // no press: the counter holds, so a lost frame loses nothing
            Assert.Equal(1, f.ShotSeq);
            var spent = w.GetComponentRO<WeaponAmmo>(unit);
            spent.Ammo0 = 39;                                     // the round left
            w.SetComponent(unit, spent);
            Frame(new Input());
            Assert.False(driver.ShotPending);

            Frame(new Input { LeftPressed = true });
            f = Frame(new Input { RightPressed = true, RightDown = true });   // ⭐ CE-1211 — RMB zooms now; it cancels nothing
            Assert.Equal((2, 0), (f.ShotSeq, f.CancelSeq));
            Assert.True(driver.ShotPending);
            var x = new Input();
            x.Pressed.Add((int)Raylib_cs.KeyboardKey.X);           // ⭐ CE-1211 (§8j Z-2) — X is the cancel
            f = Frame(x);
            Assert.Equal((2, 1), (f.ShotSeq, f.CancelSeq));
            Assert.False(driver.ShotPending);

            var keys = new Input();
            keys.Pressed.Add((int)Raylib_cs.KeyboardKey.R);
            keys.Pressed.Add((int)Raylib_cs.KeyboardKey.Two);
            f = Frame(keys);
            Assert.Equal((1, 1), (f.AmmoSeq, f.Mount));
            var four = new Input();
            four.Pressed.Add((int)Raylib_cs.KeyboardKey.Four);    // only two weapons: no fourth
            Assert.Equal(1, Frame(four).Mount);
            var one = new Input();
            one.Pressed.Add((int)Raylib_cs.KeyboardKey.One);
            Assert.Equal(0, Frame(one).Mount);

            // ⭐ CE-1127 (§8g F-2) — the rifle fires by SIGHT: a press still counts (the brain fires it at once), but nothing is held
            var two = new Input();
            two.Pressed.Add((int)Raylib_cs.KeyboardKey.Two);
            Frame(two);
            Assert.Equal(Fdp.Toolkit.Combat.OperatorFireMode.Sight, driver.FireMode);
            f = Frame(new Input { LeftPressed = true, LeftDown = true });
            Assert.Equal(3, f.ShotSeq);
            Assert.False(driver.ShotPending);
        }
    }

    /// <summary>
    /// ⭐ CE-1079 (§8e) — the HUD names the weapon and its round from the TKB: <c>&gt; 1 CANNON  120mm APFSDS 19  41/42  R: 120mm HEAT-MP
    /// LOADING</c> — the kind, the round in the breech and how many of it, the total, what <c>F</c> cycles to, and LOADING during a switch.
    /// 🔒 User, 2026-10-14: "for each weapon selecting ammo and see in the HUD what is selected".
    /// </summary>
    [Fact]
    public void CE1079_TheHud_NamesTheWeapon_TheRoundInItsBreech_WhatRCyclesTo_AndLoading()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            w.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 500 });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var tank = new Fdp.Interfaces.TkbTemplate("Tank", 500);
            tank.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun, AmmoGuid = 501,
                    Loadout = { new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = 501, Rounds = 20 }, new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = 502, Rounds = 22 } } },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, AmmoGuid = 503 },
            } });
            db.Register(tank);
            db.Register(new Fdp.Interfaces.TkbTemplate("120mm APFSDS", 501));
            db.Register(new Fdp.Interfaces.TkbTemplate("120mm HEAT-MP round", 502));
            db.Register(new Fdp.Interfaces.TkbTemplate("7.62x51 ball", 503));
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var ammo = new WeaponAmmo { Count = 2 };
            ammo.Set(0, new WeaponState { Ammo = 41, MaxAmmo = 42 }, new AmmoLoadout { Count = 2, Ammo0 = 501, Rounds0 = 19, Ammo1 = 502, Rounds1 = 22, SwitchRemaining = 4f });
            ammo.Set(1, new WeaponState { Ammo = 2000, MaxAmmo = 2000, MagazineSize = 200, MagazineRounds = 200 });
            w.AddComponent(unit, ammo);

            var lines = PossessionStation.Hud.Lines(w, unit, mount: 0, vehicle: true, hullLocked: false);
            Assert.Equal("> 1 CANNON  120mm APFSDS 19  41/42  R: 120mm HEAT-MP  LOADING", lines[1]);
            Assert.Equal("  2 MG  7.62x51 ball  2000/2000  [200]", lines[2]);
            Assert.Contains("R ammo", lines[^1]);
        }
    }

    /// <summary>⭐ CE-1168 — a person's HANDS mount (backend's B-8: every person type ends with one, empty until he picks something up) is
    /// shown in the HUD as HANDS, not as the grenade category it throws on; his rifle keeps its line.</summary>
    [Fact]
    public void CE1168_TheHud_ShowsAPersonsHandsMount_AsHands()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            w.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 600 });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var man = new Fdp.Interfaces.TkbTemplate("Rifleman", 600);
            man.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, AmmoGuid = 601 },
                Fdp.Toolkit.Combat.PersonHands.Mount(),
            } });
            db.Register(man);
            db.Register(new Fdp.Interfaces.TkbTemplate("5.56 ball", 601));
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var ammo = new WeaponAmmo { Count = 2 };
            ammo.Set(0, new WeaponState { Ammo = 210, MaxAmmo = 210 });
            ammo.Set(1, new WeaponState { Ammo = 0, MaxAmmo = 0 });
            w.AddComponent(unit, ammo);

            var lines = PossessionStation.Hud.Lines(w, unit, mount: 0, vehicle: false, hullLocked: false);
            Assert.StartsWith("> 1 RIFLE", lines[1]);
            Assert.StartsWith("  2 " + PossessionStation.Hud.HandsKind + "  ", lines[2]);
        }
    }

    /// <summary>
    /// ⭐ CE-1140 (supersedes CE-1079's "under the crosshair") — the HUD sits in the BOTTOM-LEFT corner of the VISIBLE map (the dockspace's
    /// open hole, so no docked panel covers it), out of the forward view; with no dockspace, the view's own corner. Each kind of line has
    /// its colour. 🔒 User, 2026-10-16: "the HUD could rather be in the left bottom side of the screen, not obstructing the forward view.
    /// And colored texts".
    /// </summary>
    [Fact]
    public void CE1140_TheHud_SitsBottomLeftOfTheVisibleMap_AndColoursEachKindOfLine()
    {
        var saved = Fdp.Presentation.WindowManager.OpenMapArea.Current;
        try
        {
            Fdp.Presentation.WindowManager.OpenMapArea.Current = (400f, 60f, 1400f, 800f);   // panels left and below
            var (x, y) = PossessionStation.HudOrigin(new Vector2(2200f, 1200f), lineCount: 6, width: 600);
            Assert.Equal((416, 60 + 800 - 16 - 6 * PossessionStation.HudLineHeight), (x, y));
            Fdp.Presentation.WindowManager.OpenMapArea.Current = null;                         // no dockspace: the view's corner
            (x, y) = PossessionStation.HudOrigin(new Vector2(800f, 600f), lineCount: 6, width: 300);
            Assert.Equal((16, 600 - 16 - 6 * PossessionStation.HudLineHeight), (x, y));
        }
        finally { Fdp.Presentation.WindowManager.OpenMapArea.Current = saved; }

        var (w, unit, _) = World();
        using (w)
        {
            var hurt = PossessionStation.Hud.ColourOf(w, unit, "HEALTH 80/100", false);
            Assert.True(hurt.G > hurt.R, "80 % health is green");
            Assert.NotEqual(PossessionStation.Hud.ColourOf(w, unit, "> 1 RIFLE  30/30", false), PossessionStation.Hud.ColourOf(w, unit, "  2 GRENADE  2/2", false));
            Assert.NotEqual(PossessionStation.Hud.ColourOf(w, unit, "STANCE PRONE", false), PossessionStation.Hud.ColourOf(w, unit, "LOCKED - fires", false));
        }
    }

    /// <summary>⭐ CE-1140 — the open map area is read from the dockspace only when it is a sane rectangle inside the window; anything
    /// else falls back to "unknown" (the whole view), never to a mis-placed corner.</summary>
    [Fact]
    public void CE1140_TheOpenMapArea_RejectsAnInsaneRectangle()
    {
        Assert.True(Fdp.Presentation.WindowManager.OpenMapArea.IsSane(400f, 60f, 1400f, 800f, new Vector2(2200f, 1200f)));
        Assert.False(Fdp.Presentation.WindowManager.OpenMapArea.IsSane(0f, 0f, 0f, 0f, new Vector2(2200f, 1200f)));
        Assert.False(Fdp.Presentation.WindowManager.OpenMapArea.IsSane(100f, 100f, 3000f, 800f, new Vector2(2200f, 1200f)));
        Assert.False(Fdp.Presentation.WindowManager.OpenMapArea.IsSane(float.NaN, 0f, 500f, 500f, new Vector2(2200f, 1200f)));
    }

    /// <summary>
    /// ⭐⭐ CE-1095 (docs/DESIGN_Entity_Control.md §8f — air hand-off U3) — a possessed PLANE is flown by looking: its scheme is FixedWing
    /// (no longer Person, which walked it and cycled a stance on C), the frame carries the look's heading as <c>Yaw</c>, its pitch as
    /// <c>Vertical</c> (÷ the model's 25°), W / S as the throttle and no strafe; the view sits on the plane and BANKS with it (its pitch stays
    /// the look's); the HUD gives speed, height and heading. Letting go unbanks the operator's view.
    /// </summary>
    [Fact]
    public void CE1095_APlane_FliesByTheLook_TheViewBanksWithIt_AndTheHudGivesSpeedHeightHeading()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            w.RegisterComponent<SimVelocity>();
            w.AddComponent(unit, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing });
            // flying north-east at 300 m, banked 30° to the right (right wing down)
            var bank = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 30f * MathF.PI / 180f);
            var heading = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f);
            w.SetComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 300f), Rotation = heading * bank });
            w.AddComponent(unit, new SimVelocity { Linear = new Vector3(106f, 106f, 0f) });
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.Equal(ControlScheme.FixedWing, driver.Scheme);
            sw.Camera3D.Update(0.016f);
            Assert.InRange(sw.Camera3D.Eye.Z, 301f, 302f);   // on the plane
            float roll = Fdp.Toolkit.Motion.RotorcraftModel.TiltOf(heading * bank).Roll;
            Assert.True(MathF.Abs(roll) > 0.4f);
            Assert.Equal(roll, sw.Camera3D.Roll, 4);           // the view banks with the body
            Assert.True(Vector3.Dot(sw.Camera3D.Up, sw.Camera3D.Right) is > -1e-4f and < 1e-4f);

            var input = new Input { MouseDelta = new Vector2(0f, -100f) };   // look up 0.25 rad
            input.Keys.Add((int)Raylib_cs.KeyboardKey.W);
            input.Keys.Add((int)Raylib_cs.KeyboardKey.A);
            input.Keys.Add((int)Raylib_cs.KeyboardKey.C);
            sw.Camera3D.HandleInput(input);
            w.Bus.SwapBuffers();
            var frame = Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            Assert.Equal(1f, frame.Forward);                                   // throttle up
            Assert.Equal(0f, frame.Lateral);                                    // a plane does not strafe
            Assert.Equal(0.25f / FpsCameraDriver.FullClimbLook, frame.Vertical, 3);   // the look's pitch asks the climb
            Assert.Equal(driver.Yaw, frame.Yaw);
            Assert.Equal((byte)0, frame.Stance);                                 // C is no stance on a plane
            Assert.Null(driver.Stance);

            var hud = PossessionStation.Hud.Lines(w, unit, 0, driver.IsVehicle, false, driver.Stance, false, driver.Scheme);
            Assert.Equal("SPD 150 m/s  ALT 300 m  HDG 045", hud[0]);
            Assert.Contains(hud, l => l.StartsWith("mouse: fly where you look"));
            Assert.DoesNotContain(hud, l => l.StartsWith("STANCE"));

            sw.Camera3D.Driver = null;
            Assert.Equal(0f, sw.Camera3D.Roll);                                  // the operator's own view is never banked
        }
    }
}
