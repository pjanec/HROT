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

                // The spatial grid only returns 2D positions, so source the real altitude from
                // each neighbour's authoritative SimTransform (P3D-203). Defensive: skip neighbours
                // lacking a SimTransform (their position is unknowable here).
                if (!repo.HasComponent<SimTransform>(neighbors[i].entity)) continue;
                ref readonly var neighborTf = ref repo.GetComponentRO<SimTransform>(neighbors[i].entity);

                candidates[validCount++] = new EqsResult
                {
                    EntityId  = (long)neighbors[i].entity.PackedValue,
                    PositionX = neighbors[i].pos.X,
                    PositionY = neighbors[i].pos.Y,
                    PositionZ = neighborTf.Position.Z,
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return validCount;
        }
    }
}
