using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by inverse-linear path cost (closer path = higher score).
    /// Rejects candidates where no navmesh path exists (EntityId = -1L).
    /// Runs in ScoreExpensive phase.
    /// </summary>
    public sealed class PathCostScoreTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!repo.HasComponent<SimTransform>(observer)) return;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new Vector2(tf.Position.X, tf.Position.Y);

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector2(candidate.PositionX, candidate.PositionY);

                if (navmesh.TryGetPathDistance(obsPos, targetPos, out float pathDist))
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
