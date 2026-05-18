using System.Runtime.InteropServices;
using CarKinem.Commands;
using Fdp.Core.Logging;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Replication.Services;
using Fbt;

namespace Fdp.Toolkit.Navigation.Executors
{
    // ── Parameter and tag structs ─────────────────────────────────────────────────

    /// <summary>
    /// Parameters for the <c>JoinFormation</c> behavior.
    /// Written into <see cref="BrainBlackboard.BehaviorParameters"/> by <c>BehaviorDefinition.ParseParams</c>
    /// and read by <see cref="JoinFormationExecutor.OnEnter"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JoinFormationParams
    {
        /// <summary>Network entity ID of the formation leader.</summary>
        public int LeaderNetworkId;

        /// <summary>
        /// Desired formation shape encoded as the byte value of
        /// <see cref="CarKinem.Formation.FormationType"/>:
        /// 0 = Column, 1 = Wedge, 2 = Line.
        /// Defaults to <see cref="CarKinem.Formation.FormationType.Wedge"/> (1) if
        /// unrecognised or zero-initialised.
        /// </summary>
        public byte FormationTypeId;
    }

    /// <summary>
    /// Tag component added by <see cref="JoinFormationExecutor.OnEnter"/> once
    /// <c>VehicleAPI.JoinFormation</c> has been issued.
    /// <see cref="JoinFormationExecutor.Execute"/> polls for this tag each tick to detect
    /// that the entity has successfully joined its assigned formation slot.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.InFormationTag)]
    public struct InFormationTag
    {
        /// <summary>ECS entity index of the formation leader.</summary>
        public int LeaderEntityIndex;
    }

    // ── Executor ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Action executor for the <c>JoinFormation</c> locomotion behavior
    /// (<see cref="Fdp.Toolkit.Navigation.NavigationConstants.ActionIdJoinFormation"/>).
    ///
    /// <para>
    /// <b>OnEnter:</b> reads <see cref="JoinFormationParams"/> from
    /// <see cref="BrainBlackboard.BehaviorParameters"/>, resolves the leader via
    /// <see cref="NetworkEntityMap"/>, calls <c>VehicleAPI.JoinFormation</c> and sets
    /// <see cref="LocomotionChannel.Status"/> = <see cref="NodeStatus.Running"/>.
    /// If the leader entity cannot be resolved, sets <c>Status = Failure</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Execute:</b> polls for <see cref="InFormationTag"/> presence on the entity;
    /// reports <see cref="NodeStatus.Success"/> once it is found.
    /// </para>
    ///
    /// <para>
    /// <b>OnExit:</b> no cleanup — the formation system manages its own roster state.
    /// </para>
    ///
    /// Pattern: follows <c>MoveToExecutor</c> / <c>FollowRouteExecutor</c> conventions.
    /// </summary>
    public sealed class JoinFormationExecutor : IActionExecutor<LocomotionChannel>
    {
        private readonly VehicleAPI?      _vehicleAPI;
        private readonly NetworkEntityMap _entityMap;

        public JoinFormationExecutor(VehicleAPI? vehicleAPI, NetworkEntityMap entityMap)
        {
            _vehicleAPI = vehicleAPI;
            _entityMap  = entityMap  ?? throw new System.ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // Read params written into BrainBlackboard.BehaviorParameters by BehaviorDefinition.ParseParams.
            // Use ref to avoid stack-copying the struct (fixed buffer must stay on heap).
            ref var bbRW = ref world.GetComponentRW<BrainBlackboard>(entity);
            JoinFormationParams p;
            fixed (byte* src = &bbRW.BehaviorParameters[0])
                p = *(JoinFormationParams*)src;

            // Resolve leader network ID → ECS entity.
            if (!_entityMap.TryGetEntity(p.LeaderNetworkId, out var leaderEntity))
            {
                FdpLog<JoinFormationExecutor>.Warn(
                    $"[JoinFormationExecutor] Leader network ID {p.LeaderNetworkId} not found " +
                    $"in NetworkEntityMap. Setting channel to Failure.");
                channel.Status = NodeStatus.Failure;
                return;
            }

            // Issue the join command via VehicleAPI; VehicleCommandSystem will process it
            // on the next frame and add FormationMember to the entity.
            if (_vehicleAPI != null)
                _vehicleAPI.JoinFormation(entity, leaderEntity);

            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            // Formation join is considered complete once InFormationTag is present.
            if (world.HasComponent<InFormationTag>(entity))
                channel.Status = NodeStatus.Success;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // No cleanup needed; the formation system manages its own roster state.
        }
    }
}
