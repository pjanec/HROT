using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.Jobs;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ⭐ POC 2 Q-4 (docs/DESIGN_Parallel_Simulation.md §10–§11; the parallel-simulation experiment, branch <c>exp/parallel-sim-2</c>) — the
    /// brains' per-unit utility SCORING run in parallel, BEFORE the serial brain walk. 📐 Q-0's profile: 67 % of <c>BrainTickSystem</c> is the
    /// CombatPosture decision's <c>Evaluate</c> (step 1 of <see cref="UtilityScorer.SelectPosture(EntityRepository, Entity, in UtilityDecisionDef, byte, float, ref UtilityResultBuffer, UtilityTraceWorkingMemory1024*, ushort)"/>)
    /// — inputs that only READ the world. The tree walk stays serial and untouched; only its most expensive leaf is computed ahead.
    /// <list type="number">
    /// <item>The serial <see cref="UtilityScorer.ChooseOption"/> records each (unit, decision) it actually scored — next frame's DEMAND.</item>
    /// <item><see cref="Run"/>, at the start of the brain tick: last frame's demand, minus what the decision cadence will answer from its store,
    /// is scored in parallel batches (<see cref="ParallelEntities"/>) into a per-unit buffer stamped with this run.</item>
    /// <item>The serial <c>ChooseOption</c> takes a buffer stamped by THIS run (<see cref="TryTake"/>) and applies only the hysteresis.</item>
    /// </list>
    /// ⚠ The semantic change, and why it is Throughput-only (R-4207): a pre-scored decision sees the world as it stood at the START of the
    /// brain phase, not after the units walked before it this frame (their claims, assignments, reactions) — the same class of "a friend's
    /// write seen one tick late" as R-4208. Opt-in: <c>HROT_BRAIN_PRESCORE=1</c>, active only with <c>HROT_PARALLEL_SIM=1</c> in
    /// <see cref="SimulationMode.Throughput"/>. ⭐ 0 B per steady run (arrays grown, never shrunk).
    /// </summary>
    public sealed unsafe class UtilityPrescore
    {
        /// <summary>The opt-in switch (settable for tests).</summary>
        public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("HROT_BRAIN_PRESCORE") == "1";

        /// <summary>Pre-scoring is in force: enabled, the parallel path on, Throughput mode.</summary>
        public static bool Active => Enabled && ParallelEntities.Enabled && ParallelEntities.Mode == SimulationMode.Throughput;

        private static readonly ConditionalWeakTable<EntityRepository, UtilityPrescore> s_of = new();

        /// <summary>The pre-score state of <paramref name="world"/>.</summary>
        public static UtilityPrescore Of(EntityRepository world) => s_of.GetValue(world, static _ => new UtilityPrescore());

        private sealed class Store
        {
            public UtilityResultBuffer[] Buf = new UtilityResultBuffer[256];
            public int[] Run = new int[256];          // the run that filled Buf[i] (0 = never)
            public ushort[] Generation = new ushort[256];
            public void Grow(int index)
            {
                if (index < Buf.Length) return;
                int n = Math.Max(index + 1, Buf.Length * 2);
                Array.Resize(ref Buf, n); Array.Resize(ref Run, n); Array.Resize(ref Generation, n);
            }
        }

        private readonly Dictionary<int, Store> _stores = new();
        private Entity[] _demandE = new Entity[256], _jobE = new Entity[256];
        private int[] _demandD = new int[256];
        private UtilityDecisionDef?[] _jobDef = new UtilityDecisionDef?[256];
        private Store?[] _jobStore = new Store?[256];
        private int _demandN, _run;
        private UtilityScorer? _scorer;

        /// <summary>The (unit, decision) pairs scored at the last <see cref="Run"/> (a rail reads it).</summary>
        public int LastScored { get; private set; }
        /// <summary>The pre-scores the serial walk used since creation (a rail reads it).</summary>
        public long Taken { get; private set; }

        /// <summary>Records that <paramref name="self"/> scored <paramref name="decisionId"/> this frame (serial — the brain walk).</summary>
        internal void Demand(Entity self, int decisionId, UtilityScorer scorer)
        {
            _scorer = scorer;   // the scorer that asked (one per world in production: over the shared catalog)
            if (_demandN == _demandE.Length) { Array.Resize(ref _demandE, _demandN * 2); Array.Resize(ref _demandD, _demandN * 2); }
            _demandE[_demandN] = self; _demandD[_demandN] = decisionId; _demandN++;
        }

        /// <summary>A buffer pre-scored by the CURRENT run for <paramref name="self"/> and <paramref name="decisionId"/>, copied into
        /// <paramref name="output"/>; false when there is none (the caller scores fresh).</summary>
        internal bool TryTake(Entity self, int decisionId, ref UtilityResultBuffer output)
        {
            if (_run == 0 || !_stores.TryGetValue(decisionId, out var st)) return false;
            int i = self.Index;
            if (i < 0 || i >= st.Run.Length || st.Run[i] != _run || st.Generation[i] != self.Generation) return false;
            output = st.Buf[i];
            st.Run[i] = 0;   // once: a second call in the same frame scores fresh, as the serial path would
            Taken++;
            return true;
        }

        /// <summary>Pre-scores last frame's demand in parallel. Call at the start of the brain tick, on the main thread.</summary>
        public void Run(EntityRepository repo)
        {
            _run++;
            int n = _demandN;
            _demandN = 0;
            LastScored = 0;
            if (n == 0) return;
            var registry = _scorer!.Registry;
            if (_jobE.Length < n) { Array.Resize(ref _jobE, n); Array.Resize(ref _jobDef, n); Array.Resize(ref _jobStore, n); }
            int jobs = 0;
            for (int k = 0; k < n; k++)
            {
                var e = _demandE[k];
                int d = _demandD[k];
                if (!repo.IsAlive(e)) continue;
                if (!registry.TryGet(d, out var def, out _) || def == null) continue;   // ⭐ T-3: option AND candidate decisions (context-free)
                // the cadence answers it from its store this frame ⇒ no scoring will be asked for
                if (def.CadenceSeconds > 0 && DecisionCadence.Of(repo).Reusable(repo, e, def, d, out _) != null) continue;
                if (!_stores.TryGetValue(d, out var st)) _stores[d] = st = new Store();   // once per decision
                st.Grow(e.Index);
                _jobE[jobs] = e; _jobDef[jobs] = def; _jobStore[jobs] = st;
                jobs++;
            }
            LastScored = jobs;
            if (jobs == 0) return;
            ParallelEntities.Run(repo, _jobE, jobs, new ScoreJob { Owner = this, Repo = repo, RunId = _run });
        }

        private struct ScoreJob : IEntityBatchJob
        {
            public UtilityPrescore Owner;
            public EntityRepository Repo;
            public int RunId;

            public void Execute(Entity e, int index, BatchContext ctx)
            {
                var st = Owner._jobStore[index]!;
                var def = Owner._jobDef[index]!;
                int i = e.Index;
                st.Buf[i] = default;
                // step 1 only, no trace (a traced unit is scored fresh by the serial walk) — reads the world, writes this unit's slot
                ParallelGuard.ReadOnly = true;   // ⭐ Debug: scoring may take nothing writable
                try
                {
                    if (def.Kind == DecisionKind.PostureSelect) UtilityScorer.Evaluate(Repo, e, in def, default, ref st.Buf[i], null, 0, writeWinnerRecord: false);
                    else Owner._scorer!.EvaluateCandidatesForPrescore(Repo, e, in def, ref st.Buf[i]);   // ⭐ T-3 — a candidate ranking
                }
                finally { ParallelGuard.ReadOnly = false; }
                st.Generation[i] = e.Generation;
                st.Run[i] = RunId;
            }

            public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
        }
    }
}
