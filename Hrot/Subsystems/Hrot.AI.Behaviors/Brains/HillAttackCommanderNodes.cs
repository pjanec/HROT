using Fdp.Toolkit.Determinism;   // ⭐ CE-2188 — SimRng moved to Fdp.Toolkits
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
using Fbt.Kernel;

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

        /// <summary>The commander's sensor SITE (<see cref="EqsChildSensor.Ensure"/>) — which of the commander's sensors this is.
        /// ⭐ CE-485: no longer the DDS key — the part id is allocated and reused, and the sensor dies with the behaviour run
        /// (📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D4/D5).</summary>
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
        private static Entity InFlightSensor(ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            if (s.CachedEqsRequestId == -1) return Entity.Null;
            if (s.CachedEqsRequestId == SensorBeingCreated)
            {
                var found = EqsChildSensor.Find(world, self, AreaSensorInstanceId);
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
        // ⭐ CE-504 C-2 — the shared stateful form (ref P, ref WS, Entity, EntityRepository), bound by the JSON emitter's
        //   stateful thunk and the code builder's StatefulAction.
        [SharedAiAction]
        public static NodeStatus Action_CalculateSegments(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
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
            s.LastFireCount       = 0;
            s.PrevFireCount       = 0;
            s.CachedEqsRequestId  = -1;
            s.EqsRequestTime      = 0f;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "Calculated slots=" + totalSlots + " spacing=" + spacing.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + "m.");
            return NodeStatus.Success;
        }

        /// <summary>
        /// Orders all alive subordinates to move to their assigned baseline slot
        /// by publishing <see cref="AssignTacticalIntentEvent"/> with
        /// <c>IntentId = "MoveToLocation"</c>.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_DispatchAllToBaseline(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self))
                return NodeStatus.Success;

            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);

            s.BaselineReservedMask = 0;
            int count = roster.Count;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "Dispatching baseline move intents. Subordinates=" + count + ".");

            for (int i = 0; i < count; i++)
            {
                var sub = roster.SubordinateEntities[i];
                long packed = (long)sub.PackedValue;
                if (packed == 0) continue;
                if (!CombatLife.IsAlive(world, sub)) continue;   // CE-466: knocked-out tanks take no orders

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

                world.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity     = sub,
                    IntentId   = "MoveToLocation",
                    JsonParams = json,
                    Origin     = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,   // ⭐ CE-3034 — a commander (R-193)
                });

                // ⭐ CE-2118 — staging reserves NOTHING. It spaces the tanks by ROSTER count, while BaselineReservedMask is in
                //   FIRING-LINE slot space (TotalSlots, DESIGN §2.3); setting roster bits there reserved every slot of a short
                //   line, so each wave attacker fell to "closest regardless" and two were sent back to the SAME point
                //   (measured live, hill-attack-close). Only an attacker heading back reserves a slot (DispatchWaveWithTargets).
            }
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "Baseline dispatch complete. ReservedMask=" + s.BaselineReservedMask + ".");
            return NodeStatus.Success;
        }

        /// <summary>
        /// Waits until every alive subordinate has arrived at its baseline slot.
        /// Returns <see cref="NodeStatus.Success"/> when all alive subordinates have
        /// <c>NavigationStatus.Result == NavigationResult.Arrived</c>.
        /// Dead subordinates count as arrived.
        /// Returns <see cref="NodeStatus.Running"/> if any alive subordinate has not yet arrived.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Condition_AreAllAtBaseline(ref PlatoonHillAttackParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self))
                return NodeStatus.Success;

            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            int count = roster.Count;
            int arrivedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var sub = roster.SubordinateEntities[i];
                long packed = (long)sub.PackedValue;
                if (packed == 0) continue;
                if (!CombatLife.IsAlive(world, sub)) continue;  // dead (knocked out or gone) = counts as arrived

                if (!world.HasComponent<NavigationStatus>(sub))
                {
                    if (BehaviorLog.IsTraceEnabled)
                        BehaviorLog.Trace(self, world, "Baseline wait: subordinate=" + sub.Index + " missing NavigationStatus.");
                    return NodeStatus.Running;
                }

                ref readonly var nav = ref world.GetComponentRO<NavigationStatus>(sub);

                // Treat Arrived, FailedBlocked, and FailedUnreachable as completion.
                // Only block the sequence if the tank is actively still trying to move.
                if (nav.Result == NavigationResult.InProgress)
                {
                    if (BehaviorLog.IsTraceEnabled)
                        BehaviorLog.Trace(self, world, "Baseline wait: arrived=" + arrivedCount + "/" + count + " blockingSub=" + sub.Index + ".");
                    return NodeStatus.Running;
                }
                arrivedCount++;

            }
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "All subordinates at baseline. Arrived=" + arrivedCount + "/" + count + ".");
            return NodeStatus.Success;
        }

        // ── Phase 4.2: EQS integration nodes ─────────────────────────────────────

        /// <summary>
        /// Asks the area query: ensures the commander's EQS child sensor exists and REFRESHES it (a new epoch), so the next
        /// answer is computed after this moment — the old per-wave request, on one persistent sensor.
        /// Returns <see cref="NodeStatus.Running"/> while the sensor is being created or a refreshed answer is still in flight,
        /// <see cref="NodeStatus.Success"/> once the question is asked, <see cref="NodeStatus.Failure"/> without a live area.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_RequestAreaQuery(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            // Guard: a question already in flight is not asked twice.
            var inFlight = InFlightSensor(ref s, self, world);
            if (inFlight.IsNull && s.CachedEqsRequestId == SensorBeingCreated)
                return NodeStatus.Running;       // still being created
            if (!inFlight.IsNull)
            {
                if (world.IsAlive(inFlight) && world.HasComponent<EqsCognitiveBuffer>(inFlight))
                {
                    if (!world.GetComponentRO<EqsCognitiveBuffer>(inFlight).IsReady)
                    {
                        if (BehaviorLog.IsTraceEnabled)
                            BehaviorLog.Trace(self, world, "EQS area query in flight. Sensor=" + inFlight.Index + ".");
                        return NodeStatus.Running;
                    }
                    return NodeStatus.Success;   // answered; the next node consumes it
                }
                s.CachedEqsRequestId = -1;       // the sensor vanished: ask again below
            }

            // Guard: TargetAreaEntity must be alive before submitting a query.
            if (p.TargetAreaEntity.IsNull || !world.IsAlive(p.TargetAreaEntity))
            {
                BehaviorLog.Error(self, world, "TargetAreaEntity is null or dead. Cannot execute area query.");
                // ⭐ CE-482 — FAIL LOUD: no area means the attack cannot be planned; the run ends Faulted, the plan halts.
                BehaviorFault.Raise(world, self, BehaviorFaultCode.MissingInput,
                    "Hill attack: the target area entity is missing or dead.");
                return NodeStatus.Failure;
            }

            var config = AreaSensor(p.TargetAreaEntity);
            var sensor = EqsChildSensor.Find(world, self, AreaSensorInstanceId);
            if (sensor.IsNull)
            {
                // ⭐ The creation IS the question. CE-485: on the live world the sensor exists at once; only a deferred view
                //   (Null) leaves it to be found after playback.
                sensor = EqsChildSensor.Ensure(world, self, AreaSensorInstanceId, config);
                s.CachedEqsRequestId = sensor.IsNull ? SensorBeingCreated : (long)sensor.PackedValue;
            }
            else
            {
                EqsChildSensor.Refresh(world, sensor, config);   // ask again: a new epoch, the old answer cleared
                s.CachedEqsRequestId = (long)sensor.PackedValue;
            }
            s.EqsRequestTime = world.SimulationTime;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "Asked the EQS area query. Sensor=" + (sensor.IsNull ? "creating" : sensor.Index.ToString()) + ".");
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
        [SharedAiAction]
        public static NodeStatus Condition_IsAreaQueryResolved(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            if (s.CachedEqsRequestId == -1)
                return NodeStatus.Failure;  // guard; should not occur in correct topology
            var sensor = InFlightSensor(ref s, self, world);

            bool ready = world.IsAlive(sensor)
                && world.HasComponent<EqsCognitiveBuffer>(sensor)
                && world.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady;
            if (!ready)
            {
                if (world.SimulationTime - s.EqsRequestTime > 5.0f)
                {
                    BehaviorLog.Error(self, world, "EQS area query timed out after 5.0s.");
                    // ⭐ CE-482 — FAIL LOUD: with no area on the Muscle the sensor answers NOTHING (EQS §17.5), so silence
                    //   means the question cannot be answered — never a quiet "area clear".
                    BehaviorFault.Raise(world, self, BehaviorFaultCode.NoAnswerTimeout,
                        "Hill attack: the EQS area sensor did not answer within 5 s.");
                    EqsChildSensor.Destroy(world, sensor);
                    s.CachedEqsRequestId = -1;
                    return NodeStatus.Failure;
                }
                if (BehaviorLog.IsTraceEnabled)
                    BehaviorLog.Trace(self, world, "Waiting EQS result.");
                return NodeStatus.Running;
            }

            int count = world.GetComponentRO<EqsCognitiveBuffer>(sensor).Count;
            if (count == 0)
            {
                // Area cleared: break out of the Repeater so the BTree can finish; the sensor is no longer needed.
                EqsChildSensor.Destroy(world, sensor);
                s.CachedEqsRequestId = -1;
                s.EqsRequestTime     = 0f;
                if (BehaviorLog.IsDebugEnabled)
                    BehaviorLog.Debug(self, world, "EQS resolved clear area. targets=0.");
                return NodeStatus.Failure;
            }

            s.EqsRequestTime = 0f;
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "EQS resolved targets. targets=" + count + ".");
            return NodeStatus.Success;
        }

        // ── Phase 4.4: Wave dispatch node ─────────────────────────────────────────

        /// <summary>
        /// Assigns firing slots, baseline slots, and targets to tanks in the current wave,
        /// then publishes <see cref="AssignTacticalIntentEvent"/> for each selected tank.
        /// Returns <see cref="NodeStatus.Success"/> unconditionally.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_DispatchWaveWithTargets(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            s.WaveUsedSlotsMask   = 0;
            s.ActiveAttackerCount = 0;
            byte dispatchWave = s.CurrentWave;

            // The answer: Brain-local target entities from the area sensor's buffer (copied — the loop below publishes).
            long* targets = stackalloc long[EqsResultPool.MaxTopK];
            int targetCount = 0;
            var sensor = InFlightSensor(ref s, self, world);
            if (!sensor.IsNull && world.IsAlive(sensor) && world.HasComponent<EqsCognitiveBuffer>(sensor))
            {
                ref readonly var answer = ref world.GetComponentRO<EqsCognitiveBuffer>(sensor);
                if (answer.IsReady)
                {
                    var results = answer.GetSpanRO();
                    targetCount = Math.Min(answer.Count, EqsResultPool.MaxTopK);
                    for (int k = 0; k < targetCount; k++) targets[k] = results[k].EntityId;
                }
            }
            int targetModulus = targetCount == 0 ? 1 : targetCount;  // avoid divide-by-zero

            if (!world.HasComponent<UnitRoster>(self))
            {
                s.CachedEqsRequestId      = -1;
                s.EqsRequestTime          = 0f;
                s.CurrentWave             = (byte)(1 - s.CurrentWave);
                return NodeStatus.Success;
            }

            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            int rosterCount  = roster.Count;
            bool allParticipate = rosterCount <= 3;
            int* avail = stackalloc int[16];

            int activeTankIndexInWave = 0;
            // ⭐ CE-2230 H-5 — the wave's orders are held until every member is known, so they share ONE go time (below)
            var orders = new HullDownAttackParams[8];
            var whom = stackalloc Entity[8];
            int orderCount = 0;
            double slowest = 0d;
            // ⭐ CE-2230 H-6 — where the last TWO waves came up (metres along the line), and this wave's picks so far
            float lineLength = Vector2.Distance(new Vector2(p.StartX, p.StartY), new Vector2(p.EndX, p.EndY));
            var avoid = stackalloc float[24];
            var weight = stackalloc float[24];
            int avoidCount = 0;
            for (int k = 0; k < s.LastFireCount && k < 8; k++) { weight[avoidCount] = 1f; avoid[avoidCount++] = s.LastFireAlong[k]; }
            for (int k = 0; k < s.PrevFireCount && k < 8; k++) { weight[avoidCount] = PrevWaveWeight; avoid[avoidCount++] = s.PrevFireAlong[k]; }
            var picked = stackalloc float[8];
            int pickedCount = 0;

            for (int i = 0; i < rosterCount && s.ActiveAttackerCount < 8; i++)
            {
                var sub = roster.SubordinateEntities[i];
                long packed = (long)sub.PackedValue;
                if (packed == 0) continue;
                if (!CombatLife.IsAlive(world, sub)) continue;   // CE-466: knocked-out tanks take no orders

                // Wave parity: use Entity.Index (immutable) NOT roster index i.
                if (!allParticipate && (sub.Index % 2) != s.CurrentWave) continue;

                int availCount = 0;
                // ⭐ CE-2230 H-6 — a pair needs distinct POINTS, not distinct slots: the mate's point is avoided by PickFiringPoint.
                //   📐 Live: with the mate's slot blocked, after a wave at both ends of a 3-slot line the second tank was left only slots
                //   beside the last pair's points (5.3 m). Only a slot where a tank died stays out.
                ushort blockedMask = s.BurnedSlotsMask;
                for (int j = 0; j < s.TotalSlots; j++)
                {
                    if ((blockedMask & (1 << j)) == 0) avail[availCount++] = j;
                }

                if (availCount == 0)
                {
                    BehaviorLog.Warn(self, world, "No firing-line slots available for subordinate Entity:" + sub.Index + "; skipping this wave assignment.");
                    continue;  // no slots left; skip tank
                }
                // ⭐⭐ CE-202 — REPRODUCIBLE, not fixed. This drew from Random.Shared, so two runs of the
                //    same scenario picked different slots and could not be compared at all; it is also
                //    why CE-174's mechanism made kills look intermittent. Same inputs now give the same
                //    slot, while the xorshift keeps the scatter an observer sees.
                //    ⛔ SlotOps.PickRandomFreeSlot — the curated twin of this very line — has carried
                //    the deterministic form since architect Q#8-C mandated it. This is the oracle
                //    adopting it, not a new invention.
                var slotRng = SimRng.FromSim((int)sub.Index, s.CurrentWave, world.SimulationTime);
                // ⭐⭐ CE-2230 H-6 — never come up where the last pair did: the point is a free slot plus a random shift along the line,
                //   the best of a few draws by its distance from every point of the previous wave (and this wave's own). 🔒 User,
                //   2026-10-16: "the new tank pair appears at a different location than the previous one, so the enemy cannot aim and
                //   wait for the tank to reappear at the same place."
                int firingSlot = PickFiringPoint(ref slotRng, avail, availCount, s.TotalSlots, lineLength, avoid, weight, avoidCount, out float ft);
                weight[avoidCount] = 1f;
                avoid[avoidCount++] = ft * lineLength;
                picked[pickedCount++] = ft * lineLength;

                // Interpolate firing-point world position.
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
                    if (Injury.IsInTheFight(world, targetEntity)   // CE-466: never aim at a knocked-out target
                        && world.HasComponent<NetworkIdentity>(targetEntity))
                    {
                        targetNetId = world.GetComponentRO<NetworkIdentity>(targetEntity).Value;
                    }
                }

                // Baseline slot world position.
                float bt = s.TotalSlots > 1 ? (float)baselineSlot / (s.TotalSlots - 1) : 0.5f;
                float bx = p.BaselineStartX + (p.BaselineEndX - p.BaselineStartX) * bt;
                float by = p.BaselineStartY + (p.BaselineEndY - p.BaselineStartY) * bt;
                // ⭐⭐ CE-2230 H-5 — the HIDE is where the tank already stands on the baseline (its own cover), not the slot nearest its
                //   random firing slot. 📐 Live: driving up to 80 m along the baseline to that slot, a tank arrived facing the wrong way
                //   and spent 10–15 s turning, so its pair mate went alone. From its own spot it goes out diagonally to its lane and
                //   reverses straight back. The slot bookkeeping (masks) is unchanged; a tank off the baseline still gets the slot.
                if (OnTheBaseline(world, sub, in p, out var here)) { bx = here.X; by = here.Y; }

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
                    BehaviorLog.Debug(self, world, "Dispatched subordinate Entity:" + sub.Index
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
                    CreepSpeed = 5f,   // ⭐ CE-2230 — one round per run is the tank's own doctrine now (R-2022)
                };

                orders[orderCount] = dto;
                whom[orderCount++] = sub;
                slowest = Math.Max(slowest, HideDriveSeconds(world, sub, bx, by));
            }

            // ⭐⭐ CE-2230 H-5 — ONE go time for the wave: the slowest member's drive to its hide (each first drives along the baseline to
            //   the slot nearest its random firing slot, CE-202's scatter) plus the settle. 📐 Live without it: the pair crested 12–20 s
            //   apart, never together (🔒 R-2022: "another pair comes up the hill and does the same procedure").
            double goAt = orderCount > 1 ? Clock(world) + slowest + WaveSettleSeconds : 0d;
            if (pickedCount > 0)   // ⭐ H-6 — this wave becomes the one the next must avoid; the last becomes the one before
            {
                s.PrevFireCount = s.LastFireCount;
                for (int k = 0; k < 8; k++) s.PrevFireAlong[k] = s.LastFireAlong[k];
                s.LastFireCount = (byte)pickedCount;
                for (int k = 0; k < pickedCount; k++) s.LastFireAlong[k] = picked[k];
            }
            for (int k = 0; k < orderCount; k++)
            {
                orders[k].GoAt = goAt;
                string json = JsonSerializer.Serialize(
                    orders[k],
                    Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

                world.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity     = whom[k],
                    IntentId   = "HullDownAttack",
                    JsonParams = json,
                    Origin     = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,   // ⭐ CE-3034 — a commander (R-193)
                });
            }

            s.CachedEqsRequestId      = -1;   // the answer is consumed; the sensor stays for the next wave (§4 D6)
            s.EqsRequestTime          = 0f;
            s.CurrentWave             = (byte)(1 - s.CurrentWave);
            if (BehaviorLog.IsDebugEnabled)
                BehaviorLog.Debug(self, world, "Wave dispatched. Wave=" + dispatchWave + " attackers=" + s.ActiveAttackerCount + " targets=" + targetCount + " nextWave=" + s.CurrentWave + ".");
            return NodeStatus.Success;
        }

        /// <summary>⭐ CE-2230 H-5 — the settle after the slowest arrival before the wave goes (s): the hull stops, the gun lays.</summary>
        public const double WaveSettleSeconds = 2.0;

        /// <summary>⭐ CE-2230 H-5 — a member's estimated drive to its hide (s): the ground distance at the run's hide speed
        /// (<see cref="HillAttackTankNodes.ReverseSpeed"/>), with a third for the start, the turns and the stop.</summary>
        internal static double HideDriveSeconds(EntityRepository world, Entity tank, float hideX, float hideY)
        {
            if (!world.HasComponent<SimTransform>(tank)) return 0d;
            var at = world.GetComponentRO<SimTransform>(tank).Position;
            float d = MathF.Sqrt((at.X - hideX) * (at.X - hideX) + (at.Y - hideY) * (at.Y - hideY));
            return d / HillAttackTankNodes.ReverseSpeed * 1.33;
        }

        /// <summary>⭐ CE-2230 H-6 — candidate points per free slot, spread across its shift range.</summary>
        public const int FiringPointCandidatesPerSlot = 9;

        /// <summary>⭐ CE-2230 H-6 — a candidate's shift along the line, as a fraction of the slot pitch (±): a reused slot is still a new spot.</summary>
        public const float SlotShiftFraction = 0.4f;

        /// <summary>⭐ CE-2230 H-6 — the pick is random among the candidates at least this fraction as far from the points to avoid as the
        /// farthest one — unpredictable, never close.</summary>
        public const float NearBestFraction = 0.75f;

        /// <summary>⭐ CE-2230 H-6 — how much the wave BEFORE the last counts (a distance to its points is read as this × closer): the last
        /// pair and the mate are the hard rule (R-2023), the one before shapes the pick. 📐 Live, on a 70 m line: at full weight, four
        /// earlier points plus the mate left ~8 m for everyone.</summary>
        public const float PrevWaveWeight = 1.5f;

        /// <summary>⭐ CE-2230 H-6 — a pick is never closer than this (m) to a point to avoid, when the line has room for it at all.</summary>
        public const float MinApartMetres = 12f;

        /// <summary>
        /// ⭐ CE-2230 H-6 — a firing point: across every free slot, candidates spread over ± <see cref="SlotShiftFraction"/> of the slot pitch
        /// (each with a small random jitter) are scored by their distance (m, along the line) from every point in <paramref name="avoid"/>
        /// in two tiers: ① only candidates at least <see cref="MinApartMetres"/> (or the best the line allows) from the FULL-weight points —
        /// the previous wave's and this wave's own — survive (the rule, R-2023); ② among them, distance from every point with the wave
        /// before at <c>weight</c> (<see cref="PrevWaveWeight"/>) decides, one drawn at random among the near-best (<see cref="NearBestFraction"/>).
        /// 📐 A best-of-12 RANDOM draw was tried first and its rail caught it landing 7.2 m from the last pair's point; one weighted score
        /// for both waves then let the last pair slip to 8.8 m — hence the two tiers. Returns the slot (the
        /// bookkeeping masks key on it) and the point as a fraction of the line in <paramref name="t"/>. Deterministic in
        /// <paramref name="rng"/> (CE-202).
        /// </summary>
        internal static unsafe int PickFiringPoint(ref SimRng rng, int* avail, int availCount, int totalSlots, float lineLength,
                                                   float* avoid, float* weight, int avoidCount, out float t)
        {
            float pitch = totalSlots > 1 ? 1f / (totalSlots - 1) : 0f;
            int n = Math.Min(availCount, 16) * FiringPointCandidatesPerSlot;
            var candT = stackalloc float[n];
            var candSlot = stackalloc int[n];
            var candHard = stackalloc float[n];   // to the full-weight points: the last pair and the mate — the rule (R-2023)
            var candSoft = stackalloc float[n];   // to every point, the wave before at its weight — the preference
            float bestHard = float.MinValue;
            int c = 0;
            for (int a = 0; a < availCount && a < 16; a++)
                for (int k = 0; k < FiringPointCandidatesPerSlot; k++)
                {
                    float u = FiringPointCandidatesPerSlot > 1 ? (float)k / (FiringPointCandidatesPerSlot - 1) * 2f - 1f : 0f;
                    float jitter = (rng.NextSingle() * 2f - 1f) * 0.05f * pitch;
                    float ct = Math.Clamp(SlotT(avail[a], totalSlots) + u * SlotShiftFraction * pitch + jitter, 0f, 1f);
                    float hard = float.MaxValue, soft = float.MaxValue;
                    for (int m = 0; m < avoidCount; m++)
                    {
                        float d = Math.Abs(ct * lineLength - avoid[m]);
                        soft = Math.Min(soft, d * weight[m]);
                        if (weight[m] <= 1f) hard = Math.Min(hard, d);
                    }
                    candT[c] = ct; candSlot[c] = avail[a]; candHard[c] = hard; candSoft[c] = soft; c++;
                    if (hard > bestHard) bestHard = hard;
                }
            // ① the rule: keep only candidates clear of the last pair and the mate (MinApartMetres, or the best the line allows)
            float hardFloor = bestHard == float.MaxValue ? float.MaxValue : Math.Min(bestHard, MinApartMetres);
            float bestSoft = float.MinValue;
            for (int k = 0; k < c; k++) if (candHard[k] >= hardFloor && candSoft[k] > bestSoft) bestSoft = candSoft[k];
            // ② the preference, among those: random within the near-best by distance from everything (the wave before weighted)
            float softFloor = bestSoft == float.MaxValue ? float.MaxValue : Math.Max(bestSoft * NearBestFraction, Math.Min(bestSoft, MinApartMetres));
            int good = 0;
            for (int k = 0; k < c; k++) if (candHard[k] >= hardFloor && candSoft[k] >= softFloor) good++;
            int pick = rng.NextInt(0, Math.Max(1, good));
            for (int k = 0; k < c; k++)
                if (candHard[k] >= hardFloor && candSoft[k] >= softFloor && pick-- == 0) { t = candT[k]; return candSlot[k]; }
            t = candT[0];
            return candSlot[0];
        }

        private static float SlotT(int slot, int totalSlots) => totalSlots > 1 ? (float)slot / (totalSlots - 1) : 0.5f;

        /// <summary>⭐ CE-2230 H-5 — how far off the baseline a tank still counts as standing on it (m).</summary>
        public const float OnBaselineMetres = 25f;

        /// <summary>⭐ CE-2230 H-5 — the tank's ground position, when it stands within <see cref="OnBaselineMetres"/> of the baseline.</summary>
        internal static bool OnTheBaseline(EntityRepository world, Entity tank, in PlatoonHillAttackParams p, out Vector2 here)
        {
            here = default;
            if (!world.HasComponent<SimTransform>(tank)) return false;
            var at = world.GetComponentRO<SimTransform>(tank).Position;
            here = new Vector2(at.X, at.Y);
            var a = new Vector2(p.BaselineStartX, p.BaselineStartY);
            var ab = new Vector2(p.BaselineEndX, p.BaselineEndY) - a;
            float t = ab.LengthSquared() > 1e-6f ? Math.Clamp(Vector2.Dot(here - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            return Vector2.Distance(here, a + ab * t) <= OnBaselineMetres;
        }

        /// <summary>The cycle's clock (<c>GlobalTime.TotalTime</c>) — the go time is compared against it there.</summary>
        private static double Clock(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;

        // ── Phase 4.5: Wave completion node ───────────────────────────────────────

        /// <summary>
        /// Monitors active attackers.
        /// Returns <see cref="NodeStatus.Success"/> when all attackers have returned
        /// to baseline (or were killed).
        /// Returns <see cref="NodeStatus.Running"/> while any attacker is still active.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Condition_IsWaveCompleted(ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            if (s.ActiveAttackerCount == 0) return NodeStatus.Success;

            for (int i = s.ActiveAttackerCount - 1; i >= 0; i--)
            {
                long packed    = s.ActiveEntityPacked[i];
                var attacker   = new Entity((ulong)packed);

                if (!CombatLife.IsAlive(world, attacker))   // CE-466: knocked out (Health <= 0) or gone
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
                    if (world.HasComponent<BehaviorState>(attacker))
                    {
                        var beh = world.GetComponent<BehaviorState>(attacker);
                        if (beh.ActiveBehaviorHash == HullDownAttackRunBehaviorId)
                            s.HasStartedRun[i] = 1;
                    }
                    // Do not remove; run has not started yet.
                }
                else
                {
                    // HasStartedRun == 1: check whether the run has finished.
                    if (world.HasComponent<BehaviorState>(attacker))
                    {
                        var beh = world.GetComponent<BehaviorState>(attacker);
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
                    BehaviorLog.Debug(self, world, "Wave completed.");
                return NodeStatus.Success;
            }

            if (BehaviorLog.IsTraceEnabled)
                BehaviorLog.Trace(self, world, "Waiting wave completion. ActiveAttackers=" + s.ActiveAttackerCount + ".");
            return NodeStatus.Running;
        }

        // ── Deactivators ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deactivator for <see cref="Action_RequestAreaQuery"/>. Destroys the in-flight EQS area sensor and resets
        /// <see cref="HillAttackMutableState.CachedEqsRequestId"/> to <c>-1</c> when the BTree execution pointer leaves the
        /// node via a mission-level abort, so the Muscle stops evaluating it. A null or dead sensor is a no-op.
        ///
        /// <para>⭐ CE-504 slice 4 — the shared STATEFUL deactivator form <c>(ref P, ref WS, Entity, EntityRepository)</c>. The
        /// working state <paramref name="s"/> is projected exactly as its action's is (the emitted wrapper is registered under the
        /// node's full <c>{fqn}@{offset}@{slotKey}</c> key; a curated tree pairs it through <c>SharedNodeBinder</c>).</para>
        /// </summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery")]
        public static void Deactivate_RequestAreaQuery(
            ref PlatoonHillAttackParams p, ref HillAttackMutableState s, Entity self, EntityRepository world)
        {
            EqsChildSensor.Destroy(world, InFlightSensor(ref s, self, world));
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
        // ⭐⭐ CE-1410 (R-1042) — the REGISTERED PlatoonHillAttack: node for node the retired Demos/HillAttack/PlatoonHillAttack.btree.json
        //   (ids, titles, comments kept; equal to its registration, ShippedTreesAreCSharpTests). Before CE-1410 this was an unregistered
        //   catalog-only twin of the asset (CE-430).
        [BTreeDefinition(BehaviorNames.PlatoonHillAttack, AssetId = "1a000000-0000-0000-0000-0000000000dd", Curated = true, Group = "Demos/HillAttack",
            ParamsType = typeof(PlatoonHillAttackBlackboard))]
        public static BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext> BuildPlatoonHillAttackTree()
            => new BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext>()
            .Sequence(run => run
                .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Action_CalculateSegments, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000a1"))
                    .Label("CalculateSegments")
                    .Comment("Compute firing-line slot count; zero all masks.")
                .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Action_DispatchAllToBaseline, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000a2"))
                    .Label("DispatchAllToBaseline")
                    .Comment("Publish MoveToLocation intents so every subordinate moves to its baseline slot.")
                .Action(bb => bb.Params, HillAttackCommanderNodes.Condition_AreAllAtBaseline, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000a3"))
                    .Label("AreAllAtBaseline")
                    .Comment("Running until every alive subordinate has arrived (dead/blocked count as arrived).")
                .ForceSuccess(areaClear => areaClear
                    .Repeater(-1, waves => waves
                        .Sequence(wave => wave
                            .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Action_RequestAreaQuery, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000b1"))
                                .Label("RequestAreaQuery")
                                .Comment("Submit an EQS area query over the target polygon for hostiles.")
                            .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Condition_IsAreaQueryResolved, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000b2"))
                                .Label("IsAreaQueryResolved")
                                .Comment("Running while pending; Failure when the area is clear (breaks the repeater → behavior ends); Success with targets.")
                            .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Action_DispatchWaveWithTargets, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000b3"))
                                .Label("DispatchWaveWithTargets")
                                .Comment("Select this wave's tanks (Entity.Index parity), assign firing/baseline slots + round-robin targets, publish HullDownAttack intents.")
                            .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Condition_IsWaveCompleted, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000b4"))
                                .Label("IsWaveCompleted")
                                .Comment("Running until all attackers return/die; burns the firing slot of any tank that dies."),
                            TreeNodeIds.Id("1a000000-0000-0000-0000-000000000003"))
                            .Label("WaveLoop")
                            .Comment("One wave: EQS scan → resolve → dispatch half the platoon → wait for the wave to return. Repeater(-1) repeats until IsAreaQueryResolved returns Failure (area clear)."),
                        TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000f1"))
                        .Comment("Repeat waves forever; the loop exits when IsAreaQueryResolved returns Failure (area clear)."),
                    TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000f2"))
                    .Comment("CE-459: area clear is the loop's normal exit (Failure) — turn it into Success so the sequence goes on to return the platoon to the baseline.")
                .StatefulAction(bb => bb.Params, bb => bb.State, HillAttackCommanderNodes.Action_DispatchAllToBaseline, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000a4"))
                    .Label("ReturnToBaseline")
                    .Comment("CE-459: area clear — send every subordinate back to its baseline staging slot (the platoon ends on the baseline, abreast).")
                .Action(bb => bb.Params, HillAttackCommanderNodes.Condition_AreAllAtBaseline, TreeNodeIds.Id("1a000000-0000-0000-0000-0000000000a5"))
                    .Label("AreAllBackAtBaseline")
                    .Comment("CE-459: Running until every alive subordinate is back on the baseline; then the behaviour finishes (Success)."),
                TreeNodeIds.Id("1a000000-0000-0000-0000-000000000002"))
                .Label("Sequence")
                .Comment("Setup (calc slots → move to baseline → wait for arrival) then the repeating wave loop.");

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
        [Behavior(BehaviorNames.PlatoonHillAttack, Params = typeof(global::Hrot.Map.Definitions.Behavior.PlatoonHillAttackParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134 — the resolver is this behaviour's C#; the asset owns its topology
        // ⭐ CE-1410 — no ParamsType: the C#-built tree's block (PlatoonHillAttack.Blackboard: these params at 0, then the shared State)
        //   is the layout; a resolver type would replace it and leave the State no room (CE-437, ApplyResolverOverlay).
        [Fdp.Toolkit.Behavior.BehaviorResolver("PlatoonHillAttack")]
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

            Hrot.Map.Definitions.Behavior.PlatoonHillAttackParamsJsonDto? dto;
            try
            {
                // ⭐ DESIGN_Entity_Reference.md — the CURATED contract, not a private parse twin (CE-447: "no private
                //   parse DTOs"); its TargetAreaNetworkId is an EntityRef, so the remap and the picker see it too.
                dto = JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.PlatoonHillAttackParamsJsonDto>(
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
            if (!dto.TargetAreaNetworkId.IsNone && entityMap.TryGetEntity(dto.TargetAreaNetworkId.NetworkId, out var areaEntity))
            {
                result.TargetAreaEntity = areaEntity;
            }
            else
            {
                if (!dto.TargetAreaNetworkId.IsNone)
                    BehaviorLog.ParseWarn("TargetAreaNetworkId=" + dto.TargetAreaNetworkId.NetworkId + " not found in entity map; area entity set to null.");
                result.TargetAreaEntity = Entity.Null;
            }

            Unsafe.Write(ptr, result);
        }

    }

}
