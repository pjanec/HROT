using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;

namespace Fdp.Toolkit.Orchestration
{
    public static class OrchestrationEventRegistry
    {
        public static void RegisterAll(FdpEventBus bus)
        {
            // Cluster CQRS Events
            bus.RegisterManaged<ClusterOpCompletedEvent>();
            bus.RegisterManaged<ExecuteNodeOpIntent>();
            bus.RegisterManaged<NodeOpCompletedEvent>();
            bus.RegisterManaged<NodeHeartbeatEvent>();
            bus.RegisterManaged<ClusterStateTransitionedEvent>();
            bus.RegisterManaged<ClusterStateUpdateEvent>();
            bus.RegisterManaged<AssetInventoryUpdateEvent>();
            bus.RegisterManaged<EpisodeStateChangedEvent>();
            bus.RegisterManaged<ClusterOpIntent>();

            // Cluster Op Intents
            bus.RegisterManaged<TransitionStateIntent>();
            bus.RegisterManaged<ManageEpisodeIntent>();
            bus.RegisterManaged<SeekReplayIntent>();
            bus.RegisterManaged<CancelOperationIntent>();
            bus.RegisterManaged<ExecuteStorageOpIntent>();
            bus.RegisterManaged<StorageOpCompletedEvent>();
            bus.RegisterManaged<TakeCheckpointIntent>();
            bus.RegisterManaged<LoadZoneIntent>();
            bus.RegisterManaged<ExecuteDiagnosticDumpIntent>();

            // Time Control Intents (Domain)
            bus.RegisterManaged<PauseTimeIntent>();
            bus.RegisterManaged<ResumeTimeIntent>();
            bus.RegisterManaged<StepTimeIntent>();
            bus.RegisterManaged<SetTimeScaleIntent>();
            bus.RegisterManaged<SlaveNodeSetUpdatedEvent>();
            bus.RegisterManaged<AdvanceFrameIntent>();
            bus.RegisterManaged<FrameStepCompletedEvent>();

            // Unmanaged Time Messages
            bus.Register<TkClusterStateChangedEvent>();
            bus.Register<SwitchTimeModeEvent>();
            bus.Register<TimeSyncOffsetCalculatedEvent>();
        }
    }
}
