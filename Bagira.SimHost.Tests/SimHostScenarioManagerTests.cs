using System.Numerics;
using Bagira.SimHost.UI;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for BD1-P3T2: <see cref="SimHostScenarioManager.SpawnEntityLocal"/> (called
    /// via <see cref="SimHostScenarioManager.SpawnCollisionTest"/>) must attach a
    /// <see cref="PhysicsCollider"/> so entities participate in the RVO spatial hash.
    ///
    /// Also covers the fix for "entities spawned from the SimHost control panel do not start
    /// moving": <see cref="SimTransform"/> authority must be set, and the three CQRS navigation
    /// contract components (<see cref="NavigationIntent"/>, <see cref="NavigationStatus"/>,
    /// <see cref="FrustrationTicks"/>) must be present so that
    /// <c>CarKinematicsSystem</c>, <c>NavigationIntentBridgeSystem</c>, and
    /// <c>NavigationExecutionSystem</c> pick up the entity.
    /// </summary>
    public class SimHostScenarioManagerTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<PhysicsCollider>();
            repo.RegisterComponent<NavigationIntent>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<FrustrationTicks>();
            repo.RegisterEvent<CmdFollowTrajectory>();
            return repo;
        }

        private static SimHostScenarioManager CreateScenario(EntityRepository repo)
        {
            var road       = new RoadNetworkBlob();
            var traj       = new TrajectoryPoolManager();
            var formations = new FormationTemplateManager();
            return new SimHostScenarioManager(repo, road, traj, formations);
        }

        /// <summary>
        /// BD1-P3T2 SC1: Entities spawned by SpawnCollisionTest (via SpawnEntityLocal)
        /// must carry a PhysicsCollider component.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_AddsPhysicsCollider()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            // SpawnCollisionTest calls SpawnEntityLocal twice.
            scenario.SpawnCollisionTest(VehicleClass.PersonalCar);

            var query = repo.Query().With<SimTransform>().With<PhysicsCollider>().Build();
            int count = 0;
            foreach (var _ in query) count++;

            Assert.Equal(2, count);
        }

        /// <summary>
        /// BD1-P3T2 SC1: Radius of the PhysicsCollider must be greater than zero.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_PhysicsCollider_RadiusIsPositive()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            scenario.SpawnCollisionTest(VehicleClass.Tank);

            var query = repo.Query().With<PhysicsCollider>().Build();
            foreach (var entity in query)
            {
                repo.TryGetComponent(entity, out PhysicsCollider collider);
                Assert.True(collider.Radius > 0f,
                    "PhysicsCollider.Radius must be positive for spawned entities.");
            }
        }

        // ── Authority fix: entities spawned from the control panel must move ────────────

        /// <summary>
        /// Entities from SpawnEntityLocal must have local authority over SimTransform so
        /// that CarKinematicsSystem (.WithOwned&lt;SimTransform&gt;()) includes them.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_SimTransform_HasLocalAuthority()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            scenario.SpawnCollisionTest(VehicleClass.PersonalCar);

            int withAuth    = 0;
            int withoutAuth = 0;
            var query = repo.Query().With<SimTransform>().Build();
            foreach (var entity in query)
            {
                if (repo.HasAuthority<SimTransform>(entity)) withAuth++;
                else withoutAuth++;
            }

            Assert.Equal(2, withAuth);
            Assert.Equal(0, withoutAuth);
        }

        /// <summary>
        /// Entities from SpawnEntityLocal must carry NavigationIntent so that
        /// NavigationIntentBridgeSystem and NavigationExecutionSystem can process them.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_HasNavigationIntent()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            scenario.SpawnCollisionTest(VehicleClass.PersonalCar);

            int count = 0;
            var query = repo.Query().With<NavigationIntent>().Build();
            foreach (var _ in query) count++;

            Assert.Equal(2, count);
        }

        /// <summary>
        /// Entities from SpawnEntityLocal must carry NavigationStatus so that
        /// NavigationExecutionSystem can write the CQRS reply.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_HasNavigationStatus()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            scenario.SpawnCollisionTest(VehicleClass.PersonalCar);

            int count = 0;
            var query = repo.Query().With<NavigationStatus>().Build();
            foreach (var _ in query) count++;

            Assert.Equal(2, count);
        }

        /// <summary>
        /// Entities from SpawnEntityLocal must carry FrustrationTicks so that
        /// NavigationExecutionSystem's stuck-detection does not throw a missing-component exception.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_HasFrustrationTicks()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            scenario.SpawnCollisionTest(VehicleClass.PersonalCar);

            int count = 0;
            var query = repo.Query().With<FrustrationTicks>().Build();
            foreach (var _ in query) count++;

            Assert.Equal(2, count);
        }

        /// <summary>
        /// NavigationIntent on a freshly-spawned entity must default to Mode = None
        /// (brain-death state), so the NavigationIntentBridgeSystem leaves NavState alone
        /// and direct CmdFollowTrajectory / CmdNavigateToPoint commands take effect immediately.
        /// </summary>
        [Fact]
        public void SpawnEntityLocal_NavigationIntent_DefaultsToModeNone()
        {
            using var repo = CreateWorld();
            var scenario   = CreateScenario(repo);

            scenario.SpawnCollisionTest(VehicleClass.PersonalCar);

            var query = repo.Query().With<NavigationIntent>().Build();
            foreach (var entity in query)
            {
                repo.TryGetComponent(entity, out NavigationIntent intent);
                Assert.Equal(NavigationMode.None, intent.Mode);
            }
        }
    }
}
