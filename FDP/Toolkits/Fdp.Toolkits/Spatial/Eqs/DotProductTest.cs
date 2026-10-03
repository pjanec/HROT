using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ Scores the BEARING of each candidate around a pivot (§5.4 <c>DotProduct</c>, §19.6). The reference direction is
    /// pivot → <see cref="ReferenceSlot"/>; the candidate direction is pivot → candidate; d = cos(angle between them).
    /// <list type="bullet">
    ///   <item>Score += <see cref="Weight"/> × (1 − |d − <see cref="PreferredDot"/>| / 2): full weight at the preferred bearing.
    ///     Flanking around a target = pivot slot 1, reference slot 0 (self), preferred 0 (the side) or −1 (behind).</item>
    ///   <item>Flag bit 6 <c>IsPreferredSide</c> when |d − preferred| ≤ <see cref="Tolerance"/>.</item>
    ///   <item>Planar (XY) — bearing, not elevation. Missing pivot or reference ⇒ nothing scored.</item>
    /// </list>
    /// </summary>
    public sealed class DotProductTest : IEqsTest
    {
        /// <summary>The context slot at the centre of the bearing. Default 1 (Target).</summary>
        public byte PivotSlot { get; set; } = 1;
        /// <summary>The context slot that defines bearing zero. Default 0 (Self).</summary>
        public byte ReferenceSlot { get; set; } = 0;
        /// <summary>The cosine to aim for: 1 = towards the reference, 0 = side-on, −1 = opposite. Default 0.</summary>
        public float PreferredDot { get; set; } = 0f;
        /// <summary>|d − preferred| within which bit 6 is set. Default 0.35 (≈ ±20° around a side-on bearing).</summary>
        public float Tolerance { get; set; } = 0.35f;
        /// <summary>The score at the preferred bearing. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (!EqsContext.AnchorPosition(view, observer, sensor, PivotSlot, out var pivot)) return;
            if (!EqsContext.AnchorPosition(view, observer, sensor, ReferenceSlot, out var reference)) return;
            var refDir = new Vector2(reference.X - pivot.X, reference.Y - pivot.Y);
            if (refDir.LengthSquared() < 1e-6f) return;
            refDir = Vector2.Normalize(refDir);

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                var dir = new Vector2(c.PositionX - pivot.X, c.PositionY - pivot.Y);
                if (dir.LengthSquared() < 1e-6f) continue;
                float d   = Vector2.Dot(refDir, Vector2.Normalize(dir));
                float off = MathF.Abs(d - PreferredDot);
                c.Score += Weight * (1f - Math.Clamp(off / 2f, 0f, 1f));
                c.FlagsMeaningful |= (short)(1 << 6);
                if (off <= Tolerance) c.Flags |= (short)(1 << 6);
            }
        }
    }
}
