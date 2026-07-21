using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated combined per-tank gate for <c>DispatchWaveWithTargets</c> (architect Q#8-B/E). Bundles the
    /// oracle's three per-tank skip conditions into ONE visual <c>Branch</c> condition so the
    /// <c>FlowForEach</c> body needs only two nesting levels (consider? → got-slot?) rather than four —
    /// keeping the in-body inline-<c>if</c> scheduling (P1b) shallow. Pure predicate over the entity +
    /// counts; <c>ISimulationView view</c> is baked <c>TrailingContext:"View"</c> (for the alive check).
    /// Does not modify the C# oracle.
    /// </summary>
    public static class WaveDispatchOps
    {
        /// <summary>
        /// True when the <paramref name="sub"/> should be considered for this wave: the tracker still has
        /// room (<paramref name="trackerCount"/> &lt; 8, the oracle's <c>ActiveAttackerCount &lt; 8</c>
        /// cap), the entity is alive (subsumes the oracle's <c>packed==0</c> + <c>!IsAlive</c> skips —
        /// <c>Entity.Null</c> is never alive), and it passes the wave-parity gate
        /// (<see cref="WaveParityOps.ShouldParticipate"/>). The per-slot availability check
        /// (<see cref="SlotOps.PickRandomFreeSlot"/> returning <c>-1</c>) stays a separate visual branch.
        /// </summary>
        [BlueprintCallable("Wave")]
        public static bool ShouldConsider(
            Entity sub, int trackerCount, int rosterCount, int currentWave, ISimulationView view)
        {
            if (trackerCount >= 8) return false;
            if (view is not EntityRepository world || !world.IsAlive(sub)) return false;
            return WaveParityOps.ShouldParticipate(sub, rosterCount, currentWave);
        }
    }
}
