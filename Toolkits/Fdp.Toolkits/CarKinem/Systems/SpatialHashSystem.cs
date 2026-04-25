using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// Builds spatial hash grid from positions of physics-collidable entities each frame.
    /// Only entities that carry a <c>PhysicsCollider</c> component (component ID
    /// <see cref="GlobalComponentIds.PhysicsCollider"/>) are inserted, ensuring that
    /// non-collidable entities such as observation cameras, raw waypoints, and decoupled
    /// projectiles do not incur broadphase insertion cost.
    /// Publishes grid as singleton component.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class SpatialHashSystem : IEcsModuleSystem
    {
        private SpatialHashGrid _grid;

        public SpatialHashSystem()
        {
            // Grid dimensions and origin are defined in SpatialHashConstants.
            // GridWidth x CellSizeMeters = 750 m X coverage; origin at (-375,-375)
            // centres the grid on world origin, accommodating the Urban Ambush APC spawn at y=-80.
            _grid = SpatialHashGrid.Create(
                SpatialHashConstants.GridWidth,
                SpatialHashConstants.GridHeight,
                SpatialHashConstants.CellSizeMeters,
                SpatialHashConstants.MaxEntities,
                Allocator.Persistent,
                originX: SpatialHashConstants.OriginX,
                originY: SpatialHashConstants.OriginY);
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(SpatialHashSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            _grid.Clear();

            // Query only physics-collidable entities (SimTransform + PhysicsCollider).
            // Using WithComponentId avoids a circular project dependency: FDP.Toolkit.Physics
            // already references FDP.Toolkit.CarKinem, so CarKinem cannot reference Physics.
            // GlobalComponentIds.PhysicsCollider is defined in Fdp.Core which CarKinem already references.
            var query = repo.Query()
                .With<SimTransform>()
                .WithComponentId(GlobalComponentIds.PhysicsCollider)
                .Build();

            foreach (var entity in query)
            {
                var tf = repo.GetComponent<SimTransform>(entity);
                _grid.Add(entity, new Vector2(tf.Position.X, tf.Position.Y));
            }

            // Publish as singleton (Data-Oriented pattern)
            repo.SetSingleton(new SpatialGridData { Grid = _grid });
        }
    }
}
