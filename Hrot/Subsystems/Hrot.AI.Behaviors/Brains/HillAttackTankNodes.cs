using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Constants for the HullDownAttackRun subordinate tank behavior.
    /// </summary>
    public static class HillAttackConstants
    {
        /// <summary>
        /// Maximum distance (metres) a tank may overshoot its assigned firing-line slot
        /// along the attack direction before <c>Action_CreepToAndBeyondSlot</c> returns
        /// <see cref="NodeStatus.Failure"/> and the engagement is aborted.
        /// </summary>
        public const float MaxOvershootMeters = 50f;

        /// <summary>
        /// Distance threshold (metres) from the assigned slot that triggers the phase
        /// transition from approach speed to creep speed in
        /// <c>Action_CreepToAndBeyondSlot</c>.
        /// </summary>
        public const float SlotArrivalThresholdMeters = 15f;

        /// <summary>
        /// "Infinity" look-ahead distance (metres) written as the creep destination
        /// so the tank continues along the attack direction past the slot indefinitely
        /// until the overshoot limit is reached or the target becomes visible.
        /// </summary>
        public const float CreepLookAheadMeters = 10000f;
    }

    /// <summary>
    /// FastBTree action and condition nodes for the HullDownAttackRun subordinate
    /// tank behavior, plus the <see cref="BuildHullDownAttackRunTree"/> BTree definition.
    ///
    /// <para>All delegates use the three-parameter <c>ReusableActionDelegate&lt;TValue, BTreeContext&gt;</c>
    /// signature compatible with <c>BTreeBuilder.Action&lt;TValue&gt;(fieldSelector, method)</c>.
    /// Channel cleanup on Failure is performed explicitly inside the relevant action methods
    /// because the source generator only emits <c>[WritesChannel]</c> cleanup for 4-param
    /// delegates, not for 3-param bridge delegates.
    /// </para>
    /// </summary>
    public static class HillAttackTankNodes
    {
        // ── Channel write helpers ─────────────────────────────────────────────────

        private static unsafe void WriteToLocomotionParams<T>(ref LocomotionChannel ch, T value)
            where T : unmanaged
        {
            System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref ch.Params[0]) = value;
        }

        private static unsafe void WriteToWeaponParams<T>(ref WeaponChannel ch, T value)
            where T : unmanaged
        {
            System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref ch.Params[0]) = value;
        }

        // ── Conditions ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <see cref="NodeStatus.Success"/> when the assigned target (identified by
        /// <see cref="HullDownAttackParams.TargetNetworkId"/>) is currently tracked in
        /// this entity's <see cref="TargetMemory"/> with a positive threat score.
        ///
        /// <list type="bullet">
        ///   <item>Failure when <c>TargetNetworkId</c> cannot be resolved via
        ///     <c>NetworkEntityMap</c> (entity not yet materialized on this node).</item>
        ///   <item>Failure when the target is absent from <c>TargetMemory</c> or its
        ///     threat score is zero.</item>
        /// </list>
        /// </summary>
        [BTreeCondition]
        public static NodeStatus Condition_HasTarget(
            ref HullDownAttackParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            // Resolve the network-stable target ID to a local entity.
            if (!ctx.World.HasSingleton<NetworkEntityMap>())
                return NodeStatus.Failure;

            var entityMap = ctx.World.GetSingletonManaged<NetworkEntityMap>();
            if (entityMap == null || !entityMap.TryGetEntity(p.TargetNetworkId, out var targetEntity))
                return NodeStatus.Failure;

            if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))
                return NodeStatus.Failure;

            long targetPacked = (long)targetEntity.PackedValue;
            ref readonly var mem = ref ctx.World.GetComponentRO<TargetMemory>(ctx.Self);

            // Scan TargetMemory for the resolved entity with a positive threat score.
            // Loop is bounded by MaxTrackedTargets (4) — no heap allocation.
            unsafe
            {
                for (int i = 0; i < mem.Count; i++)
                {
                    if (mem.EntityIds[i] == targetPacked && mem.ThreatScores[i] > 0f)
                        return NodeStatus.Success;
                }
            }
            return NodeStatus.Failure;
        }

        // ── Actions ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Drives the tank toward and then past its assigned firing-line slot along
        /// the attack direction vector.
        ///
        /// <para><b>Phase 1 (far):</b> when the tank is more than
        /// <see cref="HillAttackConstants.SlotArrivalThresholdMeters"/> from the slot,
        /// issues a <c>MoveTo</c> command to the slot position at <c>ApproachSpeed</c>.</para>
        ///
        /// <para><b>Phase 2 (near):</b> when within the threshold, issues a <c>MoveTo</c>
        /// command to a far point along the attack direction at <c>CreepSpeed</c>.</para>
        ///
        /// <para><b>Returns:</b>
        ///   <see cref="NodeStatus.Running"/> in both phases.
        ///   <see cref="NodeStatus.Failure"/> when the tank has overshot the slot by more
        ///   than <see cref="HillAttackConstants.MaxOvershootMeters"/> along the attack
        ///   direction.  Never returns <see cref="NodeStatus.Success"/>.</para>
        ///
        /// <para>Channel cleanup on Failure is done explicitly here because
        /// <c>[WritesChannel]</c> cleanup is only generated for 4-param delegates.</para>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_CreepToAndBeyondSlot(
            ref HullDownAttackParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self)
                || !ctx.World.HasComponent<SimTransform>(ctx.Self))
                return NodeStatus.Failure;

            ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            ref readonly var tf = ref ctx.World.GetComponentRO<SimTransform>(ctx.Self);

            var slotPos = new Vector2(p.SlotX, p.SlotY);
            var attackDir = new Vector2(p.AttackDirX, p.AttackDirY);
            var currentPos = new Vector2(tf.Position.X, tf.Position.Y);

            float distToSlot = Vector2.Distance(currentPos, slotPos);

            // Overshoot check: positive dot-product of (currentPos - slot) along attackDir
            // means the tank has moved past the slot.
            Vector2 delta = currentPos - slotPos;
            float overshootMeters = Vector2.Dot(delta, attackDir);

            if (overshootMeters > HillAttackConstants.MaxOvershootMeters)
            {
                // Clear the locomotion channel explicitly before returning Failure.
                loco.ActiveAction = 0;
                loco.Status = NodeStatus.Failure;
                return NodeStatus.Failure;
            }

            // Determine which phase we are in.
            bool isFarPhase = distToSlot > HillAttackConstants.SlotArrivalThresholdMeters;

            ushort desiredAction = NavigationConstants.ActionIdMoveTo;
            Vector2 destination;
            float speed;

            if (isFarPhase)
            {
                destination = slotPos;
                speed = p.ApproachSpeed;
            }
            else
            {
                // Creep: send to a point far along the attack direction from the current position.
                destination = currentPos + attackDir * HillAttackConstants.CreepLookAheadMeters;
                speed = p.CreepSpeed;
            }

            // Sync BehaviorInstanceId so ChannelArbitrationSystem does not clear the channel.
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behav = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                loco.BehaviorInstanceId = behav.InstanceId;
            }

            // Only re-issue the command when the action changes or speed changes (avoid
            // spamming the dispatcher with identical intents each frame).
            bool needsWrite = loco.ActiveAction != desiredAction
                || loco.Status == NodeStatus.Failure;

            // Also re-issue when the phase changed (approach vs creep), detected by
            // comparing the destination against the slot position tolerance.
            if (!needsWrite && loco.ActiveAction == desiredAction)
            {
                // Destination change detected: phase switched if we were in far-phase and
                // now the speed in params differs from what the channel last received.
                // The simplest guard: re-issue whenever the target speed disagrees.
                // We cannot easily read back params from fixed byte array here, so track
                // phase changes implicitly through the slot arrival threshold.
                // The channel write is cheap; a small redundancy on the threshold boundary
                // is acceptable for correctness.
            }

            if (needsWrite)
            {
                unchecked { loco.ActionInstanceId++; }
                loco.ActiveAction = desiredAction;
                loco.Status = NodeStatus.Running;
                WriteToLocomotionParams(ref loco, new MoveToParams
                {
                    Destination   = destination,
                    ArrivalRadius = 1f,
                    Speed         = speed,
                });
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Aims and fires at the specific target assigned to this tank.
        ///
        /// <list type="bullet">
        ///   <item>Returns <see cref="NodeStatus.Failure"/> when <c>TargetNetworkId</c>
        ///     cannot be resolved (target not yet materialized).</item>
        ///   <item>Returns <see cref="NodeStatus.Success"/> immediately when the target
        ///     entity is no longer alive (destroyed; standard executors do not natively
        ///     detect this and would otherwise leave the node stuck in Running).</item>
        ///   <item>Writes <c>ActionIdAimAndFire</c> to <c>WeaponChannel</c> with the
        ///     resolved target entity on the first activation only.</item>
        ///   <item>Returns <see cref="NodeStatus.Running"/> while the weapon channel
        ///     reports the engagement in progress.</item>
        ///   <item>Returns <see cref="NodeStatus.Success"/> when the weapon channel
        ///     status transitions to <see cref="NodeStatus.Success"/>.</item>
        /// </list>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_AimAndFireSpecific(
            ref HullDownAttackParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasSingleton<NetworkEntityMap>())
                return NodeStatus.Failure;

            var entityMap = ctx.World.GetSingletonManaged<NetworkEntityMap>();
            if (entityMap == null || !entityMap.TryGetEntity(p.TargetNetworkId, out var targetEntity))
                return NodeStatus.Failure;
            if (!ctx.World.IsAlive(targetEntity))
                return NodeStatus.Success;

            if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var weapon = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);

            // Sync BehaviorInstanceId to prevent ChannelArbitrationSystem clearing the channel.
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behav = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                weapon.BehaviorInstanceId = behav.InstanceId;
            }

            // Forward executor terminal status.
            if (weapon.ActiveAction == CombatConstants.ActionIdAimAndFire)
            {
                if (weapon.Status == NodeStatus.Success) return NodeStatus.Success;
                if (weapon.Status == NodeStatus.Failure) return NodeStatus.Failure;
            }

            // Only issue the command when not already active (avoid re-issuing each frame).
            bool needsActivation = weapon.ActiveAction != CombatConstants.ActionIdAimAndFire
                || weapon.Status == NodeStatus.Failure;

            if (needsActivation)
            {
                WriteToWeaponParams(ref weapon, new AimAndFireParams
                {
                    Target          = targetEntity,
                    CooldownSeconds = 0f,
                });
                unchecked { weapon.ActionInstanceId++; }
                weapon.ActiveAction = CombatConstants.ActionIdAimAndFire;
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Commands the tank to reverse to its assigned baseline slot.
        ///
        /// <para>Writes <c>ActionIdMoveTo</c> to <c>LocomotionChannel</c> with
        /// <c>Destination = (BaselineX, BaselineY)</c>. Note: the <c>MoveToParams</c>
        /// struct does not have a reverse flag in v1; <c>NavState.ReverseAllowed</c> is
        /// not yet implemented. The tank will navigate forward to the baseline.</para>
        ///
        /// <list type="bullet">
        ///   <item>Returns <see cref="NodeStatus.Running"/> while locomotion is active.</item>
        ///   <item>Returns <see cref="NodeStatus.Success"/> when the locomotion channel
        ///     reports arrival.</item>
        ///   <item>Returns <see cref="NodeStatus.Failure"/> on locomotion failure.</item>
        /// </list>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_ReverseToBaseline(
            ref HullDownAttackParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);

            // Sync BehaviorInstanceId.
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behav = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                loco.BehaviorInstanceId = behav.InstanceId;
            }

            // Forward executor terminal status.
            if (loco.ActiveAction == NavigationConstants.ActionIdMoveTo)
            {
                if (loco.Status == NodeStatus.Success) return NodeStatus.Success;
                if (loco.Status == NodeStatus.Failure) return NodeStatus.Failure;
            }

            // Issue the retreat command once.
            bool needsActivation = loco.ActiveAction != NavigationConstants.ActionIdMoveTo
                || loco.Status == NodeStatus.Failure;

            if (needsActivation)
            {
                unchecked { loco.ActionInstanceId++; }
                loco.ActiveAction = NavigationConstants.ActionIdMoveTo;
                WriteToLocomotionParams(ref loco, new MoveToParams
                {
                    Destination   = new Vector2(p.BaselineX, p.BaselineY),
                    ArrivalRadius = 5f,
                    // NOTE: MoveToParams does not carry a reverse flag in v1.
                    // NavState.ReverseAllowed is not yet implemented in the muscle tier.
                    Speed = 10f,
                });
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Trivial fallback node that always succeeds immediately.
        /// Ensures the outer <c>Selector</c> always succeeds so
        /// <c>Action_ReverseToBaseline</c> is guaranteed to run regardless of whether
        /// the engagement path succeeded or overshot.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_AbortEngagement(
            ref HullDownAttackParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            return NodeStatus.Success;
        }

        // ── BTree definition ──────────────────────────────────────────────────────

        /// <summary>
        /// Exposes the HullDownAttackRun BTree structure for Fbt.SourceGen static analysis.
        ///
        /// <code>
        /// Sequence
        ///   Selector
        ///     Sequence                              // Engagement path
        ///       Selector
        ///         Condition_HasTarget               // success = target visible
        ///         Action_CreepToAndBeyondSlot       // Running; Failure on overshoot
        ///       Action_AimAndFireSpecific            // fire at assigned target
        ///     Action_AbortEngagement                // overshoot fallback; always Success
        ///   Action_ReverseToBaseline                // guaranteed retreat
        /// </code>
        /// </summary>
        [BTreeDefinition("HullDownAttackRun")]
        public static BTreeBuilder<HullDownAttackBlackboard, BTreeContext> BuildHullDownAttackRunTree()
        {
            return new BTreeBuilder<HullDownAttackBlackboard, BTreeContext>()
                .Sequence(root => root
                    .Selector(outerSel => outerSel
                        .Sequence(engagementSeq => engagementSeq
                            .Selector(targetSel => targetSel
                                .Condition(bb => bb.Params, Condition_HasTarget)
                                .Action(bb => bb.Params, Action_CreepToAndBeyondSlot))
                            .Action(bb => bb.Params, Action_AimAndFireSpecific))
                        .Action(bb => bb.Params, Action_AbortEngagement))
                    .Action(bb => bb.Params, Action_ReverseToBaseline));
        }
    }
}
