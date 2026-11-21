using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ High ground (§19.6 — a test the §5.4 list does not name, cheap now that the terrain has heights): each candidate's
    /// height above the anchor in <see cref="RelativeToSlot"/> (default 1, the target), linear over <see cref="Range"/>.
    /// Score += <see cref="Weight"/> × clamp((z − anchorZ) / Range, 0, 1). Missing anchor ⇒ nothing scored.
    /// </summary>
    public sealed class HeightScoreTest : IEqsTest
    {
        /// <summary>The context slot heights are measured from. Default 1 (Target).</summary>
        public byte RelativeToSlot { get; set; } = 1;
        /// <summary>Metres above the anchor that earn the full weight. Default 6 (two storeys).</summary>
        public float Range { get; set; } = 6f;
        /// <summary>The score at or above <see cref="Range"/>. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (Range <= 0f) return;
            if (!EqsContext.AnchorPosition(view, observer, sensor, RelativeToSlot, out var anchor)) return;
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                c.Score += Weight * Math.Clamp((c.PositionZ - anchor.Z) / Range, 0f, 1f);
            }
        }
    }
}
