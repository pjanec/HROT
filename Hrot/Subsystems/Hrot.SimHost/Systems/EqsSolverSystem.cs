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
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class EqsSolverSystem : IEcsModuleSystem
    {
        // Query cached after first use.
        private EntityQuery? _sensorQuery;

        private readonly EqsSchedule _schedule = new();
        private readonly SensorMemoryStage _memory = new();
        private readonly List<Entity> _lastSchedule = new();

        // ⭐ CE-3056 — this tick's answers by what DECIDES them; a twin due later in the tick copies instead of solving.
        //   📄 docs/DESIGN_Sensors_And_Doctrine.md §5.6. Cleared every tick, so an answer is never older than its delivery.
        private readonly Dictionary<QueryShareKey, SharedAnswer> _shared = new();

        /// <summary>A shared answer: the first <c>Count</c> entries of a reused buffer (CE-3427 P-1).</summary>
        private readonly record struct SharedAnswer(EqsResult[] Buffer, int Count);

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

        /// <summary>⭐ CE-3128 — this solver thread's route planner (the same one the path solver runs; reused buffers, R-220).</summary>
        private readonly Fdp.Toolkit.Navigation.RoutePlanner _routePlanner = new();

        /// <summary>
        /// ⭐ CE-3128 (Nav v2 §5.2a D7) — the node's road graph carrier, read live (this system runs on a snapshot with no
        /// singletons): the danger sensor plans the unit's route and finds its crossings on it. Set by
        /// <c>EqsModule.ForTerrainHost</c>; null ⇒ the danger sensor answers "none" (a host with no terrain has no roads).
        /// </summary>
        public Func<CarKinem.Road.RoadNetworkHolder?>? RoadSource { get; set; }

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
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
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

            // ⭐ CE-3301 — the survey memo is keyed by (network id, child index), and a reload reuses both: its SolvedAt is the OLD
            //   timeline's, so every survey read "not due" (1 unit) until the new clock caught up — stale answers, and a solve budget
            //   left free that a fresh process spends on the surveys (hearing sensors solved 4–8 steps earlier on a reload: the red
            //   DeterminismRails.A_reload_rebuilds_the_same_authored_world). Forget it with the world (CE-3294's rule).
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _surveys.Clear();

            long tRun = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();   // ⭐ CE-3427 — the run's phases: where a run's time goes apart from the solves
            if (_lastRunStart != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(S_RunInterval, _lastRunStart);   // start to start: the real run rate
            if (_lastRunEnd != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(S_RunIdle, _lastRunEnd);           // end to start: what the host waits
            _lastRunStart = tRun;
            _currentView = view;
            _currentCmd  = view.GetCommandBuffer();
            _currentTick = Fdp.Toolkit.Perception.PerceptionClock.Now(view);   // ⭐ CE-3292 — the sim step, not the frame
            _currentRepo = repo;
            _memory.Begin(repo);
            _lastSchedule.Clear();
            _shared.Clear();
            _shareUsed = 0;
            LastSharedCopies = 0;
            LastParallelSolves = 0;
            LastDiscardedSolves = 0;

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

            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_RunSchedule, tRun);
            long tSolve = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            // ── run them in order within the budget ──
            // ⭐ Each band gets its share (Critical 50 %, Normal 35 %, Low the rest), unused slack rolling down, and the
            //   oldest sensor of every band always starts — so a busy Critical band never starves Low.
            // ⭐⭐ CE-3427 P-1 (§5.8) — with workers, a WINDOW of the places ahead is predicted from the estimates and its pure solves run in
            //   parallel; the START decision below is unchanged (actual costs, schedule order), so a misprediction costs time, never a result.
            var ordered = _schedule.Ordered();
            var workers = PoolFor(Workers);
            if (_slotOf.Length < ordered.Count) _slotOf = new int[Math.Max(ordered.Count, _slotOf.Length * 2)];
            int spent = 0, bandSpent = 0, bandBudget = 0, rank = -1, windowEnd = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (workers != null && i >= windowEnd) windowEnd = SolveWindow(workers, ordered, i, spent, bandSpent, bandBudget, rank);
                var item = ordered[i];
                if (item.Rank != rank)
                {
                    rank       = item.Rank;
                    bandSpent  = 0;
                    bandBudget = EqsSchedule.CumulativeShare(rank, BudgetUnits) - spent;
                }
                int s = workers != null ? _slotOf[i] : -1;
                if (!EqsSchedule.ShouldStart(bandSpent, item.Estimate, bandBudget))
                {
                    if (s >= 0) LastDiscardedSolves++;   // solved ahead, not started: discarded — it wrote nothing
                    continue;
                }
                int cost;
                if (s >= 0) { cost = CommitAndPersist(_slots[s]); LastParallelSolves++; }
                else cost = RunSensor(item.Sensor);
                spent     += cost;
                bandSpent += cost;
                _lastSchedule.Add(item.Sensor);
            }
            LastSpentUnits = spent;
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_RunSolve, tSolve);

            long tFlush = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            _memory.Flush(view, _currentCmd);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_RunFlush, tFlush);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Run, tRun);
            _lastRunEnd = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            Fdp.Toolkit.Diagnostics.QueryTimings.Add(S_RunSpent, spent);
            Fdp.Toolkit.Diagnostics.QueryTimings.Add(S_RunParallel, LastParallelSolves);
            Fdp.Toolkit.Diagnostics.QueryTimings.Add(S_RunDiscarded, LastDiscardedSolves);
        }

        // ⭐ CE-3427 — what the EQS costs: every sensor solve, per template, per generator and test type, and the two special solves
        private static readonly int S_Sensor = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.sensor (all)");
        private static readonly int S_Danger = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.DangerArea");
        private static readonly int S_Survey = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.WorldSurvey");
        private static readonly int S_Run         = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run (whole Execute)");
        private static readonly int S_RunSchedule = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.schedule (order every sensor)");
        private static readonly int S_RunSolve    = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.solve (the budgeted loop)");
        private static readonly int S_RunFlush    = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.flush (memory stage)");
        private static readonly int S_RunSpent    = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.units (count)");
        private static readonly int S_RunInterval = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.interval (start to start)");
        private static readonly int S_RunIdle     = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.idle (end to next start)");
        private static readonly int S_RunParallel  = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.parallel (count)");
        private static readonly int S_RunDiscarded = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("eqs.run.discarded (count)");
        private long _lastRunStart, _lastRunEnd;

        // ── CE-3427 P-1 — the window ────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ CE-3427 P-1 (§5.8) — threads that solve a sensor (the solver's own included): <c>HROT_EQS_WORKERS</c>, else a quarter of the
        /// logical CPUs (three nodes share a machine). <c>1</c> = no worker threads — every solve inline, in schedule order.
        /// </summary>
        public int Workers { get; set; } = DefaultWorkers;

        /// <summary>The default <see cref="Workers"/>.</summary>
        public static int DefaultWorkers { get; } =
            int.TryParse(Environment.GetEnvironmentVariable("HROT_EQS_WORKERS"), out int w) && w > 0 ? w : Math.Max(1, Environment.ProcessorCount / 4);

        /// <summary>⭐ CE-3427 P-1 — sensors solved on the window (any thread) and committed last tick (diagnostics / rails).</summary>
        public int LastParallelSolves { get; private set; }

        /// <summary>⭐ CE-3427 P-1 — sensors solved ahead last tick that did not start (wasted work, no trace).</summary>
        public int LastDiscardedSolves { get; private set; }

        private const int PlacesPerParticipant = 8;   // a window's size per thread: the tail of a band wastes at most this many solves each
        private SolveSlot[] _slots = Array.Empty<SolveSlot>();
        private int[] _slotOf = Array.Empty<int>();   // place in the order → its window slot, −1 = run inline
        private readonly SolveSlot _inline = new();
        private readonly HashSet<QueryShareKey> _windowKeys = new();
        private Action<int>? _solveAt;

        private EqsSolveWorkers? PoolFor(int workers)
        {
            if (workers <= 1) return null;
            _solveAt ??= k => Solve(_slots[k]);
            int places = workers * PlacesPerParticipant;
            if (_slots.Length != places)
            {
                var slots = new SolveSlot[places];
                for (int i = 0; i < places; i++) slots[i] = i < _slots.Length ? _slots[i] : new SolveSlot();
                _slots = slots;
            }
            return EqsSolveWorkers.Shared(workers);
        }

        // Predicts, from place `from`, which places the start rule would admit if every sensor cost its estimate; prepares those, solves
        // the PURE ones in parallel into the window's slots, and returns the end of the window. Everything here is side-effect free: the
        // commit loop decides with the actual costs.
        private int SolveWindow(EqsSolveWorkers pool, IReadOnlyList<(int Rank, uint LastSolved, int Index, Entity Sensor, int Estimate)> ordered,
                                int from, int spent, int bandSpent, int bandBudget, int rank)
        {
            int used = 0, j = from;
            _windowKeys.Clear();
            for (; j < ordered.Count && used < _slots.Length; j++)
            {
                _slotOf[j] = -1;
                var item = ordered[j];
                if (item.Rank != rank)
                {
                    rank       = item.Rank;
                    bandSpent  = 0;
                    bandBudget = EqsSchedule.CumulativeShare(rank, BudgetUnits) - spent;
                }
                if (!EqsSchedule.ShouldStart(bandSpent, item.Estimate, bandBudget)) continue;
                int guess = Math.Max(item.Estimate, EqsCost.Sensor);   // a never-solved sensor is not free
                spent     += guess;
                bandSpent += guess;

                var slot = _slots[used];
                Prepare(slot, item.Sensor);
                if (slot.Kind != SlotKind.Ranked || AwaitsRays(slot.Template)) continue;              // inline: special kinds, the ray test
                if (slot.Shareable && (_shared.ContainsKey(slot.ShareKey) || !_windowKeys.Add(slot.ShareKey))) continue;   // a twin copies at commit
                _slotOf[j] = used++;
            }
            if (!pool.TryRun(used, _solveAt!))
                for (int k = 0; k < used; k++) Solve(_slots[k]);   // another solver holds the pool: the same solves, on this thread
            return j;
        }

        // ── one sensor: prepare (read), solve (pure), commit (every write) ─────────────────────────────────────────────────────────

        private enum SlotKind : byte { NotKeyed, DangerArea, WorldSurvey, NoTemplate, AwaitingRays, Ranked }
        private enum SolveOutcome : byte { None, NotEvaluable, Empty, Answer, AwaitingRays }

        /// <summary>⭐ CE-3427 P-1 — one sensor's solve: what was read before it, and what it produced. Reused; written by one thread at a time.</summary>
        private sealed class SolveSlot
        {
            public Entity Sensor;
            public EqsSensor Data;
            public bool Had;
            public SensorEvalState Eval;
            public SlotKind Kind;
            public int ParentNetworkId, LocalChildIndex;
            public EqsQueryTemplate Template;
            public bool HashReset;
            public bool Shareable;
            public QueryShareKey ShareKey;
            public EqsResult[] Buffer = new EqsResult[64];
            public int Count, Cost;
            public SolveOutcome Outcome;
            public Fdp.Toolkit.Perception.Sensors.AcousticPerception.DeferredCursor Cursor;
        }

        // Evaluates one sensor inline and persists its state ONCE (schedule fields included). Returns the work units spent.
        private int RunSensor(Entity entity)
        {
            Prepare(_inline, entity);
            return CommitAndPersist(_inline);
        }

        // The commit, then the eval state written once — what RunSensor did before the split.
        private int CommitAndPersist(SolveSlot slot)
        {
            int cost = Commit(slot, out bool keyed);
            if (!keyed) return 0;   // not solvable here (no wire key): no state, no cost
            slot.Eval.LastSolvedTick = _currentTick;
            slot.Eval.LastCost       = cost;
            if (slot.Had) _currentCmd.SetComponent(slot.Sensor, slot.Eval);
            else          _currentCmd.AddComponent(slot.Sensor, slot.Eval);
            return cost;
        }

        // READS only (the solver's thread): the sensor, its eval state after an epoch or structure reset, its kind, its share key.
        private void Prepare(SolveSlot slot, Entity entity)
        {
            var repo = _currentRepo;
            ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(entity);
            slot.Sensor    = entity;
            slot.Data      = sensor;
            slot.Had       = repo.HasComponent<SensorEvalState>(entity);
            slot.Eval      = slot.Had ? repo.GetComponentRO<SensorEvalState>(entity)
                                      : new SensorEvalState { Phase = EqsEvalPhase.Idle, CurrentEpoch = sensor.Epoch };
            slot.Outcome   = SolveOutcome.None;
            slot.Cursor    = default;
            slot.HashReset = false;
            slot.Shareable = false;
            slot.Template  = default;

            // --- the wire key: the ONE rule (EqsSensorKey) ---
            // A child whose parent is gone or local-only is not solved; a purely local sensor (offline / editor) is keyed
            // (0, its entity index) — the local path EqsResultUpdateSystem matches.
            var kind = EqsSensorKey.Resolve(repo, entity, out slot.ParentNetworkId, out slot.LocalChildIndex, out _);
            if (kind == EqsSensorKeyKind.None) { slot.Kind = SlotKind.NotKeyed; return; }
            if (kind == EqsSensorKeyKind.LocalOnly) slot.LocalChildIndex = entity.Index;

            // Reset on epoch change (sensor parameters changed -> discard in-flight raycasts).
            // Preserve CurrentStructureHash so a soft reset does not trigger a spurious hard reset,
            // and preserve LastPublishedTopK so the ScoreDelta publish policy is not defeated after the new
            // epoch's first answer (a soft reset keeps publish-suppression state; PublishedThisEpoch resets, so
            // that first answer always goes out). The schedule fields are kept (they are the caller's).
            if (slot.Eval.CurrentEpoch != sensor.Epoch)
            {
                slot.Eval = new SensorEvalState
                {
                    Phase                = EqsEvalPhase.Idle,
                    CurrentEpoch         = sensor.Epoch,
                    CurrentStructureHash = slot.Eval.CurrentStructureHash,
                    LastPublishedTopK    = slot.Eval.LastPublishedTopK,
                    LastSolvedTick       = slot.Eval.LastSolvedTick,
                    LastCost             = slot.Eval.LastCost,
                };
            }

            // ⭐ CE-3072 B3 (R-213) — a danger-area sensor is not a ranked query: route + terrain → areas, on its own result path
            //   (DangerAreaResultEvent). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
            if (sensor.BlueprintId == Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.TemplateId) { slot.Kind = SlotKind.DangerArea; return; }

            // ⭐ CE-3249 (R-2037, WS-1) — a WorldSurvey sensor is not a ranked query either: geometry asked of this node's live world,
            //   answered on its own result path (WorldSurveyResultEvent). 📄 docs/DESIGN_World_Survey_Sensor.md.
            if (sensor.BlueprintId == Fdp.Toolkit.Perception.Survey.WorldSurveyChildSensor.TemplateId) { slot.Kind = SlotKind.WorldSurvey; return; }

            // Try to look up the template from the registry singleton.
            IEqsTemplateRegistry? registry = repo.HasSingletonManaged<IEqsTemplateRegistry>()
                ? repo.GetSingletonManaged<IEqsTemplateRegistry>()
                : null;
            if (registry == null || !registry.TryGetTemplate(sensor.BlueprintId, out var template))
            {
                slot.Kind = SlotKind.NoTemplate;   // Phase 1 stub fallback (empty result)
                return;
            }
            slot.Template = template;

            // Hard-reset: detect structural hot-reload by comparing template's StructureHash
            // against what the SensorEvalState recorded on last evaluation. (The cognitive buffer's reset is a write: Commit.)
            ulong liveHash = template.ComputeStructureHash();
            if (liveHash != 0 && slot.Eval.CurrentStructureHash != liveHash)
            {
                slot.HashReset                   = true;
                slot.Eval.Phase                  = EqsEvalPhase.Idle;
                slot.Eval.PendingRaycastCount    = 0;
                slot.Eval.CurrentStructureHash   = liveHash;
            }

            // Phase skip-guard: avoid re-generating candidates on the same tick that raycasts were
            // submitted, and reset to Idle on subsequent ticks so the next EQS cycle picks up the
            // ring-buffer results.  A pure early-return (no reset) would strand the sensor in
            // _AwaitingRaycasts indefinitely because nothing else resets the phase.
            if (slot.Eval.Phase == EqsEvalPhase._AwaitingRaycasts) { slot.Kind = SlotKind.AwaitingRays; return; }

            slot.Kind = SlotKind.Ranked;
            slot.Shareable = TryShareKey(entity, in sensor, out slot.ShareKey);
            if (slot.Buffer.Length < template.MaxCandidates) slot.Buffer = new EqsResult[Math.Max(template.MaxCandidates, slot.Buffer.Length * 2)];
        }

        // ⭐ CE-3427 P-1 — a RANKED sensor's solve: PURE (reads the snapshot, writes only the slot), so it may run on any thread and be
        //   discarded. Generate → filter → score → rank; the hearing cursor is held (AcousticPerception.BeginDeferredCursor).
        private void Solve(SolveSlot slot)
        {
            ref readonly var template = ref slot.Template;
            var entity = slot.Sensor;
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();   // ⭐ CE-3427 — one sensor's whole solve
            long b0 = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();
            var templateGen = template.Generator;
            int templateSlot = Fdp.Toolkit.Diagnostics.QueryTimings.SlotFor("eqs.template", template.BlueprintId, templateGen);
            Solve(slot, in template, entity);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Sensor, t0, b0);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(templateSlot, t0, b0);   // a ranked template's solve, by template
        }

        private void Solve(SolveSlot slot, in EqsQueryTemplate template, Entity entity)
        {
            // 1. Generation.
            var candidates = slot.Buffer.AsSpan(0, template.MaxCandidates);
            long tg = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            long bg = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();
            Fdp.Toolkit.Perception.Sensors.AcousticPerception.BeginDeferredCursor();
            int count;
            try { count = template.Generator.Generate(entity, ref slot.Data, _currentRepo, candidates); }
            finally { slot.Cursor = Fdp.Toolkit.Perception.Sensors.AcousticPerception.EndDeferredCursor(); }
            Fdp.Toolkit.Diagnostics.QueryTimings.End(Fdp.Toolkit.Diagnostics.QueryTimings.SlotFor("eqs.gen", template.Generator.GetType()), tg, bg);
            if (count < 0)
            {
                // Not evaluable yet (IEqsGenerator contract): publish NOTHING, so the reader keeps
                // waiting rather than reading an empty result as "nothing there".
                slot.Outcome = SolveOutcome.NotEvaluable;
                slot.Cost    = 0;
                return;
            }
            int cost = EqsCost.Sensor + (count * EqsCost.Candidate);
            if (count == 0)
            {
                // Nothing generated: still publish an empty event so Brain's IsReady ticks.
                slot.Outcome = SolveOutcome.Empty;
                slot.Cost    = cost;
                return;
            }

            var activeCandidates = candidates.Slice(0, count);

            // 2–3. FilterCheap, then FilterExpensive — ⭐ CE-3143: the shared pipeline (EqsFilters.Run), so the map's EQS verdict runs the same.
            cost += EqsFilters.Run(in template, entity, ref slot.Data, _currentView, activeCandidates);

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
                    long tt = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
                    long bt = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();
                    test.ExecuteBatch(entity, ref slot.Data, _currentView, activeCandidates);
                    Fdp.Toolkit.Diagnostics.QueryTimings.End(Fdp.Toolkit.Diagnostics.QueryTimings.SlotFor("eqs.test", test.GetType()), tt, bt);
                    cost += EqsCost.Charged(test, activeCandidates.Length);   // ⭐ CE-3279 (c) — after the batch
                }

            // 6. Top-K reduction: now that cheap scores exist, rank by Score and truncate to
            //    MaxTopK so the expensive phase (raycasts, path cost) runs only on the viable
            //    survivors (Design §5.2 reduce-between-phases / §9.3 two-pass strategy).
            activeCandidates = TruncateTopK(activeCandidates, EqsResultPool.MaxTopK);

            // 7. ScoreExpensive.
            if (template.ScoreExpensive != null)
                foreach (var test in template.ScoreExpensive)
                {
                    long tt = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
                    long bt = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();
                    test.ExecuteBatch(entity, ref slot.Data, _currentView, activeCandidates);
                    Fdp.Toolkit.Diagnostics.QueryTimings.End(Fdp.Toolkit.Diagnostics.QueryTimings.SlotFor("eqs.test", test.GetType()), tt, bt);
                    cost += EqsCost.Charged(test, activeCandidates.Length);   // ⭐ CE-3279 (c) — after the batch
                }
            slot.Cost = cost;

            // Check if any candidate has FlagPendingRay set.
            // If so, yield without writing to pool — wait for ring buffer results.
            // ⭐ CE-4039 — only on a template that RUNS the accurate line-of-sight test: a flag bit means something only to the test that
            //   writes it. 📌 The acoustic answer carries the sound's family in bits 13–15; a rifle (6) set bit 15 and every hearing solve
            //   with a rifle in it waited for raycasts that were never asked — a Regular ear was deaf to small arms.
            bool awaitsRays = AwaitsRays(template);
            for (int i = 0; awaitsRays && i < activeCandidates.Length; i++)
            {
                if ((activeCandidates[i].Flags & AccurateLineOfSightTest.FlagPendingRay) != 0)
                {
                    slot.Outcome = SolveOutcome.AwaitingRays;   // DO NOT publish EqsResultEvent while awaiting raycasts.
                    return;
                }
            }

            // 9. Final sort descending by Score.
            MemoryExtensions.Sort(activeCandidates, (a, b) => b.Score.CompareTo(a.Score));
            slot.Count   = activeCandidates.Length;   // CompactRejected / TruncateTopK keep the answer at the buffer's start
            slot.Outcome = SolveOutcome.Answer;
        }

        // EVERY write of one sensor, on the solver's thread, in schedule order: what EvaluateSensor did after the solve. Returns the work units.
        private int Commit(SolveSlot slot, out bool keyed)
        {
            keyed = slot.Kind != SlotKind.NotKeyed;
            var entity = slot.Sensor;
            switch (slot.Kind)
            {
                case SlotKind.NotKeyed:
                    return 0;
                case SlotKind.DangerArea:
                {
                    long td = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
                    SolveDangerArea(entity, slot.ParentNetworkId, slot.LocalChildIndex, in slot.Data);
                    Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Danger, td);
                    slot.Eval.Phase = EqsEvalPhase.Idle;
                    return EqsCost.Sensor + EqsCost.Path;
                }
                case SlotKind.WorldSurvey:
                {
                    long ts = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
                    int surveyCost = SolveWorldSurvey(entity, slot.ParentNetworkId, slot.LocalChildIndex, in slot.Data);
                    Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Survey, ts);
                    slot.Eval.Phase = EqsEvalPhase.Idle;
                    return surveyCost;
                }
                case SlotKind.NoTemplate:
                    PublishEmpty(entity, slot.ParentNetworkId, slot.LocalChildIndex, slot.Data.Epoch);
                    return 0;
            }

            if (slot.HashReset && _currentRepo.HasComponent<EqsCognitiveBuffer>(entity))
            {
                ref var buffer = ref _currentRepo.GetComponentRW<EqsCognitiveBuffer>(entity);
                buffer.LastUpdateTick = 0;
            }

            if (slot.Kind == SlotKind.AwaitingRays)
            {
                // Same tick as submission: nothing to do. Subsequent tick: back to Idle so the full pipeline re-runs and
                // reads the ring-buffer results that arrived since the submission.
                if (slot.Eval.AwaitingSinceTick != _currentTick) slot.Eval.Phase = EqsEvalPhase.Idle;
                return 0;
            }

            // ⭐ CE-3056 — a twin of a query already answered THIS tick copies that answer, then goes through its OWN publish
            //   policy and epoch (§5.6). Only a completed answer is ever stored, so a copy is never a half-solved one.
            if (slot.Shareable && _shared.TryGetValue(slot.ShareKey, out var sharedAnswer))
            {
                slot.Eval.Phase = EqsEvalPhase.Idle;
                LastSharedCopies++;
                if (sharedAnswer.Count == 0) PublishEmpty(entity, slot.ParentNetworkId, slot.LocalChildIndex, slot.Data.Epoch);
                else WriteResultsToPoolAndPublish(slot.ParentNetworkId, slot.LocalChildIndex, slot.Data.Epoch, ref slot.Eval, in slot.Data,
                                                  sharedAnswer.Buffer.AsSpan(0, sharedAnswer.Count));
                return EqsCost.Cheap;
            }

            if (slot.Outcome == SolveOutcome.None) Solve(slot);   // not solved ahead: solve it here, as before the split
            Fdp.Toolkit.Perception.Sensors.AcousticPerception.WriteCursor(_currentCmd, in slot.Cursor);   // ⭐ the hearing cursor, at commit

            switch (slot.Outcome)
            {
                case SolveOutcome.NotEvaluable:
                    return 0;
                case SolveOutcome.Empty:
                    if (slot.Shareable) _shared[slot.ShareKey] = new SharedAnswer(Array.Empty<EqsResult>(), 0);
                    PublishEmpty(entity, slot.ParentNetworkId, slot.LocalChildIndex, slot.Data.Epoch);
                    return slot.Cost;
                case SolveOutcome.AwaitingRays:
                    slot.Eval.Phase             = EqsEvalPhase._AwaitingRaycasts;
                    slot.Eval.AwaitingSinceTick = _currentTick;
                    return slot.Cost;
            }

            // All raycasts resolved (or no AccurateLOS test in template): the answer.
            slot.Eval.Phase = EqsEvalPhase.Idle;
            var answer = slot.Buffer.AsSpan(0, slot.Count);

            // ⭐ CE-3037 — the memory stage sees every answer, published or suppressed (a ScoreDelta that did not move is
            //   still a sighting).
            _memory.Observe(_currentView, entity, _currentTick, answer);

            if (slot.Shareable) _shared[slot.ShareKey] = new SharedAnswer(ShareCopy(answer), answer.Length);

            // 10. Write to pool and publish (updates LastPublishedTopK / PublishedThisEpoch).
            WriteResultsToPoolAndPublish(slot.ParentNetworkId, slot.LocalChildIndex, slot.Data.Epoch, ref slot.Eval, in slot.Data, answer);
            return slot.Cost;
        }

        // ⭐ CE-3427 P-1 — this tick's shared answers live in buffers reused tick after tick (the memo used to allocate one per answer).
        private readonly List<EqsResult[]> _shareBuffers = new();
        private int _shareUsed;

        private EqsResult[] ShareCopy(ReadOnlySpan<EqsResult> answer)
        {
            if (_shareUsed == _shareBuffers.Count) _shareBuffers.Add(new EqsResult[Math.Max(answer.Length, 16)]);
            var buffer = _shareBuffers[_shareUsed];
            if (buffer.Length < answer.Length) _shareBuffers[_shareUsed] = buffer = new EqsResult[answer.Length];
            _shareUsed++;
            answer.CopyTo(buffer);
            return buffer;
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
        private void SolveDangerArea(Entity entity, int parentNetworkId, int localChildIndex, in EqsSensor sensor)
        {
            var areas = new Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor[8];
            int count = Fdp.Toolkit.Squad.DangerArea.DangerAlongRouteSolve.Solve(_currentRepo, entity, in sensor, areas,
                _routePlanner, RoadSource?.Invoke());
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

        // ⭐ CE-3249 (WS-7) — per survey: when it was last solved and published, and what it said (node-local; the epoch restarts it).
        private readonly record struct SurveyMemo(uint Epoch, double SolvedAt, double PublishedAt, ulong Hash);
        private readonly Dictionary<(int, int), SurveyMemo> _surveys = new();
        private int _worldEpoch;   // ⭐ CE-3301 — the world _surveys describes
        private readonly List<Fdp.Toolkit.World.TraceCrossing> _surveyScratch = new();

        /// <summary>⭐ CE-3249 — the survey heartbeat: an unchanged answer is still published this often (s), so its age is measurable (WS-8).</summary>
        public const double SurveyHeartbeatSeconds = 1.0;

        // Solves a survey when it is DUE (its RefreshSeconds, or a new epoch); publishes when the content changed, on a new epoch, or on the
        // heartbeat. Returns the work units spent (1 when not due).
        private int SolveWorldSurvey(Entity entity, int parentNetworkId, int localChildIndex, in EqsSensor sensor)
        {
            var repo = _currentRepo;
            if (!repo.TryGetTable(typeof(Fdp.Toolkit.Perception.Survey.WorldSurveyRequest), out _)
                || !_currentView.HasManagedComponent<Fdp.Toolkit.Perception.Survey.WorldSurveyRequest>(entity))
                return 0;   // the request has not arrived yet
            var request = _currentView.GetManagedComponentRO<Fdp.Toolkit.Perception.Survey.WorldSurveyRequest>(entity);
            double now = _currentView.Time;
            var key = (parentNetworkId, localChildIndex);
            bool known = _surveys.TryGetValue(key, out var memo);
            bool newEpoch = !known || memo.Epoch != sensor.Epoch;
            if (!newEpoch && now - memo.SolvedAt < Math.Max(0.1, request.RefreshSeconds)) return 1;

            var payload = Fdp.Toolkit.Perception.Survey.WorldSurveySolve.Solve(_currentView, request, _surveyScratch);
            string json = Fdp.Toolkit.Perception.Survey.WorldSurveyRequestJson.EncodeAnswer(payload);
            ulong hash = Fdp.Toolkit.Perception.Survey.WorldSurveyRequestJson.Hash(json);
            bool publish = newEpoch || hash != memo.Hash || now - memo.PublishedAt >= SurveyHeartbeatSeconds;
            _surveys[key] = new SurveyMemo(sensor.Epoch, now, publish ? now : memo.PublishedAt, hash);
            if (publish)
            {
                var answer = new Fdp.Toolkit.Perception.Survey.WorldSurveyResultEvent
                {
                    ParentNetworkId = parentNetworkId,
                    LocalChildIndex = localChildIndex,
                    Epoch           = sensor.Epoch,
                    RefreshTick     = _currentTick,
                    PayloadJson     = json,
                    ContentHash     = hash,
                };
                // ⛔ never _currentRepo.Bus — this runs on a snapshot (see SolveDangerArea)
                if (_currentCmd is EntityCommandBuffer ecb) ecb.PublishManagedEvent(answer);
                else _currentRepo.Bus.PublishManaged(answer);
            }
            return Fdp.Toolkit.Perception.Survey.WorldSurveySolve.CostOf(request);
        }

        private void PublishEmpty(Entity entity, int parentNetworkId, int localChildIndex, uint epoch)
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

        // ⭐ CE-4039 — does the template run the test that writes FlagPendingRay? Only then is bit 15 a "wait for raycasts".
        private static bool AwaitsRays(EqsQueryTemplate template)
        {
            if (template.ScoreExpensive == null) return false;
            foreach (var test in template.ScoreExpensive)
                if (test is AccurateLineOfSightTest) return true;
            return false;
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
            int parentNetworkId,
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
