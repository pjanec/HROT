using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Perception;
using FDP.Toolkit.Perception.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Main-thread wrapper for the perception broadphase pipeline.
    /// Runs LocalGridBuilder and VisionBroadphase each tick.
    /// </summary>
    public sealed class PerceptionBroadphaseSystem : ComponentSystem
    {
        private readonly SpatialHashGrid _localGrid;
        private readonly LocalGridBuilderSystem _gridBuilder;
        private readonly VisionBroadphaseSystem _visionBroadphase;

        public PerceptionBroadphaseSystem()
        {
            _localGrid = SpatialHashGrid.Create(
                PerceptionConstants.LocalGridWidth,
                PerceptionConstants.LocalGridHeight,
                PerceptionConstants.LocalGridCellSize,
                PerceptionConstants.LocalGridMaxEntities,
                Allocator.Persistent);

            _gridBuilder = new LocalGridBuilderSystem(_localGrid);
            _visionBroadphase = new VisionBroadphaseSystem(_localGrid);
        }

        protected override void OnUpdate()
        {
            var view = (ISimulationView)World;
            var dt = DeltaTime;

            _gridBuilder.Execute(view, dt);
            _visionBroadphase.Execute(view, dt);
        }

        protected override void OnDestroy()
        {
            _localGrid.Dispose();
        }
    }
}
