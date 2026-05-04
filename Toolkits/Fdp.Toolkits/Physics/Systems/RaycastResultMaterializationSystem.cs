using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics.Systems
{
    /// <summary>
    /// Materializes <see cref="RaycastResultEvent"/>s published by <see cref="RaycastSolverSystem"/>
    /// into the <see cref="RaycastBatchData"/> ring buffer so BTree consumers can poll results
    /// without any locking.
    ///
    /// <para><b>Execution phase:</b> <see cref="SystemPhase.Input"/>, so results are visible to
    /// BTree nodes in the same frame's Simulation phase.</para>
    ///
    /// <para><b>Thread safety:</b> runs on the main thread only.  Safe to mutate struct fields
    /// via <see cref="EntityRepository"/>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class RaycastResultMaterializationSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<RaycastResultEvent>();
            if (events.IsEmpty) return;

            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            ref var batch = ref repo.GetSingleton<RaycastBatchData>();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                int slot = (int)((uint)evt.Hit.RayId % (uint)PhysicsConstants.RaycastBatchCapacity);
                batch.Hits[slot] = evt.Hit;
            }
        }
    }
}
