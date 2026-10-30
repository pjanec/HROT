using Fdp.Toolkit.Orchestration;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐ <c>CE-3354</c> ⑥ — a subsystem that HOSTS the cluster master in its process (the orchestrator; the editor's embedded core): what the runner's command-line startup load drives. The runner sends the SAME intents HTTP does
/// (<see cref="IClusterMasterControl"/>) and follows the cluster state; <see langword="null"/> from either while the host is not initialised yet.
/// </summary>
public interface IHostsClusterMaster
{
    /// <summary>The way to publish master intents onto the bus the master reads; null before the host is initialised.</summary>
    IClusterMasterControl? ClusterMasterControl { get; }

    /// <summary>The cluster state as this host sees it; null before the host is initialised.</summary>
    ClusterState? ClusterStateNow { get; }
}
