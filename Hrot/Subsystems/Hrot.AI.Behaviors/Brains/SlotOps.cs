using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated firing-line slot-selection kernels for the Hill-attack wave core (architect Q#8-B/C/E).
    /// The inner free-slot scan + pick and the closest-baseline search are self-contained algorithmic
    /// kernels with no visual-node form, so they stay curated; the outer roster iteration stays a visual
    /// <c>FlowForEach</c>. Does not modify the C# oracle (<c>HillAttackCommanderNodes</c>).
    /// </summary>
    public static class SlotOps
    {
        /// <summary>
        /// Picks a free firing-line slot for the current wave — the oracle's inner
        /// <c>avail[]</c>-scan + random pick (<c>DispatchWaveWithTargets</c>), but with a
        /// **deterministic** sim-derived seed instead of <c>Random.Shared</c> (architect Q#8-C, mandated
        /// for replay/rollback/headless-proof determinism). Free = bit clear in
        /// <c>burnedMask | waveUsedMask</c>. Returns the chosen slot in [0, <paramref name="totalSlots"/>),
        /// or <c>-1</c> when none are free (the oracle's "no slots left; skip tank" path).
        /// <para>P7: trailing <c>Entity self</c> + <c>ISimulationView view</c> are baked
        /// <c>TrailingContext:"SelfAndView"</c>. Seed = xorshift of
        /// <c>self.Index ^ currentWave ^ (int)SimulationTime</c> — same inputs → same slot, so a proof
        /// can assert the exact slot.</para>
        /// </summary>
        [BlueprintCallable("Slots")]
        public static int PickRandomFreeSlot(
            ushort burnedMask, ushort waveUsedMask, int totalSlots, int currentWave, Entity self, ISimulationView view)
        {
            int blockedMask = burnedMask | waveUsedMask;
            int cap = totalSlots < 16 ? totalSlots : 16;

            Span<int> avail = stackalloc int[16];
            int availCount = 0;
            for (int j = 0; j < cap; j++)
                if ((blockedMask & (1 << j)) == 0) avail[availCount++] = j;

            if (availCount == 0) return -1;

            float simTime = view is EntityRepository w ? w.SimulationTime : 0f;
            // ⭐ CE-202: the xorshift that used to be inlined here now lives in SimRng, so the oracle
            //   below and this curated kernel draw from ONE generator instead of two copies.
            var rng = SimRng.FromSim((int)self.Index, currentWave, simTime);
            return avail[rng.NextInt(0, availCount)];
        }

        /// <summary>
        /// Picks the return-baseline slot whose interpolated world position is closest (distance²) to the
        /// firing slot at (<paramref name="slotX"/>, <paramref name="slotY"/>) — the oracle's
        /// <c>PickClosestBaselineSlot</c>. First pass: closest UNRESERVED slot (bit clear in
        /// <paramref name="reservedMask"/>); if all reserved, second pass: closest regardless. Pure — no
        /// world/context needed (<c>TrailingContext:"None"</c>).
        /// </summary>
        [BlueprintCallable("Slots")]
        public static int PickClosestBaselineSlot(
            float baselineStartX, float baselineStartY, float baselineEndX, float baselineEndY,
            ushort reservedMask, float slotX, float slotY, int totalSlots)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int j = 0; j < totalSlots; j++)
            {
                if ((reservedMask & (1 << j)) != 0) continue;
                float d = DistSq(baselineStartX, baselineStartY, baselineEndX, baselineEndY, slotX, slotY, j, totalSlots);
                if (d < bestDist) { bestDist = d; best = j; }
            }
            if (best >= 0) return best;

            bestDist = float.MaxValue;
            for (int j = 0; j < totalSlots; j++)
            {
                float d = DistSq(baselineStartX, baselineStartY, baselineEndX, baselineEndY, slotX, slotY, j, totalSlots);
                if (d < bestDist) { bestDist = d; best = j; }
            }
            return best;
        }

        private static float DistSq(
            float bStartX, float bStartY, float bEndX, float bEndY, float slotX, float slotY, int j, int totalSlots)
        {
            float bt = totalSlots > 1 ? (float)j / (totalSlots - 1) : 0.5f;
            float bx = bStartX + (bEndX - bStartX) * bt;
            float by = bStartY + (bEndY - bStartY) * bt;
            float dx = bx - slotX, dy = by - slotY;
            return dx * dx + dy * dy;
        }
    }
}
