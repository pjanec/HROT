using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Published by <c>ClusterMaster</c> when a top-level cluster operation completes
    /// (either successfully or with a failure status code).
    /// Consumed by translators to write the DDS <c>ClusterOpStatus</c> topic.
    /// </summary>
    [EventId(9011)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ClusterOpCompletedEvent
    {
        public Guid RequestId;
        public OrchestrationStatusCode StatusCode;
        /// <summary>
        /// Pure domain result object (e.g. <c>MaxNetworkIdResult</c>).
        /// Translators serialize this to <c>ResultJson</c> for DDS.
        /// </summary>
        public object? ResultPayload;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> to fan-out a node-level operation to all
    /// participating <c>ClusterSlave</c> instances via the <c>FdpEventBus</c>.
    /// Must be routed via <c>PublishManaged</c> / <c>ConsumeManaged</c> because it
    /// contains a managed <c>object?</c> field.
    /// </summary>
    [EventId(9012)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ExecuteNodeOpIntent
    {
        public Guid TransactionId;
        public int TargetNodeId;
        public NodeOpType Operation;
        /// <summary>
        /// Strongly-typed payload struct (e.g. <c>TransitionNodePayload</c>).
        /// Handlers access it via: <c>if (intent.DomainPayload is MyPayload p) { ... }</c>
        /// Translators serialize this to <c>PayloadJson</c> for DDS.
        /// </summary>
        public object? DomainPayload;
    }

    /// <summary>
    /// Published by <c>ClusterSlave</c> after a node-level operation completes.
    /// Consumed by translators to write the DDS <c>NodeOpStatus</c> topic, and
    /// also consumed by <c>ClusterMaster</c> to correlate 2PC ACKs.
    /// </summary>
    [EventId(9013)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct NodeOpCompletedEvent
    {
        public Guid TransactionId;
        public NodeOpType Operation;
        public int NodeId;
        public OrchestrationStatusCode StatusCode;
        public bool IsParticipating;
        /// <summary>
        /// Operation-specific result data.  Known runtime types by operation:
        /// <list type="bullet">
        ///   <item><term><see cref="NodeOpType.SerializeLocal"/></term><description><c>FileManifestResult[]</c> — file paths written by the node</description></item>
        ///   <item><term>All other operations</term><description><c>null</c></description></item>
        /// </list>
        /// Translators in the Hrot layer are responsible for casting and serializing this payload.
        /// </summary>
        public object? ResultPayload;
    }

    /// <summary>
    /// Published by <c>ClusterSlave</c> once per second.
    /// Consumed by <c>NodeOpSlaveTranslator</c> to write <c>NodeHeartbeat</c> DDS topic.
    /// </summary>
    [EventId(9014)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct NodeHeartbeatEvent
    {
        public int    NodeId;
        public int    LocalStateId;
        public long   WallTicksUtc;
        public string SubsystemName;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> when the global cluster state transitions.
    /// Consumed by translators to write the DDS <c>ClusterStateTopic</c> topic.
    /// </summary>
    [EventId(9015)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ClusterStateTransitionedEvent
    {
        /// <summary>New cluster state.</summary>
        public ClusterState NewStateId;
        /// <summary>"Cluster" — identifies the global cluster state machine.</summary>
        public string SubsystemName;
        public Guid ExerciseId;
    }

    /// <summary>
    /// Published by <c>OrchestrationObserverTranslator</c> (DDS→bus) and by
    /// <c>ClusterMaster</c> (bus-mode) when the global cluster state transitions.
    /// Consumed by <c>ClusterUiCache</c> to update <c>CurrentState</c>.
    /// </summary>
    [EventId(9016)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ClusterStateUpdateEvent
    {
        /// <summary>New cluster state.</summary>
        public ClusterState CurrentState;
        public Guid ExerciseId;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> when the asset inventory is refreshed, and by
    /// <c>OrchestrationObserverTranslator</c> when a DDS <c>AssetInventoryTopic</c> arrives.
    /// Consumed by <c>ClusterOpMasterTranslator</c> to write the DDS inventory topic, and by
    /// <c>ClusterUiCache</c> to update <c>AvailableScenarios</c> / <c>AvailableExercises</c>.
    /// </summary>
    [EventId(9017)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct AssetInventoryUpdateEvent
    {
        public string[] LocalScenarios;
        public string[] LocalExercises;
        public string[] ArchivedExercises;
        public string[] UnarchivedLocalExercises;
    }

    /// <summary>
    /// Published by <c>EpisodeProcessManager</c> after the active episode set changes.
    /// Consumers (e.g. <c>ClusterUiCache</c>, tests) subscribe to this event instead of
    /// reading internal state from any process manager.
    /// </summary>
    [EventId(9018)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct EpisodeStateChangedEvent
    {
        /// <summary>Snapshot of all currently active episode IDs at time of publication.</summary>
        public HashSet<Guid> ActiveEpisodeIds;
    }

    /// <summary>
    /// Published by <c>ClusterScenarioPanel</c> (remote/ExCon path) when the operator
    /// triggers a cluster-level command.
    /// Consumed by <c>ClusterOpEgressTranslator</c> which serialises the payload and
    /// writes a <c>ClusterOpRequest</c> DDS message to the Orchestrator.
    ///
    /// <para>Use <c>FdpEventBus.PublishManaged</c> / <c>ConsumeManaged</c> because
    /// <see cref="DomainPayload"/> is a managed reference.</para>
    /// </summary>
    [EventId(9019)]
    [DataPolicy(DataPolicy.NoRecord)]
    public sealed class ClusterOpIntent
    {
        /// <summary>Unique identifier that links this command to its status reply.</summary>
        public Guid RequestId;

        /// <summary>The cluster-level operation being requested.</summary>
        public ClusterOpType OperationType;

        /// <summary>
        /// Typed payload for the operation (e.g. a string PayloadJson pass-through,
        /// or a strongly-typed DTO for full CQRS).
        /// <c>null</c> for operations that carry no payload.
        /// </summary>
        public object? DomainPayload;
    }
}
