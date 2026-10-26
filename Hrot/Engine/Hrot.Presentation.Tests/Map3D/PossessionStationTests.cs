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
        public bool MiddleDown { get; set; }      // ⭐ CE-1293 — a map drag
        public bool? Locked { get; private set; }
        public bool IsMouseButtonPressed(MapMouseButton b) => b == MapMouseButton.Left ? LeftPressed : b == MapMouseButton.Right && RightPressed;
        public bool IsMouseButtonDown(MapMouseButton b) => b == MapMouseButton.Left ? LeftDown : b == MapMouseButton.Right ? RightDown : b == MapMouseButton.Middle && MiddleDown;
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
            var station = new PossessionStation(sw, () => w) { OverShoulder = false };   // ⭐ CE-1293 — the eyes view (the shoulder view is the default)
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
            var station = new PossessionStation(sw, () => w, bodies) { OverShoulder = false };   // ⭐ CE-1293 — the eyes view
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

    /// <summary>⭐ CE-1358 (DESIGN_Weapon_Layers §5k) — the weapon line shows the barrel: its heat once it is worth a word, and OVERHEATED
    /// while the gun is held cooling. 🔴 Red-proof: drop the HUD's heat clause ⇒ red.</summary>
    [Fact]
    public void CE1358_TheHud_ShowsTheBarrelHeat_AndOverheated()
    {
        var (w, unit, _) = World();
        using (w)
        {
            var ammo = new WeaponAmmo { Count = 3 };
            ammo.Set(0, new WeaponState { Ammo = 30, MaxAmmo = 30, Heat = 0.1f });                  // cold enough: nothing said
            ammo.Set(1, new WeaponState { Ammo = 20, MaxAmmo = 30, Heat = 0.72f });                 // warm
            ammo.Set(2, new WeaponState { Ammo = 10, MaxAmmo = 30, Heat = 0.9f, Overheated = 1 });   // held, cooling
            w.AddComponent(unit, ammo);
            var lines = PossessionStation.Hud.Lines(w, unit, mount: 0, vehicle: false, hullLocked: false);
            Assert.Equal("> 1 WEAPON  30/30", lines[1]);
            Assert.Equal("  2 WEAPON  20/30  HEAT 72%", lines[2]);
            Assert.Equal("  3 WEAPON  10/30  OVERHEATED 90%", lines[3]);
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
    /// ⭐ CE-1220 (docs/DESIGN_Entity_Control.md §8k K-1) — killed while held: the 3-D view stays (no cut to the map), the fall screen names
    /// who hit him last; a release just after a hit waits a moment for the death to arrive; K runs a kill cam from the shooter's eye, Tab
    /// takes a squad mate (one possession), Esc goes back to the map as it was.
    /// </summary>
    [Fact]
    public void CE1220_KilledWhileHeld_TheViewStays_NamesTheShooter_KShowsHim_TabTakesASquadMate_EscTheMap()
    {
        var (w, me, map) = World();
        using (w)
        {
            w.RegisterComponent<EntityInfo>();
            w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            w.RegisterEvent<PossessRequestEvent>();
            w.RegisterEvent<ReleasePossessionEvent>();
            w.AddComponent(me, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var leader = w.CreateEntity();
            w.AddComponent(leader, new SimTransform { Position = new Vector3(90f, 50f, 0f), Rotation = Quaternion.Identity });
            map.Register(700, leader);
            w.AddComponent(me, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = leader });
            var sniper = w.CreateEntity();
            w.AddComponent(sniper, new SimTransform { Position = new Vector3(400f, 50f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(sniper, new EntityInfo { Name = new FixedString64("Sniper Bob"), ForceId = ForceId.Hostile });

            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            Assert.True(station.InFps);

            Hrot.UI.Common.Effects.HitFeedback.In(w).Record(sniper, me, new Vector3(100f, 50f, 1.2f), new Vector3(-1f, 0f, 0f), 90f,
                                                           Fdp.Toolkit.Time.SimClock.Of(w).TotalTime);
            w.RemoveComponent<PossessedBy>(me);                                               // the release arrives first …
            station.Update(0.016f);
            Assert.True(station.InFps);                                                       // … just after a hit: wait for the death
            w.SetComponent(me, new Health { Current = 0f, Max = 100f });                       // … and here it is
            station.Update(0.016f);
            var fall = Assert.IsType<FallScreen>(sw.Camera3D.Driver);
            Assert.Same(fall, station.Fall);
            Assert.False(station.InFps);
            Assert.True(sw.Is3D);                                                             // no cut to the map
            Assert.Equal(sniper, fall.Killer);
            Assert.Equal("Sniper Bob", fall.KillerName(w));

            var k = new Input();
            k.Pressed.Add((int)Raylib_cs.KeyboardKey.K);
            sw.Camera3D.HandleInput(k);
            sw.Camera3D.Update(0.016f);
            Assert.True(fall.KillCamOn(station.Clock));
            Assert.InRange(Vector3.Distance(sw.Camera3D.Eye, new Vector3(400f, 50f, FallScreen.KillCamEyeHeight)), 0f, 0.5f);
            Assert.True(sw.Camera3D.Forward.X < -0.9f, "the kill cam looks back at the body");

            var tab = new Input();
            tab.Pressed.Add((int)Raylib_cs.KeyboardKey.Tab);
            sw.Camera3D.HandleInput(tab);
            w.Bus.SwapBuffers();
            Assert.Equal(leader, Assert.Single(w.Bus.Read<PossessRequestEvent>().ToArray()).Unit);   // his squad: the leader

            var esc = new Input();
            esc.Pressed.Add((int)Raylib_cs.KeyboardKey.Escape);
            sw.Camera3D.HandleInput(esc);
            Assert.Null(station.Fall);
            Assert.Null(sw.Camera3D.Driver);
            Assert.False(sw.Is3D);                                                            // the map as he had it (2-D here)
        }
    }

    /// <summary>⭐ CE-1220 (§8k K-1) — a plain release (Esc, a take-over) with no hit on him leaves at once, as before.</summary>
    [Fact]
    public void CE1220_APlainRelease_LeavesAtOnce_NoFallScreen()
    {
        var (w, me, _) = World();
        using (w)
        {
            w.AddComponent(me, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            w.RemoveComponent<PossessedBy>(me);
            station.Update(0.016f);
            Assert.False(station.InFps);
            Assert.Null(station.Fall);
            Assert.Null(sw.Camera3D.Driver);
        }
    }

    /// <summary>
    /// ⭐ CE-1219 (docs/DESIGN_Entity_Control.md §8k B-1) — B held asks for the binoculars in the frame; the view narrows to their 10° only
    /// once the sim has them UP; then LMB designates (his men's MapPoint, or Target under the crosshair) instead of firing; a man who carries
    /// none is told so.
    /// </summary>
    [Fact]
    public void CE1219_BHeldAsksForTheBinoculars_TheViewNarrowsWhenUp_AndLmbDesignatesForHisMen()
    {
        var (w, unit, map) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            w.RegisterComponent<WeaponPosture>();
            w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var observer = new Fdp.Interfaces.TkbTemplate("Observer", 4714);
            observer.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.PocketItemsDto { Binoculars = true });
            db.Register(observer);
            db.Register(new Fdp.Interfaces.TkbTemplate("Rifleman", 4715));
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            w.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 4714 });
            w.AddComponent(unit, new WeaponPosture());
            var man = w.CreateEntity();
            w.AddComponent(man, new SimTransform { Position = new Vector3(105f, 50f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(man, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = unit });
            map.Register(601, man);

            var (_, sw, _) = Map();
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
            var b = new Input();
            b.Keys.Add((int)Raylib_cs.KeyboardKey.B);
            Assert.Equal(1, Frame(b).Binoculars);
            Assert.Equal(45f, sw.Camera3D.FovyDegrees);                                       // asked, not yet up: no zoom
            Assert.False(driver.NoBinoculars);
            w.GetComponentRW<WeaponPosture>(unit).HeldItem = HeldItemKind.Binoculars;          // what his brain does (EmbarkNodesTests.CE1219)
            Frame(b);
            Assert.Equal(Fdp.Toolkit.Combat.HeldItems.BinocularsFovDegrees, sw.Camera3D.FovyDegrees);

            var click = new Input { LeftPressed = true, LeftDown = true };
            click.Keys.Add((int)Raylib_cs.KeyboardKey.B);
            var f = Frame(click);
            Assert.Equal((0, (byte)0), (f.ShotSeq, f.Trigger));                                // no shot
            Assert.True(Hrot.Common.Orders.OrderDesignations.Of(w)!.TryGet(601, Hrot.Common.Orders.DesignationKind.MapPoint, out _));
            Assert.Equal("Point designated for 1 man", driver.WheelVerdict(station.Clock));

            w.GetComponentRW<WeaponPosture>(unit).HeldItem = HeldItemKind.None;
            w.SetComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 4715 });   // a man who carries none
            Frame(b);
            Assert.True(driver.NoBinoculars);
            Assert.Equal(45f, sw.Camera3D.FovyDegrees);
            Assert.Equal(0, Frame(new Input()).Binoculars);                                    // let go
        }
    }

    /// <summary>
    /// ⭐ CE-1218 (docs/DESIGN_Entity_Control.md §8k M-1) — M in first person: one still frame (he stops), the 2-D map centred on him, the
    /// cursor free, the unit still held; M on the map brings the operator back to his eyes (that same press does not reopen the map).
    /// </summary>
    [Fact]
    public void CE1218_M_OpensTheMapCentredOnHim_HeHoldsStill_AndMBringsTheOperatorBack()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            Assert.True(sw.Is3D);
            var input = new Input();
            input.Keys.Add((int)Raylib_cs.KeyboardKey.W);                                       // walking …
            sw.Camera3D.HandleInput(input);
            w.Bus.SwapBuffers();
            Assert.Equal(1f, Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray()).Forward);

            input.Pressed.Add((int)Raylib_cs.KeyboardKey.M);                                    // … M
            sw.Camera3D.HandleInput(input);
            w.Bus.SwapBuffers();
            var still = Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            Assert.Equal((0f, 0f, (byte)0), (still.Forward, still.Lateral, still.Trigger));   // he holds still
            Assert.True(station.MapPeek);
            Assert.False(sw.Is3D);
            Assert.False(input.Locked);                                                         // the cursor is free to pan
            Assert.InRange(Vector2.Distance(new Vector2(sw.CurrentPose.LookAt.X, sw.CurrentPose.LookAt.Y), new Vector2(100f, 50f)), 0f, 1f);
            input.Pressed.Clear(); input.Keys.Clear();
            station.Update(0.016f);
            Assert.True(station.InFps);                                                         // still held
            input.Pressed.Add((int)Raylib_cs.KeyboardKey.M);                                    // M on the map
            station.Update(0.016f);
            Assert.False(station.MapPeek);
            Assert.True(sw.Is3D);
            Assert.True(input.Locked);
            sw.Camera3D.HandleInput(input);                                                     // the same frame's M: not the map again
            Assert.False(station.MapPeek);
        }
    }

    /// <summary>
    /// ⭐ CE-1217 (docs/DESIGN_Entity_Control.md §8k C-1, C-2) — the compass reads the view's heading (0 north, clockwise) and shows ±60°
    /// with the eight points named; the names over heads are his side's, within 50 m, named, alive — nearest first, his own men marked.
    /// </summary>
    [Fact]
    public void CE1217_TheCompassReadsTheHeading_AndTheNamesAreHisSidesWithin50m()
    {
        Assert.Equal(90f, FpsCompass.Heading(new Vector3(1f, 0f, -0.3f)), 3);                   // east, looking a little down
        Assert.Equal(0f, FpsCompass.Heading(new Vector3(0f, 1f, 0f)), 3);
        Assert.Equal(270f, FpsCompass.Heading(new Vector3(-1f, 0f, 0f)), 3);
        var ticks = FpsCompass.Ticks(0f).ToList();
        Assert.Equal(9, ticks.Count);                                                           // −60 … 60, every 15
        Assert.Equal((0f, "N"), (ticks.Single(t => t.Degrees == 0).At, ticks.Single(t => t.Degrees == 0).Label));
        Assert.Equal((0.75f, "NE"), (ticks.Single(t => t.Degrees == 45).At, ticks.Single(t => t.Degrees == 45).Label));
        Assert.Equal((-0.75f, "NW"), (ticks.Single(t => t.Degrees == 315).At, ticks.Single(t => t.Degrees == 315).Label));
        Assert.Equal("", ticks.Single(t => t.Degrees == 15).Label);

        var (w, me, _) = World();
        using (w)
        {
            w.RegisterComponent<EntityInfo>();
            w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            w.AddComponent(me, new EntityInfo { Name = new FixedString64("Me"), ForceId = ForceId.Friend });
            Entity Man(string name, ForceId side, float east, Entity commander = default)
            {
                var m = w.CreateEntity();
                w.AddComponent(m, new SimTransform { Position = new Vector3(100f + east, 50f, 0f), Rotation = Quaternion.Identity });
                w.AddComponent(m, new EntityInfo { Name = new FixedString64(name), ForceId = side });
                if (!commander.IsNull) w.AddComponent(m, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });
                return m;
            }
            var buddy = Man("Buddy", ForceId.Friend, 20f);
            var mine = Man("Mine", ForceId.Friend, 8f, me);
            Man("Far", ForceId.Friend, 80f);
            Man("Enemy", ForceId.Hostile, 10f);
            Man("", ForceId.Friend, 12f);
            var dead = Man("Dead", ForceId.Friend, 5f);
            w.AddComponent(dead, new Health { Current = 0f, Max = 100f });
            var friends = FriendMarks.Of(w, me, new Vector3(100f, 50f, 1.7f));
            Assert.Equal(new[] { "Mine", "Buddy" }, friends.Select(f => f.Name));
            Assert.Equal(new[] { true, false }, friends.Select(f => f.Mine));
            w.SetComponent(me, new EntityInfo { Name = new FixedString64("Me"), ForceId = ForceId.Neutral });
            Assert.Empty(FriendMarks.Of(w, me, new Vector3(100f, 50f, 1.7f)));                  // a civilian marks nobody
        }
    }

    /// <summary>
    /// ⭐ CE-1216 (docs/DESIGN_Entity_Control.md §8k S-1, S-2, S-3) — the spread ring is the sim's own σ for the selected weapon at the
    /// view's scale (doubling when he moves, narrower in a zoom); a round cracking past him darkens the edges, fading in 0.4 s; his own
    /// shot kicks the view up and it settles, while his look (the frame's aim) never moves.
    /// </summary>
    [Fact]
    public void CE1216_TheSpreadRingIsTheSimsSigma_ANearMissShocks_HisShotKicksTheViewAndItSettles()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var type = new Fdp.Interfaces.TkbTemplate("Rifleman", 4713);
            type.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, TurretIndex = -1, DispersionMils = 6f },
            } });
            db.Register(type);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            w.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 4713 });

            float sigma = PossessionStation.SpreadOf(w, unit, 0);
            Assert.Equal(0.006f, sigma, 5);                                                        // 6 mils, standing, still
            float r45 = PossessionStation.SpreadRadiusPx(sigma, 45f, 800f);
            Assert.Equal(MathF.Tan(0.006f) / MathF.Tan(22.5f * MathF.PI / 180f) * 400f, r45, 2);
            Assert.True(PossessionStation.SpreadRadiusPx(sigma, 8f, 800f) > 4f * r45);            // the scope magnifies it
            Assert.Equal(0f, PossessionStation.SpreadRadiusPx(0f, 45f, 800f));

            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);

            w.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
            var round = w.CreateEntity();
            var eye = sw.Camera3D.Eye;
            w.AddComponent(round, new SimTransform { Position = eye + new Vector3(-40f, 1f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(round, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Shooter = Entity.Null });
            station.Update(0.016f);
            Assert.Equal(0f, station.Shock);
            w.SetComponent(round, new SimTransform { Position = eye + new Vector3(40f, 1f, 0f), Rotation = Quaternion.Identity });
            station.Update(0.016f);
            Assert.True(station.Shock > 0.4f, $"a crack shocks ({station.Shock})");
            for (int i = 0; i < 30; i++) station.Update(0.016f);
            Assert.Equal(0f, station.Shock);                                                     // gone in 0.4 s

            var shots = Hrot.UI.Common.Effects.RecentShots.In(w);
            shots.Record(unit, 0, 1.0);
            sw.Camera3D.Update(0.016f);                                                          // the first one he is seen with: no kick
            Assert.Equal(0f, driver.Kick);
            float look = sw.Camera3D.Pitch;
            shots.Record(unit, 0, 2.0);
            sw.Camera3D.Update(0.016f);
            Assert.True(driver.Kick > 0.8f * FpsCameraDriver.KickRadians(Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms));
            Assert.True(sw.Camera3D.Pitch > look, "the view kicks up");
            for (int i = 0; i < 20; i++) sw.Camera3D.Update(0.016f);
            Assert.Equal(0f, driver.Kick);
            Assert.Equal(look, sw.Camera3D.Pitch, 4);                                            // settled where he looked
        }
    }

    /// <summary>
    /// ⭐ CE-1214 (docs/DESIGN_Entity_Control.md §8j T-1) — Tab jumps to the next man of his squad (his leader, the men naming that leader,
    /// his own men — in network-id order, wrapping), Shift+Tab to the previous; another leader's man, a dead man and a man another
    /// station holds are not in it; the jump is ONE possession, which also lets the man he leaves go back to his brain.
    /// </summary>
    [Fact]
    public void CE1214_Tab_JumpsToTheNextManOfHisSquad_ShiftTabBack_AndTheManHeLeavesIsReleased()
    {
        var (w, me, map) = World();                                                    // network id 4242
        using (w)
        {
            w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            w.AddComponent(me, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            Entity Man(long id, Entity commander, float health = 100f)
            {
                var m = w.CreateEntity();
                w.AddComponent(m, new SimTransform { Position = new Vector3(105f, 50f, 0f), Rotation = Quaternion.Identity });
                w.AddComponent(m, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });
                w.AddComponent(m, new Health { Current = health, Max = 100f });
                map.Register(id, m);
                return m;
            }
            var leader = Man(100, Entity.Null);
            w.AddComponent(me, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = leader });
            var buddy = Man(5000, leader);
            var mine = Man(9000, me);
            Man(6000, Man(7000, Entity.Null));                                         // another squad
            Man(5500, leader, health: 0f);                                             // dead
            var held = Man(5600, leader);
            w.AddComponent(held, new PossessedBy { StationNodeId = 99, Mode = PossessionMode.Fps, Epoch = 1 });   // another station's

            Assert.Equal(new[] { 100L, 4242L, 5000L, 9000L }, SquadRoster.Of(w, me).Select(r => r.Id));
            Assert.Equal(buddy, SquadRoster.Next(w, me, 1));
            Assert.Equal(leader, SquadRoster.Next(w, me, -1));
            Assert.Equal(me, SquadRoster.Next(w, mine, 1));                            // my man's squad is his leader (me) and him: it wraps
            Assert.Equal(Entity.Null, SquadRoster.Next(w, Man(8000, Entity.Null), 1)); // alone

            w.RegisterEvent<PossessRequestEvent>();
            w.RegisterEvent<ReleasePossessionEvent>();
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var tab = new Input();
            tab.Pressed.Add((int)Raylib_cs.KeyboardKey.Tab);
            sw.Camera3D.HandleInput(tab);
            w.Bus.SwapBuffers();
            Assert.Equal(buddy, Assert.Single(w.Bus.Read<PossessRequestEvent>().ToArray()).Unit);
            Assert.Equal(me, Assert.Single(w.Bus.Read<ReleasePossessionEvent>().ToArray()).Unit);   // the one he leaves goes back to his brain

            var back = new Input();
            back.Pressed.Add((int)Raylib_cs.KeyboardKey.Tab);
            back.Keys.Add((int)Raylib_cs.KeyboardKey.LeftShift);
            sw.Camera3D.HandleInput(back);
            w.Bus.SwapBuffers();
            Assert.Equal(leader, Assert.Single(w.Bus.Read<PossessRequestEvent>().ToArray()).Unit);
        }
    }

    /// <summary>
    /// ⭐ CE-1213 (docs/DESIGN_Entity_Control.md §8j W-1) — hold T: the wheel shows the orders his MEN fulfil (Hold and Move first, none
    /// needing the form); the mouse moves its cursor, not the look; letting go sends the sector under the cursor to every man naming him as
    /// commander — never to himself, never to another leader's man — completed from the crosshair; letting go in the middle sends nothing.
    /// </summary>
    [Fact]
    public void CE1213_HoldingT_OpensTheWheelOnHisMensOrders_AndLettingGoSendsTheSectorToThem()
    {
        var (w, leader, map) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            w.AddComponent(leader, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var geo = new Fdp.Modules.Geographic.Transforms.WGS84Transform();   // every live host has one: a point order goes geodetic
            geo.SetOrigin(52.520, 13.405, 0.0);
            w.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
            var soldier = new Fdp.Core.DISEntityType { Value = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24) };
            Entity Man(long id, Entity commander)
            {
                var m = w.CreateEntity();
                w.AddComponent(m, new SimTransform { Position = new Vector3(105f, 50f, 0f), Rotation = Quaternion.Identity });
                w.AddComponent(m, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });
                w.SetDisType(m, soldier);
                map.Register(id, m);
                return m;
            }
            var a = Man(501, leader);
            var b = Man(502, leader);
            var stranger = Man(503, w.CreateEntity());
            Assert.Equal(new[] { a, b }, CommandWheel.MenOf(w, leader));
            var items = CommandWheel.Items(w, leader);
            Assert.Equal(new[] { "Hold", "Move" }, items.Take(2).Select(o => o.Name));
            Assert.All(items, o => Assert.True(CommandWheel.OneGesture(o), o.Name));
            Assert.Empty(CommandWheel.Items(w, a));                                    // a man who leads nobody: an empty ring

            Assert.Equal(0, CommandWheel.Pick(new Vector2(0f, -50f), 4));             // up, then clockwise
            Assert.Equal(1, CommandWheel.Pick(new Vector2(50f, 0f), 4));
            Assert.Equal(2, CommandWheel.Pick(new Vector2(0f, 50f), 4));
            Assert.Equal(3, CommandWheel.Pick(new Vector2(-50f, 0f), 4));
            Assert.Equal(-1, CommandWheel.Pick(new Vector2(5f, 5f), 4));              // the dead zone

            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent[] Choose(string order)
            {
                var t = new Input();
                t.Keys.Add((int)Raylib_cs.KeyboardKey.T);
                sw.Camera3D.HandleInput(t);                                            // T pressed: the wheel opens
                Assert.True(driver.WheelOpen);
                int i = driver.WheelItems.Select(o => o.Name).ToList().IndexOf(order);
                t.MouseDelta = CommandWheel.Direction(i, driver.WheelItems.Count) * 80f;
                float yaw = sw.Camera3D.Yaw;
                sw.Camera3D.HandleInput(t);                                            // held: the mouse moves the wheel's cursor
                Assert.Equal(yaw, sw.Camera3D.Yaw);                                    // … not the look
                w.Bus.SwapBuffers();
                sw.Camera3D.HandleInput(new Input());                                  // let go: sent
                Assert.False(driver.WheelOpen);
                w.Bus.SwapBuffers();
                return w.Bus.ReadManaged<Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent>().ToArray();
            }

            var hold = Choose("Hold");
            Assert.Equal(new[] { a, b }, hold.Select(o => o.Entity).OrderBy(e => e.Index));
            Assert.All(hold, o => Assert.Equal("Order.Hold", o.IntentId));
            Assert.Equal("Hold: 2 of 2 men", driver.WheelVerdict(station.Clock));

            var move = Choose("Move");
            Assert.Equal(2, move.Length);
            Assert.All(move, o => Assert.Equal("Order.Move", o.IntentId));
            Assert.All(move, o => Assert.Contains("\"Location\"", o.JsonParams));      // the crosshair's point
            Assert.DoesNotContain(move.Concat(hold), o => o.Entity == leader || o.Entity == stranger);

            var t2 = new Input();
            t2.Keys.Add((int)Raylib_cs.KeyboardKey.T);
            sw.Camera3D.HandleInput(t2);
            w.Bus.SwapBuffers();
            sw.Camera3D.HandleInput(new Input());                                      // let go in the middle
            w.Bus.SwapBuffers();
            Assert.Empty(w.Bus.ReadManaged<Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent>().ToArray());
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
    public void CE1095_APlane_FliesByTheKeys_TheViewBanksWithIt_AndTheHudGivesSpeedHeightHeading()
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
            var station = new PossessionStation(sw, () => w) { OverShoulder = false };   // ⭐ CE-1293 — the eyes view (the shoulder view is the default)
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
            // ⭐ CE-1351 — classic keys: C (or Z) dives, A turns left (the heading led ahead of the nose); the look's pitch asks nothing
            Assert.Equal(-1f, frame.Vertical);
            Assert.Equal(MathF.PI / 4f + FpsCameraDriver.FlyerTurnLead, frame.Yaw, 2);
            Assert.Equal((byte)0, frame.Stance);                                 // C is no stance on a plane
            Assert.Null(driver.Stance);

            var hud = PossessionStation.Hud.Lines(w, unit, 0, driver.IsVehicle, false, driver.Stance, false, driver.Scheme);
            Assert.Equal("SPD 150 m/s  ALT 300 m  HDG 045", hud[0]);
            Assert.Contains(hud, l => l.StartsWith("W/S throttle  A/D turn  Q/Z climb/dive"));
            Assert.DoesNotContain(hud, l => l.StartsWith("STANCE"));

            sw.Camera3D.Driver = null;
            Assert.Equal(0f, sw.Camera3D.Roll);                                  // the operator's own view is never banked
        }
    }

    /// <summary>⭐ <c>CE-1351</c> — a vehicle steers on A/D whether its view is hull-locked (V) or not, and the locked view turns with the
    /// hull; a helicopter turns on A/D (never strafes), climbs on Q and descends on Z, and V locks its view forward too. 🔒 User,
    /// 2026-10-25: "V key switches to mouse steering - i would expect normal A-D key steering" · "possessed helicopter needs some key to go
    /// up and down vertically, maybe Q/Z". ✅ Red-proof: the old mouse-steer back ⇒ Steer 0 with A held while locked.</summary>
    [Fact]
    public void CE1351_VLocksTheViewToTheHull_TheKeysStillSteer_AndAHelicopterClimbsOnQZ()
    {
        OperatorControlFrameEvent Frame(EntityRepository w, PossessionStation st, MapViewSwitch sw, params Raylib_cs.KeyboardKey[] held)
        {
            var input = new Input();
            foreach (var k in held) input.Keys.Add((int)k);
            st.Update(0.016f);
            sw.Camera3D.HandleInput(input);
            w.Bus.SwapBuffers();
            return w.Bus.Read<OperatorControlFrameEvent>().ToArray()[^1];
        }
        void Press(PossessionStation st, MapViewSwitch sw, Raylib_cs.KeyboardKey key)
        {
            var input = new Input();
            input.Pressed.Add((int)key);
            st.Update(0.016f);
            sw.Camera3D.HandleInput(input);
        }

        // a ground vehicle
        var (w, unit, _) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            w.AddComponent(unit, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled });
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w) { OverShoulder = false };
            station.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.True(driver.IsVehicle);
            Assert.Equal(1f, Frame(w, station, sw, Raylib_cs.KeyboardKey.A).Steer);    // free view: A steers
            Press(station, sw, Raylib_cs.KeyboardKey.V);
            Assert.True(driver.HullLocked);
            Assert.Equal(1f, Frame(w, station, sw, Raylib_cs.KeyboardKey.A).Steer);    // hull-locked: A still steers
            Assert.Equal(-1f, Frame(w, station, sw, Raylib_cs.KeyboardKey.D).Steer);
            w.SetComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            Assert.Equal(MathF.PI / 2f, driver.Yaw, 3);                               // the locked view turned with the hull
        }

        // a helicopter
        (w, unit, _) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            w.AddComponent(unit, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            w.SetComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 50f), Rotation = Quaternion.Identity });
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w) { OverShoulder = false };
            station.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.Equal(ControlScheme.Rotorcraft, driver.Scheme);
            var up = Frame(w, station, sw, Raylib_cs.KeyboardKey.Q, Raylib_cs.KeyboardKey.A);
            Assert.Equal(1f, up.Vertical);
            Assert.Equal(0f, up.Lateral);                                             // A turns, it does not strafe
            Assert.Equal(FpsCameraDriver.FlyerTurnLead, up.Yaw, 3);                   // the heading led to the left of the nose
            var down = Frame(w, station, sw, Raylib_cs.KeyboardKey.Z);
            Assert.Equal(-1f, down.Vertical);
            Assert.Equal(0f, down.Yaw, 3);                                            // released: it holds its heading
            Assert.Equal((byte)0, down.Stance);                                       // Z is no stance on a flyer
            Press(station, sw, Raylib_cs.KeyboardKey.V);
            Assert.True(driver.HullLocked);
        }
    }

    /// <summary>⭐ <c>CE-1352</c> — the sim moves a possessed vehicle AFTER the map placed its camera; just before the frame is drawn the
    /// camera is put back on it, so the two are never a step apart (the stutter). Over the shoulder (the chase camera) and at the eyes.
    /// ✅ Red-proof: <c>BeforeDraw</c> emptied ⇒ the camera stays 3 m behind where it should be.</summary>
    [Fact]
    public void CE1352_ThePossessedVehiclesCamera_IsPutBackOnItJustBeforeDrawing()
    {
        foreach (bool shoulder in new[] { true, false })
        {
            var (w, unit, _) = World();
            using (w)
            {
                w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
                w.AddComponent(unit, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled });
                w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
                var (_, sw, _) = Map();
                var station = new PossessionStation(sw, () => w) { OverShoulder = shoulder };
                station.Update(0.016f);
                Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
                sw.Camera3D.Update(0.016f);
                var before = sw.Camera3D.Eye;
                var tf = w.GetComponentRO<SimTransform>(unit);
                w.SetComponent(unit, tf with { Position = tf.Position + new Vector3(3f, 0f, 0f) });   // the sim's step, after the map's update
                sw.Camera3D.PrepareDraw();
                Assert.True(Vector3.Distance(before + new Vector3(3f, 0f, 0f), sw.Camera3D.Eye) < 0.05f,
                    $"shoulder={shoulder}: the camera {sw.Camera3D.Eye} did not follow the vehicle (was {before})");
            }
        }
    }

    /// <summary>⭐ <c>CE-1354</c> — after A/D is let go a helicopter HOLDS its heading. The loop as it runs: the station sends a heading each
    /// frame and its MOTION applies it two ticks on (OperatorInput, then ManualDrive), turning a small gap in one tick (40°/s). Measured live before the fix: the sent yaw
    /// alternated 0.9566 / 0.9774 frame by frame for ever — the camera and the gun ring shivering. ✅ Red-proof: the heading re-read from
    /// the airframe every frame (the old FlyerHeading(hull, 0)) ⇒ the hull alternates by one turn step.</summary>
    [Fact]
    public void CE1354_AfterATurn_AHelicopterHoldsItsHeading_NoFrameToFrameSwing()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            w.AddComponent(unit, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            w.SetComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 40f), Rotation = Quaternion.Identity });
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            const float dt = 1f / 30f, rate = 40f;
            var inFlight = new Queue<float>();   // ⭐ a frame reaches the motion two ticks on (OperatorInput, then ManualDrive), as measured live
            var hull = new List<float>();
            var sent = new List<float>();
            for (int i = 0; i < 80; i++)
            {
                // the sim's tick: last frame's heading, at the type's turn rate
                var tf = w.GetComponentRO<SimTransform>(unit);
                float yaw = Fdp.Toolkit.Motion.RotorcraftModel.YawOf(tf.Rotation);
                if (inFlight.Count >= 2) yaw = Fdp.Toolkit.Motion.RotorcraftModel.TurnToward(yaw, inFlight.Dequeue(), rate, dt);
                w.SetComponent(unit, tf with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
                // the map's frame
                station.Update(dt);
                sw.Camera3D.Update(dt);
                var input = new Input();
                if (i < 15) input.Keys.Add((int)Raylib_cs.KeyboardKey.A);   // turn left for half a second, then let go
                sw.Camera3D.HandleInput(input);
                sw.Camera3D.PrepareDraw();
                w.Bus.SwapBuffers();
                float sentYaw = w.Bus.Read<OperatorControlFrameEvent>().ToArray()[^1].Yaw;
                inFlight.Enqueue(sentYaw);
                if (i >= 40) { hull.Add(yaw); sent.Add(sentYaw); }
            }
            Assert.True(hull[0] > 0.2f, $"it turned ({hull[0]:F3} rad)");
            Assert.True(hull.Max() - hull.Min() < 1e-4f, $"the hull swings {hull.Min():F5}..{hull.Max():F5} after the turn");
            Assert.True(sent.Max() - sent.Min() < 1e-6f, $"the sent heading swings {sent.Min():F5}..{sent.Max():F5}");
        }
    }
}
