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
public sealed class PossessionStationTests
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
        public bool? Locked { get; private set; }
        public bool IsMouseButtonPressed(MapMouseButton b) => b == MapMouseButton.Left ? LeftPressed : b == MapMouseButton.Right && RightPressed;
        public bool IsMouseButtonDown(MapMouseButton b) => b == MapMouseButton.Left && LeftDown;
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
    /// ⭐⭐ CE-1074 (docs/DESIGN_Entity_Control.md §13.5) — C cycles the held person's stance, standing → crouched → prone → standing, from
    /// the stance he has when first driven; every frame carries the wanted stance (StanceId + 1) and the HUD shows it. 🔒 User,
    /// 2026-10-14: "I will need to be able to switch the possessed character to crouched as well as lying pose (cycling using a key press)".
    /// </summary>
    [Fact]
    public void CE1074_C_CyclesTheStance_StandCrouchProneStand_InEveryFrame_AndTheHudSaysIt()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);

            byte Frame(bool pressC)
            {
                var input = new Input();
                if (pressC) input.Pressed.Add((int)Raylib_cs.KeyboardKey.C);
                sw.Camera3D.HandleInput(input);
                w.Bus.SwapBuffers();
                return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray()).Stance;
            }

            Assert.Equal(1, Frame(false));     // standing (no intent ⇒ standing), held every frame
            Assert.Equal(2, Frame(true));      // crouched
            Assert.Equal(2, Frame(false));     // a frame without the key keeps it
            Assert.Equal(3, Frame(true));      // prone
            Assert.Equal(1, Frame(true));      // standing again
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.Contains("STANCE STANDING", PossessionStation.Hud.Lines(w, unit, 0, false, false, driver.Stance));
            Assert.Contains(PossessionStation.Hud.Lines(w, unit, 0, false, false, driver.Stance), l => l.Contains("C stance"));
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
    /// ⭐⭐ CE-1078 / CE-1079 / CE-1080 (docs/DESIGN_Entity_Control.md §8e) — the station's side: a LMB PRESS counts a shot and locks the
    /// unit under the crosshair (its id, and the aim point is where the ray meets its box — not its feet); RMB counts a cancel and drops
    /// the lock; F counts an ammo press; 1-4 pick a weapon that exists; the lock clears when the selected weapon's count drops. Counters,
    /// not levels: every frame carries them, so a coalesced or lost frame never loses a press.
    /// </summary>
    [Fact]
    public void CE1078_APress_CountsAShot_LocksTheUnderCrosshairUnit_RmbCancels_FCountsAmmo_NumbersPickAWeapon()
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
            f = Frame(new Input { RightPressed = true });
            Assert.Equal((2, 1), (f.ShotSeq, f.CancelSeq));
            Assert.False(driver.ShotPending);

            var keys = new Input();
            keys.Pressed.Add((int)Raylib_cs.KeyboardKey.F);
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
    /// ⭐ CE-1079 (§8e) — the HUD names the weapon and its round from the TKB: <c>&gt; 1 CANNON  120mm APFSDS 19  41/42  F: 120mm HEAT-MP
    /// LOADING</c> — the kind, the round in the breech and how many of it, the total, what <c>F</c> cycles to, and LOADING during a switch.
    /// 🔒 User, 2026-10-14: "for each weapon selecting ammo and see in the HUD what is selected".
    /// </summary>
    [Fact]
    public void CE1079_TheHud_NamesTheWeapon_TheRoundInItsBreech_WhatFCyclesTo_AndLoading()
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
            Assert.Equal("> 1 CANNON  120mm APFSDS 19  41/42  F: 120mm HEAT-MP  LOADING", lines[1]);
            Assert.Equal("  2 MG  7.62x51 ball  2000/2000  [200]", lines[2]);
            Assert.Contains("F ammo", lines[^1]);
        }
    }

    /// <summary>⭐ CE-1079 — the HUD sits UNDER the crosshair, not in a window corner: the editor draws the map beneath its docked
    /// panels, whose left column and bottom log cover the corners (a corner HUD was invisible, measured live in a 2200×1200 window); in a
    /// short view every line still fits.</summary>
    [Fact]
    public void CE1079_TheHud_SitsUnderTheCrosshair_AndFitsAShortView()
    {
        var (x, y) = PossessionStation.HudOrigin(new Vector2(2200f, 1200f), lineCount: 6, width: 600);
        Assert.Equal((1100 + 110 - 600, 600 + 84), (x, y));          // just under the crosshair, ending a little right of it
        Assert.Equal(8, PossessionStation.HudOrigin(new Vector2(400f, 300f), 6, 600).X);   // never off the left edge
        var (_, ys) = PossessionStation.HudOrigin(new Vector2(800f, 300f), lineCount: 6, width: 600);
        Assert.True(ys + 6 * PossessionStation.HudLineHeight <= 300, $"the last line ends at {ys + 6 * PossessionStation.HudLineHeight}");
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
