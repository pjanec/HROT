using System;
using System.Numerics;
using Hrot.Map.Common;
using Hrot.SimHost.Integration.Tests.Infrastructure;
using CarKinem.Core;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// TASK-S6.2 â€” Mission Execution Flow Integration Test.
    ///
    /// Validates the full path from entity creation â†’ NavState configuration â†’
    /// simulation physics â†’ GeoSpatial position change:
    ///
    ///   1. A Tank entity is spawned via the SimHost pipeline (same as S6.1).
    ///   2. The entity's <see cref="NavState"/> is configured with a distant target so
    ///      <see cref="CarKinem.Systems.CarKinematicsSystem"/> starts driving it.
    ///   3. 10 simulated seconds are advanced at 60 Hz.
    ///   4. The entity's <see cref="Fdp.Kernel.SimTransform"/> is read
    ///      back via <see cref="SimHostInstance.ReadGeoSpatial"/> and converted to local
    ///      Cartesian coordinates; we assert the vehicle moved at least 50 m from the origin.
    ///
    /// Note: this test intentionally bypasses the B-Tree / MissionDirectorSystem tier and
    /// configures NavState directly.  This exercises the full physics pipeline
    /// (SpatialHashSystem â†’ VehicleCommandSystem â†’ CarKinematicsSystem â†’
    /// CoordinateTransformSystem) without requiring a
    /// B-Tree asset at test time.
    /// </summary>
    public sealed class MissionExecutionFlowTests : IDisposable
    {
        private readonly SimHostInstance _host;

        public MissionExecutionFlowTests() => _host = new SimHostInstance();

        public void Dispose() => _host.Dispose();

        // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// After 10 seconds of simulation the tank must have moved at least 50 m
        /// from its spawn origin.
        /// </summary>
        [Fact]
        public void MoveToLocation_TankNavigates_GeoSpatialChangesAfter10s()
        {
            // â”€â”€ Arrange: spawn the entity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(0, ack.StatusCode);
            Assert.True(ack.EntityId > 0);

            // Resolve ECS entity so we can set the NavState.
            Assert.True(
                _host.EntityMap.TryGetEntity(ack.EntityId, out var entity),
                $"Entity {ack.EntityId} should be in EntityMap after CreateEntity.");

            // â”€â”€ Arrange: configure navigation target â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Set NavState directly â€” bypasses B-Tree but exercises the full physics chain.
            // Target is 1 km away so the vehicle is still moving at the 10-second mark.
            var nav = _host.World.GetComponent<NavState>(entity);
            nav.Mode             = KinematicsMode.Direct;
            nav.FinalDestination = new Vector2(1000f, 0f);   // 1 km east
            nav.TargetSpeed      = 15.0f;                    // ~54 km/h â€” tank sprint
            nav.ArrivalRadius    = 5.0f;
            nav.HasArrived       = 0;
            _host.World.SetComponent(entity, nav);

            // â”€â”€ Act: run simulation for 10 s (600 ticks @ 60 Hz) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _host.RunForSeconds(10f);

            // â”€â”€ Assert: GeoSpatial position changed â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var geo = _host.ReadGeoSpatial(ack.EntityId);
            Assert.NotNull(geo);

            var cartesian = _host.GeoToCartesian(geo!.Value.Pos);
            float distFromOrigin = Vector2.Distance(cartesian, Vector2.Zero);

            Assert.True(
                distFromOrigin > 50f,
                $"Expected the tank to have moved > 50 m, but it only moved {distFromOrigin:F1} m. " +
                $"GeoPos = ({geo.Value.Pos.Latitude:F6}Â°, {geo.Value.Pos.Longitude:F6}Â°).");
        }

        /// <summary>
        /// Entity that has arrived (HasArrived=1) should stay at its destination and
        /// not drift further away.
        /// </summary>
        [Fact]
        public void ArrivedEntity_DoesNotDrift_After10s()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var entity));

            // Set as "already arrived" at origin with zero speed.
            var nav = _host.World.GetComponent<NavState>(entity);
            nav.Mode         = KinematicsMode.None;
            nav.TargetSpeed  = 0f;
            nav.HasArrived   = 1;
            _host.World.SetComponent(entity, nav);

            _host.RunForSeconds(10f);

            var geo = _host.ReadGeoSpatial(ack.EntityId);
            Assert.NotNull(geo);

            var cartesian = _host.GeoToCartesian(geo!.Value.Pos);
            float distFromOrigin = Vector2.Distance(cartesian, Vector2.Zero);

            // Should stay within 1 m of the spawn origin (floating-point tolerance).
            Assert.True(
                distFromOrigin < 1f,
                $"Arrived entity drifted {distFromOrigin:F2} m â€” expected it to stay at origin.");
        }

        [Fact]
        public void EntityMission_MovesEntity()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(0, ack.StatusCode);
            
            var dest = _host.CartesianToGeo(new Vector3(500,0,0)); 
            var lat = dest.Latitude; 
            var lon = dest.Longitude; 
            
            var mission = new Hrot.NED.Descriptors.EntityMission
            {
                EntityId = ack.EntityId,
                Plan = new Hrot.NED.Descriptors.MissionPlan
                {
                    Tasks = new System.Collections.Generic.List<Hrot.NED.Descriptors.MissionTask>
                    {
                        new Hrot.NED.Descriptors.MissionTask
                        {
                            BehaviorId = "MoveToLocation",
                            BehaviorParams = $"{{\"TargetLat\":{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"TargetLon\":{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"Speed\":15.0,\"ArrivalRadius\":5.0}}"
                        }
                    }
                }
            };
            
            _host.PublishEntityMission(mission);
            _host.RunForSeconds(10f);

            var geo = _host.ReadGeoSpatial(ack.EntityId);
            Assert.NotNull(geo);

            var cartesian = _host.GeoToCartesian(geo!.Value.Pos);
            float distFromOrigin = Vector2.Distance(cartesian, Vector2.Zero);

            Assert.True(
                distFromOrigin > 50f,
                $"Expected movement > 50m. Actually moved {distFromOrigin:F1}m.");
        }
    }
}

