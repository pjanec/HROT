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
    /// <para>⭐ Allocation-free per frame: the query is built once and cached.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
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

            uint tick = view.Tick;
            foreach (var commander in _commanders)
            {
                bool merged = SquadPerceptionMergeSystem.Run(repo, commander, tick, MergeIntervalTicks);
                // ⭐ CE-3088 — re-assign fire only when the pool it reads changed (≈ 10 Hz), not every frame.
                if (merged) _fire?.Run(repo, commander);
            }
        }
    }
}
