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
            bus.RegisterManaged<NodeCapabilitiesEvent>();   // CE-285 (C-cap): static capability tokens, published once at join.
            bus.RegisterManaged<ClusterStateTransitionedEvent>();
            bus.RegisterManaged<OrchestratorContextChangedEvent>();
            bus.RegisterManaged<InSyncSetChangedEvent>();
            bus.RegisterManaged<NodeIdRequestEvent>();   // C1 — master-assigned node ids
            bus.RegisterManaged<NodeIdGrantEvent>();   // C6 — the master's InSync set, to every node's cluster cache
            bus.RegisterManaged<ClusterStateUpdateEvent>();
            bus.RegisterManaged<AssetInventoryUpdateEvent>();
            bus.RegisterManaged<EpisodeStateChangedEvent>();
            bus.RegisterManaged<ClusterOpIntent>();

            // Cluster Op Intents
            bus.RegisterManaged<TransitionStateIntent>();
            bus.RegisterManaged<ManageEpisodeIntent>();
            bus.RegisterManaged<SeekReplayIntent>();
            bus.RegisterManaged<SetReplayAxisIntent>();   // ⭐ CE-3329 R-3058
            bus.RegisterManaged<SetReplayLoopIntent>();   // ⭐ CE-3329 §12n
            bus.RegisterManaged<SetRunSettingsIntent>();   // ⭐ CE-4090 D14 — Record live runs
            bus.RegisterManaged<ReplaySessionPublishedEvent>();   // ⭐ CE-3329 rows 32, 45 — the session layer, to the DDS writer
            bus.RegisterManaged<ReplaySessionUpdateEvent>();      // ⭐ CE-3329 — the session layer, folded by every host
            bus.RegisterManaged<GeoReferencePublishedEvent>();    // ⭐ AQ107 C — the NED wire's frame origin, to the DDS writer
            bus.RegisterManaged<CancelOperationIntent>();
            bus.RegisterManaged<ExecuteStorageOpIntent>();
            bus.RegisterManaged<StorageOpCompletedEvent>();
            bus.RegisterManaged<TakeCheckpointIntent>();
            bus.RegisterManaged<CollectCheckpointIntent>();   // ⭐ CE-3299 B3
            bus.RegisterManaged<RestoreCheckpointIntent>();   // ⭐ CE-3299 B4
            bus.RegisterManaged<CheckUnsavedIntent>();   // ⭐ CE-4093 D20 — the unsaved-changes check round
            bus.RegisterManaged<LoadZoneIntent>();
            // ⭐ E4 — the terrain-asset build op's intent.
            bus.RegisterManaged<BuildTerrainAssetIntent>();
            // ⭐ CE-3021 — publish / refresh an author's asset tree.
            bus.RegisterManaged<AssetOpIntent>();
            bus.RegisterManaged<ExecuteDiagnosticDumpIntent>();

            // ⭐ BP-509 — the scenario load's staging→runtime id table (a managed Dictionary).
            bus.RegisterManaged<StagingRemapPublishedEvent>();

            // Time Control Intents (Domain)
            bus.RegisterManaged<PauseTimeIntent>();
            bus.RegisterManaged<SnapTimeIntent>();
            bus.RegisterManaged<JumpTimeIntent>();   // ⭐ CE-3329 R4
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
