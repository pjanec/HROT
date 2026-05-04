using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// FastBTree action nodes for the PlatoonHillAttack commander behavior.
    ///
    /// <para>All delegates use the three-parameter
    /// <c>ReusableActionDelegate&lt;TValue, BTreeContext&gt;</c> form.
    /// Mutable working state is accessed by projecting the <c>Blackboard1024</c>
    /// component (ComponentId 74) via <c>Unsafe.As</c> to
    /// <see cref="HillAttackMutableState"/>.</para>
    /// </summary>
    public static unsafe class HillAttackCommanderNodes
    {
        // Integer ID of the HullDownAttackRun subordinate behavior.
        // Compared against BehaviorState.ActiveBehaviorHash to detect run start / end.
        private const int HullDownAttackRunBehaviorId = 3013;

        // ── Phase 4.1: Setup nodes ────────────────────────────────────────────────

        /// <summary>
        /// Computes firing-line slot count and zeroes all mutable bitmasks.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_CalculateSegments(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
            ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);

            var start = new Vector2(p.StartX, p.StartY);
            var end   = new Vector2(p.EndX,   p.EndY);
            float segLen  = Vector2.Distance(start, end);
            float spacing = p.TankSpacing > 0f ? p.TankSpacing : 30f;
            int totalSlots = Math.Max(1, (int)(segLen / spacing));
            if (totalSlots > 16) totalSlots = 16;

            s.TotalSlots          = totalSlots;
            s.BurnedSlotsMask     = 0;
            s.WaveUsedSlotsMask   = 0;
            s.BaselineReservedMask = 0;
            s.ActiveAttackerCount = 0;
            s.CurrentWave         = 0;
            s.CachedEqsRequestId  = -1;
            s.CachedTargetGroupHandle = -1;
            return NodeStatus.Success;
        }

        /// <summary>
        /// Orders all alive subordinates to move to their assigned baseline slot
        /// by publishing <see cref="AssignTacticalIntentEvent"/> with
        /// <c>IntentId = "MoveToLocation"</c>.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_DispatchAllToBaseline(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
            ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);

            if (!ctx.World.HasComponent<UnitRoster>(ctx.Self))
                return NodeStatus.Success;

            ref readonly var roster = ref ctx.World.GetComponentRO<UnitRoster>(ctx.Self);

            s.BaselineReservedMask = 0;
            int count = roster.Count;

            for (int i = 0; i < count; i++)
            {
                long packed = roster.SubordinateEntities[i];
                if (packed == 0) continue;
                var sub = new Entity((ulong)packed);
                if (!ctx.World.IsAlive(sub)) continue;

                // Interpolate baseline position for this tank.
                float t  = count > 1 ? (float)i / (count - 1) : 0.5f;
                float bx = p.BaselineStartX + (p.BaselineEndX - p.BaselineStartX) * t;
                float by = p.BaselineStartY + (p.BaselineEndY - p.BaselineStartY) * t;

                // Emit JSON matching CgfNodes.MoveToLocationParamsJsonDto {X, Y, Speed}.
                string json = "{\"X\":" + bx.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                            + ",\"Y\":" + by.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                            + ",\"Speed\":15.0}";

                ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity     = sub,
                    IntentId   = "MoveToLocation",
                    JsonParams = json,
                });

                if (i < 16) s.BaselineReservedMask |= (ushort)(1 << i);
            }
            return NodeStatus.Success;
        }

        /// <summary>
        /// Waits until every alive subordinate has arrived at its baseline slot.
        /// Returns <see cref="NodeStatus.Success"/> when all alive subordinates have
        /// <c>NavigationStatus.Result == NavigationResult.Arrived</c>.
        /// Dead subordinates count as arrived.
        /// Returns <see cref="NodeStatus.Running"/> if any alive subordinate has not yet arrived.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Condition_AreAllAtBaseline(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<UnitRoster>(ctx.Self))
                return NodeStatus.Success;

            ref readonly var roster = ref ctx.World.GetComponentRO<UnitRoster>(ctx.Self);
            int count = roster.Count;

            for (int i = 0; i < count; i++)
            {
                long packed = roster.SubordinateEntities[i];
                if (packed == 0) continue;
                var sub = new Entity((ulong)packed);
                if (!ctx.World.IsAlive(sub)) continue;  // dead = counts as arrived

                if (!ctx.World.HasComponent<NavigationStatus>(sub))
                    return NodeStatus.Running;

                ref readonly var nav = ref ctx.World.GetComponentRO<NavigationStatus>(sub);

                // Treat Arrived, FailedBlocked, and FailedUnreachable as completion.
                // Only block the sequence if the tank is actively still trying to move.
                if (nav.Result == NavigationResult.InProgress)
                    return NodeStatus.Running;

            }
            return NodeStatus.Success;
        }

        // ── Phase 4.2: EQS integration nodes ─────────────────────────────────────

        /// <summary>
        /// Submits an area query for the target polygon.
        /// Returns <see cref="NodeStatus.Running"/> when the batch is full or when a
        /// previously submitted request is still being resolved.
        /// Returns <see cref="NodeStatus.Success"/> once the request is queued.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_RequestAreaQuery(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
            ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);

            // Guard: if a request is already in-flight, do not submit a duplicate.
            if (s.CachedEqsRequestId != -1)
            {
                var existing = AreaQueryBatchHelper.GetAreaQueryResult(ctx.World, s.CachedEqsRequestId);
                if (!existing.IsReady)
                    return NodeStatus.Running;
                // Result is ready; advance sequence so next node can consume it.
                return NodeStatus.Success;
            }

            // Submit fresh request.
            long id = AreaQueryBatchHelper.RequestAreaQuery(ctx.World, ctx.Self, p.TargetAreaEntity, ForceId.Hostile);
            if (id == -1)
                return NodeStatus.Running;  // batch full; retry next frame

            s.CachedEqsRequestId = id;
            return NodeStatus.Success;
        }

        /// <summary>
        /// Polls for the area query result.
        /// Returns <see cref="NodeStatus.Running"/> while the result is not yet ready.
        /// Returns <see cref="NodeStatus.Failure"/> when the area is clear (TargetCount == 0).
        /// Returns <see cref="NodeStatus.Success"/> when targets are present; caches the
        /// <c>TargetGroupHandle</c> for use by <see cref="Action_DispatchWaveWithTargets"/>.
        /// Per SC-HA011-5, <c>CachedEqsRequestId</c> is NOT cleared on the Success path.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Condition_IsAreaQueryResolved(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
            ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);

            if (s.CachedEqsRequestId == -1)
                return NodeStatus.Failure;  // guard; should not occur in correct topology

            var result = AreaQueryBatchHelper.GetAreaQueryResult(ctx.World, s.CachedEqsRequestId);
            if (!result.IsReady)
                return NodeStatus.Running;

            if (result.TargetCount == 0)
            {
                // Area cleared: break out of the Repeater so the BTree can finish.
                s.CachedEqsRequestId      = -1;
                s.CachedTargetGroupHandle = -1;
                return NodeStatus.Failure;
            }

            // Targets found: cache the pool handle for Action_DispatchWaveWithTargets.
            // CachedEqsRequestId is intentionally NOT cleared here (SC-HA011-5).
            s.CachedTargetGroupHandle = result.TargetGroupHandle;
            return NodeStatus.Success;
        }

        // ── Phase 4.4: Wave dispatch node ─────────────────────────────────────────

        /// <summary>
        /// Assigns firing slots, baseline slots, and targets to tanks in the current wave,
        /// then publishes <see cref="AssignTacticalIntentEvent"/> for each selected tank.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_DispatchWaveWithTargets(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
            ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);

            s.WaveUsedSlotsMask   = 0;
            s.ActiveAttackerCount = 0;

            // Resolve target count from the cached EQS result.
            int targetCount = 0;
            if (s.CachedEqsRequestId != -1)
            {
                var eqsResult = AreaQueryBatchHelper.GetAreaQueryResult(ctx.World, s.CachedEqsRequestId);
                if (eqsResult.IsReady)
                    targetCount = eqsResult.TargetCount;
            }
            // Fallback: probe pool if result is no longer in the batch.
            if (targetCount == 0 && s.CachedTargetGroupHandle >= 0)
            {
                while (true)
                {
                    long t = AreaQueryBatchHelper.GetTargetFromPool(ctx.World, s.CachedTargetGroupHandle, targetCount);
                    if (t == 0L) break;
                    targetCount++;
                    if (targetCount > 1024) break;  // safety cap
                }
            }
            if (targetCount == 0) targetCount = 1;  // avoid divide-by-zero

            if (!ctx.World.HasComponent<UnitRoster>(ctx.Self))
            {
                s.CachedTargetGroupHandle = -1;
                s.CachedEqsRequestId      = -1;
                s.CurrentWave             = (byte)(1 - s.CurrentWave);
                return NodeStatus.Success;
            }

            ref readonly var roster = ref ctx.World.GetComponentRO<UnitRoster>(ctx.Self);
            int rosterCount  = roster.Count;
            bool allParticipate = rosterCount <= 3;

            int activeTankIndexInWave = 0;

            for (int i = 0; i < rosterCount && s.ActiveAttackerCount < 8; i++)
            {
                long packed = roster.SubordinateEntities[i];
                if (packed == 0) continue;
                var sub = new Entity((ulong)packed);
                if (!ctx.World.IsAlive(sub)) continue;

                // Wave parity: use Entity.Index (immutable) NOT roster index i.
                if (!allParticipate && (sub.Index % 2) != s.CurrentWave) continue;

                // Pick the first available firing-line slot.
                int firingSlot = GetFirstAvailableSlot((ushort)(s.BurnedSlotsMask | s.WaveUsedSlotsMask), s.TotalSlots);
                if (firingSlot < 0) continue;  // no slots left; skip tank

                // Interpolate firing-slot world position.
                float ft = s.TotalSlots > 1 ? (float)firingSlot / (s.TotalSlots - 1) : 0.5f;
                float fx = p.StartX + (p.EndX - p.StartX) * ft;
                float fy = p.StartY + (p.EndY - p.StartY) * ft;

                // Pick closest unreserved baseline slot (distance-squared).
                int baselineSlot = PickClosestBaselineSlot(ref p, ref s, fx, fy, s.TotalSlots);

                // Round-robin target assignment.
                int targetIdx   = activeTankIndexInWave % targetCount;
                long targetPacked = AreaQueryBatchHelper.GetTargetFromPool(ctx.World, s.CachedTargetGroupHandle, targetIdx);
                long targetNetId  = 0L;
                if (targetPacked != 0L)
                {
                    var targetEntity = new Entity((ulong)targetPacked);
                    if (ctx.World.IsAlive(targetEntity)
                        && ctx.World.HasComponent<NetworkIdentity>(targetEntity))
                    {
                        targetNetId = ctx.World.GetComponentRO<NetworkIdentity>(targetEntity).Value;
                    }
                }

                // Baseline slot world position.
                float bt = s.TotalSlots > 1 ? (float)baselineSlot / (s.TotalSlots - 1) : 0.5f;
                float bx = p.BaselineStartX + (p.BaselineEndX - p.BaselineStartX) * bt;
                float by = p.BaselineStartY + (p.BaselineEndY - p.BaselineStartY) * bt;

                // Write SoA tracker entry.
                int idx = s.ActiveAttackerCount;
                s.ActiveEntityPacked[idx]      = (long)sub.PackedValue;
                s.ActiveSlotIndex[idx]          = (byte)firingSlot;
                s.ReturnBaselineSlotIndex[idx]  = (byte)baselineSlot;
                s.HasStartedRun[idx]            = 0;
                s.WaveUsedSlotsMask            |= (ushort)(1 << firingSlot);
                s.BaselineReservedMask         |= (ushort)(1 << baselineSlot);
                s.ActiveAttackerCount++;
                activeTankIndexInWave++;

                // Serialize JSON for HullDownAttackRun.
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string json = "{\"SlotX\":"    + fx.ToString("G6", ic)
                            + ",\"SlotY\":"    + fy.ToString("G6", ic)
                            + ",\"BaselineX\":" + bx.ToString("G6", ic)
                            + ",\"BaselineY\":" + by.ToString("G6", ic)
                            + ",\"AttackDirX\":" + p.AttackDirX.ToString("G6", ic)
                            + ",\"AttackDirY\":" + p.AttackDirY.ToString("G6", ic)
                            + ",\"TargetNetworkId\":" + targetNetId.ToString(ic)
                            + ",\"ApproachSpeed\":15"
                            + ",\"CreepSpeed\":5}";

                ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity     = sub,
                    IntentId   = "HullDownAttack",
                    JsonParams = json,
                });
            }

            s.CachedTargetGroupHandle = -1;
            s.CachedEqsRequestId      = -1;
            s.CurrentWave             = (byte)(1 - s.CurrentWave);
            return NodeStatus.Success;
        }

        // ── Phase 4.5: Wave completion node ───────────────────────────────────────

        /// <summary>
        /// Monitors active attackers.
        /// Returns <see cref="NodeStatus.Success"/> when all attackers have returned
        /// to baseline (or were killed).
        /// Returns <see cref="NodeStatus.Running"/> while any attacker is still active.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Condition_IsWaveCompleted(
            ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
            ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);

            if (s.ActiveAttackerCount == 0) return NodeStatus.Success;

            for (int i = s.ActiveAttackerCount - 1; i >= 0; i--)
            {
                long packed    = s.ActiveEntityPacked[i];
                var attacker   = new Entity((ulong)packed);

                if (!ctx.World.IsAlive(attacker))
                {
                    // Tank died: permanently burn the slot it was assigned.
                    s.BurnedSlotsMask     |= (ushort)(1 << s.ActiveSlotIndex[i]);
                    s.BaselineReservedMask &= (ushort)~(1 << s.ReturnBaselineSlotIndex[i]);
                    SwapRemove(ref s, i);
                }
                else if (s.HasStartedRun[i] == 0)
                {
                    // Intent is still propagating through the ingress pipeline.
                    // Once we see the HullDownAttackRun hash, mark as started.
                    if (ctx.World.HasComponent<BehaviorState>(attacker))
                    {
                        var beh = ctx.World.GetComponent<BehaviorState>(attacker);
                        if (beh.ActiveBehaviorHash == HullDownAttackRunBehaviorId)
                            s.HasStartedRun[i] = 1;
                    }
                    // Do not remove; run has not started yet.
                }
                else
                {
                    // HasStartedRun == 1: check whether the run has finished.
                    if (ctx.World.HasComponent<BehaviorState>(attacker))
                    {
                        var beh = ctx.World.GetComponent<BehaviorState>(attacker);
                        if (beh.ActiveBehaviorHash != HullDownAttackRunBehaviorId)
                        {
                            // Run complete (returned to baseline or abort path).
                            s.BaselineReservedMask &= (ushort)~(1 << s.ReturnBaselineSlotIndex[i]);
                            SwapRemove(ref s, i);
                        }
                    }
                }
            }

            return s.ActiveAttackerCount == 0 ? NodeStatus.Success : NodeStatus.Running;
        }

        // ── BTree definition ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds the PlatoonHillAttack commander BTree.
        ///
        /// <code>
        /// Sequence
        ///   Action_CalculateSegments
        ///   Action_DispatchAllToBaseline
        ///   Condition_AreAllAtBaseline
        ///   Repeater(-1)
        ///     Sequence
        ///       Action_RequestAreaQuery
        ///       Condition_IsAreaQueryResolved
        ///       Action_DispatchWaveWithTargets
        ///       Condition_IsWaveCompleted
        /// </code>
        /// </summary>
        [BTreeDefinition("PlatoonHillAttack")]
        public static BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext> BuildPlatoonHillAttackTree()
        {
            return new BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(bb => bb.Params, Action_CalculateSegments)
                    .Action(bb => bb.Params, Action_DispatchAllToBaseline)
                    .Action(bb => bb.Params, Condition_AreAllAtBaseline)
                    .Repeater(-1, rep => rep
                        .Sequence(wseq => wseq
                            .Action(bb => bb.Params, Action_RequestAreaQuery)
                            .Action(bb => bb.Params, Condition_IsAreaQueryResolved)
                            .Action(bb => bb.Params, Action_DispatchWaveWithTargets)
                            .Action(bb => bb.Params, Condition_IsWaveCompleted))));
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static int GetFirstAvailableSlot(ushort blockedMask, int totalSlots)
        {
            for (int i = 0; i < totalSlots; i++)
            {
                if ((blockedMask & (1 << i)) == 0) return i;
            }
            return -1;
        }

        private static int PickClosestBaselineSlot(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s,
            float slotX, float slotY, int totalSlots)
        {
            int   best     = -1;
            float bestDist = float.MaxValue;

            // First pass: closest unreserved slot.
            for (int j = 0; j < totalSlots; j++)
            {
                if ((s.BaselineReservedMask & (1 << j)) != 0) continue;
                float bt = totalSlots > 1 ? (float)j / (totalSlots - 1) : 0.5f;
                float bx = p.BaselineStartX + (p.BaselineEndX - p.BaselineStartX) * bt;
                float by = p.BaselineStartY + (p.BaselineEndY - p.BaselineStartY) * bt;
                float dx = bx - slotX; float dy = by - slotY;
                float d  = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = j; }
            }

            if (best >= 0) return best;

            // Second pass (edge case — all slots reserved): closest regardless.
            bestDist = float.MaxValue;
            for (int j = 0; j < totalSlots; j++)
            {
                float bt = totalSlots > 1 ? (float)j / (totalSlots - 1) : 0.5f;
                float bx = p.BaselineStartX + (p.BaselineEndX - p.BaselineStartX) * bt;
                float by = p.BaselineStartY + (p.BaselineEndY - p.BaselineStartY) * bt;
                float dx = bx - slotX; float dy = by - slotY;
                float d  = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = j; }
            }
            return best;
        }

        private static void SwapRemove(ref HillAttackMutableState s, int index)
        {
            int last = s.ActiveAttackerCount - 1;
            if (index != last)
            {
                s.ActiveEntityPacked[index]     = s.ActiveEntityPacked[last];
                s.ActiveSlotIndex[index]         = s.ActiveSlotIndex[last];
                s.ReturnBaselineSlotIndex[index] = s.ReturnBaselineSlotIndex[last];
                s.HasStartedRun[index]           = s.HasStartedRun[last];
            }
            s.ActiveAttackerCount--;
        }

        // ── TASK-HA016: ParsePlatoonHillAttackParams (cold path) ─────────────────

        /// <summary>
        /// Parses a JSON string authored in the scenario editor and writes a
        /// <see cref="PlatoonHillAttackParams"/> value into the blackboard memory pointer.
        /// Converts geodetic coordinates to ENU Cartesian via
        /// <paramref name="geoTransform"/> when available; falls back to
        /// longitude/latitude as X/Y in Cartesian-only contexts.
        /// The attack direction is computed as the left-hand perpendicular of the
        /// normalised firing-line vector — it is not authored directly.
        /// </summary>
        public static unsafe void ParsePlatoonHillAttackParams(
            string json,
            byte* ptr,
            Fdp.Modules.Geographic.IGeographicTransform? geoTransform,
            NetworkEntityMap entityMap)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Unsafe.Write(ptr, default(PlatoonHillAttackParams));
                return;
            }

            PlatoonHillAttackParamsJsonDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<PlatoonHillAttackParamsJsonDto>(json, JsonOptions);
            }
            catch
            {
                Unsafe.Write(ptr, default(PlatoonHillAttackParams));
                return;
            }

            if (dto == null)
            {
                Unsafe.Write(ptr, default(PlatoonHillAttackParams));
                return;
            }

            var result = new PlatoonHillAttackParams
            {
                TankSpacing = dto.TankSpacing > 0f ? dto.TankSpacing : 30f,
            };

            // Resolve firing-line and baseline positions.
            if (geoTransform != null)
            {
                var start = geoTransform.ToCartesian(dto.FiringLineStart.Latitude, dto.FiringLineStart.Longitude, 0.0);
                result.StartX = start.X; result.StartY = start.Y;

                var end = geoTransform.ToCartesian(dto.FiringLineEnd.Latitude, dto.FiringLineEnd.Longitude, 0.0);
                result.EndX = end.X; result.EndY = end.Y;

                var baselineStart = geoTransform.ToCartesian(dto.BaselineStart.Latitude, dto.BaselineStart.Longitude, 0.0);
                result.BaselineStartX = baselineStart.X; result.BaselineStartY = baselineStart.Y;

                var baselineEnd = geoTransform.ToCartesian(dto.BaselineEnd.Latitude, dto.BaselineEnd.Longitude, 0.0);
                result.BaselineEndX = baselineEnd.X; result.BaselineEndY = baselineEnd.Y;
            }
            else
            {
                // Cartesian-only fallback (tests / offline contexts).
                result.StartX = (float)dto.FiringLineStart.Longitude; result.StartY = (float)dto.FiringLineStart.Latitude;
                result.EndX = (float)dto.FiringLineEnd.Longitude; result.EndY = (float)dto.FiringLineEnd.Latitude;
                result.BaselineStartX = (float)dto.BaselineStart.Longitude; result.BaselineStartY = (float)dto.BaselineStart.Latitude;
                result.BaselineEndX = (float)dto.BaselineEnd.Longitude; result.BaselineEndY = (float)dto.BaselineEnd.Latitude;
            }

            // Compute attack direction as left-hand perpendicular of firing-line direction.
            var fireVec = new Vector2(result.EndX - result.StartX, result.EndY - result.StartY);
            float len = fireVec.Length();
            if (len > 0.0001f)
            {
                var norm = fireVec / len;
                result.AttackDirX = -norm.Y;
                result.AttackDirY =  norm.X;
            }

            // Resolve target area entity.
            if (dto.TargetAreaNetworkId != 0 && entityMap.TryGetEntity(dto.TargetAreaNetworkId, out var areaEntity))
                result.TargetAreaEntity = areaEntity;
            else
                result.TargetAreaEntity = Entity.Null;

            Unsafe.Write(ptr, result);
        }

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    // ── ParseParams DTO (private to this assembly; cold path only) ───────────────

    /// <summary>
    /// Private JSON deserialization helper for <c>ParsePlatoonHillAttackParams</c>.
    /// Must never be referenced from BTree hot-path nodes.
    /// </summary>
    internal sealed class PlatoonHillAttackParamsJsonDto
    {
        public PickableGeoPoint FiringLineStart    { get; set; }
        public PickableGeoPoint FiringLineEnd      { get; set; }
        public PickableGeoPoint BaselineStart      { get; set; }
        public PickableGeoPoint BaselineEnd        { get; set; }
        public float     TankSpacing        { get; set; }
        public long      TargetAreaNetworkId { get; set; }
    }
}
