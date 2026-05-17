using Fdp.Core;
using Hrot.Orchestrator.Events;

namespace Hrot.Orchestrator;

public static class OrchestratorEventRegistry
{
    /// <summary>
    /// Registers Orchestrator-internal events.
    /// Called by the standalone Orchestrator and the offline Editor (which hosts a local ClusterMaster).
    /// </summary>
    public static void RegisterInternalEvents(FdpEventBus bus)
    {
        bus.RegisterManaged<GlobalContextManifestReadyEvent>();
        bus.RegisterManaged<ExecutePrefetchIntent>();
        bus.RegisterManaged<PrefetchStagingCompletedEvent>();
        bus.RegisterManaged<ExportArchiveBegunEvent>();
        bus.RegisterManaged<ImportArchiveBegunEvent>();
        bus.RegisterManaged<MergeLogsIntent>();
        bus.RegisterManaged<LogMergeCompletedEvent>();
    }
}
