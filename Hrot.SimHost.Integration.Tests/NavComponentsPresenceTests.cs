using System.Numerics;
using Hrot.Map.Common;
using Hrot.SimHost.Integration.Tests.Infrastructure;
using CarKinem.Core;
using FDP.Toolkit.Navigation;
using Xunit;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// CT-MOD1-C2 — Integration tests verifying that spawned entities have the
    /// CQRS navigation contract components (<see cref="NavigationIntent"/>,
    /// <see cref="NavigationStatus"/>, <see cref="FrustrationTicks"/>) physically
    /// attached after creation via the TKB blueprint pipeline.
    ///
    /// <para>
    /// Root cause of the original bug: <c>NedTkbBuilder.WithBehavior</c> did not
    /// add <see cref="NavigationIntent"/>, <see cref="NavigationStatus"/>, or
    /// <see cref="FrustrationTicks"/> to the vehicle blueprint.  When
    /// <c>MoveToExecutor.OnEnter</c> called
    /// <c>world.GetComponentRW&lt;NavigationIntent&gt;(entity)</c> it threw
    /// <c>InvalidOperationException: Entity missing NavigationIntent</c>.
    /// </para>
    ///
    /// <para>
    /// Fix: <see cref="Hrot.Map.Definitions.Tkb.NedTkbBuilder.WithBehavior"/>
    /// now calls <c>template.AddComponent(new NavigationIntent())</c>,
    /// <c>template.AddComponent(new NavigationStatus())</c>, and
    /// <c>template.AddComponent(new FrustrationTicks())</c> so the ECS structs
    /// are present from the moment the entity is instantiated by the TKB system.
    /// </para>
    /// </summary>
    public sealed class NavComponentsPresenceTests : System.IDisposable
    {
        private readonly SimHostInstance _host;

        public NavComponentsPresenceTests() => _host = new SimHostInstance();

        public void Dispose() => _host.Dispose();

        // ── Test 1 ───────────────────────────────────────────────────────────

        /// <summary>
        /// After spawning a <c>Tank_M1Abrams</c> entity, the ECS world must contain
        /// a <see cref="NavigationIntent"/> component on that entity.
        /// </summary>
        [Fact]
        public void SpawnedEntity_HasNavigationIntent_Component()
        {
            // ── Arrange / Act ──────────────────────────────────────────────
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);

            Assert.Equal(0, ack.StatusCode);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var entity),
                $"Entity {ack.EntityId} must be registered in EntityMap.");

            // ── Assert ────────────────────────────────────────────────────
            Assert.True(
                _host.World.HasComponent<NavigationIntent>(entity),
                "Spawned M1 Abrams entity must have a NavigationIntent component " +
                "(NedTkbBuilder.WithBehavior must add it to the template).");
        }

        // ── Test 2 ───────────────────────────────────────────────────────────

        /// <summary>
        /// After spawning a <c>Tank_M1Abrams</c> entity, the ECS world must contain
        /// a <see cref="NavigationStatus"/> component on that entity.
        /// </summary>
        [Fact]
        public void SpawnedEntity_HasNavigationStatus_Component()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);

            Assert.Equal(0, ack.StatusCode);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var entity));

            Assert.True(
                _host.World.HasComponent<NavigationStatus>(entity),
                "Spawned M1 Abrams entity must have a NavigationStatus component.");
        }

        // ── Test 3 ───────────────────────────────────────────────────────────

        /// <summary>
        /// After spawning a <c>Tank_M1Abrams</c> entity, the ECS world must contain
        /// a <see cref="FrustrationTicks"/> component on that entity.
        /// </summary>
        [Fact]
        public void SpawnedEntity_HasFrustrationTicks_Component()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);

            Assert.Equal(0, ack.StatusCode);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var entity));

            Assert.True(
                _host.World.HasComponent<FrustrationTicks>(entity),
                "Spawned M1 Abrams entity must have a FrustrationTicks component.");
        }

        // ── Test 4 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Assigning a MoveTo mission and running the simulation for 600 ticks
        /// must NOT throw <c>InvalidOperationException</c> (previously thrown by
        /// <c>MoveToExecutor.OnEnter</c> when <c>NavigationIntent</c> was absent).
        /// This reproduces the exact Hrot.ClusterRunner <c>-x all</c> crash scenario.
        /// </summary>
        [Fact]
        public void EntityMission_MoveToLocation_DoesNotThrowMissingNavigationIntent()
        {
            // ── Arrange: spawn the entity ──────────────────────────────────
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(0, ack.StatusCode);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var entity));

            // ── Arrange: configure a MoveTo mission ────────────────────────
            // Drive 500 m in +X direction from spawn point.
            var destCart = new Vector2(500f, 0f);
            var destGeo  = _host.CartesianToGeo(new System.Numerics.Vector3(destCart.X, destCart.Y, 0f));

            var mission = new Hrot.NED.Descriptors.EntityMission
            {
                EntityId = ack.EntityId,
                Plan = new Hrot.NED.Descriptors.MissionPlan
                {
                    Tasks = new System.Collections.Generic.List<Hrot.NED.Descriptors.MissionTask>
                    {
                        new Hrot.NED.Descriptors.MissionTask
                        {
                            BehaviorId     = "MoveToLocation",
                            BehaviorParams = $"{{\"TargetLat\":{destGeo.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                             $"\"TargetLon\":{destGeo.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                             $"\"Speed\":15.0,\"ArrivalRadius\":10.0}}"
                        }
                    }
                }
            };

            // ── Act: publish mission and run simulation ─────────────────────
            // The previous crash happened on the very first tick that the BTree
            // evaluated MoveToExecutor.OnEnter.  Running 120 ticks is sufficient
            // to trigger the executor and verify no exception is thrown.
            var ex = Record.Exception(() =>
            {
                _host.PublishEntityMission(mission);
                _host.RunForTicks(120);
            });

            // ── Assert: no exception ──────────────────────────────────────
            Assert.Null(ex);
        }

        // ── Test 5 ───────────────────────────────────────────────────────────

        /// <summary>
        /// All vehicle blueprint types (M1 Abrams, Bradley, HMMWV, T-72, Rifleman)
        /// must have NavigationIntent on spawn — not just the tank. This ensures the
        /// fix applies to every entity template that uses WithBehavior.
        /// </summary>
        [Theory]
        [InlineData(TkbEntityTypes.Tank_M1Abrams)]
        [InlineData(TkbEntityTypes.IFV_Bradley)]
        [InlineData(TkbEntityTypes.Truck_HMMWV)]
        [InlineData(TkbEntityTypes.Tank_T72)]
        [InlineData(TkbEntityTypes.Infantry_Rifleman)]
        public void SpawnedEntity_AllVehicleTypes_HaveNavigationIntent(long tkbType)
        {
            var ack = _host.CreateEntity(tkbType);

            Assert.Equal(0, ack.StatusCode);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var entity),
                $"Entity {ack.EntityId} must appear in EntityMap for TkbType={tkbType}.");

            Assert.True(
                _host.World.HasComponent<NavigationIntent>(entity),
                $"TkbType={tkbType}: spawned entity must have NavigationIntent.");
        }
    }
}
