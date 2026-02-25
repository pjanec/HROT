using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.Kernel.Collections;

namespace CarKinem.Systems
{
    /// <summary>
    /// Builds spatial hash grid from vehicle positions each frame.
    /// Publishes grid as singleton component.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class SpatialHashSystem : ComponentSystem
    {
        private SpatialHashGrid _grid;
        
        protected override void OnCreate()
        {
            // Grid dimensions and origin are defined in SpatialHashConstants.
            // GridWidth × CellSizeMeters = 750 m X coverage; origin at (-375,-375)
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
        
        protected override void OnUpdate()
        {
            _grid.Clear();
            
            // Query all vehicles (universal query via SimTransform)
            var query = World.Query().With<SimTransform>().Build();
            
            foreach (var entity in query)
            {
                var tf = World.GetComponent<SimTransform>(entity);
                _grid.Add(entity, new Vector2(tf.Position.X, tf.Position.Y));
            }
            
            // Publish as singleton (Data-Oriented pattern)
            World.SetSingleton(new SpatialGridData { Grid = _grid });
        }
        
        protected override void OnDestroy()
        {
            _grid.Dispose();
        }
    }
}
