using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fbt.Kernel;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Blackboard parameters for <see cref="EqsCombatNodes.Action_MoveToOptimalCover"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveToOptimalCoverParams
    {
        /// <summary>Desired travel speed (m/s).</summary>
        public float Speed;
        /// <summary>Distance from the cover point that counts as arrival (m).</summary>
        public float ArrivalRadius;
        /// <summary>
        /// Optional: if valid, read EqsCognitiveBuffer from the child sensor entity.
        /// If invalid (default), fall back to reading from ctx.Self.
        /// </summary>
        public EqsSensorHandle SensorHandle;
    }

    /// <summary>
    /// FastBTree action and condition nodes for EQS-driven cover seeking.
    /// </summary>
    public static class EqsCombatNodes
    {
        /// <summary>
        /// Returns Success if the entity's <see cref="TargetMemory"/> contains at least one
        /// entry with a positive threat score; Failure otherwise.
        /// </summary>
        [SharedAiCondition]
        public static NodeStatus Condition_HasTarget(ref MoveToOptimalCoverParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<TargetMemory>(self))
                return NodeStatus.Failure;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            unsafe
            {
                for (int i = 0; i < mem.Count; i++)
                    if (mem.Freshness[i] > 0f) return NodeStatus.Success;
            }
            return NodeStatus.Failure;
        }

        /// <summary>
        /// Reads the top-ranked entry from <see cref="EqsCognitiveBuffer"/> and drives
        /// <see cref="LocomotionChannel"/> with a MoveTo action toward that position.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_MoveToOptimalCover(ref MoveToOptimalCoverParams p, Entity self, EntityRepository world)
        {
            // 1. Resolve the entity to read the buffer from.
            Entity bufferEntity = p.SensorHandle.IsValid && world.IsAlive(p.SensorHandle.ChildId)
                ? p.SensorHandle.ChildId
                : self;

            // 2. Guard: require both components (LocomotionChannel stays on self).
            if (!world.HasComponent<EqsCognitiveBuffer>(bufferEntity) ||
                !world.HasComponent<LocomotionChannel>(self))
                return NodeStatus.Failure;

            // 3. Buffer must be ready and non-empty
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(bufferEntity);
            if (!buffer.IsReady || buffer.Count == 0)
                return NodeStatus.Failure;

            var bestCover = buffer.GetTop();
            var targetPos = new Vector3(bestCover.PositionX, bestCover.PositionY, bestCover.PositionZ);

            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);

            // 4. Propagate behavior instance ID to prevent channel arbitration stomping
            LocomotionMoveTo.StampOwner(world, self, ref channel);

            // 5. Forward terminal status from the executor
            if (channel.ActiveAction == NavigationConstants.ActionIdMoveTo)
            {
                if (channel.Status == NodeStatus.Success) return NodeStatus.Success;
                if (channel.Status == NodeStatus.Failure) return NodeStatus.Failure;
            }

            // 6. Activate the locomotion channel (⭐ CE-2092: the one channel write, LocomotionMoveTo)
            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdMoveTo ||
                                   channel.Status == NodeStatus.Failure;

            if (needsActivation)
                LocomotionMoveTo.Issue(world, self, targetPos, p.Speed, p.ArrivalRadius);

            return NodeStatus.Running;
        }

        /// <summary>
        /// Stub: holds entity in place. Always returns Running.
        /// Full locomotion integration is deferred to Phase 7.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_HoldPosition(ref MoveToOptimalCoverParams p, Entity self, EntityRepository world)
        {
            return NodeStatus.Running;
        }

        /// <summary>
        /// Stub: wanders indefinitely. Always returns Running.
        /// Full locomotion integration is deferred to Phase 7.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_Wander(ref MoveToOptimalCoverParams p, Entity self, EntityRepository world)
        {
            return NodeStatus.Running;
        }
    }
}
