using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Physics.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8 LS-15 / R5-9 / R5-10) — THE LASER'S SPOT, on the muscle: the
    /// nearest body in front is hit (its entity named), else the terrain; nothing when the laser is off; a spot written on a paused frame is a
    /// <see cref="LaserSpot.Preview"/>; nothing while a replay plays; the beam never meets the man carrying the device; 0 B per steady frame.
    /// </summary>
    public sealed class LaserSpotSystemTests : IDisposable
    {
        private readonly EntityRepository _w = TestWorldFactory.Create();
        private SpatialHashGrid _grid;   // a struct over native arrays: added to in place, re-published after each add
        private readonly LaserSpotSystem _sys = new();

        public LaserSpotSystemTests()
        {
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<DeviceInfo>();
            _w.RegisterComponent<DevicePose>();
            _w.RegisterComponent<LaserPointing>();
            _w.RegisterComponent<LaserSpot>();
            // 2 km × 2 km of 20 m cells round the origin
            _grid = SpatialHashGrid.Create(100, 100, 20f, 1000, Allocator.Persistent, originX: -1000f, originY: -1000f);
            _grid.Clear();
            _w.SetSingleton(new SpatialGridData { Grid = _grid });
        }

        public void Dispose() { _grid.Dispose(); _w.Dispose(); }

        /// <summary>A flat ground at z = 0 with a 10 m concrete wall across the beam at x = 600 (y −50 … 50).</summary>
        private void Terrain()
            => _w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [-1000, -1000, 1000, 1000], "groundZ": 0 }, "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 10, "thickness": 0.5, "material": "concrete" }, "geometry": { "type": "LineString", "coordinates": [[600,-50],[600,50]] } } ] }
            """, "laser-range"));

        private Entity Body(Vector3 at, float radius = 0.5f, float height = 2f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new PhysicsCollider { Radius = radius, Height = height, CollisionLayer = 1 });
            _grid.Add(e, new Vector2(at.X, at.Y));
            _w.SetSingleton(new SpatialGridData { Grid = _grid });
            return e;
        }

        /// <summary>A device at (0, 0, 1.5) pointing east (+X), its laser on with request <paramref name="seq"/>.</summary>
        private Entity Device(uint seq = 1, bool on = true, Quaternion? facing = null)
        {
            var d = _w.CreateEntity();
            _w.AddComponent(d, new SimTransform { Position = new Vector3(0f, 0f, 1.5f), Rotation = facing ?? Quaternion.Identity });
            _w.AddComponent(d, new DeviceInfo { DeviceType = 77 });
            _w.AddComponent(d, new LaserPointing { On = on ? (byte)1 : (byte)0, Band = 1, Power = 1f, Seq = seq });
            return d;
        }

        private LaserSpot Spot(Entity d) => _w.HasComponent<LaserSpot>(d) ? _w.GetComponentRO<LaserSpot>(d) : default;

        [Fact]
        public void S1c_TheNearestBodyInFront_IsHit_AndNamed()
        {
            var far = Body(new Vector3(450f, 0f, 0f));
            var near = Body(new Vector3(200f, 0.2f, 0f));
            Body(new Vector3(-100f, 0f, 0f));            // behind the device: never
            Body(new Vector3(300f, 40f, 0f));            // beside the beam: never
            var d = Device(seq: 7);
            _sys.Execute(_w, 0.016f);
            var s = Spot(d);
            Assert.Equal(1, s.Count);
            Assert.Equal(7u, s.PointingSeq);
            Assert.Equal(0, s.Preview);
            Assert.Equal(near, s.Points[0].Hit);
            Assert.InRange(s.Points[0].Range, 199f, 200f);   // the near face of its 0.5 m circle
            Assert.True(s.Points[0].Strength > 0f && s.Points[0].Strength < 1f, $"strength {s.Points[0].Strength}");
            Assert.NotEqual(far, s.Points[0].Hit);
        }

        [Fact]
        public void S1c_NoBodyOnTheLine_TheTerrainAnswers_WithNoEntity()
        {
            Terrain();
            Body(new Vector3(800f, 0f, 0f));             // behind the wall
            var d = Device();
            _sys.Execute(_w, 0.016f);
            var s = Spot(d);
            Assert.Equal(1, s.Count);
            Assert.True(s.Points[0].Hit.IsNull, "the wall is terrain: no entity");
            Assert.InRange(s.Points[0].Range, 599f, 600.5f);
        }

        [Fact]
        public void S1c_TheDevicesOwnPose_TurnsTheBeam()
        {
            var north = Body(new Vector3(0f, 300f, 0f));
            Body(new Vector3(300f, 0f, 0f));
            var d = Device(facing: Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));   // the carrier faces north
            _sys.Execute(_w, 0.016f);
            Assert.Equal(north, Spot(d).Points[0].Hit);
            // and the device's pose relative to its mount (DevicePose) turns it back east
            _w.AddComponent(d, new DevicePose { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f) });
            _sys.Execute(_w, 0.016f);
            Assert.NotEqual(north, Spot(d).Points[0].Hit);
        }

        [Fact]
        public void S1c_TheBeamNeverMeetsTheManCarryingTheDevice()
        {
            var man = Body(new Vector3(0.3f, 0f, 0f));   // the device sits in his kit, inside his circle
            var target = Body(new Vector3(250f, 0f, 0f));
            var d = Device();
            _w.AddComponent(d, new IsEmbarkedTag { VehicleEntity = man, Seat = 4 });
            _sys.Execute(_w, 0.016f);
            Assert.Equal(target, Spot(d).Points[0].Hit);
        }

        [Fact]
        public void S1c_NothingWhenTheLaserIsOff_AndTheSpotClearsWhenItGoesOff()
        {
            Body(new Vector3(200f, 0f, 0f));
            var off = Device(on: false);
            _sys.Execute(_w, 0.016f);
            Assert.False(_w.HasComponent<LaserSpot>(off), "never lit: no spot");
            var d = Device();
            _sys.Execute(_w, 0.016f);
            Assert.Equal(1, Spot(d).Count);
            _w.GetComponentRW<LaserPointing>(d).On = 0;
            _sys.Execute(_w, 0.016f);
            Assert.Equal(0, Spot(d).Count);
            Assert.Equal(0u, Spot(d).PointingSeq);
        }

        [Fact]
        public void S1c_ASpotWrittenOnAPausedFrame_IsAPreview()
        {
            Body(new Vector3(200f, 0f, 0f));
            var d = Device();
            _sys.Execute(_w, 0f);                         // paused: dt = 0
            Assert.Equal(1, Spot(d).Count);
            Assert.Equal(1, Spot(d).Preview);
            _sys.Execute(_w, 0.016f);                     // time advances: a real spot
            Assert.Equal(0, Spot(d).Preview);
        }

        [Fact]
        public void S1c_NothingWhileAReplayPlays()
        {
            Body(new Vector3(200f, 0f, 0f));
            var d = Device();
            Fdp.Toolkit.Replay.ReplayState.Enter(_w);
            _sys.Execute(_w, 0.016f);
            Assert.False(_w.HasComponent<LaserSpot>(d));
            Assert.Equal(0, _sys.Traces);
        }

        [Fact]
        public void S1c_ASteadyFrame_AllocatesNothing()
        {
            Terrain();
            Body(new Vector3(200f, 0f, 0f));
            Device();
            Device(on: false);
            // warm: the query, the terrain view, JIT tiering — ⚠ 30 frames went red once in six runs (a tier-up's call-counting install on this
            //   thread inside the measured window), so warm well past the tier-1 threshold
            for (int i = 0; i < 300; i++) _sys.Execute(_w, i % 2 == 0 ? 0.016f : 0f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) _sys.Execute(_w, i % 2 == 0 ? 0.016f : 0f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(_sys.Traces >= 350);
        }
    }
}
