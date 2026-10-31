using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Map.Common;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.UI;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Verifies that <see cref="SimHostScenarioManager"/> asks for entities through <c>EntityCreation.RequestEntityCreation</c>
    /// with correct behavior, blackboard, and <see cref="EntityInfo"/> payloads so that
    /// entities are visible on the IG map and execute their assigned BTree behaviours.
    /// </summary>
    public class SimHostScenarioManagerTests
    {
        // ⭐ CE-259ax — the manager is an AUTHOR (R-145): it asks the PRODUCTION creation pack, so what these rails read is the
        //   EntityCreationRequest that RequestEntityCreation published on the world bus (not a SpawnEntityCommand order).
        private const int NodeId = 7;

        private sealed class Rig
        {
            public required SimHostScenarioManager Sut;
            public required EntityRepository World;
        }

        private static (SimHostScenarioManager Sut, Rig Bus) CreateSut()
        {
            var world      = new EntityRepository();
            var tkb        = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            var creation   = Hrot.TestShared.TestEntityCreation.For(world, tkb, NodeId);
            var traj       = new TrajectoryPoolManager();
            var formations = new FormationTemplateManager();
            var road       = new RoadNetworkBlob();

            var sut = new SimHostScenarioManager(
                repo:        world,
                road:        road,
                traj:        traj,
                formations:  formations,
                creation:    creation);

            return (sut, new Rig { Sut = sut, World = world });
        }

        private static List<EntityCreationRequest> ExtractCommands(Rig rig)
        {
            rig.World.Bus.SwapBuffers();   // the request is read after the frame's swap
            return ((Fdp.ModuleHost.Abstractions.ISimulationView)rig.World)
                .ReadManagedEvents<EntityCreationRequest>().ToList();
        }

        /// <summary>
        /// ⭐ <c>CE-259ax</c> — every spawner is an author that OWNS what it places ("mine" = the pack's own node id) and asks for peers'
        /// acks. 🔴 Mutation: restore <c>owner:</c> to the default 0 and this reddens — the owner would silently become the arbiter.
        /// </summary>
        [Fact]
        public void EverySpawner_AsksForAnEntityThisNodeOwns_ThroughTheAffordance()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnVehicle(new Vector2(1, 2), Vector2.UnitX);
            sut.SpawnRoamers(1, VehicleClass.Tank);
            sut.SpawnRoadUsers(1, VehicleClass.PersonalCar);
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 2);
            sut.SpawnCollisionTest(VehicleClass.PersonalCar);

            var requests = ExtractCommands(bus);
            Assert.Equal(1 + 1 + 1 + 2 + 2, requests.Count);
            Assert.All(requests, r =>
            {
                Assert.Equal(NodeId, r.OwnerAppInstanceId);
                Assert.Equal(Fdp.Toolkit.Replication.ReliableInitType.AllPeers, r.InitType);
                Assert.Contains(r.InitialComponents!, c => c is SimTransform);
            });
        }

        // ── SpawnRoamers ────────────────────────────────────────────────────────

        [Fact]
        public void SpawnRoamers_PublishesCorrectNumberOfSpawnCommands()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnRoamers(3, VehicleClass.PersonalCar);
            Assert.Equal(3, ExtractCommands(bus).Count);
        }

        [Fact]
        public void SpawnRoamers_EachCommand_HasEntityInfo()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnRoamers(2, VehicleClass.Tank);

            foreach (var cmd in ExtractCommands(bus))
            {
                Assert.NotNull(cmd.InitialComponents);
                Assert.Contains(cmd.InitialComponents, c => c is EntityInfo);
            }
        }

        [Fact]
        public void SpawnRoamers_EachCommand_HasCorrectTkbType()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnRoamers(1, VehicleClass.Tank);

            Assert.Equal(TkbEntityTypes.Tank_M1Abrams, ExtractCommands(bus)[0].TkbType);
        }

        // ── SpawnRoadUsers ─────────────────────────────────────────────────────

        [Fact]
        public void SpawnRoadUsers_NoRoad_PublishesWanderCommands()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnRoadUsers(3, VehicleClass.PersonalCar);

            Assert.Equal(3, ExtractCommands(bus).Count);
        }

        // ── SpawnCollisionTest ────────────────────────────────────────────────

        [Fact]
        public void SpawnCollisionTest_PublishesTwoSpawnCommands()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnCollisionTest(VehicleClass.PersonalCar);
            Assert.Equal(2, ExtractCommands(bus).Count);
        }

        [Fact]
        public void SpawnCollisionTest_BothCommands_HaveFollowRouteBehavior()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnCollisionTest(VehicleClass.PersonalCar);

            foreach (var cmd in ExtractCommands(bus))
            {
                Assert.NotNull(cmd.InitialComponents);
                var intent = cmd.InitialComponents.OfType<NavigationIntent>().Single();
                Assert.Equal(NavigationMode.FollowRoute, intent.Mode);
            }
        }

        [Fact]
        public void SpawnCollisionTest_BothCommands_HaveDistinctNonZeroTrajectoryIds()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnCollisionTest(VehicleClass.PersonalCar);

            var cmds    = ExtractCommands(bus);
            var trajIds = new List<int>();

            foreach (var cmd in cmds)
            {
                var intent = cmd.InitialComponents!.OfType<NavigationIntent>().Single();
                Assert.True(intent.TrajectoryId > 0, $"Expected non-zero TrajectoryId but got {intent.TrajectoryId}");
                trajIds.Add(intent.TrajectoryId);
            }

            Assert.NotEqual(trajIds[0], trajIds[1]);
        }

        [Fact]
        public void SpawnCollisionTest_BothCommands_HaveEntityInfo()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnCollisionTest(VehicleClass.PersonalCar);

            foreach (var cmd in ExtractCommands(bus))
                Assert.Contains(cmd.InitialComponents!, c => c is EntityInfo);
        }

        // ── SpawnFormation ───────────────────────────────────────────────────────

        [Fact]
        public void SpawnFormation_PublishesExpectedNumberOfRequests()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 4);
            Assert.Equal(4, ExtractCommands(bus).Count); // 1 leader + 3 followers
        }

        /// <summary>The affordance has no pre-allocated-id parameter (design §3: only the scenario extractor, a translator, needs one).</summary>
        [Fact]
        public void SpawnFormation_LeaderIsNotPreallocated_TheSystemAllocatesIt()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 2);

            Assert.All(ExtractCommands(bus), r => Assert.Equal(0L, r.PreAllocatedNetworkId));
        }

        [Fact]
        public void SpawnFormation_AllCommands_HaveEntityInfo()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 3);

            foreach (var cmd in ExtractCommands(bus))
                Assert.Contains(cmd.InitialComponents!, c => c is EntityInfo);
        }
    }
}

