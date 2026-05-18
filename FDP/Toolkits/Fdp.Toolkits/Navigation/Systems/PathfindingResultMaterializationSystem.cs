using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Materializes <see cref="PathfindingResultEvent"/>s published by
    /// <see cref="PathfindingSolverSystem"/> into the <see cref="PathfindingBatchData"/> ring buffer
    /// so the Brain BTree can read results without any locking.
    ///
    /// <para><b>Execution phase:</b> <see cref="SystemPhase.Input"/>, so results are visible to
    /// BTree nodes in the same frame's Simulation phase.</para>
    ///
    /// <para><b>Thread safety:</b> runs on the main thread only.  Safe to mutate struct fields
    /// via <see cref="EntityRepository"/>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class PathfindingResultMaterializationSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<PathfindingResultEvent>();
            if (events.IsEmpty) return;

            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            ref var batch = ref repo.GetSingleton<PathfindingBatchData>();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                int slot = (int)((uint)evt.RequestId % (uint)PathfindingBatchData.DefaultCapacity);

                batch.Results[slot] = new PathResult
                {
                    RequestId           = evt.RequestId,
                    IsReachable         = evt.IsReachable,
                    TotalDistanceMeters = evt.TotalDistanceMeters,
                    RouteHandle         = evt.RouteHandle,
                    SourceNodeId        = evt.SourceNodeId,
                };
            }
        }
    }
}
