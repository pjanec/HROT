using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    private static float Wrap(float a) => Fdp.Toolkit.Perception.Look.LookOf.Wrap(a);

    /// <summary>
    /// ⭐⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-3, UC-7) — the shoulder view is the default; a car's driver gets the chase camera (a
    /// vehicle's distance, behind and above), and his look, left alone for 1.5 s, eases back behind the car — ⚠ not an armed turret's,
    /// whose look IS the gun's order (§8p OS-4).
    /// </summary>
    [Fact]
    public void CE1293_TheShoulderViewIsTheDefault_ACarsDriverGetsTheChaseCamera_AndHisLookReCentres_NotAnArmedTurrets()
    {
        var (w, car, _) = World();
        using (w)
        {
            w.RegisterComponent<Locomotion>();
            w.AddComponent(car, new Locomotion { Class = LocomotionClass.Wheeled });
            w.AddComponent(car, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            Assert.True(station.OverShoulder);                                                                         // UC-3
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.Equal(ControlScheme.Vehicle, driver.RigScheme);
            float back = Vector3.Distance(sw.Camera3D.Eye, driver.Eye);
            Assert.True(back > UnitCameraRig.VehicleDefault * 0.9f, $"the chase camera stands back ({back:0.0} m)");
            Assert.True(sw.Camera3D.Eye.Z > driver.Eye.Z + 1f, "and above");

            sw.Camera3D.HandleInput(new Input { MouseDelta = new Vector2(-300f, 0f) });                                 // look left
            float looked = driver.Yaw;
            Assert.True(MathF.Abs(Wrap(looked)) > 0.3f);
            for (int i = 0; i < 10; i++) sw.Camera3D.Update(0.1f);                                                     // 1 s: not yet
            Assert.Equal(looked, driver.Yaw, 3);
            for (int i = 0; i < 30; i++) sw.Camera3D.Update(0.1f);                                                     // then back behind the car
            Assert.True(MathF.Abs(Wrap(driver.Yaw)) < 0.01f, $"re-centred ({driver.Yaw:0.000})");

            // an armed turret: the look is the gun's order — it stays where he put it
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            w.AddComponent(car, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 700 });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var ifv = new Fdp.Interfaces.TkbTemplate("IFV", 700);
            ifv.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Autocannon, TurretIndex = 0 } } });
            db.Register(ifv);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            sw.Camera3D.HandleInput(new Input { MouseDelta = new Vector2(-300f, 0f) });
            Assert.False(driver.Recentres);
            looked = driver.Yaw;
            for (int i = 0; i < 40; i++) sw.Camera3D.Update(0.1f);
            Assert.Equal(looked, driver.Yaw, 3);
        }
    }

    /// <summary>
    /// ⭐ CE-1293 (UC-8) — a man in a seat (a gunner at his gun) rides the VEHICLE's chase camera from his seat — the vehicle's distance,
    /// not a person's shoulder — and Alt + wheel sets the vehicle's distance.
    /// </summary>
    [Fact]
    public void CE1293_AManInASeat_RidesTheVehiclesChaseCamera_FromHisSeat()
    {
        var (w, gunner, _) = World();
        using (w)
        {
            w.RegisterComponent<Locomotion>();
            w.RegisterComponent<IsEmbarkedTag>();
            var truck = w.CreateEntity();
            w.AddComponent(truck, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(truck, new Locomotion { Class = LocomotionClass.Wheeled });
            w.AddComponent(gunner, new IsEmbarkedTag { VehicleEntity = truck });
            w.AddComponent(gunner, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.True(driver.Aboard);
            Assert.Equal(ControlScheme.Vehicle, driver.RigScheme);
            float back = Vector3.Distance(sw.Camera3D.Eye, driver.Eye);
            Assert.True(back > UnitCameraRig.VehicleDefault * 0.9f, $"the vehicle's chase distance, not a person's shoulder ({back:0.0} m)");

            var alt = new Input { MouseWheelMove = -1f };
            alt.Keys.Add((int)Raylib_cs.KeyboardKey.LeftAlt);
            sw.Camera3D.HandleInput(alt);
            Assert.Equal(UnitCameraRig.VehicleDefault + FpsCameraDriver.DistanceStep * UnitCameraRig.ScaleOf(ControlScheme.Vehicle),
                         station.ShoulderDistance(ControlScheme.Vehicle), 3);
            Assert.Equal(UnitCameraRig.PersonDefault, station.ShoulderDistance(ControlScheme.Person));
        }
    }

    /// <summary>
    /// ⭐ CE-1293 (UC-9) — the muzzle-blocked mark: aiming across a building from outside it, the round would stop at its wall — short of
    /// the crosshair's point — and <see cref="PossessionStation.BlockedShot"/> says where; aiming along open ground, or at the wall itself,
    /// there is no mark.
    /// </summary>
    [Fact]
    public void CE1293_TheRoundWouldStopShort_AtAWallTheCameraSeesPast_AndThereIsNoMarkInTheOpen()
    {
        var terrain = Map3DTests.TestTown();
        using var w = Map3DTests.WorldWith(terrain);
        var q = Fdp.Toolkit.World.WorldQuery.Of(w)!;
        if (!w.IsComponentTypeRegistered<SimTransform>()) w.RegisterComponent<SimTransform>();
        var man = w.CreateEntity();

        bool Inside(float x, float y) { var s = q.SurfacesAt(x, y, out int g); return s.Count > g + 1 && s[^1] - s[g] > 4f; }
        float Ground(float x, float y) { var s = q.SurfacesAt(x, y, out int g); return s[g]; }

        // a line along +X: outside, through a building, outside again
        Vector3? from = null, to = null, open = null, openTo = null;
        for (float y = terrain.BoundsMin.Y + 10; y < terrain.BoundsMax.Y - 10 && (from == null || open == null); y += 2f)
            for (float x = terrain.BoundsMin.X + 10; x < terrain.BoundsMax.X - 30; x += 1f)
            {
                if (from == null && !Inside(x, y) && Inside(x + 3f, y))
                {
                    float far = x + 3f;
                    while (far < terrain.BoundsMax.X - 10 && Inside(far, y)) far += 0.5f;
                    if (far >= terrain.BoundsMax.X - 10) continue;
                    from = new Vector3(x - 2f, y, Ground(x - 2f, y));
                    to = new Vector3(far + 3f, y, Ground(far + 3f, y) + 1.5f);
                }
                if (open == null && Enumerable.Range(0, 25).All(k => !Inside(x + k, y)) && MathF.Abs(Ground(x, y) - Ground(x + 24f, y)) < 0.3f)
                {
                    open = new Vector3(x, y, Ground(x, y));
                    openTo = new Vector3(x + 20f, y, Ground(x + 20f, y) + 1.5f);
                }
                if (from != null && open != null) break;
            }
        Assert.True(from != null && open != null, "the test town has a building to shoot across and open ground");

        w.AddComponent(man, new SimTransform { Position = from!.Value, Rotation = Quaternion.Identity });
        var stop = PossessionStation.BlockedShot(w, man, 0, to!.Value);
        Assert.NotNull(stop);
        Assert.True(stop!.Value.X > from.Value.X && stop.Value.X < to.Value.X - PossessionStation.BlockedMargin, $"stopped short, at the wall ({stop})");
        Assert.Null(PossessionStation.BlockedShot(w, man, 0, stop.Value));                                             // the crosshair ON the wall: no mark

        w.SetComponent(man, new SimTransform { Position = open!.Value, Rotation = Quaternion.Identity });
        Assert.Null(PossessionStation.BlockedShot(w, man, 0, openTo!.Value));                                          // open ground: no mark
    }
}
