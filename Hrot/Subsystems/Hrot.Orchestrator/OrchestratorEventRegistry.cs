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
        bus.RegisterManaged<ExecutePrefetchIntent>();
        bus.RegisterManaged<PrefetchStagingCompletedEvent>();
        bus.RegisterManaged<PrefetchDistributionCompletedEvent>();   // L8 — what a parked transition waits on.
        bus.RegisterManaged<ExportArchiveBegunEvent>();
        bus.RegisterManaged<SaveScenarioJsonBegunEvent>();
        bus.RegisterManaged<ImportArchiveBegunEvent>();
        bus.RegisterManaged<MergeLogsIntent>();
        bus.RegisterManaged<LogMergeCompletedEvent>();
    }
}
