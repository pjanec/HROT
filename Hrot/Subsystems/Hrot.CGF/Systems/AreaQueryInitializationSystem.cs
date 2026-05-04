using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Brain-tier system that resets the EQS batch state at the start of each
    /// simulation frame, mirroring the pattern used by the pathfinding and raycast
    /// initialization systems.
    ///
    /// <para><b>Responsibilities:</b></para>
    /// <list type="number">
    ///   <item>Resets <see cref="AreaQueryBatchData.Count"/> to zero so Brain BTree nodes
    ///   can submit new requests in the upcoming tick.</item>
    ///   <item>Resets the <see cref="EqsTargetPool"/> free list (zeroes all packed entity
    ///   handles) so the Muscle-tier solver can write fresh results into clean slots.</item>
    /// </list>
    ///
    /// <para><b>Execution phase:</b> <see cref="SystemPhase.Input"/> — registered as the
    /// first element in <c>CgfLogicPack.InputSystems</c>, so it runs before
    /// <c>BTreeTickSystem</c> and any Brain-tier translators, guaranteeing a clean EQS
    /// state at the start of every frame. (Note: <c>SystemPhase.PreInput</c> does not
    /// exist in this engine; explicit list ordering achieves the equivalent guarantee.)</para>
    ///
    /// <para>If <see cref="AreaQueryBatchData"/> is not present (singleton not yet
    /// initialized), the system does nothing.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class AreaQueryInitializationSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                return;

            AreaQueryBatchHelper.ResetBatch(repo);
        }
    }
}
