using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ N-11 (CE-4473; docs/DESIGN_Parallel_Simulation.md §18.2, D-6) — the component types brain code (the library behaviours, the helpers
    /// generated code calls, and the generated code itself) reads on ANOTHER unit: inside the brains' per-entity phase such a read is served
    /// from the last-tick view, so the view copies exactly these. Seeded from the entity-access census (§18.1a) and completed by the
    /// determinism rail run with the checks on (<c>HROT_READ_CHECK=1</c>: an undeclared cross read throws, naming the type).
    /// ⚠ The design's target is the GENERATOR emitting each asset's set (§18.2) — this hand list stands for it until then.
    /// </summary>
    public static class BrainReads
    {
        /// <summary>Declares the set (idempotent).</summary>
        public static void Declare()
        {
            // measured by ParallelBrainsDeterminismTests under HROT_READ_CHECK=2 (the survey) — who is in the fight, what side, whose part
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.Health>();
            ParallelReadCheck.DeclareUnmanaged<EntityInfo>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.PartMetadata>();
            // measured on the scale rig (CGF, 300 men, squads marching — the survey, HROT_READ_CHECK=2): a squad play reads its members
            ParallelReadCheck.DeclareUnmanaged<SimTransform>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.BehaviorState>();   // ⭐ every unit writes its own in the phase
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Behavior.Components.Roe>();
            ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        }
    }
}
