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
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Logging;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// FastBTree action nodes for the PlatoonHillAttack commander behavior.
    ///
    /// <para>S3-G: the six mutable-state nodes use the four-parameter stateful form
    /// <c>(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState, ref BTreeContext)</c>.
    /// The <see cref="HillAttackMutableState"/> working state lives in the behaviour's ONE block — projected by the
    /// JSON emitter's thunk from <c>PlatoonHillAttack_Block.St.State</c> (production, <c>CE-437</c>) or by the code
    /// builder's <c>StatefulAction</c> from <see cref="PlatoonHillAttackBlackboard.State"/>
    /// (<see cref="BuildPlatoonHillAttackTree"/>, <c>CE-430</c>). ⛔ HISTORY: a Behavior-scoped partition slot
    /// (S3-G), and before it a <c>Blackboard1024</c> + <c>Unsafe.As</c> offset.
    /// <c>Condition_AreAllAtBaseline</c> touches no working state and stays three-parameter.</para>
    /// </summary>
    public static unsafe class HillAttackCommanderNodes
    {
        // Integer ID of the HullDownAttackRun subordinate behavior.
        // Compared against BehaviorState.ActiveBehaviorHash to detect run start / end.
        private static readonly int HullDownAttackRunBehaviorId = BehaviorHash.FromName(BehaviorNames.HullDownAttackRun);

        // ── The area query: one EQS child sensor (DESIGN_Hill_Attack_Eqs_Migration.md §3.2, §4 D2/D5/D6) ──

        /// <summary>
        /// The EQS template the commander asks — <c>EntitiesOfForceInArea</c> (<c>Hrot.SimHost</c>, Muscle side). ⭐ A Brain
        /// behaviour names a template by its AssetId, exactly as a blueprint's <c>SpawnEqsSensor.TemplateAssetId</c> does;
        /// <c>HillAttackNodeTests.EQS_AreaSensor_AsksTheEntitiesOfForceInAreaTemplate</c> pins it equal to the template's own constant.
        /// </summary>
        public const string AreaTemplateAssetId = "3e5a7c91-2b4d-4f86-a0c3-5d7e9f1b2a64";

        /// <summary>The sensor's <c>PartMetadata.InstanceId</c> under the commander — its DDS key. Fixed, so a sensor left by an
        /// aborted run is re-found, never duplicated.</summary>
        public const int AreaSensorInstanceId = 0x48410001;

        private static readonly uint AreaTemplateBlueprintId = EqsTemplateRegistry.BlueprintIdOf(new Guid(AreaTemplateAssetId));

        /// <summary>The sensor configuration for <paramref name="area"/>: hostile, alive entities inside its polygon.</summary>
        public static EqsSensor AreaSensor(Entity area) => new EqsSensor
        {
            BlueprintId   = AreaTemplateBlueprintId,
            Epoch         = 1u,
            FactionFilter = 1u << (int)ForceId.Hostile,
            ContextSlot1  = area,
        };

        /// <summary><see cref="HillAttackMutableState.CachedEqsRequestId"/> while the sensor that answers is still being created
        /// — its CREATION is the question (the first answer is computed after it), so no refresh is needed (§3.2).</summary>
        public const long SensorBeingCreated = -2;

        /// <summary>The sensor whose answer is awaited, or <see cref="Entity.Null"/>. While it is being created it is FOUND
        /// (an ECB handle is not an entity) and cached once it exists.</summary>
        private static Entity InFlightSensor(ref HillAttackMutableState s, ref BTreeContext ctx)
        {
            if (s.CachedEqsRequestId == -1) return Entity.Null;
            if (s.CachedEqsRequestId == SensorBeingCreated)
            {
                var found = EqsChildSensor.Find(ctx.World, ctx.Self, AreaSensorInstanceId);
                if (!found.IsNull) s.CachedEqsRequestId = (long)found.PackedValue;
                return found;
            }
            return s.CachedEqsRequestId < 0 ? Entity.Null : new Entity((ulong)s.CachedEqsRequestId);
        }

        // ── Phase 4.1: Setup nodes ────────────────────────────────────────────────

        /// <summary>
        /// Computes firing-line slot count and zeroes all mutable bitmasks.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        // S3-G: no [BTreeAction] — stateful (4-param) nodes are bound by the JSON emitter's stateful
        // thunk / the code builder's StatefulAction helper, not FbtActionRegistrar's generic path.
        public static NodeStatus Action_CalculateSegments(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
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
            s.EqsRequestTime      = 0f;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "Calculated slots=" + totalSlots + " spacing=" + spacing.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "m.");
            return NodeStatus.Success;
        }

        /// <summary>
        /// Orders all alive subordinates to move to their assigned baseline slot
        /// by publishing <see cref="AssignTacticalIntentEvent"/> with
        /// <c>IntentId = "MoveToLocation"</c>.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        public static NodeStatus Action_DispatchAllToBaseline(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<UnitRoster>(ctx.Self))
                return NodeStatus.Success;

            ref readonly var roster = ref ctx.World.GetComponentRO<UnitRoster>(ctx.Self);

            s.BaselineReservedMask = 0;
            int count = roster.Count;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "Dispatching baseline move intents. Subordinates=" + count + ".");

            for (int i = 0; i < count; i++)
            {
                var sub = roster.SubordinateEntities[i];
                long packed = (long)sub.PackedValue;
                if (packed == 0) continue;
                if (!CombatLife.IsAlive(ctx.World, sub)) continue;   // CE-466: knocked-out tanks take no orders

                // Interpolate baseline position for this tank.
                float t  = count > 1 ? (float)i / (count - 1) : 0.5f;
                float bx = p.BaselineStartX + (p.BaselineEndX - p.BaselineStartX) * t;
                float by = p.BaselineStartY + (p.BaselineEndY - p.BaselineStartY) * t;

                var dto = new CgfNodes.MoveToLocationParams
                {
                    X = bx,
                    Y = by,
                    Speed = 15.0f,
                    ArrivalRadius = 5.0f
                };

                string json = JsonSerializer.Serialize(
                    dto,
                    Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

                ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity     = sub,
                    IntentId   = "MoveToLocation",
                    JsonParams = json,
                });

                if (i < 16) s.BaselineReservedMask |= (ushort)(1 << i);
            }
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "Baseline dispatch complete. ReservedMask=" + s.BaselineReservedMask + ".");
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
            int arrivedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var sub = roster.SubordinateEntities[i];
                long packed = (long)sub.PackedValue;
                if (packed == 0) continue;
                if (!CombatLife.IsAlive(ctx.World, sub)) continue;  // dead (knocked out or gone) = counts as arrived

                if (!ctx.World.HasComponent<NavigationStatus>(sub))
                {
                    if (BehaviorLog.IsTraceEnabled)
                        BehaviorLog.Trace(ref ctx, "Baseline wait: subordinate=" + sub.Index + " missing NavigationStatus.");
                    return NodeStatus.Running;
                }

                ref readonly var nav = ref ctx.World.GetComponentRO<NavigationStatus>(sub);

                // Treat Arrived, FailedBlocked, and FailedUnreachable as completion.
                // Only block the sequence if the tank is actively still trying to move.
                if (nav.Result == NavigationResult.InProgress)
                {
                    if (BehaviorLog.IsTraceEnabled)
                        BehaviorLog.Trace(ref ctx, "Baseline wait: arrived=" + arrivedCount + "/" + count + " blockingSub=" + sub.Index + ".");
                    return NodeStatus.Running;
                }
                arrivedCount++;

            }
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "All subordinates at baseline. Arrived=" + arrivedCount + "/" + count + ".");
            return NodeStatus.Success;
        }

        // ── Phase 4.2: EQS integration nodes ─────────────────────────────────────

        /// <summary>
        /// Asks the area query: ensures the commander's EQS child sensor exists and REFRESHES it (a new epoch), so the next
        /// answer is computed after this moment — the old per-wave request, on one persistent sensor.
        /// Returns <see cref="NodeStatus.Running"/> while the sensor is being created or a refreshed answer is still in flight,
        /// <see cref="NodeStatus.Success"/> once the question is asked, <see cref="NodeStatus.Failure"/> without a live area.
        /// </summary>
        public static NodeStatus Action_RequestAreaQuery(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            // Guard: a question already in flight is not asked twice.
            var inFlight = InFlightSensor(ref s, ref ctx);
            if (inFlight.IsNull && s.CachedEqsRequestId == SensorBeingCreated)
                return NodeStatus.Running;       // still being created
            if (!inFlight.IsNull)
            {
                if (ctx.World.IsAlive(inFlight) && ctx.World.HasComponent<EqsCognitiveBuffer>(inFlight))
                {
                    if (!ctx.World.GetComponentRO<EqsCognitiveBuffer>(inFlight).IsReady)
                    {
                        if (BehaviorLog.IsTraceEnabled)
                            BehaviorLog.Trace(ref ctx, "EQS area query in flight. Sensor=" + inFlight.Index + ".");
                        return NodeStatus.Running;
                    }
                    return NodeStatus.Success;   // answered; the next node consumes it
                }
                s.CachedEqsRequestId = -1;       // the sensor vanished: ask again below
            }

            // Guard: TargetAreaEntity must be alive before submitting a query.
            if (p.TargetAreaEntity.IsNull || !ctx.World.IsAlive(p.TargetAreaEntity))
            {
                BehaviorLog.Error(ref ctx, "TargetAreaEntity is null or dead. Cannot execute area query.");
                return NodeStatus.Failure;
            }

            var config = AreaSensor(p.TargetAreaEntity);
            var sensor = EqsChildSensor.Ensure(ctx.World, ctx.Self, AreaSensorInstanceId, config);
            if (sensor.IsNull)
            {
                // Created this frame (it exists after the command buffer plays back): the creation IS the question.
                s.CachedEqsRequestId = SensorBeingCreated;
            }
            else
            {
                EqsChildSensor.Refresh(ctx.World, sensor, config);   // ask again: a new epoch, the old answer cleared
                s.CachedEqsRequestId = (long)sensor.PackedValue;
            }
            s.EqsRequestTime = ctx.World.SimulationTime;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "Asked the EQS area query. Sensor=" + (sensor.IsNull ? "creating" : sensor.Index.ToString()) + ".");
            return NodeStatus.Success;
        }

        /// <summary>
        /// Polls the area query's answer.
        /// Returns <see cref="NodeStatus.Running"/> while no answer for the current epoch has arrived;
        /// <see cref="NodeStatus.Failure"/> when the area is clear (0 targets) or after 5 s without an answer (⭐ with no area
        /// on the Muscle the sensor answers NOTHING — EQS design §17.5 — so the timeout, never a false "clear", ends it);
        /// <see cref="NodeStatus.Success"/> when targets are present. ⭐ <c>CachedEqsRequestId</c> is NOT cleared on Success
        /// (SC-HA011-5): the dispatch reads the answer from the same sensor.
        /// </summary>
        public static NodeStatus Condition_IsAreaQueryResolved(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            if (s.CachedEqsRequestId == -1)
                return NodeStatus.Failure;  // guard; should not occur in correct topology
            var sensor = InFlightSensor(ref s, ref ctx);

            bool ready = ctx.World.IsAlive(sensor)
                && ctx.World.HasComponent<EqsCognitiveBuffer>(sensor)
                && ctx.World.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady;
            if (!ready)
            {
                if (ctx.World.SimulationTime - s.EqsRequestTime > 5.0f)
                {
                    BehaviorLog.Error(ref ctx, "EQS area query timed out after 5.0s.");
                    EqsChildSensor.Destroy(ctx.World, sensor);
                    s.CachedEqsRequestId = -1;
                    return NodeStatus.Failure;
                }
                if (BehaviorLog.IsTraceEnabled)
                    BehaviorLog.Trace(ref ctx, "Waiting EQS result.");
                return NodeStatus.Running;
            }

            int count = ctx.World.GetComponentRO<EqsCognitiveBuffer>(sensor).Count;
            if (count == 0)
            {
                // Area cleared: break out of the Repeater so the BTree can finish; the sensor is no longer needed.
                EqsChildSensor.Destroy(ctx.World, sensor);
                s.CachedEqsRequestId = -1;
                s.EqsRequestTime     = 0f;
                if (BehaviorLog.IsDebugEnabled)
                    BehaviorLog.Debug(ref ctx, "EQS resolved clear area. targets=0.");
                return NodeStatus.Failure;
            }

            s.EqsRequestTime = 0f;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "EQS resolved targets. targets=" + count + ".");
            return NodeStatus.Success;
        }

        // ── Phase 4.4: Wave dispatch node ─────────────────────────────────────────

        /// <summary>
        /// Assigns firing slots, baseline slots, and targets to tanks in the current wave,
        /// then publishes <see cref="AssignTacticalIntentEvent"/> for each selected tank.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        public static NodeStatus Action_DispatchWaveWithTargets(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            s.WaveUsedSlotsMask   = 0;
            s.ActiveAttackerCount = 0;
            byte dispatchWave = s.CurrentWave;

            // The answer: Brain-local target entities from the area sensor's buffer (copied — the loop below publishes).
            long* targets = stackalloc long[EqsResultPool.MaxTopK];
            int targetCount = 0;
            var sensor = InFlightSensor(ref s, ref ctx);
            if (!sensor.IsNull && ctx.World.IsAlive(sensor) && ctx.World.HasComponent<EqsCognitiveBuffer>(sensor))
            {
                ref readonly var answer = ref ctx.World.GetComponentRO<EqsCognitiveBuffer>(sensor);
                if (answer.IsReady)
                {
                    var results = answer.GetSpanRO();
                    targetCount = Math.Min(answer.Count, EqsResultPool.MaxTopK);
                    for (int k = 0; k < targetCount; k++) targets[k] = results[k].EntityId;
                }
            }
            int targetModulus = targetCount == 0 ? 1 : targetCount;  // avoid divide-by-zero

            if (!ctx.World.HasComponent<UnitRoster>(ctx.Self))
            {
                s.CachedEqsRequestId      = -1;
                s.EqsRequestTime          = 0f;
                s.CurrentWave             = (byte)(1 - s.CurrentWave);
                return NodeStatus.Success;
            }

            ref readonly var roster = ref ctx.World.GetComponentRO<UnitRoster>(ctx.Self);
            int rosterCount  = roster.Count;
            bool allParticipate = rosterCount <= 3;
            int* avail = stackalloc int[16];

            int activeTankIndexInWave = 0;

            for (int i = 0; i < rosterCount && s.ActiveAttackerCount < 8; i++)
            {
                var sub = roster.SubordinateEntities[i];
                long packed = (long)sub.PackedValue;
                if (packed == 0) continue;
                if (!CombatLife.IsAlive(ctx.World, sub)) continue;   // CE-466: knocked-out tanks take no orders

                // Wave parity: use Entity.Index (immutable) NOT roster index i.
                if (!allParticipate && (sub.Index % 2) != s.CurrentWave) continue;

                int availCount = 0;
                ushort blockedMask = (ushort)(s.BurnedSlotsMask | s.WaveUsedSlotsMask);
                for (int j = 0; j < s.TotalSlots; j++)
                {
                    if ((blockedMask & (1 << j)) == 0) avail[availCount++] = j;
                }

                if (availCount == 0)
                {
                    BehaviorLog.Warn(ref ctx, "No firing-line slots available for subordinate Entity:" + sub.Index + "; skipping this wave assignment.");
                    continue;  // no slots left; skip tank
                }
                // ⭐⭐ CE-202 — REPRODUCIBLE, not fixed. This drew from Random.Shared, so two runs of the
                //    same scenario picked different slots and could not be compared at all; it is also
                //    why CE-174's mechanism made kills look intermittent. Same inputs now give the same
                //    slot, while the xorshift keeps the scatter an observer sees.
                //    ⛔ SlotOps.PickRandomFreeSlot — the curated twin of this very line — has carried
                //    the deterministic form since architect Q#8-C mandated it. This is the oracle
                //    adopting it, not a new invention.
                var slotRng = SimRng.FromSim((int)sub.Index, s.CurrentWave, ctx.World.SimulationTime);
                int firingSlot = avail[slotRng.NextInt(0, availCount)];

                // Interpolate firing-slot world position.
                float ft = s.TotalSlots > 1 ? (float)firingSlot / (s.TotalSlots - 1) : 0.5f;
                float fx = p.StartX + (p.EndX - p.StartX) * ft;
                float fy = p.StartY + (p.EndY - p.StartY) * ft;

                // Pick closest unreserved baseline slot (distance-squared).
                int baselineSlot = PickClosestBaselineSlot(ref p, ref s, fx, fy, s.TotalSlots);

                // Round-robin target assignment.
                int targetIdx   = activeTankIndexInWave % targetModulus;
                long targetPacked = targetIdx < targetCount ? targets[targetIdx] : 0L;
                long targetNetId  = 0L;
                if (targetPacked != 0L)
                {
                    var targetEntity = new Entity((ulong)targetPacked);
                    if (CombatLife.IsAlive(ctx.World, targetEntity)   // CE-466: never aim at a knocked-out target
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

                if (BehaviorLog.IsDebugEnabled)
                {
                    BehaviorLog.Debug(ref ctx, "Dispatched subordinate Entity:" + sub.Index
                        + " to FiringSlot=" + firingSlot + " BaselineSlot=" + baselineSlot
                        + " TargetNetworkId=" + targetNetId + ".");
                }

                var dto = new HullDownAttackParams
                {
                    SlotX = fx,
                    SlotY = fy,
                    BaselineX = bx,
                    BaselineY = by,
                    AttackDirX = p.AttackDirX,
                    AttackDirY = p.AttackDirY,
                    TargetNetworkId = targetNetId,
                    ApproachSpeed = 15f,
                    CreepSpeed = 5f,
                    MaxRounds = 1,
                    RoundsFired = 0,
                    LastObservedAmmo = -1
                };

                string json = JsonSerializer.Serialize(
                    dto,
                    Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

                ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity     = sub,
                    IntentId   = "HullDownAttack",
                    JsonParams = json,
                });
            }

            s.CachedEqsRequestId      = -1;   // the answer is consumed; the sensor stays for the next wave (§4 D6)
            s.EqsRequestTime          = 0f;
            s.CurrentWave             = (byte)(1 - s.CurrentWave);
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(ref ctx, "Wave dispatched. Wave=" + dispatchWave + " attackers=" + s.ActiveAttackerCount + " targets=" + targetCount + " nextWave=" + s.CurrentWave + ".");
            return NodeStatus.Success;
        }

        // ── Phase 4.5: Wave completion node ───────────────────────────────────────

        /// <summary>
        /// Monitors active attackers.
        /// Returns <see cref="NodeStatus.Success"/> when all attackers have returned
        /// to baseline (or were killed).
        /// Returns <see cref="NodeStatus.Running"/> while any attacker is still active.
        /// </summary>
        public static NodeStatus Condition_IsWaveCompleted(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, ref BehaviorTreeState state, ref BTreeContext ctx)
        {
            if (s.ActiveAttackerCount == 0) return NodeStatus.Success;

            for (int i = s.ActiveAttackerCount - 1; i >= 0; i--)
            {
                long packed    = s.ActiveEntityPacked[i];
                var attacker   = new Entity((ulong)packed);

                if (!CombatLife.IsAlive(ctx.World, attacker))   // CE-466: knocked out (Health <= 0) or gone
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
                            // Release the baseline slot so subsequent waves can reuse it.
                            s.BaselineReservedMask &= (ushort)~(1 << s.ReturnBaselineSlotIndex[i]);
                            SwapRemove(ref s, i);
                        }
                    }
                }
            }

            if (s.ActiveAttackerCount == 0)
            {
                if (BehaviorLog.IsDebugEnabled)
                    BehaviorLog.Debug(ref ctx, "Wave completed.");
                return NodeStatus.Success;
            }

            if (BehaviorLog.IsTraceEnabled)
                BehaviorLog.Trace(ref ctx, "Waiting wave completion. ActiveAttackers=" + s.ActiveAttackerCount + ".");
            return NodeStatus.Running;
        }

        // ── Deactivators ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deactivator for <see cref="Action_RequestAreaQuery"/>. Destroys the in-flight EQS area sensor and resets
        /// <see cref="HillAttackMutableState.CachedEqsRequestId"/> to <c>-1</c> when the BTree execution pointer leaves the
        /// node via a mission-level abort, so the Muscle stops evaluating it. A null or dead sensor is a no-op.
        ///
        /// <para>S3-G: five-parameter stateful deactivator. The working state <paramref name="s"/> is
        /// projected from the behaviour-scoped partition slot by the emitted wrapper (registered under the
        /// node's full <c>{fqn}@{offset}@{slotKey}</c> key) — no <c>Blackboard1024</c> / <c>Unsafe.As</c>.</para>
        /// </summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery@0")]
        public static void Deactivate_RequestAreaQuery(
            ref PlatoonHillAttackParams p,
            ref HillAttackMutableState s,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            EqsChildSensor.Destroy(ctx.World, InFlightSensor(ref s, ref ctx));
            s.CachedEqsRequestId = -1;
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
            // ⭐ CE-430 (Q76 §12.23): the six mutable-state nodes project BOTH fields of the one block —
            //   bb.Params and bb.State — so all six share one State by construction, with no slot key,
            //   scope or manifest. Condition_AreAllAtBaseline touches no working state (3-param form).
            return new BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                        bb => bb.Params, bb => bb.State, Action_CalculateSegments, new Guid("1a000000-0000-0000-0000-0000000000a1"))
                    .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                        bb => bb.Params, bb => bb.State, Action_DispatchAllToBaseline, new Guid("1a000000-0000-0000-0000-0000000000a2"))
                    .Action(bb => bb.Params, Condition_AreAllAtBaseline)
                    // ⭐ CE-459: area clear ends the wave loop with Failure; ForceSuccess lets the sequence go on to
                    //   return the platoon to the baseline (mirrors PlatoonHillAttack.btree.json).
                    .ForceSuccess(fs => fs
                        .Repeater(-1, rep => rep
                            .Sequence(wseq => wseq
                                .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                                    bb => bb.Params, bb => bb.State, Action_RequestAreaQuery, new Guid("1a000000-0000-0000-0000-0000000000b1"))
                                .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                                    bb => bb.Params, bb => bb.State, Condition_IsAreaQueryResolved, new Guid("1a000000-0000-0000-0000-0000000000b2"))
                                .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                                    bb => bb.Params, bb => bb.State, Action_DispatchWaveWithTargets, new Guid("1a000000-0000-0000-0000-0000000000b3"))
                                .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                                    bb => bb.Params, bb => bb.State, Condition_IsWaveCompleted, new Guid("1a000000-0000-0000-0000-0000000000b4")))))
                    .StatefulAction<PlatoonHillAttackBlackboard, PlatoonHillAttackParams, HillAttackMutableState>(
                        bb => bb.Params, bb => bb.State, Action_DispatchAllToBaseline, new Guid("1a000000-0000-0000-0000-0000000000a4"))
                    .Action(bb => bb.Params, Condition_AreAllAtBaseline));
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
        /// The attack direction is computed as the <b>perpendicular of the normalised
        /// firing-line vector, signed to point away from the baseline</b> — it is not
        /// authored directly. See the computation for why the sign needs the baseline.
        /// </summary>
        /// <summary>
        /// Resolver (ParseParamsDelegate shape): fetches the geographic transform and
        /// NetworkEntityMap from world singletons and delegates to
        /// <see cref="ParsePlatoonHillAttackParams"/>. This is what the behavior registers as its
        /// resolver — no registration-time closure over geo/entity-map is needed.
        /// </summary>
        [Fdp.Toolkit.Behavior.BehaviorResolver("PlatoonHillAttack",
            ParamsType = typeof(Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams))]
        public static unsafe void ResolvePlatoonHillAttackParams(
            string json, byte* ptr, int capacity, Fdp.Core.EntityRepository world, Entity self)
        {
            var geo = world.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                ? world.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                : null;
            var map = (world.HasSingletonManaged<NetworkEntityMap>()
                ? world.GetSingletonManaged<NetworkEntityMap>()
                : null) ?? new NetworkEntityMap();
            ParsePlatoonHillAttackParams(json, ptr, capacity, geo, map);
        }

        public static unsafe void ParsePlatoonHillAttackParams(
            string json,
            byte* ptr, int capacity,
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
                dto = JsonSerializer.Deserialize<PlatoonHillAttackParamsJsonDto>(
                    json,
                    Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            }
            catch (Exception ex)
            {
                BehaviorLog.ParseError("Failed to deserialize PlatoonHillAttack JSON: " + ex.Message);
                Unsafe.Write(ptr, default(PlatoonHillAttackParams));
                return;
            }

            if (dto == null)
            {
                BehaviorLog.ParseError("PlatoonHillAttack JSON deserialized to null.");
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

            // Attack direction: the PERPENDICULAR of the firing line, signed to point AWAY
            // from the baseline.
            //
            // ⚠ This used to be normalize(firingCenter - baselineCenter) — the baseline-to-
            // firing-line approach vector — which is a different quantity whenever the baseline
            // is not parallel to the firing line and centred opposite it. The two agree only in
            // that special case, which is why SC-HA016-2 could not tell them apart. The tanks
            // creep along this vector and the overshoot guard projects onto it, so it must be
            // the normal of the firing line: the line is the position to hold, and "forward"
            // means straight out from it, not "whatever bearing we happened to approach on".
            //
            // The baseline is still needed, but only to CHOOSE THE SIGN: a perpendicular has
            // two directions and only the one leading away from where the platoon staged is
            // the attack direction.
            var baselineCenter = new Vector2(
                (result.BaselineStartX + result.BaselineEndX) * 0.5f,
                (result.BaselineStartY + result.BaselineEndY) * 0.5f);
            var firingCenter = new Vector2(
                (result.StartX + result.EndX) * 0.5f,
                (result.StartY + result.EndY) * 0.5f);
            var awayFromBaseline = firingCenter - baselineCenter;

            var firingVec = new Vector2(result.EndX - result.StartX, result.EndY - result.StartY);
            float firingLen = firingVec.Length();

            if (firingLen > 0.0001f)
            {
                var tangent = firingVec / firingLen;
                var perpendicular = new Vector2(tangent.Y, -tangent.X);

                // Flip to the half-plane the baseline is NOT in. A dot of exactly zero means the
                // baseline centre lies ON the firing line, so neither side is "away" — keep the
                // right-hand normal rather than pretending the data decided.
                if (Vector2.Dot(perpendicular, awayFromBaseline) < 0f)
                    perpendicular = -perpendicular;

                result.AttackDirX = perpendicular.X;
                result.AttackDirY = perpendicular.Y;
            }
            else
            {
                // Degenerate firing line (start == end): it has no tangent and therefore no
                // normal. Fall back to the approach vector, which at least points at the enemy.
                float awayLen = awayFromBaseline.Length();
                if (awayLen > 0.0001f)
                {
                    var norm = awayFromBaseline / awayLen;
                    result.AttackDirX = norm.X;
                    result.AttackDirY = norm.Y;
                }
                else
                {
                    result.AttackDirX = 1f;
                    result.AttackDirY = 0f;
                }
            }

            // Resolve target area entity.
            if (dto.TargetAreaNetworkId != 0 && entityMap.TryGetEntity(dto.TargetAreaNetworkId, out var areaEntity))
            {
                result.TargetAreaEntity = areaEntity;
            }
            else
            {
                if (dto.TargetAreaNetworkId != 0)
                    BehaviorLog.ParseWarn("TargetAreaNetworkId=" + dto.TargetAreaNetworkId + " not found in entity map; area entity set to null.");
                result.TargetAreaEntity = Entity.Null;
            }

            Unsafe.Write(ptr, result);
        }

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
