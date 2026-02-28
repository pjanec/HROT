using System;
using System.Numerics;
using Bagira.Map.Common;
using Bagira.SimHost.Integration.Tests.Infrastructure;
using CarKinem.Core;
using Xunit;

namespace Bagira.SimHost.Integration.Tests
{
    /// <summary>
    /// TASK-S6.2 — Mission Execution Flow Integration Test.
    ///
    /// Validates the full path from entity creation → NavState configuration →
    /// simulation physics → GeoSpatial position change:
    ///
    ///   1. A Tank entity is spawned via the SimHost pipeline (same as S6.1).
    ///   2. The entity's <see cref="NavState"/> is configured with a distant target so
    ///      <see cref="CarKinem.Systems.CarKinematicsSystem"/> starts driving it.
    ///   3. 10 simulated seconds are advanced at 60 Hz.
    ///   4. The entity's <see cref="Fdp.Modules.Geographic.Components.GeoTransform"/> is read
    ///      back via <see cref="SimHostInstance.ReadGeoSpatial"/> and converted to local
    ///      Cartesian coordinates; we assert the vehicle moved at least 50 m from the origin.
    ///
    /// Note: this test intentionally bypasses the B-Tree / MissionDirectorSystem tier and
    /// configures NavState directly.  This exercises the full physics pipeline
    /// (SpatialHashSystem → VehicleCommandSystem → CarKinematicsSystem →
    /// SimTransformBridgeSystem → CoordinateTransformSystem) without requiring a
    /// B-Tree asset at test time.
    /// </summary>
    public sealed class MissionExecutionFlowTests : IDisposable
    {
        private readonly SimHostInstance _host;

        public MissionExecutionFlowTests() => _host = new SimHostInstance();

        public void Dispose() => _host.Dispose();

        // ── Tests ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// After 10 seconds of simulation the tank must have moved at least 50 m
        /// from its spawn origin.
        /// </summary>
        [Fact]
        public void MoveToLocation_TankNavigates_GeoSpatialChangesAfter10s()
        {
            // ── Arrange: spawn the entity ─────────────────────────────────────────────────
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(0, ack.ErrorCode);
            Assert.True(ack.NewEntityId > 0);

            // Resolve ECS entity so we can set the NavState.
            Assert.True(
                _host.EntityMap.TryGetEntity(ack.NewEntityId, out var entity),
                $"Entity {ack.NewEntityId} should be in EntityMap after CreateEntity.");

            // ── Arrange: configure navigation target ─────────────────────────────────────
            // Set NavState directly — bypasses B-Tree but exercises the full physics chain.
            // Target is 1 km away so the vehicle is still moving at the 10-second mark.
            var nav = _host.World.GetComponent<NavState>(entity);
            nav.Mode             = NavigationMode.Direct;
            nav.FinalDestination = new Vector2(1000f, 0f);   // 1 km east
            nav.TargetSpeed      = 15.0f;                    // ~54 km/h — tank sprint
            nav.ArrivalRadius    = 5.0f;
            nav.HasArrived       = 0;
            _host.World.SetComponent(entity, nav);

            // ── Act: run simulation for 10 s (600 ticks @ 60 Hz) ─────────────────────────
            _host.RunForSeconds(10f);

            // ── Assert: GeoSpatial position changed ───────────────────────────────────────
            var geo = _host.ReadGeoSpatial(ack.NewEntityId);
            Assert.NotNull(geo);

            var cartesian = _host.GeoToCartesian(geo!.Value.Pos);
            float distFromOrigin = Vector2.Distance(cartesian, Vector2.Zero);

            Assert.True(
                distFromOrigin > 50f,
                $"Expected the tank to have moved > 50 m, but it only moved {distFromOrigin:F1} m. " +
                $"GeoPos = ({geo.Value.Pos.Latitude:F6}°, {geo.Value.Pos.Longitude:F6}°).");
        }

        /// <summary>
        /// Entity that has arrived (HasArrived=1) should stay at its destination and
        /// not drift further away.
        /// </summary>
        [Fact]
        public void ArrivedEntity_DoesNotDrift_After10s()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.True(_host.EntityMap.TryGetEntity(ack.NewEntityId, out var entity));

            // Set as "already arrived" at origin with zero speed.
            var nav = _host.World.GetComponent<NavState>(entity);
            nav.Mode         = NavigationMode.None;
            nav.TargetSpeed  = 0f;
            nav.HasArrived   = 1;
            _host.World.SetComponent(entity, nav);

            _host.RunForSeconds(10f);

            var geo = _host.ReadGeoSpatial(ack.NewEntityId);
            Assert.NotNull(geo);

            var cartesian = _host.GeoToCartesian(geo!.Value.Pos);
            float distFromOrigin = Vector2.Distance(cartesian, Vector2.Zero);

            // Should stay within 1 m of the spawn origin (floating-point tolerance).
            Assert.True(
                distFromOrigin < 1f,
                $"Arrived entity drifted {distFromOrigin:F2} m — expected it to stay at origin.");
        }
    }
}
