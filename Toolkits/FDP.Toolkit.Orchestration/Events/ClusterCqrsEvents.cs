using Fdp.Kernel;

namespace FDP.Toolkit.Orchestration
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
        /// <summary>Uses <c>OrchestrationStatusCode</c> constants.</summary>
        public int StatusCode;
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
        public int NodeId;
        /// <summary>Uses <c>OrchestrationStatusCode</c> constants.</summary>
        public int StatusCode;
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
    /// Consumed by translators to write the DDS <c>SystemStateTopic</c> topic.
    /// </summary>
    [EventId(9015)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ClusterStateTransitionedEvent
    {
        /// <summary>New cluster state numeric value (<c>ClusterState</c> enum).</summary>
        public int    NewStateId;
        /// <summary>"Cluster" — identifies the global cluster state machine.</summary>
        public string SubsystemName;
    }
}
