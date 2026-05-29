using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Context passed to every Utility AI input reader during a scoring pass.
    /// Carries the entity repository and the relevant entity references so readers
    /// can query any ECS component without being bound to a specific system.
    /// </summary>
    public struct UtilityInputCtx
    {
        /// <summary>The ECS repository for the active world.</summary>
        public EntityRepository Repo;
        /// <summary>The entity whose utility is being evaluated.</summary>
        public Entity Self;
        /// <summary>The context entity (target, candidate, leader) being scored against.</summary>
        public Entity Context;
        /// <summary>Per-consideration parameters (sensor blueprint ID, max range, etc.).</summary>
        public InputParams Params;
    }

    /// <summary>
    /// Registry mapping Utility AI input IDs to their reader function pointers.
    /// Readers are registered once at startup (or in tests) and looked up per consideration.
    /// Phase 2 will replace this dictionary with a source-generated array for O(1) dispatch.
    /// </summary>
    public static unsafe class UtilityInputReaderStore
    {
        private static readonly Dictionary<ushort, nint> _readers = new();

        /// <summary>
        /// Registers a reader function for the given input ID.
        /// Re-registering an ID replaces the previous reader.
        /// </summary>
        public static void Register(ushort inputId, delegate*<in UtilityInputCtx, float> reader)
        {
            _readers[inputId] = (nint)reader;
        }

        /// <summary>
        /// Retrieves the reader for <paramref name="inputId"/>.
        /// Returns <c>false</c> when the ID has not been registered; <paramref name="reader"/>
        /// is set to <c>null</c> in that case.
        /// </summary>
        internal static bool TryGet(ushort inputId, out delegate*<in UtilityInputCtx, float> reader)
        {
            if (_readers.TryGetValue(inputId, out nint ptr))
            {
                reader = (delegate*<in UtilityInputCtx, float>)ptr;
                return true;
            }
            reader = null;
            return false;
        }

        /// <summary>
        /// Removes all registered readers. Intended for test teardown only.
        /// </summary>
        public static void Clear() => _readers.Clear();
    }

    /// <summary>
    /// Core Utility AI scorer: evaluates a <see cref="UtilityDecisionDef"/> against a set of
    /// options (or candidates) and ranks them by descending score into a
    /// <see cref="UtilityResultBuffer"/>.
    /// </summary>
    /// <remarks>
    /// All hot-path methods are allocation-free: intermediate float arrays use
    /// <c>stackalloc</c>. Managed arrays (<c>def.Options</c>, <c>opt.Considerations</c>)
    /// are read-only inputs.
    /// </remarks>
    public unsafe class UtilityScorer
    {
        private readonly UtilityRegistry _registry;

        /// <summary>
        /// Creates a scorer bound to <paramref name="registry"/>.
        /// The registry is used by instance overloads; static overloads do not need it.
        /// </summary>
        public UtilityScorer(UtilityRegistry registry)
        {
            _registry = registry;
        }

        // ── Instance API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluates the decision identified by <paramref name="decisionId"/> for
        /// <paramref name="self"/>, writing ranked results into the entity's
        /// <see cref="UtilityResultBuffer"/> component.
        /// Dispatches to candidate iteration for ThreatRanking / WeaponSelection, or to
        /// <see cref="Evaluate(EntityRepository, Entity, in UtilityDecisionDef, Entity, ref UtilityResultBuffer, UtilityTraceWorkingMemory1024*, ushort)"/>
        /// for PostureSelect.
        /// Does nothing when the decision is not registered.
        /// </summary>
        public void Evaluate(EntityRepository repo, Entity self, int decisionId,
            Entity context = default, ushort tick = 0)
        {
            if (!_registry.TryGet(decisionId, out var def, out _) || def == null) return;
            if (!repo.HasComponent<UtilityResultBuffer>(self)) return;
            ref var output = ref repo.GetComponentRW<UtilityResultBuffer>(self);

            UtilityTraceWorkingMemory1024* tracePtr = null;
            if (repo.HasComponent<UtilityDebugFlags>(self) && repo.HasComponent<UtilityTraceWorkingMemory1024>(self))
            {
                ref readonly var flags = ref repo.GetComponentRO<UtilityDebugFlags>(self);
                if (flags.TraceEnabled != 0)
                    tracePtr = (UtilityTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(
                        ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(self));
            }

            if (def.Kind == DecisionKind.PostureSelect)
            {
                Evaluate(repo, self, in def, context, ref output, tracePtr, tick);
            }
            else
            {
                EvaluateCandidates(repo, self, in def, context, ref output, tracePtr, tick);
            }
        }

        /// <summary>
        /// Evaluates the PostureSelect decision identified by <paramref name="decisionId"/>
        /// with hysteresis and returns the winning posture byte.
        /// Reads the current posture from <see cref="UtilityResultBuffer.Top"/>.
        /// Does nothing and returns 0 when the decision is not registered.
        /// </summary>
        public byte SelectPosture(EntityRepository repo, Entity self, int decisionId,
            ushort tick = 0)
        {
            if (!_registry.TryGet(decisionId, out var def, out float hysteresis) || def == null) return 0;
            if (!repo.HasComponent<UtilityResultBuffer>(self)) return 0;
            ref var output = ref repo.GetComponentRW<UtilityResultBuffer>(self);

            byte activePosture = output.Count > 0 ? output.GetSpanRO()[0].WinningPostureId : (byte)0;

            UtilityTraceWorkingMemory1024* tracePtr = null;
            if (repo.HasComponent<UtilityDebugFlags>(self) && repo.HasComponent<UtilityTraceWorkingMemory1024>(self))
            {
                ref readonly var flags = ref repo.GetComponentRO<UtilityDebugFlags>(self);
                if (flags.TraceEnabled != 0)
                    tracePtr = (UtilityTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(
                        ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(self));
            }

            return SelectPosture(repo, self, in def, activePosture, hysteresis, ref output, tracePtr, tick);
        }

        // ── Private: candidate evaluation ─────────────────────────────────────────

        private void EvaluateCandidates(EntityRepository repo, Entity self,
            in UtilityDecisionDef def, Entity externalContext,
            ref UtilityResultBuffer output,
            UtilityTraceWorkingMemory1024* tracePtr, ushort tick)
        {
            // Build candidate list on the stack (max 16).
            const int MaxCandidates = 16;
            Entity* candidates    = stackalloc Entity[MaxCandidates];
            bool*   isSelfMount   = stackalloc bool[MaxCandidates];
            int     candidateCount = 0;

            if (def.Kind == DecisionKind.ThreatRanking)
            {
                // Candidates = contacts in self's TargetMemory.
                if (repo.HasComponent<TargetMemory>(self))
                {
                    ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(self);
                    for (int i = 0; i < mem.Count && candidateCount < MaxCandidates; i++)
                    {
                        candidates[candidateCount]  = new Entity((ulong)mem.EntityIds[i]);
                        isSelfMount[candidateCount] = false;
                        candidateCount++;
                    }
                }
            }
            else // WeaponSelection
            {
                // Candidates = self (if it has WeaponMountInfo) + child mounts.
                if (repo.HasComponent<WeaponMountInfo>(self))
                {
                    candidates[candidateCount]  = self;
                    isSelfMount[candidateCount] = true;
                    candidateCount++;
                }

                var query = repo.Query()
                    .With<WeaponMountInfo>()
                    .With<PartMetadata>()
                    .Build();
                foreach (var e in query)
                {
                    if (candidateCount >= MaxCandidates) break;
                    ref readonly var pm = ref repo.GetComponentRO<PartMetadata>(e);
                    if (!pm.ParentEntity.Equals(self)) continue;
                    candidates[candidateCount]  = e;
                    isSelfMount[candidateCount] = false;
                    candidateCount++;
                }
            }

            if (candidateCount == 0)
            {
                output.Count          = 0;
                output.RunnerUpMargin = 0f;
                return;
            }

            // Score each candidate.
            float* scores  = stackalloc float[candidateCount];
            long*  handles = stackalloc long[candidateCount];
            int*   indices = stackalloc int[candidateCount];

            var tmpBuffer = new UtilityResultBuffer();
            for (int ci = 0; ci < candidateCount; ci++)
            {
                Entity candidate = candidates[ci];
                Entity evalSelf    = (def.Kind == DecisionKind.WeaponSelection) ? candidate : self;
                Entity evalContext = (def.Kind == DecisionKind.WeaponSelection) ? externalContext : candidate;

                Evaluate(repo, evalSelf, in def, evalContext, ref tmpBuffer, null, tick);
                scores[ci]  = tmpBuffer.Count > 0 ? tmpBuffer.GetSpanRO()[0].Score : 0f;
                handles[ci] = (long)candidate.PackedValue;
                indices[ci] = ci;
            }

            // Sort descending by score.
            InsertionSort(scores, indices, candidateCount);

            int fillCount = candidateCount < Fdp.Toolkit.Utility.UtilityConstants.TopN
                ? candidateCount
                : Fdp.Toolkit.Utility.UtilityConstants.TopN;

            var outSpan = output.GetSpanRW();
            for (int i = 0; i < fillCount; i++)
            {
                int ci = indices[i];
                outSpan[i] = new UtilityResultEntry
                {
                    CandidateHandle  = handles[ci],
                    Score            = scores[i],
                    WinningPostureId = 0
                };
            }
            for (int i = fillCount; i < Fdp.Toolkit.Utility.UtilityConstants.TopN; i++)
                outSpan[i] = default;

            output.Count          = fillCount;
            output.RunnerUpMargin = (fillCount >= 2) ? (scores[0] - scores[1]) : 0f;

            if (tracePtr != null && fillCount > 0)
            {
                tracePtr->WriteWinnerRecord(tick,
                    winnerOptionId:      0,
                    winnerDefinitionIdx: 0,
                    winnerScore:         scores[0],
                    runnerUpMargin:      output.RunnerUpMargin);
            }
        }
        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Scores every option in <paramref name="def"/>, ranks them descending, and writes the
        /// result into <paramref name="output"/>.
        /// </summary>
        /// <param name="repo">Active ECS repository.</param>
        /// <param name="self">Entity being evaluated.</param>
        /// <param name="def">Decision definition (authored options and considerations).</param>
        /// <param name="context">Context entity (target, candidate, or Entity.Null for none).</param>
        /// <param name="output">Buffer that receives the sorted results.</param>
        /// <param name="trace">Optional trace buffer; pass <c>null</c> to skip recording.</param>
        /// <param name="tick">Current simulation tick for trace timestamps.</param>
        public static void Evaluate(
            EntityRepository repo,
            Entity self,
            in UtilityDecisionDef def,
            Entity context,
            ref UtilityResultBuffer output,
            UtilityTraceWorkingMemory1024* trace,
            ushort tick = 0)
        {
            int optionCount = def.Options.Length;
            if (optionCount == 0)
            {
                output.Count          = 0;
                output.RunnerUpMargin = 0f;
                return;
            }

            // Stack-allocate parallel score/index arrays (optionCount <= TopN = 16).
            float* scores  = stackalloc float[optionCount];
            int*   indices = stackalloc int[optionCount];

            for (int i = 0; i < optionCount; i++)
            {
                indices[i] = i;
                scores[i]  = EvaluateOption(repo, self, context, def.Options[i], trace, tick, (byte)i);
            }

            // Insertion sort: descending score, ties broken by lower OptionId.
            InsertionSort(scores, indices, optionCount);

            // Write sorted results into output buffer.
            Span<UtilityResultEntry> outSpan = output.GetSpanRW();
            int fillCount = optionCount < UtilityConstants.TopN ? optionCount : UtilityConstants.TopN;
            for (int i = 0; i < fillCount; i++)
            {
                int optIdx = indices[i];
                outSpan[i] = new UtilityResultEntry
                {
                    Score             = scores[i],
                    CandidateHandle   = 0L,
                    WinningPostureId  = (byte)def.Options[optIdx].OptionId
                };
            }
            // Zero out any remaining slots.
            for (int i = fillCount; i < UtilityConstants.TopN; i++)
                outSpan[i] = default;

            output.Count          = fillCount;
            output.RunnerUpMargin = (fillCount >= 2) ? (scores[0] - scores[1]) : 0f;

            // Winner summary trace record.
            if (trace != null && fillCount > 0)
            {
                trace->WriteWinnerRecord(tick,
                    winnerOptionId:      (byte)def.Options[indices[0]].OptionId,
                    winnerDefinitionIdx: (byte)indices[0],
                    winnerScore:         scores[0],
                    runnerUpMargin:      output.RunnerUpMargin);
            }
        }

        /// <summary>
        /// Evaluates the decision and then applies a hysteresis bonus to the currently active
        /// posture before re-ranking. Returns the <c>OptionId</c> of the winning posture.
        /// </summary>
        /// <param name="repo">Active ECS repository.</param>
        /// <param name="self">Entity being evaluated.</param>
        /// <param name="def">Decision definition (must be <see cref="DecisionKind.PostureSelect"/>).</param>
        /// <param name="activePostureId">OptionId of the currently executing posture.</param>
        /// <param name="hysteresisBonus">Bonus added to the active posture score after raw scoring.</param>
        /// <param name="output">Buffer that receives the sorted results (after hysteresis).</param>
        /// <param name="trace">Optional trace buffer.</param>
        /// <param name="tick">Current simulation tick.</param>
        /// <returns>The winning posture's <c>OptionId</c>.</returns>
        public static byte SelectPosture(
            EntityRepository repo,
            Entity self,
            in UtilityDecisionDef def,
            byte activePostureId,
            float hysteresisBonus,
            ref UtilityResultBuffer output,
            UtilityTraceWorkingMemory1024* trace,
            ushort tick = 0)
        {
            // Step 1: score all options normally (no bias).
            Evaluate(repo, self, in def, default, ref output, trace, tick);

            if (output.Count == 0)
                return 0;

            int optionCount = def.Options.Length;

            // Step 2: copy scores from output to a mutable stack buffer for re-ranking.
            float* scores  = stackalloc float[optionCount];
            int*   indices = stackalloc int[optionCount];

            ReadOnlySpan<UtilityResultEntry> outRO = output.GetSpanRO();
            for (int i = 0; i < output.Count; i++)
            {
                indices[i] = i;
                scores[i]  = outRO[i].Score;
            }
            // Zero out unused slots.
            for (int i = output.Count; i < optionCount; i++)
            {
                indices[i] = i;
                scores[i]  = 0f;
            }

            // Step 3: apply hysteresis bonus to the active posture (post-scoring).
            for (int i = 0; i < output.Count; i++)
            {
                if (outRO[i].WinningPostureId == activePostureId)
                {
                    scores[i] += hysteresisBonus;
                    break;
                }
            }

            // Step 4: re-sort with the bonus applied.
            InsertionSort(scores, indices, output.Count);

            // Step 5: rewrite the output buffer in the new order.
            // Build a temporary snapshot to avoid reading half-overwritten data.
            UtilityResultEntry* snap = stackalloc UtilityResultEntry[UtilityConstants.TopN];
            for (int i = 0; i < output.Count; i++)
                snap[i] = outRO[indices[i]];

            Span<UtilityResultEntry> outSpan = output.GetSpanRW();
            for (int i = 0; i < output.Count; i++)
            {
                var e = snap[i];
                e.Score  = scores[i];
                outSpan[i] = e;
            }

            output.RunnerUpMargin = (output.Count >= 2) ? (scores[0] - scores[1]) : 0f;

            return outSpan[0].WinningPostureId;
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        private static float EvaluateOption(
            EntityRepository repo,
            Entity self,
            Entity context,
            UtilityOption opt,
            UtilityTraceWorkingMemory1024* trace,
            ushort tick,
            byte optionIndex)
        {
            int consCount = opt.Considerations.Length;
            if (consCount == 0)
                return 0f;

            float* curveOutputs = stackalloc float[consCount];
            float* weights      = stackalloc float[consCount];

            float runningAgg = (opt.Mode == ScoringMode.WeightedProduct) ? 1f : 0f;

            for (int c = 0; c < consCount; c++)
            {
                ref readonly var cons = ref opt.Considerations[c];

                // Build context for the reader.
                var inputCtx = new UtilityInputCtx
                {
                    Repo    = repo,
                    Self    = self,
                    Context = context,
                    Params  = cons.Params
                };

                // Invoke the registered reader; default to 0 if not registered.
                float raw = 0f;
                if (UtilityInputReaderStore.TryGet(cons.InputId, out var reader) && reader != null)
                    raw = reader(in inputCtx);

                float norm     = raw;  // Phase 1: normalisation is identity
                float curveOut = cons.Curve.Evaluate(norm);

                curveOutputs[c] = curveOut;
                weights[c]      = cons.Weight;

                // Update running aggregate for trace (approximation only — Aggregator sees all at once).
                if (opt.Mode == ScoringMode.WeightedProduct)
                    runningAgg *= curveOut;
                else
                    runningAgg += cons.Weight * curveOut;

                if (trace != null)
                {
                    trace->WriteConsiderationRecord(tick, optionIndex, cons.InputId,
                        raw, norm, curveOut, cons.Weight, runningAgg);
                }
            }

            return Aggregator.Aggregate(
                new System.ReadOnlySpan<float>(curveOutputs, consCount),
                new System.ReadOnlySpan<float>(weights, consCount),
                opt.Mode);
        }

        /// <summary>
        /// In-place insertion sort: descending by score; ties broken by ascending original index
        /// (which corresponds to lower OptionId for options in definition order).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InsertionSort(float* scores, int* indices, int count)
        {
            for (int i = 1; i < count; i++)
            {
                float keyScore = scores[i];
                int   keyIdx   = indices[i];
                int   j        = i - 1;
                // Swap when current score is strictly greater, or equal with smaller index.
                while (j >= 0 && (scores[j] < keyScore ||
                       (scores[j] == keyScore && indices[j] > keyIdx)))
                {
                    scores[j + 1]  = scores[j];
                    indices[j + 1] = indices[j];
                    j--;
                }
                scores[j + 1]  = keyScore;
                indices[j + 1] = keyIdx;
            }
        }
    }
}
