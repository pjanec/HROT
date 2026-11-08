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

    /// <summary>⭐ CE-4330 ⑨ (📄 docs/DESIGN_Recording_Storage.md §3 P3) — the storage transfer this host's master is running now (an archive / import), or null.
    /// The quit guard asks about it ONLY here: the transfer runs where the master runs. Read on the frame thread.</summary>
    StorageJob? RunningTransfer => null;
}
