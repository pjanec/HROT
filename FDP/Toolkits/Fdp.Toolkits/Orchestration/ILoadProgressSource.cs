namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> part B (R-3070, R-3072) — a cluster-state handler that runs a node's LOAD (today the one <c>LoadPhaseChain</c>), as the slave sees it: it names the step it is on.
    /// ⭐ The slave needs no Hrot type for this — the toolkit owns the seam, the chain implements it. A pending prepare whose handler implements it IS "a load held on this node"
    /// (<see cref="NodePhase.Loading"/>), which is what <c>ClusterSlave.CancelLoad</c> may cancel.
    /// </summary>
    public interface ILoadProgressSource : IClusterStateHandler
    {
        /// <summary>The name of the load step in flight (<i>"KnowledgeBase"</i>, <i>"Terrain"</i>, <i>"ScenarioEntities"</i>), or <see langword="null"/> when none. Read from the frame thread; a worker may write it.</summary>
        string? CurrentLoadStep { get; }
    }
}
