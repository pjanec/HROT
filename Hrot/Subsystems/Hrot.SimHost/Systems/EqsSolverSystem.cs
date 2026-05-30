using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Phase 2 EQS solver system (Muscle-tier, time-sliced).
    ///
    /// <para>Reads <see cref="IEqsTemplateRegistry"/> from the repo's managed singleton slot
    /// (registered by EqsModule.Initialize or tests). If no registry is found or the template
    /// lookup fails, falls back to Phase 1 stub behaviour (empty event).</para>
    ///
    /// <para>Pool lazy-init: creates <see cref="EqsResultPool"/> singleton on first Execute
    /// if not already present.</para>
    ///
    /// <para>Driven at 10 Hz by <see cref="Modules.EqsModule"/>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EqsSolverSystem : IEcsModuleSystem
    {
        // Iterator state for time-sliced entity traversal.
        private readonly IteratorState _iteratorState = new IteratorState();

        // Query cached after first use.
        private EntityQuery? _sensorQuery;

        // Pre-allocated context fields to prevent hidden closure allocations.
        // EvaluateSensor is passed as Action<Entity> to QueryTimeSliced.
        private IEntityCommandBuffer _currentCmd = null!;
        private ISimulationView _currentView = null!;
        private uint _currentTick;
        private EntityRepository _currentRepo = null!;

        /// <summary>Wall-clock budget in milliseconds per Execute call.</summary>
        public double EqsBudgetMs { get; set; } = 4.0;

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

            // Store frame context in fields to avoid closure allocation.
            _currentView = view;
            _currentCmd  = view.GetCommandBuffer();
            _currentTick = view.Tick;
            _currentRepo = repo;

            // Time-sliced iteration: yields if EqsBudgetMs is exceeded.
            repo.QueryTimeSliced(
                _sensorQuery,
                _iteratorState,
                EqsBudgetMs,
                TimeSliceMetric.WallClockTime,
                EvaluateSensor);
        }

        private void EvaluateSensor(Entity entity)
        {
            var repo = _currentRepo;

            ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(entity);

            // --- 3-branch compound identity resolution ---
            // Branch 1: child entity with PartMetadata -> derive key from parent's NetworkIdentity.
            // Branch 2: entity directly has NetworkIdentity -> legacy single-sensor path.
            // Branch 3: no NetworkIdentity anywhere -> local-only (offline/editor) path.
            long parentNetworkId;
            int  localChildIndex;
            if (repo.HasComponent<PartMetadata>(entity))
            {
                var meta   = repo.GetComponentRO<PartMetadata>(entity);
                var parent = meta.ParentEntity;
                if (!repo.IsAlive(parent) || !repo.HasComponent<NetworkIdentity>(parent))
                    return; // parent gone or local-only child
                parentNetworkId = repo.GetComponentRO<NetworkIdentity>(parent).Value;
                localChildIndex = meta.InstanceId;
            }
            else if (repo.HasComponent<NetworkIdentity>(entity))
            {
                parentNetworkId = repo.GetComponentRO<NetworkIdentity>(entity).Value;
                localChildIndex = 0;
            }
            else
            {
                // Purely local sensor (offline / editor).
                parentNetworkId = 0;
                localChildIndex = entity.Index;
            }

            // --- SensorEvalState management ---
            // Lazy-read SensorEvalState if present; otherwise create a default.
            SensorEvalState evalState;
            if (repo.HasComponent<SensorEvalState>(entity))
                evalState = repo.GetComponentRO<SensorEvalState>(entity);
            else
                evalState = new SensorEvalState { Phase = EqsEvalPhase.Idle, CurrentEpoch = sensor.Epoch };

            // Reset on epoch change (sensor parameters changed -> discard in-flight raycasts).
            // Preserve CurrentStructureHash so a soft reset does not trigger a spurious hard reset,
            // and preserve LastPublishedTopK so the ScoreDelta publish policy is not defeated on the
            // first tick after a parameter tweak (a soft reset must keep publish-suppression state).
            if (evalState.CurrentEpoch != sensor.Epoch)
            {
                ulong savedHash = evalState.CurrentStructureHash;
                var   savedTopK = evalState.LastPublishedTopK;
                evalState = new SensorEvalState
                {
                    Phase                = EqsEvalPhase.Idle,
                    CurrentEpoch         = sensor.Epoch,
                    CurrentStructureHash = savedHash,
                    LastPublishedTopK    = savedTopK,
                };
            }

            // Try to look up the template from the registry singleton.
            IEqsTemplateRegistry? registry = repo.HasSingletonManaged<IEqsTemplateRegistry>()
                ? repo.GetSingletonManaged<IEqsTemplateRegistry>()
                : null;

            if (registry == null || !registry.TryGetTemplate(sensor.BlueprintId, out var template))
            {
                // No registry or unknown template: Phase 1 stub fallback (empty result).
                _currentCmd.PublishEvent(new EqsResultEvent
                {
                    ParentNetworkId = parentNetworkId,
                    LocalChildIndex = localChildIndex,
                    Epoch           = sensor.Epoch,
                    RefreshTick     = (uint)(_currentTick + 1),
                    ResultHandle    = 0,
                    EntryCount      = 0,
                });
                return;
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

            // 1. Generation.
            Span<EqsResult> candidates = stackalloc EqsResult[template.MaxCandidates];
            int count = template.Generator.Generate(entity, ref Unsafe.AsRef(in sensor), repo, candidates);
            if (count == 0)
            {
                // Nothing generated: still publish an empty event so Brain's IsReady ticks.
                _currentCmd.PublishEvent(new EqsResultEvent
                {
                    ParentNetworkId = parentNetworkId,
                    LocalChildIndex = localChildIndex,
                    Epoch           = sensor.Epoch,
                    RefreshTick     = (uint)(_currentTick + 1),
                    ResultHandle    = 0,
                    EntryCount      = 0,
                });
                // Persist evalState so CurrentStructureHash from hard-reset is not lost.
                evalState.CurrentStructureHash = liveHash != 0 ? liveHash : evalState.CurrentStructureHash;
                if (repo.HasComponent<SensorEvalState>(entity))
                    _currentCmd.SetComponent(entity, evalState);
                else
                    _currentCmd.AddComponent(entity, evalState);
                return;
            }

            var activeCandidates = candidates.Slice(0, count);

            // 2. FilterCheap.
            if (template.FilterCheap != null)
                foreach (var test in template.FilterCheap)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);

            // 3. FilterExpensive (stubs go here in Phase 3+).
            if (template.FilterExpensive != null)
                foreach (var test in template.FilterExpensive)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);

            // 4. Compact rejected (-1L) candidates BEFORE cheap scoring so scoring tests never
            //    see rejection sentinels. Do NOT truncate here: no scoring has run yet, so the
            //    Score field is meaningless and a score-ordered truncation at this point would
            //    discard candidates effectively at random (bypassing DistanceScoreTest et al.
            //    for the Top-K selection).
            activeCandidates = CompactRejected(activeCandidates);

            // 5. ScoreCheap.
            if (template.ScoreCheap != null)
                foreach (var test in template.ScoreCheap)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);

            // 6. Top-K reduction: now that cheap scores exist, rank by Score and truncate to
            //    MaxTopK so the expensive phase (raycasts, path cost) runs only on the viable
            //    survivors (Design §5.2 reduce-between-phases / §9.3 two-pass strategy).
            activeCandidates = TruncateTopK(activeCandidates, EqsResultPool.MaxTopK);

            // 7. ScoreExpensive.
            if (template.ScoreExpensive != null)
                foreach (var test in template.ScoreExpensive)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);

            // Check if any candidate has FlagPendingRay set.
            // If so, yield without writing to pool — wait for ring buffer results.
            bool anyPendingRay = false;
            for (int i = 0; i < activeCandidates.Length; i++)
            {
                if ((activeCandidates[i].Flags & AccurateLineOfSightTest.FlagPendingRay) != 0)
                {
                    anyPendingRay = true;
                    break;
                }
            }

            if (anyPendingRay)
            {
                evalState.Phase           = EqsEvalPhase._AwaitingRaycasts;
                evalState.AwaitingSinceTick = _currentTick;
                if (repo.HasComponent<SensorEvalState>(entity))
                    _currentCmd.SetComponent(entity, evalState);
                else
                    _currentCmd.AddComponent(entity, evalState);
                return; // DO NOT publish EqsResultEvent while awaiting raycasts.
            }

            // All raycasts resolved (or no AccurateLOS test in template): proceed to sort + write.
            // Update structure hash so next tick does not trigger a spurious hard-reset.
            evalState.CurrentStructureHash = template.ComputeStructureHash();
            evalState.Phase = EqsEvalPhase.Idle;

            // 9. Final sort descending by Score.
            MemoryExtensions.Sort(activeCandidates, (a, b) => b.Score.CompareTo(a.Score));

            // 10. Write to pool and publish (persists evalState, including LastPublishedTopK update).
            WriteResultsToPoolAndPublish(entity, parentNetworkId, localChildIndex, sensor.Epoch, ref evalState, in sensor, activeCandidates);
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
            Entity entity,
            long parentNetworkId,
            int  localChildIndex,
            uint epoch,
            ref SensorEvalState evalState,
            in EqsSensor sensor,
            Span<EqsResult> finalCandidates)
        {
            // ScoreDelta policy: suppress publish when all top-K score deltas are within threshold.
            if ((EqsPublishPolicy)sensor.PublishPolicy == EqsPublishPolicy.ScoreDelta)
            {
                bool anyExceedsThreshold = false;
                int  compareCount        = Math.Min(finalCandidates.Length, 16);
                ReadOnlySpan<float> lastPublished = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<TopKScoreCache, float>(ref evalState.LastPublishedTopK), 16);
                for (int i = 0; i < compareCount; i++)
                {
                    float delta = MathF.Abs(finalCandidates[i].Score - lastPublished[i]);
                    if (delta > sensor.ScoreDeltaThreshold)
                    {
                        anyExceedsThreshold = true;
                        break;
                    }
                }
                if (!anyExceedsThreshold)
                {
                    // No significant change — persist evalState and skip publish.
                    if (_currentRepo.HasComponent<SensorEvalState>(entity))
                        _currentCmd.SetComponent(entity, evalState);
                    else
                        _currentCmd.AddComponent(entity, evalState);
                    return;
                }
                // Update cache with current top-K scores before publishing.
                Span<float> cache = MemoryMarshal.CreateSpan(
                    ref Unsafe.As<TopKScoreCache, float>(ref evalState.LastPublishedTopK), 16);
                for (int i = 0; i < compareCount; i++)
                    cache[i] = finalCandidates[i].Score;
                // Zero out any slots beyond current result count.
                for (int i = compareCount; i < 16; i++)
                    cache[i] = 0f;
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

            if (_currentRepo.HasComponent<SensorEvalState>(entity))
                _currentCmd.SetComponent(entity, evalState);
            else
                _currentCmd.AddComponent(entity, evalState);
        }
    }
}
