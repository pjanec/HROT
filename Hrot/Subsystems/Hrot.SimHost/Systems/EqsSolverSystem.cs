using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// The EQS solver (Muscle tier), driven at 10 Hz by <see cref="Modules.EqsModule"/>.
    ///
    /// <para>⭐⭐ <c>CE-3037</c> (S4) — <b>a budget of COUNTED work, not milliseconds</b> (docs/DESIGN_Sensors_And_Doctrine.md
    /// §5.3–§5.4): every live sensor is ordered by band, then oldest result, then entity index (<see cref="EqsSchedule"/>);
    /// sensors run in that order until <see cref="BudgetUnits"/> is spent, each counting its work (<see cref="EqsCost"/>).
    /// A sensor whose last cost does not fit what is left waits a tick — and is then the oldest, so it runs first (alone if
    /// it must). 🔴 It replaces wall-clock slicing (<c>QueryTimeSliced(…, WallClockTime)</c>), which scheduled a different
    /// set of sensors on every run and every machine. ⭐ The memory stage (<see cref="SensorMemoryStage"/>) turns a perception
    /// sensor's answers into acquired / lost transitions of its unit.</para>
    ///
    /// <para>Reads <see cref="IEqsTemplateRegistry"/> from the repo's managed singleton slot. An unknown template answers
    /// "empty". The <see cref="EqsResultPool"/> singleton is created on first Execute.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EqsSolverSystem : IEcsModuleSystem
    {
        // Query cached after first use.
        private EntityQuery? _sensorQuery;

        private readonly EqsSchedule _schedule = new();
        private readonly SensorMemoryStage _memory = new();
        private readonly List<Entity> _lastSchedule = new();

        // ⭐ CE-3056 — this tick's answers by what DECIDES them; a twin due later in the tick copies instead of solving.
        //   📄 docs/DESIGN_Sensors_And_Doctrine.md §5.6. Cleared every tick, so an answer is never older than its delivery.
        private readonly Dictionary<QueryShareKey, EqsResult[]> _shared = new();

        /// <summary>What decides a QUERY sensor's answer — and nothing about its delivery (epoch, publish policy, priority
        /// stay per owner). Two sensors with equal keys answer identically in one tick.</summary>
        private readonly record struct QueryShareKey(
            Entity Self, uint BlueprintId, float SearchRadius, uint FactionFilter, float ThreatThreshold,
            Entity Context0, Entity Context1, Entity Context2);

        // Frame context for the per-sensor evaluation.
        private IEntityCommandBuffer _currentCmd = null!;
        private ISimulationView _currentView = null!;
        private uint _currentTick;
        private EntityRepository _currentRepo = null!;

        /// <summary>
        /// ⭐ Work units one solver tick may spend (<see cref="EqsCost"/>). 📐 Sized for VISION since <c>CE-3038</c> (it rides in
        /// this budget now): a 500 m all-round visual sensor costs ~280 units, so 150 000 solves EVERY sensor of ~500 units
        /// each tick — what the old perception chain did — and beyond that the oldest go first (measured 2026-10-04, debug
        /// build: 500 units = 142 k units / 76 ms; DESIGN_Sensors_And_Doctrine.md §9.4).
        /// </summary>
        public int BudgetUnits { get; set; } = DefaultBudgetUnits;

        /// <summary>The default <see cref="BudgetUnits"/>.</summary>
        public const int DefaultBudgetUnits = 150_000;

        /// <summary>The sensors that ran last tick, in run order (diagnostics / the determinism rail).</summary>
        public IReadOnlyList<Entity> LastSchedule => _lastSchedule;

        /// <summary>Work units spent last tick.</summary>
        public int LastSpentUnits { get; private set; }

        /// <summary>⭐ CE-3056 — sensors answered last tick by COPYING a twin's answer instead of solving (diagnostics / rails).</summary>
        public int LastSharedCopies { get; private set; }

        /// <summary>The memory stage (test hook).</summary>
        public SensorMemoryStage Memory => _memory;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // Lazy-init pool singleton (allocated once, lives on the repo).
            if (!repo.HasSingleton<EqsResultPool>())
            {
                var pool = new EqsResultPool
                {
                    NextFreeIndex = 0,
                    Results = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, Allocator.Persistent),
                };
                repo.SetSingletonUnmanaged(pool);
            }

            // Build sensor query once; use All lifecycle so it works both offline (Active)
            // and in the distributed Muscle node (Ghost).
            // NetworkIdentity is NOT required: child-entity sensors have PartMetadata instead,
            // and local-only (editor) sensors may have neither.
            _sensorQuery ??= repo.Query()
                .With<EqsSensor>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            _currentView = view;
            _currentCmd  = view.GetCommandBuffer();
            _currentTick = view.Tick;
            _currentRepo = repo;
            _memory.Begin(repo);
            _lastSchedule.Clear();
            _shared.Clear();
            LastSharedCopies = 0;

            // ── order every live sensor; an ended one costs nothing and is handled at once ──
            _schedule.Begin();
            foreach (var entity in _sensorQuery)
            {
                ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(entity);
                if (sensor.Suspended)
                {
                    // ⭐ CE-486 — an ended sensor publishes NOTHING and drops its evaluation state: the carrier OUTLIVES a
                    //   lifetime (a part id is reused, the instance is never disposed), so the next lifetime starts exactly
                    //   as a fresh carrier did. 📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D5 ③. It also holds nothing.
                    if (repo.HasComponent<SensorEvalState>(entity)) _currentCmd.RemoveComponent<SensorEvalState>(entity);
                    _memory.Clear(view, entity);
                    continue;
                }
                var eval = repo.HasComponent<SensorEvalState>(entity) ? repo.GetComponentRO<SensorEvalState>(entity) : default;
                _schedule.Add(entity, sensor.Priority, eval.LastSolvedTick, eval.LastCost);
            }

            // ── run them in order within the budget ──
            // ⭐ Each band gets its share (Critical 50 %, Normal 35 %, Low the rest), unused slack rolling down, and the
            //   oldest sensor of every band always starts — so a busy Critical band never starves Low.
            int spent = 0, bandSpent = 0, bandBudget = 0, rank = -1;
            foreach (var item in _schedule.Ordered())
            {
                if (item.Rank != rank)
                {
                    rank       = item.Rank;
                    bandSpent  = 0;
                    bandBudget = EqsSchedule.CumulativeShare(rank, BudgetUnits) - spent;
                }
                if (!EqsSchedule.ShouldStart(bandSpent, item.Estimate, bandBudget)) continue;
                int cost = RunSensor(item.Sensor);
                spent     += cost;
                bandSpent += cost;
                _lastSchedule.Add(item.Sensor);
            }
            LastSpentUnits = spent;

            _memory.Flush(view, _currentCmd);
        }

        // Evaluates one sensor and persists its state ONCE (schedule fields included). Returns the work units spent.
        private int RunSensor(Entity entity)
        {
            var repo = _currentRepo;
            ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(entity);
            bool had = repo.HasComponent<SensorEvalState>(entity);
            SensorEvalState evalState = had
                ? repo.GetComponentRO<SensorEvalState>(entity)
                : new SensorEvalState { Phase = EqsEvalPhase.Idle, CurrentEpoch = sensor.Epoch };

            int cost = EvaluateSensor(entity, ref evalState, out bool keyed);
            if (!keyed) return 0;   // not solvable here (no wire key): no state, no cost

            evalState.LastSolvedTick = _currentTick;
            evalState.LastCost       = cost;
            if (had) _currentCmd.SetComponent(entity, evalState);
            else     _currentCmd.AddComponent(entity, evalState);
            return cost;
        }

        // One evaluation. evalState is updated in place (the caller persists it once); returns the work units counted.
        private int EvaluateSensor(Entity entity, ref SensorEvalState evalState, out bool keyed)
        {
            var repo = _currentRepo;
            keyed = false;

            ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(entity);

            // --- the wire key: the ONE rule (EqsSensorKey) ---
            // A child whose parent is gone or local-only is not solved; a purely local sensor (offline / editor) is keyed
            // (0, its entity index) — the local path EqsResultUpdateSystem matches.
            var kind = EqsSensorKey.Resolve(repo, entity, out long parentNetworkId, out int localChildIndex, out _);
            if (kind == EqsSensorKeyKind.None) return 0;
            if (kind == EqsSensorKeyKind.LocalOnly) localChildIndex = entity.Index;
            keyed = true;

            // Reset on epoch change (sensor parameters changed -> discard in-flight raycasts).
            // Preserve CurrentStructureHash so a soft reset does not trigger a spurious hard reset,
            // and preserve LastPublishedTopK so the ScoreDelta publish policy is not defeated after the new
            // epoch's first answer (a soft reset keeps publish-suppression state; PublishedThisEpoch resets, so
            // that first answer always goes out). The schedule fields are kept (they are the caller's).
            if (evalState.CurrentEpoch != sensor.Epoch)
            {
                evalState = new SensorEvalState
                {
                    Phase                = EqsEvalPhase.Idle,
                    CurrentEpoch         = sensor.Epoch,
                    CurrentStructureHash = evalState.CurrentStructureHash,
                    LastPublishedTopK    = evalState.LastPublishedTopK,
                    LastSolvedTick       = evalState.LastSolvedTick,
                    LastCost             = evalState.LastCost,
                };
            }

            // ⭐ CE-3072 B3 (R-213) — a danger-area sensor is not a ranked query: route + terrain → areas, on its own result path
            //   (DangerAreaResultEvent). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
            if (sensor.BlueprintId == Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.TemplateId)
            {
                SolveDangerArea(entity, parentNetworkId, localChildIndex, in sensor);
                evalState.Phase = EqsEvalPhase.Idle;
                return EqsCost.Sensor + EqsCost.Path;
            }

            // Try to look up the template from the registry singleton.
            IEqsTemplateRegistry? registry = repo.HasSingletonManaged<IEqsTemplateRegistry>()
                ? repo.GetSingletonManaged<IEqsTemplateRegistry>()
                : null;

            if (registry == null || !registry.TryGetTemplate(sensor.BlueprintId, out var template))
            {
                // No registry or unknown template: Phase 1 stub fallback (empty result).
                PublishEmpty(entity, parentNetworkId, localChildIndex, sensor.Epoch);
                return 0;
            }

            // Hard-reset: detect structural hot-reload by comparing template's StructureHash
            // against what the SensorEvalState recorded on last evaluation.
            ulong liveHash = template.ComputeStructureHash();
            if (liveHash != 0 && evalState.CurrentStructureHash != liveHash)
            {
                evalState.Phase                = EqsEvalPhase.Idle;
                evalState.PendingRaycastCount  = 0;
                evalState.CurrentStructureHash = liveHash;
                if (repo.HasComponent<EqsCognitiveBuffer>(entity))
                {
                    ref var buffer = ref repo.GetComponentRW<EqsCognitiveBuffer>(entity);
                    buffer.LastUpdateTick = 0;
                }
            }

            // Phase skip-guard: avoid re-generating candidates on the same tick that raycasts were
            // submitted, and reset to Idle on subsequent ticks so the next EQS cycle picks up the
            // ring-buffer results.  A pure early-return (no reset) would strand the sensor in
            // _AwaitingRaycasts indefinitely because nothing else resets the phase.
            if (evalState.Phase == EqsEvalPhase._AwaitingRaycasts)
            {
                // Same tick as submission: nothing to do. Subsequent tick: back to Idle so the full pipeline re-runs and
                // reads the ring-buffer results that arrived since the submission.
                if (evalState.AwaitingSinceTick != _currentTick) evalState.Phase = EqsEvalPhase.Idle;
                return 0;
            }

            // ⭐ CE-3056 — a twin of a query already answered THIS tick copies that answer, then goes through its OWN publish
            //   policy and epoch (§5.6). Only a completed answer is ever stored, so a copy is never a half-solved one.
            bool shareable = TryShareKey(entity, in sensor, out var shareKey);
            if (shareable && _shared.TryGetValue(shareKey, out var sharedAnswer))
            {
                evalState.Phase = EqsEvalPhase.Idle;
                LastSharedCopies++;
                if (sharedAnswer.Length == 0) PublishEmpty(entity, parentNetworkId, localChildIndex, sensor.Epoch);
                else WriteResultsToPoolAndPublish(parentNetworkId, localChildIndex, sensor.Epoch, ref evalState, in sensor, sharedAnswer.AsSpan());
                return EqsCost.Cheap;
            }

            // 1. Generation.
            Span<EqsResult> candidates = stackalloc EqsResult[template.MaxCandidates];
            int count = template.Generator.Generate(entity, ref Unsafe.AsRef(in sensor), repo, candidates);
            if (count < 0)
            {
                // Not evaluable yet (IEqsGenerator contract): publish NOTHING, so the reader keeps
                // waiting rather than reading an empty result as "nothing there".
                return 0;
            }
            int cost = EqsCost.Sensor + (count * EqsCost.Candidate);
            if (count == 0)
            {
                // Nothing generated: still publish an empty event so Brain's IsReady ticks.
                if (shareable) _shared[shareKey] = Array.Empty<EqsResult>();
                PublishEmpty(entity, parentNetworkId, localChildIndex, sensor.Epoch);
                return cost;
            }

            var activeCandidates = candidates.Slice(0, count);

            // 2. FilterCheap.
            if (template.FilterCheap != null)
                foreach (var test in template.FilterCheap)
                {
                    cost += activeCandidates.Length * EqsCost.WeightOf(test);
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);
                }

            // 3. FilterExpensive (stubs go here in Phase 3+).
            if (template.FilterExpensive != null)
                foreach (var test in template.FilterExpensive)
                {
                    cost += activeCandidates.Length * EqsCost.WeightOf(test);
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);
                }

            // 4. Compact rejected (-1L) candidates BEFORE cheap scoring so scoring tests never
            //    see rejection sentinels. Do NOT truncate here: no scoring has run yet, so the
            //    Score field is meaningless and a score-ordered truncation at this point would
            //    discard candidates effectively at random (bypassing DistanceScoreTest et al.
            //    for the Top-K selection).
            activeCandidates = CompactRejected(activeCandidates);

            // 5. ScoreCheap.
            if (template.ScoreCheap != null)
                foreach (var test in template.ScoreCheap)
                {
                    cost += activeCandidates.Length * EqsCost.WeightOf(test);
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);
                }

            // 6. Top-K reduction: now that cheap scores exist, rank by Score and truncate to
            //    MaxTopK so the expensive phase (raycasts, path cost) runs only on the viable
            //    survivors (Design §5.2 reduce-between-phases / §9.3 two-pass strategy).
            activeCandidates = TruncateTopK(activeCandidates, EqsResultPool.MaxTopK);

            // 7. ScoreExpensive.
            if (template.ScoreExpensive != null)
                foreach (var test in template.ScoreExpensive)
                {
                    cost += activeCandidates.Length * EqsCost.WeightOf(test);
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);
                }

            // Check if any candidate has FlagPendingRay set.
            // If so, yield without writing to pool — wait for ring buffer results.
            for (int i = 0; i < activeCandidates.Length; i++)
            {
                if ((activeCandidates[i].Flags & AccurateLineOfSightTest.FlagPendingRay) != 0)
                {
                    evalState.Phase             = EqsEvalPhase._AwaitingRaycasts;
                    evalState.AwaitingSinceTick = _currentTick;
                    return cost; // DO NOT publish EqsResultEvent while awaiting raycasts.
                }
            }

            // All raycasts resolved (or no AccurateLOS test in template): proceed to sort + write.
            evalState.Phase = EqsEvalPhase.Idle;

            // 9. Final sort descending by Score.
            MemoryExtensions.Sort(activeCandidates, (a, b) => b.Score.CompareTo(a.Score));

            // ⭐ CE-3037 — the memory stage sees every answer, published or suppressed (a ScoreDelta that did not move is
            //   still a sighting).
            _memory.Observe(_currentView, entity, _currentTick, activeCandidates);

            if (shareable) _shared[shareKey] = activeCandidates.ToArray();

            // 10. Write to pool and publish (updates LastPublishedTopK / PublishedThisEpoch).
            WriteResultsToPoolAndPublish(parentNetworkId, localChildIndex, sensor.Epoch, ref evalState, in sensor, activeCandidates);
            return cost;
        }

        // ⭐ CE-3056 — a QUERY sensor (no SensorTag, no SensorCapability) with a PLACED self is shareable: a perception sensor's
        //   tests read its own capability, so two of them can differ with equal EqsSensor fields, and each feeds the memory
        //   stage itself.
        private bool TryShareKey(Entity entity, in EqsSensor sensor, out QueryShareKey key)
        {
            var repo = _currentRepo;
            if ((repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.SensorTag>()
                    && repo.HasComponent<Fdp.Toolkit.Perception.Components.SensorTag>(entity))
                || ((ISimulationView)repo).HasManagedComponent<Fdp.Toolkit.Perception.Components.SensorCapability>(entity))
            {
                key = default;
                return false;
            }
            // ⚠ No placed self ⇒ the answer may depend on the carrier itself (a local sensor's generator can read it): never share.
            var self = EqsContext.Self(_currentView, entity, sensor);
            if (self.IsNull) { key = default; return false; }
            key = new QueryShareKey(self, sensor.BlueprintId, sensor.SearchRadius,
                                    sensor.FactionFilter, sensor.ThreatThreshold,
                                    sensor.ContextSlot0, sensor.ContextSlot1, sensor.ContextSlot2);
            return true;
        }

        // ⭐ CE-3072 B3 — the areas along the sensor's route, published for the egress (cluster) and the Brain (fused world).
        private void SolveDangerArea(Entity entity, long parentNetworkId, int localChildIndex, in EqsSensor sensor)
        {
            var areas = new Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[8];
            int count = Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.Solve(_currentRepo, entity, in sensor, areas);
            var answer = new Fdp.Toolkit.Squad.DangerArea.DangerAreaResultEvent
            {
                ParentNetworkId = parentNetworkId,
                LocalChildIndex = localChildIndex,
                Epoch           = sensor.Epoch,
                RefreshTick     = _currentTick,
                Areas           = areas,
                Count           = count,
            };
            // ⛔ NOT _currentRepo.Bus: EqsModule is SlowBackground — asynchronous, on a SNAPSHOT — so a direct publish lands
            //   on the snapshot's bus and is lost (measured: the cross-host rail never got an answer). The command buffer is
            //   played back into the live world, as the ranked answer's EqsResultEvent is.
            if (_currentCmd is EntityCommandBuffer ecb) ecb.PublishManagedEvent(answer);
            else _currentRepo.Bus.PublishManaged(answer);   // a synchronous caller (tests) with no recording buffer
        }

        private void PublishEmpty(Entity entity, long parentNetworkId, int localChildIndex, uint epoch)
        {
            _memory.Observe(_currentView, entity, _currentTick, ReadOnlySpan<EqsResult>.Empty);
            _currentCmd.PublishEvent(new EqsResultEvent
            {
                ParentNetworkId = parentNetworkId,
                LocalChildIndex = localChildIndex,
                Epoch           = epoch,
                RefreshTick     = (uint)(_currentTick + 1),
                ResultHandle    = 0,
                EntryCount      = 0,
            });
        }

        // Compacts the span in place by removing rejection sentinels (EntityId == -1L),
        // preserving valid positional candidates (EntityId == 0). Does NOT sort or truncate —
        // truncation must wait until cheap scores have been computed (see TruncateTopK).
        private static Span<EqsResult> CompactRejected(Span<EqsResult> candidates)
        {
            int validCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].EntityId != -1L)
                    candidates[validCount++] = candidates[i];
            }

            return candidates.Slice(0, validCount);
        }

        // Ranks the (already-compacted, already cheap-scored) span descending by Score and
        // truncates to maxTopK. Must run AFTER ScoreCheap so the truncation reflects real scores
        // rather than the zero-initialised Score field, and BEFORE ScoreExpensive so costly tests
        // run only on the viable survivors.
        private static Span<EqsResult> TruncateTopK(Span<EqsResult> candidates, int maxTopK)
        {
            if (candidates.Length > maxTopK)
            {
                MemoryExtensions.Sort(candidates, (a, b) => b.Score.CompareTo(a.Score));
                return candidates.Slice(0, maxTopK);
            }

            return candidates;
        }

        private void WriteResultsToPoolAndPublish(
            long parentNetworkId,
            int  localChildIndex,
            uint epoch,
            ref SensorEvalState evalState,
            in EqsSensor sensor,
            Span<EqsResult> finalCandidates)
        {
            // ScoreDelta policy: suppress publish when all top-K score deltas are within threshold — but ⭐ never the
            // first answer of an epoch (PublishedThisEpoch): the soft reset keeps LastPublishedTopK, and an epoch bump is
            // the Brain asking for a NEW answer (EQS 1.3 §17.6). Without this a refresh — or a new lifetime on a reused
            // part id — whose scores had not moved was never answered.
            if ((EqsPublishPolicy)sensor.PublishPolicy == EqsPublishPolicy.ScoreDelta)
            {
                bool anyExceedsThreshold = !evalState.PublishedThisEpoch;
                int  compareCount        = Math.Min(finalCandidates.Length, 16);
                ReadOnlySpan<float> lastPublished = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<TopKScoreCache, float>(ref evalState.LastPublishedTopK), 16);
                for (int i = 0; i < compareCount && !anyExceedsThreshold; i++)
                {
                    float delta = MathF.Abs(finalCandidates[i].Score - lastPublished[i]);
                    if (delta > sensor.ScoreDeltaThreshold)
                        anyExceedsThreshold = true;
                }
                if (!anyExceedsThreshold)
                    return;   // no significant change — skip publish (the caller persists evalState)
                // Update cache with current top-K scores before publishing.
                Span<float> cache = MemoryMarshal.CreateSpan(
                    ref Unsafe.As<TopKScoreCache, float>(ref evalState.LastPublishedTopK), 16);
                for (int i = 0; i < compareCount; i++)
                    cache[i] = finalCandidates[i].Score;
                // Zero out any slots beyond current result count.
                for (int i = compareCount; i < 16; i++)
                    cache[i] = 0f;
            }

            // ⭐ CE-2097 — TopChanged policy: publish only when the top candidate's identity changes (EqsPublishPolicy docs;
            //   EQS 1.3: "the top entry's identity changes"). Same first-answer-of-an-epoch exception as ScoreDelta.
            //   Empty ⇄ non-empty is a change.
            if ((EqsPublishPolicy)sensor.PublishPolicy == EqsPublishPolicy.TopChanged)
            {
                bool hasTop = finalCandidates.Length > 0;
                if (evalState.PublishedThisEpoch && hasTop == evalState.LastPublishedHadTop
                    && (!hasTop || SameIdentity(finalCandidates[0], in evalState)))
                    return;   // same top — skip publish (the caller persists evalState)
                evalState.LastPublishedHadTop = hasTop;
                if (hasTop)
                {
                    evalState.LastPublishedTopEntityId = finalCandidates[0].EntityId;
                    evalState.LastPublishedTopX        = finalCandidates[0].PositionX;
                    evalState.LastPublishedTopY        = finalCandidates[0].PositionY;
                    evalState.LastPublishedTopZ        = finalCandidates[0].PositionZ;
                }
            }

            ref var pool = ref _currentRepo.GetSingletonUnmanaged<EqsResultPool>();
            // WriteAndWrap takes ReadOnlySpan<EqsResult>.
            int handle = pool.WriteAndWrap((ReadOnlySpan<EqsResult>)finalCandidates);

            _currentCmd.PublishEvent(new EqsResultEvent
            {
                ParentNetworkId = parentNetworkId,
                LocalChildIndex = localChildIndex,
                Epoch           = epoch,
                RefreshTick     = (uint)(_currentTick + 1),
                ResultHandle    = handle,
                EntryCount      = finalCandidates.Length,
            });
            evalState.PublishedThisEpoch = true;
        }

        /// <summary>⭐ CE-2097 — is <paramref name="top"/> the same candidate as the last published top? An entity-shaped
        /// answer compares the entity; a positional one (EntityId 0) compares the position.</summary>
        private static bool SameIdentity(in EqsResult top, in SensorEvalState evalState)
            => top.EntityId != 0
                ? top.EntityId == evalState.LastPublishedTopEntityId
                : evalState.LastPublishedTopEntityId == 0
                  && top.PositionX == evalState.LastPublishedTopX
                  && top.PositionY == evalState.LastPublishedTopY
                  && top.PositionZ == evalState.LastPublishedTopZ;
    }
}
