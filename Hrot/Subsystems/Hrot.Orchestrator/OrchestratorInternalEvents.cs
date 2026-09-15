using System;
using System.Collections.Generic;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

// CE-278: GlobalContextManifestReadyEvent retired — it carried the SaveScenario=2 Orchestrator.json
// manifest entry from GlobalContextProcessManager to StorageProcessManager; both ends are removed.

/// <summary>
/// Published by <see cref="ClusterMaster"/> when a PrefetchScenario operation step is
/// encountered in a trajectory or a standalone PrefetchScenario op request is received.
/// Consumed by <see cref="AssetPrefetchProcessManager"/>.
/// </summary>
internal struct ExecutePrefetchIntent
{
    /// <summary>Original cluster op request ID. Used to report failure.</summary>
    public Guid RequestId;
    /// <summary>Logical scenario identifier (sub-directory under NAS root).</summary>
    public string ScenarioId;
    /// <summary>Active node IDs captured at fan-out time (for PrefetchFiles fan-out).</summary>
    public List<int> ActiveNodeIds;
}

/// <summary>
/// Published by <see cref="AssetPrefetchProcessManager"/> when the gateway
/// <c>PrefetchScenarioAsync</c> task completes (success or failure).
/// Consumed by <see cref="AssetPrefetchProcessManager"/> to drive the PrefetchFiles fan-out or
/// report a timeout failure.
/// </summary>
internal struct PrefetchStagingCompletedEvent
{
    public Guid   RequestId;
    public string ScenarioId;
    public bool   IsSuccess;
    /// <summary>Active node IDs to fan out PrefetchFiles to on success.</summary>
    public List<int> ActiveNodeIds;
}

/// <summary>
/// Published by <see cref="ClusterMaster"/> when an ExportArchive SerializeLocal fan-out
/// is initiated. Carries the archive request context so <see cref="StorageProcessManager"/>
/// can route the completed NAS pull to the correct archive request ID.
/// </summary>
internal struct ExportArchiveBegunEvent
{
    /// <summary>SerializeLocal fan-out transaction ID (key in the pending-transactions dict).</summary>
    public Guid TransactionId;
    /// <summary>Original ExportArchive request ID for the final status publication.</summary>
    public Guid ArchiveRequestId;
    /// <summary>Cancellation token source for the NAS pull; also stored in ClusterMaster._activeCancellations.</summary>
    public System.Threading.CancellationTokenSource Cts;
}

/// <summary>
/// CE-277(c2) — published by <see cref="ClusterMaster"/> when a <c>SaveScenarioJson</c> SerializeLocal
/// fan-out is initiated. Carries the transaction id → scenario name mapping so
/// <see cref="StorageProcessManager"/> can, after the NAS pull, run <c>ScenarioMergeCore</c> on the pulled
/// per-node slices and write the one canonical <c>scenarios/&lt;name&gt;/scenario.json</c>.
/// </summary>
internal struct SaveScenarioJsonBegunEvent
{
    /// <summary>SerializeLocal fan-out transaction ID (matches the ClusterOpCompletedEvent.RequestId).</summary>
    public Guid TransactionId;
    /// <summary>Relative scenario name / subfolder under the NAS scenarios root (never a filesystem path).</summary>
    public string ScenarioName;
}

/// <summary>
/// Published by <see cref="ClusterMaster"/> when an ImportArchive operation is initiated.
/// Consumed by <see cref="StorageProcessManager"/> to perform the NAS-to-node prefetch via
/// <see cref="StorageGatewayModule.PrefetchArchiveAsync"/>.
/// </summary>
internal struct ImportArchiveBegunEvent
{
    /// <summary>Original ImportArchive request ID for the final status publication.</summary>
    public Guid RequestId;
    /// <summary>Exercise GUID in text form, used as subdirectory under the NAS root.</summary>
    public string ExerciseId;
    /// <summary>Per-node distribution targets for the archive prefetch.</summary>
    public System.Collections.Generic.List<NodeDistributionTarget> Targets;
    /// <summary>Cancellation token source; also stored in ClusterMaster._activeCancellations.</summary>
    public System.Threading.CancellationTokenSource Cts;
}
