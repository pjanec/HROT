using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates positional (EntityId=0) EQS candidates from the ICoverProvider singleton.
    /// Uses stackalloc for the intermediate CoverPoint buffer -- zero heap allocation.
    /// </summary>
    public sealed class CoverPointsGenerator : IEqsGenerator
    {
        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonManaged<ICoverProvider>()) return 0;

            ICoverProvider provider = repo.GetSingletonManaged<ICoverProvider>()!;

            if (!repo.HasComponent<SimTransform>(observer)) return 0;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var center = new Vector2(tf.Position.X, tf.Position.Y);

            // Intermediate stackalloc buffer for raw cover points.
            Span<CoverPoint> rawPoints = stackalloc CoverPoint[candidates.Length];
            int rawCount = provider.GetCoverPointsInRadius(center, sensor.SearchRadius, rawPoints);

            for (int i = 0; i < rawCount; i++)
            {
                // EntityId = 0 marks a positional candidate (no entity attached).
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L,
                    PositionX = rawPoints[i].PositionX,
                    PositionY = rawPoints[i].PositionY,
                    PositionZ = rawPoints[i].PositionZ, // P3D-203: stream cover altitude.
                    Score     = rawPoints[i].Quality, // Seed score with cover quality.
                    Flags     = rawPoints[i].StanceHeight,
                };
            }

            return rawCount;
        }
    }
}
