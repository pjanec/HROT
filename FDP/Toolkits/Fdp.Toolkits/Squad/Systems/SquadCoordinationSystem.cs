using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-454</c> — the ONE system that puts the squad layer on a frame.</b>
    /// 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md</c> §2–§3 (slice <c>W2</c>).
    ///
    /// <para>The squad "systems" are plain <c>Run(repo, commander, …)</c> library calls (design §7: the primitives are
    /// a library all three authoring forms call). This driver walks every commander this node is authoritative for and
    /// calls them in the order the design states. ⭐ One driver, not one wrapper per library call, so the ORDER lives
    /// here rather than in kernel registration order.</para>
    ///
    /// <para>⭐ <c>CE-3088</c> — after a merge it runs the squad's fire distribution (<c>ThreatMatrixAssignmentSystem</c>), which
    /// was simply never wired (Squad Wiring §5 never names it). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §11.</para>
    ///
    /// <para>⚠ What it deliberately does NOT call (design §5): <see cref="CommanderUtilityTickSystem"/> (D3 — every
    /// starter consideration reads the active danger area, and no real provider exists), the movement-mode broadcast
    /// (D5 — its component id collides, <c>QA-037</c>, and Muscle has no reader), and maneuver execution (D2 — authored
    /// as a behaviour on the commander).</para>
    ///
    /// <para>⭐ <c>CE-2123</c> S2 — and it raises the squad's sensing edges on the commander (<see cref="SquadSensing"/>):
    /// contact / all-clear when a merge takes the pool across zero, under-fire when a member was hit or shot at.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.4.</para>
    ///
    /// <para>⭐ Allocation-free per frame: the query is built once and cached.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class SquadCoordinationSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private readonly Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem? _fire;
        private EntityQuery? _commanders;

        /// <summary>Minimum ticks between cadence merges (design §4, S-2: ~10 Hz at 60 tps).</summary>
        public uint MergeIntervalTicks { get; set; } = 6;

        /// <param name="gateOnAuthority">⭐ the pack's P3-3b execution gate: process only commanders whose cognitive
        /// state this node owns — the same rule <c>TacticalIntentResolutionSystem</c> applies.</param>
        /// <param name="fire">⭐ <c>CE-3088</c> — the squad's fire distribution, run after each merge of the pool it reads
        /// (📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §11). Null = no assignment (a test world, a host that does not want
        /// it). ⚠ <c>CgfLogicPack</c> — every Brain host — PASSES it; its rail asserts that on the constructed pack.</param>
        public SquadCoordinationSystem(bool gateOnAuthority = false, Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem? fire = null)
        {
            _gateOnAuthority = gateOnAuthority;
            _fire = fire;
        }

        /// <summary>The fire-distribution step this driver runs after a merge (null = none) — read by the pack's rail.</summary>
        public Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem? FireAssignment => _fire;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(SquadCoordinationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            _commanders ??= repo.Query()
                .With<SquadCognitiveState>()
                .With<UnitRoster>()
                .WithOwnedWhen<BehaviorState>(_gateOnAuthority)
                .Build();

            uint tick = Fdp.Toolkit.Perception.PerceptionClock.Now(view);   // ⭐ CE-3292 — the clock LastSeenTick is stamped in
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (!(Parallel && RunPerSquad(repo, tick, now)))
                foreach (var commander in _commanders)
                    Coordinate(repo, commander, tick, now);
            // ⭐ CE-2123 S2 — a member hit or shot at ⇒ its squad is under fire.
            SquadSensing.UnderFire(repo, _gateOnAuthority);
            Changes.Rounds(repo, _gateOnAuthority, now);         // ⭐ CE-2283 — a man's place untenable, fire from beyond (sniper)
        }

        /// <summary>One squad's step, in the design's order — the serial walk's body and the per-squad job's (F-11).</summary>
        private void Coordinate(EntityRepository repo, Entity commander, uint tick, double now)
        {
            SquadSensing.Succession(repo, commander, now);   // ⭐ CE-2125 — the leader fell ⇒ the next man, a lull, an edge
            SquadSensing.MemberDown(repo, commander);        // ⭐ CE-2152 C-c — a man is down ⇒ one edge on the commander
            int before = repo.GetComponentRO<SquadCognitiveState>(commander).Contacts.Count;
            bool merged = SquadPerceptionMergeSystem.Run(repo, commander, tick, MergeIntervalTicks);
            Changes.Squad(repo, commander, now);             // ⭐ CE-2283 — in the EA, flanked, a friend down beside a man
            if (!merged) return;
            // ⭐ CE-3088 — re-assign fire only when the pool it reads changed (≈ 10 Hz), not every frame.
            _fire?.Run(repo, commander);
            // ⭐ CE-2123 S2 — the squad's contact / all-clear edges, on the commander.
            SquadSensing.PoolEdges(repo, commander, before);
        }

        // ════ ⭐⭐ F-11 (CE-4474; R-4212 ③, docs/DESIGN_Parallel_Simulation.md §18.5) — SQUAD-LEVEL WORK RUNS IN PARALLEL PER SQUAD ═══════════
        //   A squad's step writes its commander AND its members (a successor's designation, each man's SquadMemberSense) — so the job is the
        //   SQUAD: it owns its commander and its roster (ParallelReadCheck.OwnAlso), and everything else follows the per-entity rule (other
        //   units read from the last tick or as declared stable reads, structural changes / publishes / egress marks deferred, played in batch
        //   order). ⭐ Rosters NEST (force → platoon → squad: 41 of the 300-man rig's 43 commanders are themselves a roster's member) ⇒ one
        //   PHASE PER LEVEL, top-down: commanders at one depth own disjoint sets BY CONSTRUCTION (a man has one commander), and a level's
        //   members are the next level's jobs, which run after it. A roster that breaks that (one entity in two sets of a level — a stale
        //   entry) ⇒ this tick runs the serial walk: the parallel result must never depend on it.

        /// <summary>⭐ F-11 — the squads run as per-level parallel phases: the switch the brains follow (<c>HROT_PARALLEL_SIM=1</c> or the
        /// threaded node, <c>HROT_THREADED_KERNEL=3</c>). Off by default; tests set it.</summary>
        public static bool Parallel = (Fdp.Core.Jobs.ParallelEntities.Enabled || Fdp.ModuleHost.ThreadedKernelGate.ComposedWorlds)
            && Environment.GetEnvironmentVariable("HROT_SERIAL_SQUADS") != "1";   // ⭐ N-15 — the measuring switch: this phase alone serial

        private const int MaxDepth = 16;   // a command chain deeper than this is a cycle (a stale roster), not a hierarchy
        private Entity[] _all = new Entity[32];
        private int[] _depth = new int[32];
        private Entity[] _level = new Entity[32];
        private int[] _stamp = new int[64];   // per entity index: the run that claimed it (exclusivity of a level's sets)
        private int _run;
        private EntityRepository? _phaseRepo;
        private uint _phaseTick;
        private double _phaseNow;

        /// <summary>The per-level phases. False ⇒ nothing ran (a level's sets overlap): the caller walks serially.</summary>
        private bool RunPerSquad(EntityRepository repo, uint tick, double now)
        {
            int n = 0, deepest = 0;
            foreach (var commander in _commanders!)
            {
                if (n == _all.Length) { Array.Resize(ref _all, n * 2); Array.Resize(ref _depth, n * 2); Array.Resize(ref _level, n * 2); }
                int d = DepthOf(repo, commander);
                _all[n] = commander; _depth[n] = d; n++;
                if (d > deepest) deepest = d;
            }
            if (n == 0) return true;
            if (!Disjoint(repo, n, deepest)) return false;
            _phaseRepo = repo; _phaseTick = tick; _phaseNow = now;
            try
            {
                for (int level = 0; level <= deepest; level++)
                {
                    int m = 0;
                    for (int i = 0; i < n; i++) if (_depth[i] == level) _level[m++] = _all[i];   // query order within a level
                    if (m > 0) Fdp.Core.Jobs.ParallelEntities.Run(repo, _level, m, new SquadJob { System = this });
                }
            }
            finally { _phaseRepo = null; }
            return true;
        }

        /// <summary>How many commanders are above <paramref name="commander"/> (its <c>UnitSubordinate.Commander</c> chain).</summary>
        private static int DepthOf(EntityRepository repo, Entity commander)
        {
            int d = 0;
            for (var e = commander; d < MaxDepth && repo.HasComponent<UnitSubordinate>(e); d++)
            {
                e = repo.GetComponentRO<UnitSubordinate>(e).Commander;
                if (e.IsNull || !repo.IsAlive(e)) break;
            }
            return d;
        }

        /// <summary>Within every level, no entity is a job AND a member, or a member of two rosters.</summary>
        private bool Disjoint(EntityRepository repo, int n, int deepest)
        {
            for (int level = 0; level <= deepest; level++)
            {
                if (++_run == int.MaxValue) { Array.Clear(_stamp); _run = 1; }
                for (int i = 0; i < n; i++) if (_depth[i] == level && !Claim(_all[i].Index)) return false;
                for (int i = 0; i < n; i++)
                {
                    if (_depth[i] != level) continue;
                    ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(_all[i]);
                    for (int k = 0; k < roster.Count; k++)
                    {
                        var member = roster.SubordinateEntities[k];
                        if (!member.IsNull && !Claim(member.Index)) return false;
                    }
                }
            }
            return true;
        }

        private bool Claim(int index)
        {
            if (index >= _stamp.Length) Array.Resize(ref _stamp, Math.Max(index + 1, _stamp.Length * 2));
            if (_stamp[index] == _run) return false;
            _stamp[index] = _run;
            return true;
        }

        private struct SquadJob : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads
        {
            public SquadCoordinationSystem System;
            public void Execute(Entity commander, int index, Fdp.Core.Jobs.BatchContext ctx)
            {
                var repo = System._phaseRepo!;
                ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
                for (int k = 0; k < roster.Count; k++)
                    if (!roster.SubordinateEntities[k].IsNull) Fdp.Core.ParallelReadCheck.OwnAlso(roster.SubordinateEntities[k]);
                System.Coordinate(repo, commander, System._phaseTick, System._phaseNow);
            }
            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }
            // what a squad's step reads on units OUTSIDE its squad that nothing writes while the phase runs — measured: the survey of the 1 000-man
            // stepped editor run (HROT_READ_CHECK=2) names one: a contact's sound signature, read by the heard-vs-seen merge (ThreatEvaluationSystem.ClassesOf)
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads.Unmanaged<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>();
        }

        /// <summary>⭐ <c>CE-2283</c> slice M-1 — the fighting-position changes this driver raises (and their per-man memory).</summary>
        public SquadSenseChanges Changes { get; } = new();
    }
}
