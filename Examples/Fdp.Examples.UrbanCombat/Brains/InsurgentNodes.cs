using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Perception.Components;

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
        /// <see cref="WeaponChannel.ActiveAction"/> and packing
        /// <see cref="AimAndFireParams"/> into <see cref="WeaponChannel.Params"/>.
        ///
        /// <para>
        /// Also increments <see cref="WeaponChannel.ActionInstanceId"/> whenever the
        /// action changes (or revives from a Failure state) so that
        /// <see cref="Fdp.Toolkit.Behavior.Systems.WeaponDispatcherSystem"/> triggers
        /// <see cref="AimAndFireExecutor.OnEnter"/>, which stores the target in the
        /// channel's <c>State</c> buffer and sets <c>Status = Running</c>.
        /// </para>
        /// </summary>
        /// <returns><see cref="NodeStatus.Running"/> while the target is alive.</returns>
        public static unsafe NodeStatus Action_AimAndFire(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))
                return NodeStatus.Failure;

            if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))
                return NodeStatus.Failure;

            var mem = ctx.World.GetComponent<TargetMemory>(ctx.Self);
            if (mem.Count == 0)
                return NodeStatus.Failure;

            // Reconstruct the target Entity from the packed long stored in TargetMemory.
            var targetEntity = new Entity((ulong)mem.EntityIds[0]);

            ref var channel = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);

            // Write AimAndFireParams into the channel's inline Params buffer.
            fixed (byte* ptr = channel.Params)
                *(AimAndFireParams*)ptr = new AimAndFireParams { Target = targetEntity, CooldownTicks = 0 };

            // Signal a new dispatch whenever the action is being (re)activated so that
            // WeaponDispatcherSystem calls OnEnter (which copies Params → State and sets
            // Status = Running, enabling Execute on the same tick).
            bool needsReactivation =
                channel.ActiveAction != CombatConstants.ActionIdAimAndFire
                || channel.Status == NodeStatus.Failure;

            if (needsReactivation)
                unchecked { channel.ActionInstanceId++; }

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
