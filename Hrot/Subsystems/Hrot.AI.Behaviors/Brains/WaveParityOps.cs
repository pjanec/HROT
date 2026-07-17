using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated wave-parity predicate for <c>DispatchWaveWithTargets</c> (architect Q#8-E). Reproduces the
    /// oracle's per-tank participation gate: small platoons (roster ≤ 3) send everyone every wave;
    /// otherwise a tank participates only when its <b>immutable</b> <c>Entity.Index</c> parity matches the
    /// current wave (<c>(sub.Index % 2) == CurrentWave</c>). Kept curated because <c>Entity.Index</c> has
    /// no read-node and the <c>%</c>/<c>||</c> composition is awkward visually. Pure
    /// (<c>TrailingContext:"None"</c>). Does not modify the C# oracle.
    /// </summary>
    public static class WaveParityOps
    {
        /// <summary>
        /// True when <paramref name="sub"/> should participate in the current wave:
        /// <c>rosterCount &lt;= 3 || (sub.Index % 2) == currentWave</c>.
        /// </summary>
        public static bool ShouldParticipate(Entity sub, int rosterCount, int currentWave)
            => rosterCount <= 3 || ((int)sub.Index % 2) == currentWave;
    }
}
