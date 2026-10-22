using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2232</c> (docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §4a) — prefer a HIDE that can be FOUGHT from: per
    /// candidate, the firing query's donut round it (<see cref="DonutGenerator.Point"/>, nearest ring first; points inside a wall dropped,
    /// <see cref="EqsTerrainSight.TryPlace"/>) is put through the firing query's own sight rule (<see cref="CheapLineOfSightTest"/>, the
    /// candidate's eye, <see cref="EqsLosRequire.Visible"/> on slot 1). A hide with a ring point that sees gains
    /// <see cref="Weight"/> × (1 − ½ · d / <see cref="Radius"/>), d the nearest such point; none ⇒ nothing. No terrain ⇒ nothing.
    /// 📐 Live <c>ua-javelin-lock</c>: every hide in the L-Block's inner corner was hidden and blind — no point within the 25 m firing
    /// ring saw the Javelin team — and the tank went from one blind hide to the next for 100 s without exposing.
    /// </summary>
    public sealed class FiringReachTest : IEqsTest, IEqsCostWeight
    {
        private readonly CheapLineOfSightTest _sees = new() { ContextSlotIndex = 1, Viewer = EqsLosViewer.Candidate, Require = EqsLosRequire.Visible };

        /// <summary>The ring's outer radius — <see cref="FindVehicleFiringPoint"/>'s (the cycle's default <c>FiringSearchRadius</c>). Default 25.</summary>
        public float Radius { get; set; } = 25f;

        /// <summary>Rings between <see cref="InnerFraction"/> × <see cref="Radius"/> and <see cref="Radius"/>. Default 2.</summary>
        public int Rings { get; set; } = 2;

        /// <summary>Points per ring (the firing query lays 16; a sample is enough here). Default 12.</summary>
        public int PointsPerRing { get; set; } = 12;

        /// <summary>The inner ring as a fraction of <see cref="Radius"/>. Default 0.3, the firing query's.</summary>
        public float InnerFraction { get; set; } = 0.3f;

        /// <summary>What a hide with a firing point next to it gains. Default 2: below hidden-from-all (3), above nearness + path (≤ 2).</summary>
        public float Weight { get; set; } = 2f;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public int CostPerCandidate => Rings * PointsPerRing * EqsCost.Sight;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (EqsTerrainSight.Sight(view, null) == null || Radius <= 0f || Rings <= 0 || PointsPerRing <= 0) return;
            var world = Fdp.Toolkit.World.WorldQuery.Of(view);
            Span<EqsResult> ring = stackalloc EqsResult[PointsPerRing];
            for (int c = 0; c < candidates.Length; c++)
            {
                ref var cand = ref candidates[c];
                if (cand.EntityId == -1L) continue;
                var at = new Vector2(cand.PositionX, cand.PositionY);
                for (int r = 0; r < Rings; r++)   // nearest ring first: the first ring that sees holds the nearest point
                {
                    int n = 0;
                    for (int k = 0; k < PointsPerRing; k++)
                        if (EqsTerrainSight.TryPlace(world, DonutGenerator.Point(at, Radius, Rings, PointsPerRing, InnerFraction, r, k), cand.PositionZ, out var p))
                            ring[n++] = new EqsResult { EntityId = 0L, PositionX = p.X, PositionY = p.Y, PositionZ = p.Z };
                    if (n == 0) continue;
                    _sees.ExecuteBatch(observer, ref sensor, view, ring.Slice(0, n));
                    float nearest = float.PositiveInfinity;
                    for (int i = 0; i < n; i++)   // judged AND kept — a sight the test skipped (its threat gate) is not a "sees"
                        if (ring[i].EntityId != -1L && (ring[i].FlagsMeaningful & (1 << 1)) != 0)
                            nearest = MathF.Min(nearest, Vector2.Distance(at, new Vector2(ring[i].PositionX, ring[i].PositionY)));
                    if (float.IsPositiveInfinity(nearest)) continue;
                    cand.Score += Weight * (1f - 0.5f * MathF.Min(1f, nearest / Radius));
                    break;
                }
            }
        }
    }
}
