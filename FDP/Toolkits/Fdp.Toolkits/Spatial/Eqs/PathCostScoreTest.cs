using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by inverse-linear path cost (closer path = higher score).
    /// Rejects candidates where no navmesh path exists (EntityId = -1L).
    /// Runs in ScoreExpensive phase.
    /// </summary>
    public sealed class PathCostScoreTest : IEqsTest, IEqsCostWeight
    {
        /// <inheritdoc/>
        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Path;   // CE-3037 — path weight (DESIGN_Sensors_And_Doctrine §5.3)

        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!EqsContext.SelfPosition(repo, observer, sensor, out var obsPos)) return;   // ⭐ §19 H3
            uint layer = EqsContext.SelfLayer(repo, observer, sensor);                      // ⭐ §19 H5

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            // INavmeshProvider is Z-up like Sim (R-182 / W7): no swizzle (P3D-205).

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector3(candidate.PositionX, candidate.PositionY, candidate.PositionZ);

                float pathDist = navmesh.PathCost(obsPos, targetPos, layer);
                if (pathDist != float.MaxValue)
                {
                    // Inverse-linear falloff: shorter path = higher score. Additive.
                    float score = 1.0f - Math.Clamp(pathDist / maxDist, 0f, 1f);
                    candidate.Score += score;
                }
                else
                {
                    candidate.EntityId = -1L; // Reject: no path.
                }
            }
        }
    }
}
