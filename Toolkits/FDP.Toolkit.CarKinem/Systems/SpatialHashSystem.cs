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
            // 150x150 cells * 5m = 750x750m world coverage
            _grid = SpatialHashGrid.Create(150, 150, 5.0f, 100000, Allocator.Persistent);
        }
        
        protected override void OnUpdate()
        {
            _grid.Clear();
            
            // Query all vehicles (universal query via SimTransform)
            var query = World.Query().With<SimTransform>().Build();
            
            foreach (var entity in query)
            {
                var tf = World.GetComponent<SimTransform>(entity);
                _grid.Add(entity.Index, new Vector2(tf.Position.X, tf.Position.Y));
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
