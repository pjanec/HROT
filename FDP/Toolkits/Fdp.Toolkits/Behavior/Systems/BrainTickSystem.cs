using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Jobs;
using Fdp.ModuleHost.Abstractions;
using Fbt;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Behavior.Runners;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>O7c</c>-④b — THE ONE SYSTEM THAT STEPS AN ENTITY'S ROOT BRAIN.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.14 *(the design)* · §31.16 *(the as-built)*.
    ///
    /// <para>🔒 <b>User, <c>2026-09-23</c>:</b> <i>"But there will likely be no hsm tick system, will it?
    /// Cant we merge all the occurence traversal and ticking into a single system?"</i></para>
    ///
    /// <para>⛔⛔ <b>It is stronger than "likely": <c>HsmTickSystem&lt;T&gt;</c> COULD NOT SURVIVE.</b> It
    /// was generic over the ECS component that wrapped the instance, so retiring <c>BrainHsm128</c>
    /// leaves it with no <c>T</c>. ⇒ it becomes either a non-generic twin of <c>BTreeTickSystem</c> or
    /// it merges — and two near-identical non-generic systems is the duplication <c>B3</c> already paid
    /// to remove once.</para>
    ///
    /// <para>⭐⭐ <b>TWO ARMS, ONE BODY — and the split is MEASURED, not assumed.</b> §31.14.1 probed
    /// ten structural elements: the terminal-event dedup dictionary, the lifecycle pruning, the
    /// <c>BrainTier</c> discriminator, the registry lookup, the trace-buffer resolution, the
    /// <c>BehaviorFinishedEvent</c> publish and the authority gate are shared by BOTH paradigms and
    /// are the BODY. ⭐ The arms differ in exactly three things: <b>where the state comes from</b>,
    /// <b>which kernel steps it</b>, and <b>how terminality is read</b>.</para>
    ///
    /// <para>⭐⭐⭐ <b>S4 (<c>DESIGN_Unified_Behaviour_Run</c> U-2) — the arms are now RUNNERS.</b> Each tier's kernel, brain
    /// lookup, pause, trace and quirks (HSM's MobilityLost interrupt and hosted children) live in its
    /// <see cref="Runners.IBehaviorRunner"/>; this system keeps ONE arm (<c>Run</c>) for what every tier shares — the
    /// finished guard, the block and its reload restart, the finish and the fault. ⚠ The paragraph above is the O7c
    /// history of how two systems became one.</para>
    ///
    /// <para>⛔ <b><c>BlueprintTickSystem</c> is deliberately NOT merged in</b>, on four measured
    /// grounds (§31.14.2): it is constructed by two roots outside <c>CognitiveRuntimeModule</c>, it
    /// ticks world singletons that belong to no entity, it iterates EVERY slot by kind where the brain
    /// roots look up ONE slot by a computed key, and it carries no authority gate. ⚠ Merging it would
    /// be a second, much weaker argument wearing the first one's clothes.</para>
    ///
    /// <para>Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/> so stale channels are
    /// cleared before a brain writes new actions. The brain order
    /// (arbitration → interrupt → <b>tick</b> → cleanup → pulse) is preserved exactly; this merge
    /// removes a NODE from that chain and never reorders it.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in CognitiveRuntimeModule.
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public unsafe class BrainTickSystem : IEcsModuleSystem, IProfiledSystem
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>
        /// ⭐⭐ One cached query per tier, built on the first tick. 📄 §31.7. ⚠ Index-aligned with
        /// <c>BlueprintTierTable.Ascending</c>; an entry is <c>null</c> where that tier is not
        /// registered on this world, so the walk must skip nulls.
        /// </summary>
        private EntityQuery?[]? _tierQueries;

        /// <summary>
        /// Tracks the <see cref="BehaviorState.InstanceId"/> for which a terminal
        /// <see cref="BehaviorFinishedEvent"/> was last published, keyed by entity index.
        /// ⭐ ONE dictionary for both paradigms, which is correct by construction: an entity has ONE
        /// brain, selected by <c>BehaviorState.BrainTier</c>.
        /// </summary>
        private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();
        private int _worldEpoch;   // ⭐ CE-3076 — the dedup records belong to ONE world (WorldEpoch, CE-2101)

        // Reusable buffers for the stale-entry sweep; avoids per-frame heap allocation.
        private readonly HashSet<int> _seenThisFrame = new();
        private readonly List<int>    _staleKeys     = new();

        /// <summary>
        /// Number of entity indices currently tracked for <see cref="BehaviorFinishedEvent"/>
        /// deduplication. Exposed for test verification only; should drop to zero after a
        /// <c>DestructionOrder</c> for the entity is processed.
        /// </summary>
        internal int TrackedEntityCount => _publishedTerminalForInstanceId.Count;

        public string ProfileName => nameof(BrainTickSystem);

        /// <summary>
        /// ⭐⭐⭐ <b><c>P3</c> step <c>3b</c> — the EXECUTION gate.</b> When true, this system processes only
        /// entities whose cognitive state THIS node owns. 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c>
        /// §3.5.
        ///
        /// <para>⛔⛔ <b>Authority gates REPLICATION, not EXECUTION</b> — every egress translator checks
        /// <c>HasAuthority</c>, but a query does not. ⇒ without this flag, declining the brain state stops
        /// a node PUBLISHING the brain and does not stop it RUNNING one, and two nodes tick the same
        /// tree. ⭐ That is what would have made the whole design cosmetic.</para>
        ///
        /// <para>⚠ <b>Defaults to <c>false</c>, and that is load-bearing rather than cautious.</b> A
        /// promoted ghost owns nothing until a role policy or an explicit grant says otherwise, so turning
        /// this on before the node has a policy would stop it processing every entity it did not create —
        /// reproducing <c>CE-256</c> while fixing it. ⇒ the host turns it on in the same breath as handing
        /// over the policy (step 4).</para>
        ///
        /// <para>⭐ <b>ONE gate now covers both paradigms</b>, which is what the two systems had to keep
        /// in step by hand: <c>BrainTier</c> selects the arm, so gating the walk gates the whole brain.</para>
        /// </summary>
        private readonly bool _gateOnAuthority;

        /// <summary>
        /// ⚠⚠ CE-4433 P-4 — a MEASUREMENT EXPERIMENT, never shipped on: <c>HROT_SOLDIER_CADENCE=&lt;seconds&gt;</c> gives every brain no cadence POLICY owns
        /// (soldiers, vehicles — the rules have theirs) the <see cref="CadenceMode.Periodic"/> cadence at that period, staggered, woken by a
        /// <c>SensorChangedEvent</c> (the SOP slot's wake). ⚠ Its tick still receives ONE frame's dt (<c>Run</c> passes the frame's), so a
        /// dt-integrating timer inside a brain runs slow — measured, not fixed (docs/DESIGN_Evaluation_Cost_And_Cadence.md §3 D6). 0 = off.
        /// </summary>
        public static float ExperimentalSoldierCadence =
            float.TryParse(Environment.GetEnvironmentVariable("HROT_SOLDIER_CADENCE"), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var cadenceSeconds) && cadenceSeconds > 0f ? cadenceSeconds : 0f;

        public BrainTickSystem(BehaviorRegistry registry, bool gateOnAuthority = false,
            Fdp.Toolkit.Blueprints.Systems.IReloadLogSink? reloadLog = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _gateOnAuthority = gateOnAuthority;
            // ⚠ Optional exactly as BlueprintTickSystem's is — no production host constructs a sink for either tier today.
            _reloadLog = reloadLog ?? Fdp.Toolkit.Blueprints.Systems.NullReloadLogSink.Instance;
        }

        private readonly Fdp.Toolkit.Blueprints.Systems.IReloadLogSink _reloadLog;

        public void Execute(ISimulationView view, float deltaTime)
        {
            long tExecute = Fdp.Toolkit.Diagnostics.QueryTimings.Begin(), bExecute = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();          // ⭐ CE-4395 B-1 — the whole tick, on the slots' basis
            try { ExecuteCore(view, deltaTime); }
            finally { if (tExecute != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Execute, tExecute, bExecute); }
        }

        private void ExecuteCore(ISimulationView view, float deltaTime)
        {
            // ⭐ CE-3076 (CE-2101's rule, DESIGN_Cluster_Load_Phase §8) — both dedup records are keyed by entity INDEX, which the
            //   world boundary REUSES, and InstanceId restarts at 1 per entity: a new unit landing on the index of a last-world
            //   unit that finished its first run would never tick (Run returns at the "already finished" guard). ⚠ The stale
            //   sweep does not cover it — it keeps every index it sees, and the new entity is seen.
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch))
            {
                _publishedTerminalForInstanceId.Clear();
                _blueprintLayout.Clear();
                ReleaseAllCadenceReaders();   // ⭐ CE-2382 — a new world: no slot of the old one is owed events
            }

            // ⭐ CE-3293 S5 (R-3036 rule 8) — no hand pause gate: [TickMode(WhenTimeAdvances)] keeps this system off a frame that does not advance

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BrainTickSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // ⭐⭐⭐ DISCOVERY IS THE TIER WALK — the shape BlueprintTickSystem has had since B3, and
            //   which BTree adopted in O7c-②. 📐 There is no brain COMPONENT left to query on: both
            //   roots live in occurrence slots, so the thing to enumerate is "entities carrying a
            //   store", and BrainTier discriminates inside the loop.
            //   📄 §31.7 — BuildTierQueries owns smallest-first ordering, build-once caching and the
            //   skip for a tier this world never registered.
            _tierQueries ??= BlueprintTierTable.BuildTierQueries(
                repo, qb => qb.WithOwnedWhen<BehaviorState>(_gateOnAuthority));

            var tiers = BlueprintTierTable.Ascending;

            // ⭐ CE-446: a blueprint behaviour's tick takes the frame's command buffer, like an Instance tick.
            _ecb = view.GetCommandBuffer();

            // ⭐ POC 2 Q-4 (opt-in HROT_BRAIN_PRESCORE=1, Throughput mode) — the brains' utility scoring, ahead of the serial walk, in parallel
            // ⭐ N-11 — with the brains a parallel phase the scoring already runs on every core, inside each unit's own tick: no pre-score
            if (Fdp.Toolkit.Utility.UtilityPrescore.Active && !Parallel) Fdp.Toolkit.Utility.UtilityPrescore.Of(repo).Run(repo);

            // Prune the dedup cache using reliable lifecycle events.
            foreach (var evt in repo.ReadEvents<DestructionOrder>())
            {
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);
                ReleaseCadenceReader(evt.Entity.Index);   // ⭐ CE-2382 — a dead slot must not pin the event history
            }
            foreach (var evt in repo.ReadEvents<ClearBehaviorEvent>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);

            long tSweep = Fdp.Toolkit.Diagnostics.QueryTimings.Begin(), bSweep = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();          // ⭐ CE-4395 B-1 — measurement only
            SweepStaleDedupEntries(tiers.Count);
            if (tSweep != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Sweep, tSweep, bSweep);

            // ⭐ CE-2382 (DESIGN_Evaluation_Cost_And_Cadence §3.0–§3.2) — ONE clock for the frame (sim time), shared by the task slots' cadence and the SOP slot's.
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : (_sopClock += deltaTime);
            _history = repo.EventHistory;
            if (++_cadenceSweep >= 300) { _cadenceSweep = 0; SweepStaleCadenceReaders(repo); }
            bool drain = CadenceDrain.TakeRequest(repo);   // ⭐ CE-2385 (D8) — a checkpoint is due: every cadenced slot ticks this frame
            // ⚠ CE-4433 P-4 — EXPERIMENT ONLY (HROT_SOLDIER_CADENCE, default off): a brain no policy cadences is woken by its senses (the SOP's wake)
            if (ExperimentalSoldierCadence > 0f && _history != null)
                foreach (var evt in repo.ReadEvents<Fdp.Toolkit.Perception.Events.SensorChangedEvent>())
                    if (repo.IsAlive(evt.Unit) && repo.HasComponent<BehaviorState>(evt.Unit))
                    {
                        ref readonly var b = ref repo.GetComponentRO<BehaviorState>(evt.Unit);
                        if (b.CadencePolicy == 0 && b.Cadence == CadenceMode.Periodic && b.CadenceWake == 0) repo.GetComponentRW<BehaviorState>(evt.Unit).CadenceWake = 1;
                    }

            // ⭐ S4 — the MobilityLost interrupt (CE-324) moved into HsmRunner with the rest of the HSM tier's quirks.

            if (Parallel) RunParallel(repo, tiers, deltaTime, now, drain);
            else
            for (int t = 0; t < tiers.Count; t++)
            {
                var q = _tierQueries![t];
                if (q is null) continue;            // tier not registered on this world

                foreach (var entity in q)
                {
                    // ⭐⭐ CE-3137 U-0 (§34): a multi-block unit sits in SEVERAL tier queries — run it once, in the
                    //   query of the first tier it carries (one mask read). ⚠ Not a single BehaviorState query: that
                    //   would change the visit order for every unit, and this keeps it for one-block units.
                    if (!Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.IsFirstVisit(repo, entity, t)) continue;

                    var behavior = repo.GetComponent<BehaviorState>(entity);
                    // ⚠ CE-4433 P-4 — EXPERIMENT ONLY: every brain no policy owns (soldiers, vehicles) becomes Periodic at the switch's period, due now
                    if (ExperimentalSoldierCadence > 0f && _history != null && behavior.CadencePolicy == 0 && behavior.Cadence == CadenceMode.EveryFrame)
                    {
                        ref var w = ref repo.GetComponentRW<BehaviorState>(entity);
                        w.Cadence = CadenceMode.Periodic; w.CadencePeriod = ExperimentalSoldierCadence; w.CadenceNextTick = now;
                        behavior = w;
                    }

                    // ⭐⭐ CE-2382 — THE CADENCE GATE (generic: a POLICY sets BehaviorState.Cadence; nothing set ⇒ EveryFrame = every frame, as before).
                    //   ⚠ Only where the world keeps an event history: a cadenced slot reads its events through its own window, and without one
                    //   it would lose them — so a bare world (a unit rail) ticks every brain every frame.
                    bool cadenced = _history != null && behavior.Cadence != CadenceMode.EveryFrame;
                    if (cadenced && !drain && !CadenceGate.Due(behavior.Cadence, behavior.CadenceNextTick, behavior.CadenceWake, now)) continue;

                    // ⭐⭐ CE-3137 U-0c — the unit's blocks are resolved ONCE for its whole tick (runner, hosted subtrees,
                    //   generated thunks, unit memory); every slot lookup below is then a pure scan. Q87 §3b ①.
                    using var storeView = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.BeginTickView(repo, entity);

                    // ⭐⭐ BrainTier is THE discriminator, and it always was — an entity carrying an
                    //   occurrence store may be a BTree brain, an HSM brain or a pure blueprint host.
                    //   ⛔ The brain components were a second, redundant discriminator; losing them is
                    //   not losing a filter.
                    if (!_registry.TryGetDefinition(behavior.ActiveBehaviorHash, out var def))
                        continue;

                    int previousWindow = cadenced ? OpenCadenceWindow(repo, entity) : NoWindow;
                    long tRun = Fdp.Toolkit.Diagnostics.QueryTimings.Begin(), bRun = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();          // ⭐ CE-4395 B-1 — per behaviour, measurement only
                    try
                    {
                        if (BehaviorRunners.For(behavior.BrainTier) is { } runner)
                            Run(runner, repo, entity, behavior, def, deltaTime);
                    }
                    finally
                    {
                        if (cadenced) repo.CloseStepWindow(previousWindow);
                        if (_history != null) AfterCadencedTick(repo, entity, now);
                        if (tRun != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(DefinitionSlot("brain.task:", def), tRun, bRun);
                    }

                    // ⭐ CE-482 — FAIL LOUD: a run that raised a fault this tick (and did not already finish) ends now, through
                    //   the normal finish — so its commands, channels and owned parts are released like any other end.
                    //   📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D2.
                    if (BehaviorFault.IsPending(repo, entity, behavior.InstanceId))
                        Finish(repo, entity, behavior, NodeStatus.Failure);
                }
            }

            if (drain) CadenceDrain.MarkDrained(repo);                              // ⭐ CE-2385 — no window is open now
            CadenceDrain.SetWindowsOpen(repo, _cadenceReaders.Count > 0);
            long tSops = Fdp.Toolkit.Diagnostics.QueryTimings.Begin(), bSops = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();
            TickSopSlots(repo, deltaTime, now);   // ⭐ CE-3035 — the SOP slot, after every task has ticked
            if (tSops != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Sops, tSops, bSops);
        }

        // ════ ⭐⭐⭐ N-11 (CE-4473; R-4212, docs/DESIGN_Parallel_Simulation.md §18) — THE BRAINS AS A PER-ENTITY PARALLEL PHASE ════════════
        //   The same steps as the serial walk above, split by what they touch:
        //   ① SERIAL pre-pass, tier order — everything that reads or writes this system's own records (the dedup, the relayout record, the
        //      cadence readers) or another entity: who is due, the already-finished guard, a relayout restart, the cadence reader;
        //   ② the PHASE (ParallelEntities, batch by index — deterministic at any thread count): each due unit's tree / HSM / blueprint ticks
        //      on its batch thread — its own entity and parts written directly, other units read from the last tick, structural changes,
        //      publishes and its commands into the batch's buffer (EntityRepository's per-entity rule, N-10), its cadence window per thread;
        //   ③ SERIAL post-pass, batch order — the finish, the cadence bookkeeping, the fault finish (exactly the serial walk's order).
        //   ⛔ Off (the default): the serial walk, unchanged.

        /// <summary>⭐ N-11 — the brains run as a per-entity parallel phase: with the engine's parallel simulation (<c>HROT_PARALLEL_SIM=1</c>,
        /// <see cref="ParallelEntities.Enabled"/> — the switch every converted per-entity system follows) or the threaded node
        /// (<c>HROT_THREADED_KERNEL=3</c>). Off by default; tests set it.</summary>
        public static bool Parallel = ParallelEntities.Enabled || Fdp.ModuleHost.ThreadedKernelGate.ComposedWorlds;

        private struct Due
        {
            public Entity Entity;
            public BehaviorDefinition Definition;
            public IBehaviorRunner Runner;
            public BehaviorState Behavior;
            public int Reader;   // the cadence reader (-1: an every-frame brain, no window)
        }

        private Due[] _due = new Due[64];
        private Entity[] _dueEntities = new Entity[64];

        private const int StatusShift = 2;   // Deferred.Code = due index << 2 | status (0 running, 1 success, 2 failure)

        private void RunParallel(EntityRepository repo, IReadOnlyList<BlueprintTierSpec> tiers, float deltaTime, double now, bool drain)
        {
            BrainPhase.Prepare(repo);   // registrations (structural) and the per-frame indexes brain code reads — never built inside the phase
            int n = 0;
            for (int t = 0; t < tiers.Count; t++)
            {
                var q = _tierQueries![t];
                if (q is null) continue;
                foreach (var entity in q)
                {
                    if (!Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.IsFirstVisit(repo, entity, t)) continue;
                    var behavior = repo.GetComponent<BehaviorState>(entity);
                    if (ExperimentalSoldierCadence > 0f && _history != null && behavior.CadencePolicy == 0 && behavior.Cadence == CadenceMode.EveryFrame)
                    {
                        ref var w = ref repo.GetComponentRW<BehaviorState>(entity);
                        w.Cadence = CadenceMode.Periodic; w.CadencePeriod = ExperimentalSoldierCadence; w.CadenceNextTick = now;
                        behavior = w;
                    }
                    bool cadenced = _history != null && behavior.Cadence != CadenceMode.EveryFrame;
                    if (cadenced && !drain && !CadenceGate.Due(behavior.Cadence, behavior.CadenceNextTick, behavior.CadenceWake, now)) continue;
                    if (!_registry.TryGetDefinition(behavior.ActiveBehaviorHash, out var def)) continue;
                    if (BehaviorRunners.For(behavior.BrainTier) is not { } runner) continue;

                    // Run's own serial half: the already-finished guard and the relayout restart (both this system's records)
                    bool tick = !(_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint doneFor) && doneFor == behavior.InstanceId);
                    if (tick)
                    {
                        int blockBytes = RootParamsAccess.RootParamsBytes(def);
                        if (blockBytes > 0)
                        {
                            using var storeView = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.BeginTickView(repo, entity);
                            tick = RestartIfRelaidOut(repo, entity, behavior, def, blockBytes);
                        }
                    }

                    if (n == _due.Length) { Array.Resize(ref _due, n * 2); Array.Resize(ref _dueEntities, n * 2); }
                    _due[n] = new Due { Entity = entity, Definition = def, Runner = runner, Behavior = behavior, Reader = cadenced ? ReaderOf(entity) : NoWindow };
                    _dueEntities[n] = entity;
                    if (!tick) _due[n].Runner = null!;   // skipped this tick (finished, or restarting) — the post-pass still runs for it
                    n++;
                }
            }
            if (n == 0) return;

            _phaseRepo = repo; _phaseDelta = deltaTime; _phaseNow = now;
            try { ParallelEntities.Run(repo, _dueEntities, n, new BrainJob { System = this }); }
            finally
            {
                _phaseRepo = null;
                Array.Clear(_due, 0, n);   // no definition / runner held past the tick
            }
        }

        private EntityRepository? _phaseRepo;
        private float _phaseDelta;
        private double _phaseNow;

        /// <summary>② — one unit's tick on its batch thread (Run's parallel half).</summary>
        private int TickInPhase(int index, BatchContext batch)
        {
            ref readonly var due = ref _due[index];
            if (due.Runner is null) return 0;
            var repo = _phaseRepo!;
            var entity = due.Entity;
            int previousWindow = due.Reader != NoWindow ? repo.OpenStepWindow(due.Reader) : NoWindow;
            try
            {
                using var storeView = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.BeginTickView(repo, entity);
                if (!due.Runner.TryGetRootBrain(repo, entity, due.Definition, out byte* brain, out int brainBytes)) return 0;
                ref byte block = ref BehaviorBlock.None;
                if (RootParamsAccess.RootParamsBytes(due.Definition) > 0) block = ref RootParamsAccess.RootRef(repo, entity);
                var ctx = new BehaviorRunContext
                {
                    World = repo, Self = entity, Definition = due.Definition, InstanceId = due.Behavior.InstanceId,
                    Ecb = batch.Commands, DeltaTime = _phaseDelta,
                };
                var status = due.Runner.Tick(ref ctx, brain, brainBytes, ref block);
                return status == NodeStatus.Success ? 1 : status == NodeStatus.Failure ? 2 : 0;
            }
            finally
            {
                if (due.Reader != NoWindow) repo.CloseStepWindow(previousWindow);
            }
        }

        /// <summary>③ — the serial walk's follow-ups for one unit, in batch order.</summary>
        private void AfterPhase(EntityRepository repo, in Deferred d)
        {
            int index = d.Code >> StatusShift, status = d.Code & ((1 << StatusShift) - 1);
            ref readonly var due = ref _due[index];
            var entity = due.Entity;
            if (status != 0) Finish(repo, entity, due.Behavior, status == 1 ? NodeStatus.Success : NodeStatus.Failure);
            if (_history != null) AfterCadencedTick(repo, entity, _phaseNow);
            if (BehaviorFault.IsPending(repo, entity, due.Behavior.InstanceId)) Finish(repo, entity, due.Behavior, NodeStatus.Failure);
        }

        private struct BrainJob : IEntityBatchJob
        {
            public BrainTickSystem System;
            public void Execute(Entity e, int index, BatchContext ctx)
                => ctx.Defer(e, (index << StatusShift) | System.TickInPhase(index, ctx));
            public void ApplyDeferred(EntityRepository repo, in Deferred d) => System.AfterPhase(repo, in d);
        }

        // ════ ⭐⭐ CE-2382 — THE TASK SLOT'S CADENCE: each cadenced slot reads the bus through ITS OWN EventAccumulator window ═══════════
        //   📄 docs/DESIGN_Evaluation_Cost_And_Cadence.md §3.0–§3.2 · the window: docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §1a–§1b.
        //   A slot ticking every 0.2 s sees, through ReadEvents, every event since its OWN last tick — each kill / capture / hit counted once.

        private const int NoWindow = -1;
        private EventAccumulator? _history;
        private EventAccumulator? _readersHistory;
        private readonly Dictionary<int, (Entity Entity, int Reader)> _cadenceReaders = new();
        private int _cadenceSweep;

        /// <summary>The cadence readers held now — a rail reads it to prove an indefinite sleep or a dead slot releases its reader.</summary>
        public int CadenceReaderCount => _cadenceReaders.Count;

        private int OpenCadenceWindow(EntityRepository repo, Entity entity)
            => repo.OpenStepWindow(ReaderOf(entity));

        private int ReaderOf(Entity entity)
        {
            if (!ReferenceEquals(_readersHistory, _history)) { _cadenceReaders.Clear(); _readersHistory = _history; }   // another history: its ids mean nothing here
            if (_cadenceReaders.TryGetValue(entity.Index, out var held) && held.Entity == entity) return held.Reader;
            if (held.Entity != default) _history!.UnregisterReader(held.Reader);   // the index was reused
            int reader = _history!.RegisterReaderAtCurrentFrame();
            _cadenceReaders[entity.Index] = (entity, reader);
            return reader;
        }

        /// <summary>After ANY task tick: the slot's cadence as its policy left it — schedule, clear the wake, keep / mark / release its window.
        /// ⚠ Runs for a slot that became cadenced DURING this tick too (a policy's first tick), so its window starts now and nothing is lost.</summary>
        private void AfterCadencedTick(EntityRepository repo, Entity entity, double now)
        {
            if (!repo.IsAlive(entity) || !repo.HasComponent<BehaviorState>(entity)) { ReleaseCadenceReader(entity.Index); return; }
            byte policy = repo.GetComponentRO<BehaviorState>(entity).CadencePolicy;
            if (policy != 0)
            {
                CadencePolicies.Apply(policy, repo, entity, now);   // the policy reads what the tick left and sets the mode / wakes
                if (!repo.IsAlive(entity) || !repo.HasComponent<BehaviorState>(entity)) { ReleaseCadenceReader(entity.Index); return; }
            }
            ref readonly var after = ref repo.GetComponentRO<BehaviorState>(entity);
            if (after.Cadence == CadenceMode.EveryFrame)
            {
                if (_cadenceReaders.ContainsKey(entity.Index)) ReleaseCadenceReader(entity.Index);
                return;
            }
            ref var b = ref repo.GetComponentRW<BehaviorState>(entity);
            if (b.Cadence == CadenceMode.Periodic) b.CadenceNextTick = CadenceGate.Next(now, b.CadencePeriod, entity.Index);
            b.CadenceWake = 0;
            if (CadenceGate.KeepsWindow(b.Cadence, b.CadenceNextTick)) _history!.MarkRead(ReaderOf(entity));
            else ReleaseCadenceReader(entity.Index);   // asleep until woken: it reads nothing, so it must not pin the history
        }

        private void ReleaseCadenceReader(int entityIndex)
        {
            if (!_cadenceReaders.Remove(entityIndex, out var held)) return;
            _readersHistory?.UnregisterReader(held.Reader);
        }

        private void ReleaseAllCadenceReaders()
        {
            foreach (var held in _cadenceReaders.Values) _readersHistory?.UnregisterReader(held.Reader);
            _cadenceReaders.Clear();
        }

        private readonly List<int> _deadCadence = new();

        private void SweepStaleCadenceReaders(EntityRepository repo)
        {
            _deadCadence.Clear();
            foreach (var kv in _cadenceReaders) if (!repo.IsAlive(kv.Value.Entity)) _deadCadence.Add(kv.Key);
            foreach (var i in _deadCadence) ReleaseCadenceReader(i);
        }

        // ════ ⭐⭐ CE-3035 — THE SOP SLOT (R-189, R-195, R-198) ════════════════════════════════════════════════════════════
        //   📄 docs/DESIGN_Sensors_And_Doctrine.md §6–§7 · docs/DESIGN_Decision_Layer.md §4.
        //   The SAME runners, inside a BrainSlotScope (storage, owned parts and faults resolve the SOP's run). It ticks every
        //   SopTickPeriod — staggered by entity index so units do not all decide on one frame — and at once when woken: a
        //   sensing change, a finished task, or a refused SOP assignment. It never finishes (Success / Failure restart it),
        //   a FAULT stops it and leaves it visible (R-193), and it commands no channels: a write is reverted — the task keeps
        //   its command — and the SOP faults (design §4.5).

        /// <summary>⭐ <c>CE-3035</c> — how often an SOP decides when nothing wakes it (5 Hz, design §11 G5).</summary>
        public const double SopTickPeriod = 0.2;

        private EntityQuery? _sopQuery;
        private readonly HashSet<int> _sopWokenThisFrame = new();
        private double _sopClock;

        private void TickSopSlots(EntityRepository repo, float deltaTime, double now)
        {
            if (!repo.IsComponentTypeRegistered<SopState>()) return;
            _sopQuery ??= repo.Query().With<SopState>().WithOwnedWhen<BehaviorState>(_gateOnAuthority).Build();

            _sopWokenThisFrame.Clear();
            foreach (var evt in repo.ReadEvents<Fdp.Toolkit.Perception.Events.SensorChangedEvent>()) _sopWokenThisFrame.Add(evt.Unit.Index);
            foreach (var evt in repo.ReadEvents<BehaviorFinishedEvent>()) _sopWokenThisFrame.Add(evt.Entity.Index);

            foreach (var entity in _sopQuery)
            {
                ref var sop = ref repo.GetComponentRW<SopState>(entity);
                if (sop.SopHash == BehaviorIds.None || sop.SopFaulted != 0) continue;

                // ⭐ CE-2382 (D7) — the SOP slot's cadence is the SAME rule as the task slot's (CadenceGate), on its own fields
                byte wake = sop.SopWake != 0 || _sopWokenThisFrame.Contains(entity.Index) ? (byte)1 : (byte)0;
                if (!CadenceGate.Due(CadenceMode.Periodic, sop.SopNextTick, wake, now)) continue;
                sop.SopWake = 0;
                sop.SopNextTick = CadenceGate.Next(now, SopTickPeriod, entity.Index);   // staggered

                if (!_registry.TryGetDefinition(sop.SopHash, out var def)) continue;
                if (BehaviorRunners.For(sop.SopBrainTier) is not { } runner) continue;

                long tSop = Fdp.Toolkit.Diagnostics.QueryTimings.Begin(), bSop = Fdp.Toolkit.Diagnostics.QueryTimings.Bytes();          // ⭐ CE-4395 B-1 — per SOP, measurement only
                TickOneSop(runner, repo, entity, sop.SopHash, sop.SopInstanceId, def, deltaTime);
                if (tSop != 0) Fdp.Toolkit.Diagnostics.QueryTimings.End(DefinitionSlot("brain.sop:", def), tSop, bSop);
            }
        }

        private void TickOneSop(IBehaviorRunner runner, EntityRepository repo, Entity entity, int sopHash, uint sopRun,
                                BehaviorDefinition def, float deltaTime)
        {
            using var view = BrainSlotScope.Enter(entity, sopHash, sopRun);
            using var storeView = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.BeginTickView(repo, entity); // ⭐ U-0c

            if (!runner.TryGetRootBrain(repo, entity, def, out byte* brain, out int brainBytes)) return;
            ref byte block = ref BehaviorBlock.None;
            if (RootParamsAccess.RootParamsBytes(def) > 0)
            {
                if (!RootParamsAccess.TryGetRootBytes(repo, entity, out _)) return;
                block = ref RootParamsAccess.RootRef(repo, entity);
            }

            var guard = new ChannelGuard(repo, entity);
            var ctx = new BehaviorRunContext
            {
                World = repo, Self = entity, Definition = def, InstanceId = sopRun, Ecb = _ecb, DeltaTime = deltaTime,
            };
            var status = runner.Tick(ref ctx, brain, brainBytes, ref block);

            if (guard.RevertIfWritten(repo, entity))
                BehaviorFault.Raise(repo, entity, BehaviorFaultCode.SopCommandedChannel,
                    $"The SOP '{def.Name}' wrote a movement / weapon / interaction channel. An SOP acts only by assigning " +
                    "behaviours (Do when idle / React); the write was reverted and the task keeps its command.");

            if (BehaviorFault.IsPending(repo, entity, sopRun))
            {
                BehaviorFault.Take(repo, entity, sopRun);
                repo.GetComponentRW<SopState>(entity).SopFaulted = 1;   // stopped, logged (BehaviorFault.Raise), visible
                return;
            }

            if (status == NodeStatus.Success || status == NodeStatus.Failure)
                runner.Start(def, brain, brainBytes);   // an SOP never finishes — it starts over
        }

        /// <summary>⭐ <c>CE-3035</c> — a copy of the unit's three channels before an SOP tick, to catch and revert any write.</summary>
        private readonly struct ChannelGuard
        {
            private readonly bool _hasLoco, _hasWeapon, _hasInteract;
            private readonly LocomotionChannel _loco;
            private readonly WeaponChannel _weapon;
            private readonly InteractionChannel _interact;

            public ChannelGuard(EntityRepository repo, Entity entity)
            {
                _hasLoco     = repo.IsComponentTypeRegistered<LocomotionChannel>()  && repo.HasComponent<LocomotionChannel>(entity);
                _hasWeapon   = repo.IsComponentTypeRegistered<WeaponChannel>()      && repo.HasComponent<WeaponChannel>(entity);
                _hasInteract = repo.IsComponentTypeRegistered<InteractionChannel>() && repo.HasComponent<InteractionChannel>(entity);
                _loco     = _hasLoco     ? repo.GetComponentRO<LocomotionChannel>(entity)  : default;
                _weapon   = _hasWeapon   ? repo.GetComponentRO<WeaponChannel>(entity)      : default;
                _interact = _hasInteract ? repo.GetComponentRO<InteractionChannel>(entity) : default;
            }

            /// <summary>Restore every channel the tick changed; <c>true</c> if any was.</summary>
            public bool RevertIfWritten(EntityRepository repo, Entity entity)
            {
                bool written = false;
                if (_hasLoco && !Same(repo.GetComponentRO<LocomotionChannel>(entity), _loco))
                { repo.GetComponentRW<LocomotionChannel>(entity) = _loco; written = true; }
                if (_hasWeapon && !Same(repo.GetComponentRO<WeaponChannel>(entity), _weapon))
                { repo.GetComponentRW<WeaponChannel>(entity) = _weapon; written = true; }
                if (_hasInteract && !Same(repo.GetComponentRO<InteractionChannel>(entity), _interact))
                { repo.GetComponentRW<InteractionChannel>(entity) = _interact; written = true; }
                return written;
            }

            private static bool Same<T>(in T a, in T b) where T : unmanaged
                => MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in b)));
        }

        /// <summary>
        /// ⭐⭐ <b>Drop dedup entries for entities that left the walk — and the premise is RESTATED
        /// rather than inherited.</b> 📄 §31.14.6.
        ///
        /// <para>📐 The sweep existed in <c>HsmTickSystem</c> and <b>not at all</b> in
        /// <c>BTreeTickSystem</c> — two systems that should behave identically did not, and a merge
        /// that copied whichever twin it started from would have silently picked a winner.</para>
        ///
        /// <para>⛔ <b>Its old justification is obsolete by this programme:</b> <i>"entities no longer in
        /// the query — brain component removed without a lifecycle event"</i>. 🔴 There is no brain
        /// component any more. ⭐ <b>The premise in slot terms:</b> an entity leaves the walk when its
        /// STORE goes or its <c>BrainTier</c> changes; the dictionary is keyed by <c>entity.Index</c>,
        /// which the ECS <b>reuses</b>, so a stale entry on a recycled index would suppress a genuine
        /// <c>BehaviorFinishedEvent</c> for a DIFFERENT entity. ⇒ that is a correctness argument, and
        /// it applies to the BTree arm exactly as much — <b>the merge FIXES a latent BTree gap rather
        /// than importing an HSM quirk.</b></para>
        /// </summary>
        private void SweepStaleDedupEntries(int tierCount)
        {
            // ⭐ CE-446 step 3: the blueprint layout record is keyed the same way, for the same reason.
            if (_publishedTerminalForInstanceId.Count == 0 && _blueprintLayout.Count == 0) return;

            _seenThisFrame.Clear();
            for (int t = 0; t < tierCount; t++)
            {
                var q = _tierQueries![t];
                if (q is null) continue;
                foreach (var seenEntity in q)
                    _seenThisFrame.Add(seenEntity.Index);
            }

            if (_seenThisFrame.Count == 0)
            {
                _publishedTerminalForInstanceId.Clear();
                _blueprintLayout.Clear();
                return;
            }

            _staleKeys.Clear();
            foreach (var key in _publishedTerminalForInstanceId.Keys)
                if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _publishedTerminalForInstanceId.Remove(key);

            _staleKeys.Clear();
            foreach (var key in _blueprintLayout.Keys)
                if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _blueprintLayout.Remove(key);
        }

        // ══ THE ONE ARM — every tier, through its runner (S4) ═════════════════════════════════

        /// <summary>
        /// ⭐⭐⭐ <b>S4 (<c>DESIGN_Unified_Behaviour_Run</c> §3, U-2) — one arm for every tier.</b> The runner locates the
        /// brain state and steps it; this body owns what every tier shares: the already-finished guard, the block and
        /// its reload restart (<c>CE-452</c>), and the finish (<c>CE-449</c>). ⭐ The same <c>Runner.Tick</c> over the same
        /// (brain, block) pair is what hosting will call (S5) — root = hosted with no host.
        /// </summary>
        // ⭐ CE-4395 B-1 — the brain's timing slots (inclusive). Named once per definition, then a lookup: no allocation in the steady state.
        private static readonly int S_Execute = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("brain.Execute (whole)");
        private static readonly int S_Sops = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("brain.TickSopSlots (whole)");
        private static readonly int S_Sweep = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("brain.SweepStaleDedupEntries");
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BehaviorDefinition, int[]> _defSlots = new();

        private static int DefinitionSlot(string family, BehaviorDefinition def)
        {
            var slots = _defSlots.GetValue(def, _ => new[] { -1, -1 });
            int k = family == "brain.task:" ? 0 : 1;
            if (slots[k] < 0) slots[k] = Fdp.Toolkit.Diagnostics.QueryTimings.Slot(family + def.Name);
            return slots[k];
        }

        private void Run(IBehaviorRunner runner, EntityRepository repo, Entity entity, in BehaviorState behavior,
                         BehaviorDefinition def, float deltaTime)
        {
            // ⭐ Finished runs never tick again: Finish clears (BrainTier = 0), and this guards the same frame.
            if (_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint doneFor)
                && doneFor == behavior.InstanceId)
                return;

            if (!runner.TryGetRootBrain(repo, entity, def, out byte* brain, out int brainBytes))
                return;

            // ⭐⭐⭐ P4-② — THE BLACKBOARD IS THE ROOT PARAMS SLOT, resolved once per entity per tick and handed down
            //   as a `ref byte`. ⛔ The no-block case is gated on a checkable predicate (RootParamsBytes), never a
            //   null-guess; "no block" is the shared SENTINEL, so a projection from it fails loudly (CE-431).
            ref byte block = ref BehaviorBlock.None;
            int blockBytes = RootParamsAccess.RootParamsBytes(def);
            if (blockBytes > 0)
            {
                // ⭐ CE-452: a reload that changed the block's width or layout restarts the behaviour instead of ticking it.
                if (!RestartIfRelaidOut(repo, entity, behavior, def, blockBytes)) return;
                block = ref RootParamsAccess.RootRef(repo, entity);
            }

            var ctx = new BehaviorRunContext
            {
                World = repo, Self = entity, Definition = def, InstanceId = behavior.InstanceId, Ecb = _ecb, DeltaTime = deltaTime,
            };
            var status = runner.Tick(ref ctx, brain, brainBytes, ref block);

            if (status == NodeStatus.Success || status == NodeStatus.Failure)
                Finish(repo, entity, behavior, status);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-449</c> — finishing is TERMINAL, for every tier.</b> 📄 <c>BD1-DESIGN.md</c> §1.0a: root
        /// <c>Success</c>/<c>Failure</c> ⇒ <i>"the entire behavior has concluded"</i>.
        /// <para>
        /// 🔒 User, <c>2026-09-30</c>: <i>"Every behavior must be finishable … must be finishable and clean up resources"</i> ·
        /// <i>"finishing a behavior should cancel the commands exactly same as Clear Behavior does … each channel resets."</i>
        /// ⇒ publish <see cref="BehaviorFinishedEvent"/> once, then run THE clear
        /// (<see cref="BehaviorIngressSystem.Clear"/>): every slot of the behaviour is freed, <c>InstanceId</c> is bumped so
        /// <c>ChannelArbitrationSystem</c> resets each channel to its default, and <c>BrainTier = 0</c> so nothing ticks it again.
        /// </para>
        /// <para>
        /// ⛔ Before this, a BTree re-ran from its root every frame after finishing (the interpreter resets
        /// <c>RunningNodeIndex</c>) and an HSM ran again because this system cleared its <c>Terminated</c> latch. ⚠ A tree that
        /// is MEANT to loop says so with a <c>Repeater</c> at its root.
        /// </para>
        /// </summary>
        private void Finish(EntityRepository repo, Entity entity, in BehaviorState behavior, NodeStatus result)
        {
            if (_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint prev) && prev == behavior.InstanceId)
                return;

            // ⭐ CE-482: a fault the run raised overrides how it ended — even a Success returned in the same tick.
            var fault = BehaviorFault.Take(repo, entity, behavior.InstanceId);
            if (fault != BehaviorFaultCode.None) result = NodeStatus.Failure;
            var origin = behavior.Origin;   // ⚠ `behavior` may alias the component Clear rewrites — read it first
            var finished = new BehaviorFinishedEvent { Entity = entity, Result = result, FaultCode = fault, Origin = origin };
            repo.Bus.Publish(finished);
            // ⭐ CE-3167 — an operator's order keeps its outcome after the clear below, for GET /entities/{id}/order.
            if (origin == BehaviorOrigin.Operator)
                OperatorOrderOutcome.Record(repo, entity, behavior.ActiveBehaviorHash, behavior.InstanceId, finished.Outcome, fault);
            _publishedTerminalForInstanceId[entity.Index] = behavior.InstanceId;
            // ⭐ CE-1249 (BV-10) — the task ended by itself: its result, and the fault that overrode it
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.Write(repo, entity, Fdp.Toolkit.Behavior.Diagnostics.BrainJournalKind.TaskEnded,
                a: behavior.ActiveBehaviorHash, b: (int)fault, origin: (byte)origin, urgency: (byte)result);
            BehaviorIngressSystem.Clear(repo, entity, _registry);
            // ⭐ CE-2078 (R-199 ②) — a reaction ended ⇒ the task it paused restarts, through the gate, next frame.
            if (BehaviorOriginRank.Pauses(origin)) BehaviorIngressSystem.ResumePausedTask(repo, entity);   // ⭐ CE-3172 — or a possession
        }

        /// <summary>⭐ <c>CE-2078</c> — a run that cannot go on is cleared; if it was a reaction, the task it paused restarts.</summary>
        private void ClearResumingAPausedTask(EntityRepository repo, Entity entity)
        {
            bool reaction = BehaviorOriginRank.Pauses(repo.GetComponentRO<BehaviorState>(entity).Origin);   // ⭐ CE-3172
            BehaviorIngressSystem.Clear(repo, entity, _registry);
            if (reaction) BehaviorIngressSystem.ResumePausedTask(repo, entity);
        }

        /// <summary>⭐ CE-446: the frame's command buffer, handed to every runner (a blueprint tick records into it).</summary>
        private Fdp.Interfaces.IEntityCommandBuffer? _ecb;

        /// <summary>The layout each running behaviour started with — keyed by <c>entity.Index</c>, valid only for the recorded
        /// <c>(InstanceId, behaviour hash)</c>; <c>Pending</c> = a restart was requested for that instance. Swept with the
        /// terminal dedup.</summary>
        private readonly Dictionary<int, (uint InstanceId, int Behavior, ulong Layout, bool Pending)> _blueprintLayout = new();

        /// <summary>
        /// ⭐⭐ <b><c>CE-452</c> — a hot reload that re-lays-out a RUNNING behaviour's root block RESTARTS it through the ONE
        /// start pipeline, with the parameters it was started with.</b> 📄 <c>Architect_Question_77</c> §5.12.
        ///
        /// <para>
        /// ⭐ Design basis: <c>btree-ai-action-binding/SLICE2-DESIGN.md</c> Flaw 2 — on a layout-changing reload
        /// <i>"re-publish <c>AssignBehaviorEvent</c> for every entity running that behavior"</i> so ingress re-provisions
        /// correctly-sized slots; <c>AI_Editor_Shared_Infrastructure.md</c> §17 — Soft keeps state, Hard restarts.
        /// ⭐ Detected per entity on the tick, because no reload path tells a world which entities run the reloaded
        /// behaviour (<c>DESIGN_Cgf_Editor_Sharing_Slice3</c> §10.3).
        /// </para>
        /// <list type="bullet">
        /// <item>WIDTH changed (every tier) — the block no longer fits the definition; ⛔ ticking would read/write past it.</item>
        /// <item>LAYOUT changed (blueprint tier — the generated <c>StructureHash</c>); a BTree/HSM root has no layout hash yet.</item>
        /// </list>
        /// <para>
        /// ⭐ On either: this tick is SKIPPED and an <c>AssignBehaviorEvent</c> is published with the
        /// <see cref="BehaviorStartRecord"/>'s name + JSON (authored defaults when there is none), so next frame's
        /// <c>BehaviorIngressSystem.Start</c> rebuilds everything — params, own resolver, hosted children (they re-seed from
        /// the rebuilt block), store growth (structural, so ingress-only). ⚠ A restart is a new instance: <c>InstanceId</c>
        /// bumps, so each channel resets once. ⛔ If the instance is STILL here next tick, the restart failed (the parse
        /// threw, or the name is gone) ⇒ it is CLEARED rather than ticked over a block that does not fit.
        /// </para>
        /// <returns><c>false</c> when this tick must be skipped.</returns>
        /// </summary>
        private bool RestartIfRelaidOut(
            EntityRepository repo, Entity entity, in BehaviorState behavior, BehaviorDefinition def, int blockBytes)
        {
            bool known = _blueprintLayout.TryGetValue(entity.Index, out var started)
                         && started.InstanceId == behavior.InstanceId
                         && started.Behavior == behavior.ActiveBehaviorHash;

            if (known && started.Pending)
            {
                _blueprintLayout.Remove(entity.Index);
                ClearResumingAPausedTask(repo, entity);
                return false;
            }

            // ⚠ Per entity per tick on every brain — written only when the instance or its layout changed.
            if (!known || started.Layout != def.BlueprintStructureHash)
                _blueprintLayout[entity.Index] = (behavior.InstanceId, behavior.ActiveBehaviorHash, def.BlueprintStructureHash, false);

            bool present = RootParamsAccess.TryGetRootBytes(repo, entity, out _, out int length);
            bool widthChanged  = present && length != blockBytes;
            bool layoutChanged = known && started.Layout != def.BlueprintStructureHash;
            if (!widthChanged && !layoutChanged) return true;   // ⚠ absent: RootRef throws with the causes

            string? name = null, json = null;
            if (repo.HasManagedComponent<BehaviorStartRecord>(entity))
            {
                var record = ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(entity);
                if (record.InstanceId == behavior.InstanceId) { name = record.BehaviorName; json = record.JsonParams; }
            }
            if (name == null && !_registry.TryGetName(behavior.ActiveBehaviorHash, out name))
            {
                ClearResumingAPausedTask(repo, entity);
                return false;
            }

            repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = name!, JsonParams = json ?? "{}",
                Origin = BehaviorOrigin.Self });   // ⭐ CE-3034 — a restart of the same run keeps its origin
            _blueprintLayout[entity.Index] = (behavior.InstanceId, behavior.ActiveBehaviorHash, def.BlueprintStructureHash, true);
            _reloadLog.OnHardReset(behavior.ActiveBehaviorHash, entity,
                known ? started.Layout : 0UL, def.BlueprintStructureHash);
            return false;
        }

    }
}
