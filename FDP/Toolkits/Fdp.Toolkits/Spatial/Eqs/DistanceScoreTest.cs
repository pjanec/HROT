using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by distance from the anchor in <see cref="FromSlot"/> (default 0 = self), linear over the sensor's
    /// <see cref="EqsSensor.SearchRadius"/>: near = 1 → far = 0, or the reverse when <see cref="PreferFar"/> (a retreat point
    /// AWAY from the threat). 3-D distance (P3D-205). Runs in the ScoreCheap phase.
    /// ⭐ §19: the anchor resolves through <see cref="EqsContext"/>, so a child sensor (whose carrier has no position) scores
    /// from its parent. ⛔ SUPERSEDED: read the observer's <c>SimTransform</c> only.
    /// ⭐ <c>CE-2164</c> (R-3004) — from ANOTHER slot (a retreat away from the threat) the scale is the distances the candidates can
    /// have: they lie within the radius of the self (slot 0), so from <c>max(0, d − R)</c> to <c>d + R</c> where d is the self's own
    /// distance. 📌 It was 0…R from the anchor: a threat beyond the radius put every candidate at 1 — "farther" never decided, and
    /// on open ground the retreat took the nearest point. From the self (slot 0) d = 0, so the scale is 0…R as before.
    /// </summary>
    public sealed class DistanceScoreTest : IEqsTest
    {
        /// <summary>The context slot distances are measured from. Default 0 (Self).</summary>
        public byte FromSlot { get; set; } = 0;
        /// <summary>True ⇒ farther scores higher. Default false (nearer scores higher).</summary>
        public bool PreferFar { get; set; }
        /// <summary>The score at the preferred end. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;
            if (!EqsContext.AnchorPosition(view, observer, sensor, FromSlot, out var from)) return;
            float lo = 0f, span = maxDist;
            if (FromSlot != 0 && EqsContext.AnchorPosition(view, observer, sensor, 0, out var self))
            {
                float d = Vector3.Distance(from, self);
                lo = MathF.Max(0f, d - maxDist);
                span = d + maxDist - lo;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];
                if (candidate.EntityId == -1L) continue;
                var targetPos = new Vector3(candidate.PositionX, candidate.PositionY, candidate.PositionZ);
                float near = 1.0f - Math.Clamp((Vector3.Distance(from, targetPos) - lo) / span, 0f, 1f);
                candidate.Score += Weight * (PreferFar ? 1f - near : near);
            }
        }
    }
}
