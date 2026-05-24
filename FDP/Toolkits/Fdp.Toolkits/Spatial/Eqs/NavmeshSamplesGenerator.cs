using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates positional (EntityId=0) EQS candidates by sampling random reachable
    /// navmesh positions within the sensor's search radius.
    /// </summary>
    public sealed class NavmeshSamplesGenerator : IEqsGenerator
    {
        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return 0;
            if (!repo.HasComponent<SimTransform>(observer)) return 0;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var center = new Vector2(tf.Position.X, tf.Position.Y);

            // Intermediate stackalloc buffer for raw positions.
            Span<Vector2> rawPoints = stackalloc Vector2[candidates.Length];
            int rawCount = navmesh.GetRandomPointsInRadius(center, sensor.SearchRadius, rawPoints);

            for (int i = 0; i < rawCount; i++)
            {
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L, // Positional candidate.
                    PositionX = rawPoints[i].X,
                    PositionY = rawPoints[i].Y,
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return rawCount;
        }
    }
}
