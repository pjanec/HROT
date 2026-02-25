using Fbt;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Perception.Components;

namespace Fdp.Examples.UrbanCombat.Brains
{
    /// <summary>
    /// BTree node delegates for the Insurgent "Ambush_BT" behaviour tree.
    ///
    /// <para>All three methods share the <see cref="NodeLogicDelegate{TBlackboard,TContext}"/>
    /// signature accepted by <c>Fbt.Runtime.ActionRegistry</c>.  They use
    /// <see cref="BTreeContext.World"/> (DEBT-007, BATCH-13) to read/write ECS components
    /// without any static or thread-local state.</para>
    ///
    /// <para>Tree structure (<c>Assets/Ambush.json</c>):</para>
    /// <code>
    /// Selector
    ///   └─ Sequence
    ///        ├─ Condition_HasTarget    ← returns Success if TargetMemory.Count > 0
    ///        └─ Action_AimAndFire      ← writes WeaponChannel.ActiveAction
    ///   └─ Action_HoldPosition        ← fallback; returns Running
    /// </code>
    /// </summary>
    public static class InsurgentNodes
    {
        /// <summary>
        /// Condition node: succeeds if this entity's <see cref="TargetMemory"/> contains
        /// at least one live threat entry (<see cref="TargetMemory.Count"/> &gt; 0).
        /// </summary>
        /// <returns>
        /// <see cref="NodeStatus.Success"/> when a target is present;
        /// <see cref="NodeStatus.Failure"/> otherwise.
        /// </returns>
        public static NodeStatus Condition_HasTarget(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))
                return NodeStatus.Failure;

            var tm = ctx.World.GetComponent<TargetMemory>(ctx.Self);
            return tm.Count > 0 ? NodeStatus.Success : NodeStatus.Failure;
        }

        /// <summary>
        /// Action node: engages the current target by writing
        /// <see cref="CombatConstants.ActionIdAimAndFire"/> (= 1) into
        /// <see cref="WeaponChannel.ActiveAction"/>.
        /// </summary>
        /// <returns><see cref="NodeStatus.Running"/> while the target is alive.</returns>
        public static NodeStatus Action_AimAndFire(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var channel = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
            channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
            return NodeStatus.Running;
        }

        /// <summary>
        /// Action node: hold position — does nothing except signal <see cref="NodeStatus.Running"/>.
        /// Used as the fallback branch in the Ambush Selector when no target is present.
        /// </summary>
        public static NodeStatus Action_HoldPosition(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            // Stationary — no locomotion or weapon intent written.
            return NodeStatus.Running;
        }
    }
}
