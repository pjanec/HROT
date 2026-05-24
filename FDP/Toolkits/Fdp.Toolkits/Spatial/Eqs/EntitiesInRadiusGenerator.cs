using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates entity-shaped EQS candidates from the spatial hash grid.
    /// Uses stackalloc for the intermediate (Entity, Vector2) buffer -- zero heap allocation.
    /// </summary>
    public sealed class EntitiesInRadiusGenerator : IEqsGenerator
    {
        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonUnmanaged<SpatialGridData>()) return 0;
            if (!repo.HasComponent<SimTransform>(observer)) return 0;

            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new Vector2(tf.Position.X, tf.Position.Y);

            ref readonly var gridData = ref repo.GetSingletonUnmanaged<SpatialGridData>();

            // Intermediate stackalloc buffer: (Entity, Vector2) pairs from the grid.
            Span<(Entity entity, Vector2 pos)> neighbors =
                stackalloc (Entity, Vector2)[candidates.Length];

            int rawCount = gridData.Grid.QueryNeighbors(obsPos, sensor.SearchRadius, neighbors);

            int validCount = 0;
            for (int i = 0; i < rawCount; i++)
            {
                // Exclude the observer entity itself from results.
                if (neighbors[i].entity == observer) continue;

                candidates[validCount++] = new EqsResult
                {
                    EntityId  = (long)neighbors[i].entity.PackedValue,
                    PositionX = neighbors[i].pos.X,
                    PositionY = neighbors[i].pos.Y,
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return validCount;
        }
    }
}
