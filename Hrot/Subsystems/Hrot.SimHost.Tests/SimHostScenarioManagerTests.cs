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
    /// Verifies that <see cref="SimHostScenarioManager"/> publishes <see cref="SpawnEntityCommand"/>
    /// events with correct doctrine, blackboard, and <see cref="EntityInfo"/> payloads so that
    /// entities are visible on the IG map and execute their assigned BTree behaviours.
    /// </summary>
    public class SimHostScenarioManagerTests
    {
        // ── CapturingBus stub ─────────────────────────────────────────────────────

        private sealed class CapturingBus : IEventBus
        {
            public readonly List<object> ManagedEvents = new();
            public void Publish<T>(T evt) where T : unmanaged { }
            public void PublishManaged<T>(T evt) => ManagedEvents.Add(evt!);
        }

        // ── Stub allocator ───────────────────────────────────────────────────────

        private sealed class StubAllocator : INetworkIdAllocator
        {
            private long _next;
            public StubAllocator(long startId = 500) => _next = startId;
            public long AllocateId() => _next++;
            public void Reset(long startId = 0) => _next = startId;
            public void Dispose() { }
        }

        // ── Factory ───────────────────────────────────────────────────────────

        private static (SimHostScenarioManager Sut, CapturingBus Bus) CreateSut(
            INetworkIdAllocator? allocator = null)
        {
            var bus        = new CapturingBus();
            var repo       = new EntityRepository();
            var traj       = new TrajectoryPoolManager();
            var formations = new FormationTemplateManager();
            var road       = new RoadNetworkBlob();

            var sut = new SimHostScenarioManager(
                repo:        repo,
                road:        road,
                traj:        traj,
                formations:  formations,
                spawnBus:    bus,
                idAllocator: allocator);

            return (sut, bus);
        }

        private static List<SpawnEntityCommand> ExtractCommands(CapturingBus bus)
        {
            var result = new List<SpawnEntityCommand>();
            foreach (var ev in bus.ManagedEvents)
                if (ev is SpawnEntityCommand cmd) result.Add(cmd);
            return result;
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
        public void SpawnCollisionTest_BothCommands_HaveFollowRouteDoctrine()
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
        public void SpawnFormation_WithoutAllocator_PublishesExpectedNumberOfCommands()
        {
            var (sut, bus) = CreateSut(allocator: null);
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 4);
            Assert.Equal(4, ExtractCommands(bus).Count); // 1 leader + 3 followers
        }

        [Fact]
        public void SpawnFormation_WithAllocator_LeaderUsesPreallocatedNetworkId()
        {
            var alloc  = new StubAllocator(startId: 500);
            var (sut, bus) = CreateSut(alloc);
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 2);

            var cmds = ExtractCommands(bus);
            Assert.Equal(500L, cmds[0].NetworkId); // leader uses the pre-allocated ID
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

