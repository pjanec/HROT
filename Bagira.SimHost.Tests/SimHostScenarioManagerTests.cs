using System.Numerics;
using Bagira.SimHost.UI;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for BD1-P3T2: <see cref="SimHostScenarioManager.SpawnEntityLocal"/> (called
    /// via <see cref="SimHostScenarioManager.SpawnCollisionTest"/>) must attach a
    /// <see cref="PhysicsCollider"/> so entities participate in the RVO spatial hash.
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
    }
}
