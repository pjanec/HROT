using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Materializes <see cref="AreaQueryResultEvent"/>s published by
    /// <see cref="AreaQuerySolverSystem"/> into the <see cref="AreaQueryBatchData"/> ring buffer
    /// and advances <see cref="EqsTargetPool.NextFreeIndex"/> so the Brain BTree can read results
    /// without any locking.
    ///
    /// <para><b>Execution phase:</b> <see cref="SystemPhase.Input"/>, so results are visible to
    /// BTree nodes in the same frame's Simulation phase.</para>
    ///
    /// <para><b>Thread safety:</b> runs on the main thread only.  Safe to mutate struct fields
    /// via <see cref="EntityRepository.SetSingleton{T}"/>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class AreaQueryResultMaterializationSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<AreaQueryResultEvent>();
            if (events.IsEmpty) return;

            if (view is not EntityRepository repo) return;

            if (!repo.HasSingleton<AreaQueryBatchData>()) return;
            if (!repo.HasSingleton<EqsTargetPool>()) return;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
            var pool = repo.GetSingleton<EqsTargetPool>();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                int slot = (int)((uint)evt.RequestId % (uint)AreaQueryBatchData.DefaultCapacity);

                batch.Results[slot] = new AreaQueryResult
                {
                    RequestId         = evt.RequestId,
                    IsReady           = true,
                    TargetCount       = evt.TargetCount,
                    TargetGroupHandle = evt.TargetGroupHandle,
                    SourceNodeId      = evt.SourceNodeId,
                };

                // Last event in the batch wins; events are produced sequentially by the solver
                // so the final event reflects the post-tick pool cursor.
                pool.NextFreeIndex = evt.NewPoolNextFreeIndex;
            }

            repo.SetSingleton(pool);
        }
    }
}
