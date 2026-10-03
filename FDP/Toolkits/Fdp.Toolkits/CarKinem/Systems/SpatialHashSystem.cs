using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;

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
        /// <summary>The grid's current placement — a rail reads it; consumers read <c>SpatialGridData</c>.</summary>
        public GridGeometry Geometry => new(_grid.OriginX, _grid.OriginY, _grid.CellSize);

        private SpatialHashGrid _grid;

        // ⭐ CE-3018 — the terrain this grid was last fitted to (reference compare: a commit swaps the singleton object).
        private TerrainWorld? _fittedTo;

        public SpatialHashSystem()
        {
            // Grid dimensions and origin are defined in SpatialHashConstants.
            // GridWidth x CellSizeMeters = 1500 m X coverage; origin at (-750,-750)
            // centres the grid on world origin and covers larger scenario extents.
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

            // ⭐ CE-3018 (W9) — follow the resident terrain: on a new world, REBASE (same memory, new origin/cell) so an
            //    entity outside today's [-750,750)² is no longer silently missing from avoidance. Main thread, the grid's
            //    only writer — the same thread that already clears it every frame (docs/DESIGN_Terrain_World.md §4.4).
            var world = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            if (!ReferenceEquals(world, _fittedTo))
            {
                _fittedTo = world;
                var g = SpatialGridFit.For(
                    new Vector2(SpatialHashConstants.OriginX, SpatialHashConstants.OriginY),
                    SpatialHashConstants.GridWidth, SpatialHashConstants.GridHeight, SpatialHashConstants.CellSizeMeters, world);
                _grid.Rebase(g.OriginX, g.OriginY, g.CellSize);
            }

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
