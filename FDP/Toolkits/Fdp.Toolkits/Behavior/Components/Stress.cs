using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2270</c> TS-B (R-2034 TS-4, behaviors) — a combatant's live STRESS 0–1: what fire, a friend falling, being outnumbered or
    /// alone have done to him, calming with time (faster in cover, faster near his leader). Kept by <c>StressSystem</c> on the brain
    /// node; read by the damping inputs (TS-5) and the break (TS-6). Brain-local (NoScenario, never sent — TS-8), like the civilians'
    /// own state. ⛔ Combatants only (TS-7): a civilian keeps <c>CivilianState</c>; a vehicle has none.
    /// 📄 docs/DESIGN_Temperament_And_Stress.md §4 TS-4.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Stress)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Stress
    {
        /// <summary>0 calm … 1 the most he can bear.</summary>
        public float Level;
        /// <summary>Sim time of his last stimulus (a round, a hit, a friend down).</summary>
        public double LastStimulusAt;
        /// <summary>Sim time of the last pass (the continuous terms run over the time since).</summary>
        public double LastStepAt;
        /// <summary>⭐ TS-6 (<c>CE-2272</c>) — 1 while he is broken.</summary>
        public byte Broken;
        /// <summary>⭐ TS-6 — sim time his level fell under the recovery mark (0 = not under it).</summary>
        public double CalmSince;
    }
}
