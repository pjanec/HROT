using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.AI.Behaviors.Logging;

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
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private sealed class HullDownAttackParamsJsonDto
        {
            public float SlotX { get; set; }
            public float SlotY { get; set; }
            public float BaselineX { get; set; }
            public float BaselineY { get; set; }
            public float AttackDirX { get; set; }
            public float AttackDirY { get; set; }
            public float ApproachSpeed { get; set; }
            public float CreepSpeed { get; set; }
            public long TargetNetworkId { get; set; }
            public int MaxRounds { get; set; }
        }
        // ── Channel write helpers ─────────────────────────────────────────────────

        private static unsafe void WriteToLocomotionParams<T>(ref LocomotionChannel ch, T value)
            where T : unmanaged
        {
            System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref ch.Params[0]) = value;
        }

        private static unsafe T ReadLocomotionParams<T>(ref LocomotionChannel ch)
            where T : unmanaged
        {
            return System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref ch.Params[0]);
        }

        private static unsafe void WriteToWeaponParams<T>(ref WeaponChannel ch, T value)
            where T : unmanaged
        {
            System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref ch.Params[0]) = value;
        }

        private static void ClearWeaponActionIfActive(ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))
                return;

            ref var wc = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
            if (wc.ActiveAction != 0)
            {
                wc.ActiveAction = 0;
                unchecked { wc.ActionInstanceId++; }
            }
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
            if (!ctx.World.HasSingletonManaged<NetworkEntityMap>())
            {
                BehaviorLog.Warn(ref ctx, "NetworkEntityMap singleton not found; cannot resolve TargetNetworkId.");
                return NodeStatus.Failure;
            }

            var entityMap = ctx.World.GetSingletonManaged<NetworkEntityMap>();
            if (entityMap == null || !entityMap.TryGetEntity(p.TargetNetworkId, out var targetEntity))
            {
                BehaviorLog.Warn(ref ctx, "TargetNetworkId=" + p.TargetNetworkId + " not found in entity map; target may not have replicated yet.");
                return NodeStatus.Failure;
            }

            if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))
            {
                BehaviorLog.Warn(ref ctx, "Entity is missing TargetMemory component; cannot evaluate target tracking.");
                return NodeStatus.Failure;
            }

            long targetPacked = (long)targetEntity.PackedValue;
            ref readonly var mem = ref ctx.World.GetComponentRO<TargetMemory>(ctx.Self);

            // Scan TargetMemory for the resolved entity with a positive threat score.
            // Loop is bounded by MaxTrackedTargets (4) — no heap allocation.
            unsafe
            {
                for (int i = 0; i < mem.Count; i++)
                {
                    if (mem.EntityIds[i] == targetPacked && mem.ThreatScores[i] > 0f)
                    {
                        if (BehaviorLog.IsTraceEnabled)
                            BehaviorLog.Trace(ref ctx, "Target acquired in memory. TargetNetworkId=" + p.TargetNetworkId + ".");
                        return NodeStatus.Success;
                    }
                }
            }
            if (BehaviorLog.IsTraceEnabled)
                BehaviorLog.Trace(ref ctx, "Target not found in memory. TargetNetworkId=" + p.TargetNetworkId + ".");
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
            {
                BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel or SimTransform; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

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
            if (BehaviorLog.IsTraceEnabled)
            {
                BehaviorLog.Trace(ref ctx,
                    "Creep progress: distToSlot=" + distToSlot.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + "m overshoot=" + overshootMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + "m limit=" + HillAttackConstants.MaxOvershootMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "m.");
            }

            if (overshootMeters > HillAttackConstants.MaxOvershootMeters)
            {
                // Clear the locomotion channel explicitly before returning Failure.
                loco.ActiveAction = 0;
                loco.Status = NodeStatus.Failure;
                if (BehaviorLog.IsDebugEnabled)
                    BehaviorLog.Debug(ref ctx, "Creep failed due to overshoot. Overshoot=" + overshootMeters.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m.");
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

            // Only re-issue the command when the action changes, status is Failure, or
            // the speed changes (approach->creep phase transition).  Read back the last
            // written speed from the channel's param buffer to detect the transition.
            bool needsWrite = loco.ActiveAction != desiredAction
                || loco.Status == NodeStatus.Failure;

            if (!needsWrite && loco.ActiveAction == desiredAction)
            {
                // Detect approach-to-creep phase change: if the speed in the channel
                // differs from the intended speed the muscle tier is still running the
                // old command.  Incrementing ActionInstanceId signals a new intent so
                // LocomotionDispatcherSystem picks up the updated CreepSpeed.
                unsafe
                {
                    var lastParams = ReadLocomotionParams<MoveToParams>(ref loco);
                    if (MathF.Abs(lastParams.Speed - speed) > 0.001f)
                    {
                        needsWrite = true;
                        if (BehaviorLog.IsDebugEnabled)
                            BehaviorLog.Debug(ref ctx, "Creep phase transition speed update. Previous=" + lastParams.Speed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " New=" + speed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".");
                    }
                }
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
                if (BehaviorLog.IsDebugEnabled)
                    BehaviorLog.Debug(ref ctx, "Issued MoveTo action. Speed=" + speed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " destination=(" + destination.X.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "," + destination.Y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ").");
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
            if (!ctx.World.HasSingletonManaged<NetworkEntityMap>())
            {
                BehaviorLog.Warn(ref ctx, "NetworkEntityMap singleton not found; cannot resolve TargetNetworkId.");
                return NodeStatus.Failure;
            }

            var entityMap = ctx.World.GetSingletonManaged<NetworkEntityMap>();
            if (entityMap == null || !entityMap.TryGetEntity(p.TargetNetworkId, out var targetEntity))
            {
                BehaviorLog.Warn(ref ctx, "TargetNetworkId=" + p.TargetNetworkId + " not found in entity map; target may not have replicated yet or was destroyed.");
                return NodeStatus.Failure;
            }
            if (!ctx.World.IsAlive(targetEntity))
                return NodeStatus.Success;

            if (ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
            {
                ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
                if (loco.ActiveAction != 0)
                {
                    loco.ActiveAction = 0;
                    unchecked { loco.ActionInstanceId++; }
                }
            }

            if (p.MaxRounds > 0 && p.RoundsFired >= p.MaxRounds)
            {
                ClearWeaponActionIfActive(ref ctx);
                return NodeStatus.Success;
            }

            if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))
            {
                BehaviorLog.Error(ref ctx, "Entity is missing WeaponChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            if (!ctx.World.HasComponent<WeaponState>(ctx.Self))
            {
                BehaviorLog.Error(ref ctx, "Entity is missing WeaponState; cannot track rounds fired.");
                return NodeStatus.Failure;
            }

            ref var weapon = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
            var ws = ctx.World.GetComponent<WeaponState>(ctx.Self);

            if (p.LastObservedAmmo < 0)
            {
                p.LastObservedAmmo = ws.Ammo;
            }
            else if (ws.Ammo < p.LastObservedAmmo)
            {
                p.RoundsFired += (p.LastObservedAmmo - ws.Ammo);
                p.LastObservedAmmo = ws.Ammo;
                if (p.MaxRounds > 0 && p.RoundsFired >= p.MaxRounds)
                {
                    ClearWeaponActionIfActive(ref ctx);
                    return NodeStatus.Success;
                }
            }
            else if (ws.Ammo > p.LastObservedAmmo)
            {
                p.LastObservedAmmo = ws.Ammo;
            }

            // Sync BehaviorInstanceId to prevent ChannelArbitrationSystem clearing the channel.
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behav = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                weapon.BehaviorInstanceId = behav.InstanceId;
            }

            // Forward executor terminal status.
            if (weapon.ActiveAction == CombatConstants.ActionIdAimAndFire)
            {
                if (BehaviorLog.IsTraceEnabled)
                    BehaviorLog.Trace(ref ctx, "Weapon channel active. Status=" + weapon.Status + ".");
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
                    CooldownSeconds = 10f,
                });
                unchecked { weapon.ActionInstanceId++; }
                weapon.ActiveAction = CombatConstants.ActionIdAimAndFire;
                if (BehaviorLog.IsDebugEnabled)
                    BehaviorLog.Debug(ref ctx, "Engaging target. TargetEntity=" + targetEntity.Index + " TargetNetworkId=" + p.TargetNetworkId + ".");
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Commands the tank to reverse to its assigned baseline slot.
        ///
        /// <para>Writes <c>ActionIdMoveTo</c> to <c>LocomotionChannel</c> with
        /// <c>Destination = (BaselineX, BaselineY)</c> and
        /// <c>ReverseAllowed = 1</c> so the muscle tier resolves a reverse-velocity
        /// trajectory back to the baseline slot.</para>
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
                if (loco.Status == NodeStatus.Success)
                {
                    ctx.World.Bus.PublishManaged(new ClearBehaviorEvent { Entity = ctx.Self });
                    return NodeStatus.Success;
                }
                if (loco.Status == NodeStatus.Failure)
                {
                    ctx.World.Bus.PublishManaged(new ClearBehaviorEvent { Entity = ctx.Self });
                    return NodeStatus.Failure;
                }
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
                    Destination    = new Vector2(p.BaselineX, p.BaselineY),
                    ArrivalRadius  = 5f,
                    Speed          = 12f,
                    ReverseAllowed = 1,
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

        public static unsafe void ParseHullDownAttackParams(string json, byte* ptr)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                System.Runtime.CompilerServices.Unsafe.Write(ptr, default(HullDownAttackParams));
                return;
            }

            try
            {
                var dto = JsonSerializer.Deserialize<HullDownAttackParamsJsonDto>(json, JsonOptions);
                if (dto != null)
                {
                    var p = new HullDownAttackParams
                    {
                        SlotX = dto.SlotX,
                        SlotY = dto.SlotY,
                        BaselineX = dto.BaselineX,
                        BaselineY = dto.BaselineY,
                        AttackDirX = dto.AttackDirX,
                        AttackDirY = dto.AttackDirY,
                        ApproachSpeed = dto.ApproachSpeed,
                        CreepSpeed = dto.CreepSpeed,
                        TargetNetworkId = dto.TargetNetworkId,
                        MaxRounds = dto.MaxRounds,
                        RoundsFired = 0,
                        LastObservedAmmo = -1
                    };

                    System.Runtime.CompilerServices.Unsafe.Write(ptr, p);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                BehaviorLog.ParseError("Failed to parse HullDownAttackParams JSON: " + ex.Message);
            }

            System.Runtime.CompilerServices.Unsafe.Write(ptr, default(HullDownAttackParams));
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
