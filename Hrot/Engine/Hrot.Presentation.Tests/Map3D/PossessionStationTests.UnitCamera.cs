using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Behavior.Possession;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>
    /// ⭐⭐ CE-1293 (docs/DESIGN_Unit_Camera.md §4, UC-1) — the camera continuum: far away it orbits the unit's centre, near it is over his
    /// shoulder (to the right and up, the §8p offsets), at zero it is his eye; a vehicle's distances are 4× a person's.
    /// </summary>
    [Fact]
    public void CE1293_TheContinuum_OrbitsTheCentreFarAway_IsOverTheShoulderNear_AndHisEyeAtZero()
    {
        var centre = new Vector3(0f, 0f, 0.9f);
        var eye = new Vector3(0.1f, 0f, 1.6f);
        var east = Vector3.UnitX;

        Assert.Equal(eye, UnitCameraRig.Pivot(centre, eye, east, 0f, ControlScheme.Person, side: 1));               // zero: his eye
        var near = UnitCameraRig.Pivot(centre, eye, east, 2.2f, ControlScheme.Person, side: 1);
        Assert.Equal(-UnitCameraRig.Side, near.Y, 3);                                                                 // to his right (−Y)
        Assert.Equal(eye.Z + UnitCameraRig.Lift, near.Z, 3);
        var left = UnitCameraRig.Pivot(centre, eye, east, 2.2f, ControlScheme.Person, side: -1);
        Assert.Equal(UnitCameraRig.Side, left.Y, 3);                                                                  // Q: his left
        Assert.Equal(centre, UnitCameraRig.Pivot(centre, eye, east, 20f, ControlScheme.Person, side: 1));           // far: his centre
        Assert.Equal(1f, UnitCameraRig.NearBlend(9f, ControlScheme.Vehicle));                                       // a vehicle at 9 m: near
        Assert.Equal(0f, UnitCameraRig.NearBlend(9f * 4f + 20f, ControlScheme.Vehicle), 3);

        // playing, the camera goes back along the LEVEL look (looking down does not lift it); watching, it orbits
        var down = Vector3.Normalize(new Vector3(1f, 0f, -1f));
        Assert.Equal(near.Z, UnitCameraRig.Camera(near, down, 2.2f, orbitPitch: false).Z, 3);
        Assert.True(UnitCameraRig.Camera(near, down, 2.2f, orbitPitch: true).Z > near.Z + 1f);
    }

    /// <summary>⭐ CE-1293 (UC-4) — the spring arm stops the camera in front of the first wall, and comes back out slowly.</summary>
    [Fact]
    public void CE1293_TheSpringArm_StopsInFrontOfAWall_AndEasesBackOut()
    {
        var pivot = new Vector3(0f, 0f, 1.6f);
        var wanted = new Vector3(-4f, 0f, 1.6f);
        Assert.Equal(wanted, UnitCameraRig.SpringArm(pivot, wanted, (_, _) => null));
        var stopped = UnitCameraRig.SpringArm(pivot, wanted, (_, _) => 0.5f);                                       // a wall 2 m behind
        Assert.Equal(-(2f - UnitCameraRig.ArmMargin), stopped.X, 3);

        Assert.Equal(1f, UnitCameraRig.EaseArm(3f, 1f, 0.016f));                                                    // in at once
        Assert.Equal(1f + UnitCameraRig.ArmOutRate * 0.1f, UnitCameraRig.EaseArm(1f, 3f, 0.1f), 3);                 // out slowly
    }

    /// <summary>
    /// ⭐⭐ CE-1293 (UC-5, UC-6, UC-11, R-1033) — playing over the shoulder: the plain wheel still switches weapons, Alt + wheel moves the
    /// camera (in to the eyes switches the view off); RMB aims from the eyes; E / Q put the camera over that shoulder; standing, the body
    /// lags the aim up to 45°; moving, the frame's W is along the CAMERA (turned into the body's frame).
    /// </summary>
    [Fact]
    public void CE1293_OverTheShoulder_AltWheelIsTheDistance_RmbAimsFromTheEyes_TheBodyLagsTheAim_AndWMovesAlongTheCamera()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w) { OverShoulder = true };
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);

            OperatorControlFrameEvent Frame(float dx = 0f, float wheel = 0f, bool rmb = false, params Raylib_cs.KeyboardKey[] hold)
            {
                var input = new Input { MouseDelta = new Vector2(dx, 0f), MouseWheelMove = wheel, RightDown = rmb };
                foreach (var h in hold) input.Keys.Add((int)h);
                sw.Camera3D.HandleInput(input);
                w.Bus.SwapBuffers();
                return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            }
            static float Deg(float r) => r * 180f / MathF.PI;

            float d0 = station.ShoulderDistance(ControlScheme.Person);
            Assert.Equal(UnitCameraRig.PersonDefault, d0);
            Frame(wheel: -1f, hold: Raylib_cs.KeyboardKey.LeftAlt);                                                    // Alt + wheel back
            Assert.Equal(d0 + FpsCameraDriver.DistanceStep, station.ShoulderDistance(ControlScheme.Person), 3);
            int mount = driver.Mount;
            Frame(wheel: -1f);                                                                                          // the plain wheel …
            Assert.Equal(mount, driver.Mount);                                                                          // … one weapon here: still it, the distance unchanged
            Assert.Equal(d0 + FpsCameraDriver.DistanceStep, station.ShoulderDistance(ControlScheme.Person), 3);

            Assert.True(driver.ShoulderViewOn);
            Frame(rmb: true);
            Assert.False(driver.ShoulderViewOn);                                                                        // RMB: from the eyes
            Frame();
            Assert.True(driver.ShoulderViewOn);

            Frame(hold: Raylib_cs.KeyboardKey.Q);
            Assert.Equal(-1, station.ShoulderSide);                                                                     // Q: the left shoulder, and it stays
            Frame();
            Assert.Equal(-1, station.ShoulderSide);
            Frame(hold: Raylib_cs.KeyboardKey.E);
            Assert.Equal(1, station.ShoulderSide);

            // standing: the body lags the aim up to 45°
            float perPixel = 0f;
            { float y0 = driver.Yaw; Frame(dx: -10f); perPixel = (driver.Yaw - y0) / 10f; }
            float body = Frame().Yaw;
            var within = Frame(dx: -(30f * MathF.PI / 180f) / perPixel);
            Assert.Equal(body, within.Yaw, 3);                                                                          // 30°: the body stays
            var past = Frame(dx: -(30f * MathF.PI / 180f) / perPixel);
            Assert.Equal(45f, Deg(driver.Yaw - past.Yaw), 1);                                                          // 60°: dragged, 45° behind

            // moving: W is along the camera — with the body 45° off, the frame's axes carry it (forward cos 45°, lateral sin 45°)
            sw.Camera3D.Update(0.001f);                                                                                 // a tiny dt: the body barely turns
            var walk = Frame(hold: Raylib_cs.KeyboardKey.W);
            float off = driver.Yaw - walk.Yaw;
            Assert.Equal(MathF.Cos(off), walk.Forward, 2);
            Assert.Equal(MathF.Sin(off), walk.Lateral, 2);

            Frame(wheel: 10f, hold: Raylib_cs.KeyboardKey.LeftAlt);                                                    // Alt + wheel all the way in
            Assert.False(station.OverShoulder);                                                                         // the eyes
        }
    }
}
