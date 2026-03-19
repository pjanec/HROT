using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.UI;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.NetworkSpawning.Events;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Tests
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
        public void SpawnRoamers_EachCommand_HasWanderMilitaryDoctrine()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnRoamers(2, VehicleClass.PersonalCar);

            foreach (var cmd in ExtractCommands(bus))
            {
                Assert.NotNull(cmd.InitialComponents);
                var doctrine = cmd.InitialComponents.OfType<DoctrineState>().Single();
                Assert.Equal(SimHostDoctrineIds.WanderMilitary_BT, doctrine.ActiveDoctrineHash);
            }
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
        public void SpawnRoamers_EachCommand_HasBrainBlackboard()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnRoamers(1, VehicleClass.PersonalCar);

            var cmd = ExtractCommands(bus)[0];
            Assert.NotNull(cmd.InitialComponents);
            Assert.Contains(cmd.InitialComponents, c => c is BrainBlackboard);
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

            var cmds = ExtractCommands(bus);
            Assert.Equal(3, cmds.Count);
            foreach (var cmd in cmds)
            {
                var doctrine = cmd.InitialComponents!.OfType<DoctrineState>().Single();
                Assert.Equal(SimHostDoctrineIds.WanderMilitary_BT, doctrine.ActiveDoctrineHash);
            }
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
                var doctrine = cmd.InitialComponents.OfType<DoctrineState>().Single();
                Assert.Equal(SimHostDoctrineIds.FollowRoute_BT, doctrine.ActiveDoctrineHash);
            }
        }

        [Fact]
        public unsafe void SpawnCollisionTest_BothCommands_HaveDistinctNonZeroTrajectoryIds()
        {
            var (sut, bus) = CreateSut();
            sut.SpawnCollisionTest(VehicleClass.PersonalCar);

            var cmds   = ExtractCommands(bus);
            var trajIds = new List<int>();

            foreach (var cmd in cmds)
            {
                var bb = cmd.InitialComponents!.OfType<BrainBlackboard>().Single();
                int trajId = *(int*)(&bb); // TrajectoryId is first int in FollowRouteParams
                Assert.True(trajId > 0, $"Expected non-zero TrajectoryId but got {trajId}");
                trajIds.Add(trajId);
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
        public void SpawnFormation_LeaderCommand_HasWanderMilitaryDoctrine()
        {
            var alloc  = new StubAllocator();
            var (sut, bus) = CreateSut(alloc);
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Column, count: 2);

            var leaderCmd = ExtractCommands(bus)[0];
            var doctrine  = leaderCmd.InitialComponents!.OfType<DoctrineState>().Single();
            Assert.Equal(SimHostDoctrineIds.WanderMilitary_BT, doctrine.ActiveDoctrineHash);
        }

        [Fact]
        public unsafe void SpawnFormation_FollowerCommand_HasJoinFormationDoctrineWithLeaderNetworkId()
        {
            var alloc  = new StubAllocator(startId: 500);
            var (sut, bus) = CreateSut(alloc);
            sut.SpawnFormation(VehicleClass.PersonalCar, FormationType.Wedge, count: 2);

            var followerCmd = ExtractCommands(bus)[1];
            var doctrine    = followerCmd.InitialComponents!.OfType<DoctrineState>().Single();
            Assert.Equal(SimHostDoctrineIds.JoinFormation_BT, doctrine.ActiveDoctrineHash);

            var bb = followerCmd.InitialComponents.OfType<BrainBlackboard>().Single();
            var p = (JoinFormationParams*)(&bb);
            Assert.Equal(500, p->LeaderNetworkId); // must match pre-allocated leader ID
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

