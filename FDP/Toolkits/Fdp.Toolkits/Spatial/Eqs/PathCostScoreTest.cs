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
            var obsPos = new Vector3(tf.Position.X, 0f, tf.Position.Y);

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector3(candidate.PositionX, 0f, candidate.PositionY);

                // TODO NAV-P0-T5: use NavAgentProfile.PreferredLayerMask from ctx.Self
                float pathDist = navmesh.PathCost(obsPos, targetPos);
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
