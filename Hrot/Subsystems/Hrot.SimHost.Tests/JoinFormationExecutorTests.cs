using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Tests
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

            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            // 🔴 P3-C: the executor reads its params from the ROOT PARAMS OCCURRENCE SLOT now, so
            //   the world needs the tier components it lives in.
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<InFormationTag>();
            world.RegisterEvent<CmdJoinFormation>();

            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
        }

        /// <summary>
        /// Writes <paramref name="p"/> into the entity's ROOT PARAMS OCCURRENCE SLOT at offset 0,
        /// simulating what <c>BehaviorDefinition.ParseParams</c> would do.
        ///
        /// <para>🔴 <c>P3-C</c> (<c>2026-09-21</c>): this used to write
        /// <c>BrainBlackboard.BehaviorParameters</c>. ⭐ Same offset, same bytes — only the anchor
        /// moved (§29.6) — but the slot has to be ATTACHED first, which is what ingress does in
        /// production and what this helper now does for the test.</para>
        /// </summary>
        private static unsafe void WriteBlackboardParams(
            EntityRepository world, Entity entity, JoinFormationParams p)
        {
            const int HarnessBehaviourHash = 0x7E5703;

            if (!world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity))
                world.AddComponent(entity, new Fdp.Toolkit.Behavior.Components.BehaviorState());
            ref var st = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity);
            st.ActiveBehaviorHash = HarnessBehaviourHash;

            var spec = Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.Select(
                Fdp.Toolkit.Behavior.BehaviorConstants.MaxBehaviorParamByteSize + 16, requiredSlots: 1);
            if (Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.GetStoreSize(world, entity) == 0)
            {
                spec.Add(world, entity);
                Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.Initialize(
                    spec.Memory(world, entity), spec.TotalSize, (byte)spec.MaxSlots);
            }

            byte* dst = Fdp.Toolkit.Behavior.RootParamsAccess.ResolveOrAttachRoot(
                world, entity, HarnessBehaviourHash,
                Fdp.Toolkit.Behavior.BehaviorConstants.MaxBehaviorParamByteSize,
                Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.BTree, out _);

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
