using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.Modules.Geographic.Components;
using ModuleHost.Core.Abstractions;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Runs at the start of every frame (<see cref="SystemPhase.Input"/>).
    /// Ensures the <see cref="TerrainQueryBatchData"/> singleton exists and
    /// resets its <see cref="TerrainQueryBatchData.Count"/> to zero so that
    /// <see cref="TerrainQuerySubmitSystem"/> starts each tick with a clean slate.
    ///
    /// <para>
    /// The singleton is allocated here via the world on first access and
    /// ownership is transferred to the world (same pattern as
    /// <c>PhysicsToolkitModule</c> / <c>RaycastBatchData</c>).
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class TerrainQueryInitializationSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = (EntityRepository)view;

            if (!world.HasSingleton<TerrainQueryBatchData>())
            {
                world.SetSingleton(new TerrainQueryBatchData
                {
                    Requests = new NativeArray<TerrainQueryRequest>(TerrainQueryBatchData.DefaultCapacity, Allocator.Persistent),
                    Results  = new NativeArray<TerrainQueryResult>(TerrainQueryBatchData.DefaultCapacity, Allocator.Persistent),
                    Count    = 0,
                });
                return; // Count already zero on fresh allocation
            }

            ref var batch = ref world.GetSingleton<TerrainQueryBatchData>();
            batch.Count = 0;
        }
    }
}
