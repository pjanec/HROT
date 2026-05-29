using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by proximity to the observer. Linear falloff: 1.0 at origin,
    /// 0.0 at SearchRadius. Skips rejected (-1L) candidates. Runs in the ScoreCheap phase.
    /// </summary>
    public sealed class DistanceScoreTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasComponent<SimTransform>(observer)) return;

            ref readonly var obsTf = ref repo.GetComponentRO<SimTransform>(observer);
            // Sim (Z-up) 3D distance: altitude differences now affect proximity scoring (P3D-205).
            var obsPos = new Vector3(obsTf.Position.X, obsTf.Position.Y, obsTf.Position.Z);

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip rejected candidates.
                if (candidate.EntityId == -1L) continue;

                // Use the position already packed by the generator (Sim Z-up).
                var targetPos = new Vector3(candidate.PositionX, candidate.PositionY, candidate.PositionZ);
                float dist = Vector3.Distance(obsPos, targetPos);

                // Linear falloff: closer = higher score. Additive.
                float score = 1.0f - Math.Clamp(dist / maxDist, 0f, 1f);
                candidate.Score += score;
            }
        }
    }
}
