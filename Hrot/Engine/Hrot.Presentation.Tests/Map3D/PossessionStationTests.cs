using System.Collections.Generic;
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
        public bool? Locked { get; private set; }
        public bool IsMouseButtonPressed(MapMouseButton b) => false;
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
            Assert.Equal("  WEAPON 0  27/30  [7]", lines[1]);
            Assert.Equal("> WEAPON 1  2/4", lines[2]);
            Assert.Contains("Esc release", lines[3]);
        }
    }
}
