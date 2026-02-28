using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using Bagira.SimHost.Systems;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="JoinFormationExecutor"/> (TASK-S4.4).
    ///
    /// Tests exercise <see cref="JoinFormationExecutor.OnEnter"/> and
    /// <see cref="JoinFormationExecutor.Execute"/> in isolation — no ECS system group
    /// is involved, so the executor methods are called directly.
    /// </summary>
    public class JoinFormationExecutorTests
    {
        // ── World factory ─────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();

            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<InFormationTag>();
            world.RegisterEvent<CmdJoinFormation>();

            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
        }

        /// <summary>
        /// Writes <paramref name="p"/> into the entity's <see cref="BrainBlackboard.Memory"/>
        /// at offset 0, simulating what <c>DoctrineDefinition.ParseParams</c> would do.
        /// </summary>
        private static unsafe void WriteBlackboardParams(
            EntityRepository world, Entity entity, JoinFormationParams p)
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            fixed (byte* dst = &bb.Memory[0])
                *(JoinFormationParams*)dst = p;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// When the leader entity IS registered in the <see cref="NetworkEntityMap"/>,
        /// <see cref="JoinFormationExecutor.OnEnter"/> must call <c>VehicleAPI.JoinFormation</c>
        /// and set <c>channel.Status = Running</c>.
        /// </summary>
        [Fact]
        public void JoinFormation_LeaderFound_SetsRunning()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world  = CreateWorld();
            var entityMap    = new NetworkEntityMap();

            // Create the follower and leader entities.
            var follower = world.CreateEntity();
            var leader   = world.CreateEntity();

            world.AddComponent(follower, new BrainBlackboard());
            world.AddComponent(follower, new LocomotionChannel { Status = default });

            // Register leader in the NetworkEntityMap under network ID 10.
            const int leaderNetworkId = 10;
            entityMap.Register(leaderNetworkId, leader);

            // Write JoinFormationParams into the follower's blackboard.
            WriteBlackboardParams(world, follower, new JoinFormationParams
            {
                LeaderNetworkId = leaderNetworkId,
                FormationTypeId = (byte)FormationType.Column,
            });

            // VehicleAPI requires an ISimulationView — the EntityRepository implements it.
            var vehicleAPI = new VehicleAPI((ISimulationView)world);
            var executor   = new JoinFormationExecutor(vehicleAPI, entityMap);

            var channel = new LocomotionChannel { Status = default };

            // ── Act ───────────────────────────────────────────────────────────
            executor.OnEnter(follower, ref channel, world);

            // ── Assert ────────────────────────────────────────────────────────
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        /// <summary>
        /// When the leader entity is NOT registered in the <see cref="NetworkEntityMap"/>,
        /// <see cref="JoinFormationExecutor.OnEnter"/> must set
        /// <c>channel.Status = Failure</c> without throwing.
        /// </summary>
        [Fact]
        public void JoinFormation_LeaderNotFound_SetsFailure()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world  = CreateWorld();
            var entityMap    = new NetworkEntityMap(); // leader NOT registered

            var follower = world.CreateEntity();
            world.AddComponent(follower, new BrainBlackboard());

            WriteBlackboardParams(world, follower, new JoinFormationParams
            {
                LeaderNetworkId = 999, // Unknown network ID
                FormationTypeId = (byte)FormationType.Wedge,
            });

            var executor = new JoinFormationExecutor(vehicleAPI: null, entityMap);
            var channel  = new LocomotionChannel { Status = default };

            // ── Act ───────────────────────────────────────────────────────────
            executor.OnEnter(follower, ref channel, world);

            // ── Assert ────────────────────────────────────────────────────────
            Assert.Equal(NodeStatus.Failure, channel.Status);
        }

        /// <summary>
        /// When <see cref="InFormationTag"/> is present on the entity,
        /// <see cref="JoinFormationExecutor.Execute"/> must set
        /// <c>channel.Status = Success</c>.
        /// </summary>
        [Fact]
        public void JoinFormation_Execute_SuccessOnFormationTag()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var entity      = world.CreateEntity();

            world.AddComponent(entity, new LocomotionChannel { Status = NodeStatus.Running });

            // Simulate the tag being set by an external system (e.g., VehicleCommandSystem).
            world.AddComponent(entity, new InFormationTag { LeaderEntityIndex = 0 });

            var executor = new JoinFormationExecutor(vehicleAPI: null, new NetworkEntityMap());
            var channel  = new LocomotionChannel { Status = NodeStatus.Running };

            // ── Act ───────────────────────────────────────────────────────────
            executor.Execute(entity, ref channel, world, dt: 0.016f);

            // ── Assert ────────────────────────────────────────────────────────
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        /// <summary>
        /// When <see cref="InFormationTag"/> is absent,
        /// <see cref="JoinFormationExecutor.Execute"/> must leave the channel in
        /// <see cref="NodeStatus.Running"/> (keep polling).
        /// </summary>
        [Fact]
        public void JoinFormation_Execute_KeepsRunningWithoutFormationTag()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateWorld();
            var entity      = world.CreateEntity();

            // No InFormationTag added.
            var executor = new JoinFormationExecutor(vehicleAPI: null, new NetworkEntityMap());
            var channel  = new LocomotionChannel { Status = NodeStatus.Running };

            // ── Act ───────────────────────────────────────────────────────────
            executor.Execute(entity, ref channel, world, dt: 0.016f);

            // ── Assert ────────────────────────────────────────────────────────
            Assert.Equal(NodeStatus.Running, channel.Status);
        }
    }
}
