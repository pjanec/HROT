using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Input;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>
    /// ⭐⭐ SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-109) — ONE "a view follows an entity" rule: a view bound to a device whose
    /// <c>DeviceView</c> names a live entity (a ground station's drone) puts the 3-D camera ON it — the existing <see cref="FollowCameraDriver"/> at its eye, along its
    /// heading at the gimbal's pitch — and the camera suggestion is that follow (Follow, Eyes, bound to the drone); the operator's own view again once the device
    /// names nothing or the view is the eye.
    /// 🔴 Red-proof (compiles): <c>DeviceFilm</c> returning false ⇒ the camera stays at his eyes ⇒ red.
    /// </summary>
    [Fact]
    public void SDS2c_AViewBoundToADeviceFilmingADrone_RidesTheDroneAtTheGimbalPitch()
    {
        var (w, unit, map) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = (Fdp.Core.StationId)(0), Mode = PossessionMode.Fps, Epoch = 1 });
            w.RegisterComponent<Fdp.Toolkit.Behavior.Devices.DeviceInfo>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Devices.DevicePose>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Devices.DeviceView>();
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            w.Bus.Register<Fdp.Toolkit.Behavior.Devices.DeviceInputEvent>();
            var gs = w.CreateEntity();
            w.AddComponent(gs, new SimTransform { Position = new Vector3(100f, 50f, 1.2f), Rotation = Quaternion.Identity });
            w.AddComponent(gs, new Fdp.Toolkit.Behavior.Devices.DeviceInfo { DeviceType = 642, Behavior = new FixedString64(Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceDroneStation) });
            w.AddComponent(gs, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 7002 });
            w.AddComponent(gs, new PossessedBy { StationNodeId = (Fdp.Core.StationId)(0), Mode = PossessionMode.Operated, Epoch = 1 });
            w.AddComponent(gs, new Fdp.Toolkit.Behavior.Devices.DevicePose { Orientation = Quaternion.Identity });
            map.Register(7002, gs);
            var drone = w.CreateEntity();   // 300 m north, 40 m up, its nose north
            w.AddComponent(drone, new SimTransform { Position = new Vector3(100f, 350f, 40f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w) { Keymap = new Keymap(), OverShoulder = false };
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            station.CycleView(1);
            Assert.Equal(gs, station.BoundDevice);
            sw.Camera3D.Update(0.016f);
            Assert.True(sw.Camera3D.Eye.Y < 60f, $"no DeviceView yet: his own eyes ({sw.Camera3D.Eye})");

            float gimbal = -30f * MathF.PI / 180f;
            w.AddComponent(gs, new Fdp.Toolkit.Behavior.Devices.DeviceView { Entity = drone, Mode = Fdp.Toolkit.Behavior.Devices.DeviceViewMode.Eyes, Pitch = gimbal });
            sw.Camera3D.Update(0.016f);
            Assert.True(Vector3.Distance(sw.Camera3D.Eye, new Vector3(100f, 350f, 40f)) < 3f, $"the camera rides the drone ({sw.Camera3D.Eye})");
            Assert.Equal(gimbal, sw.Camera3D.Pitch, 3);
            Assert.True(Vector3.Dot(Vector3.Normalize(new Vector3(sw.Camera3D.Forward.X, sw.Camera3D.Forward.Y, 0f)), Vector3.UnitY) > 0.99f, "along its heading");
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.Equal(drone, driver.FilmedEntity);
            var s = new Hrot.Map.Common.Components.CameraSuggestion();
            driver.Describe(w, ref s);
            Assert.Equal(Hrot.Map.Common.Components.CameraSource.Follow, s.Source);
            Assert.Equal(Hrot.Map.Common.Components.CameraMode.Eyes, s.Mode);
            Assert.Equal(drone, s.Bound);

            w.SetComponent(gs, new Fdp.Toolkit.Behavior.Devices.DeviceView { Entity = drone, Mode = Fdp.Toolkit.Behavior.Devices.DeviceViewMode.Eyes, Pitch = 2f * gimbal });   // CAM DOWN
            sw.Camera3D.Update(0.016f);
            Assert.Equal(2f * gimbal, sw.Camera3D.Pitch, 3);

            w.SetComponent(gs, default(Fdp.Toolkit.Behavior.Devices.DeviceView));   // the station off: names nothing
            sw.Camera3D.Update(0.016f);
            Assert.True(sw.Camera3D.Eye.Y < 60f, $"his own view again ({sw.Camera3D.Eye})");
            Assert.True(driver.FilmedEntity.IsNull);
        }
    }
}
