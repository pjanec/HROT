using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// One exercise of the cluster's inventory (what can be replayed / kept / exported).
    /// </summary>
    /// <param name="ParentExerciseId">⭐ CE-4097 (AQ108 D5) — the exercise this one BRANCHED from (a replay going live); <c>Guid.Empty</c> for a run
    /// that began at a load. ⛔ Not a second record of the branch: it is the recording's own <c>RecordingMetadata.ParentExerciseId</c>
    /// (<see cref="ExerciseBranch"/> reads it), and the live run's is what the nodes report from the very <c>RecordingBranch</c> they write it from.
    /// Default = not a branch, so an old peer's inventory reads as one.</param>
    /// <param name="BranchWallTicks">⭐ The branch point in the metadata's own unit: the parent's WALL time at the frame the branch left it
    /// (<c>RecordingMetadata.BranchWallTicks</c>; 0 when not a branch). A host converts it to the replay axis as
    /// <c>atSeconds = (BranchWallTicks − zero) / 1e7</c> with the replay's zero (<c>ReplayPosition</c>) — no sim-seconds twin.</param>
    public record struct ExerciseInventoryItem(Guid ExerciseId, DateTime StartTimeUtc, TimeSpan Duration, string? ScenarioId,
                                               Guid ParentExerciseId = default, long BranchWallTicks = 0);

    /// <summary>
    /// Published by <c>ClusterMaster</c> when a top-level cluster operation completes
    /// (either successfully or with a failure status code).
    /// Consumed by translators to write the DDS <c>ClusterOpStatus</c> topic.
    /// </summary>
    [EventId(9011)]
    [DataPolicy(DataPolicy.NoReplay)]
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
    [DataPolicy(DataPolicy.NoReplay)]
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
    [DataPolicy(DataPolicy.NoReplay)]
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
    [DataPolicy(DataPolicy.NoReplay)]
    public struct NodeHeartbeatEvent
    {
        public int    NodeId;
        public int    LocalStateId;
        public long   WallTicksUtc;
        public string SubsystemName;
        /// <summary>⭐ C2 (R-4006 I5) — the cluster session this node follows; empty until it has seen one.</summary>
        public Guid   SessionId;
        /// <summary>⭐⭐ <c>CE-3345</c> part B (R-3072, AQ110 A) — what the node reports about ITSELF: its <see cref="NodePhase"/>. Default 0 = <see cref="NodePhase.Ready"/> (a sender that never sets it).</summary>
        public NodePhase Phase;
        /// <summary>⭐ <c>CE-3345</c> — the load step the node is on (while <see cref="NodePhase.Loading"/> / <see cref="NodePhase.CatchingUp"/>); <see langword="null"/> otherwise.</summary>
        public string? Step;
        /// <summary>⭐ <c>CE-3345</c> — why the node's last prepare failed (while <see cref="NodePhase.Failed"/>); <see langword="null"/> otherwise.</summary>
        public string? Reason;
        /// <summary>⭐ <c>CE-3354</c> ② — the node's unique human-given NAME (default <c>&lt;subsystem&gt;-&lt;nodeId&gt;</c>); <see langword="null"/> from a sender that sends none (an old peer ⇒ the receiver derives the default).</summary>
        public string? NodeName;
        // CE-286 (C-roles): the `NodeRole Roles` field was REMOVED. Roles are static and now travel as
        // fdp.role.* tokens on the durable NodeCapabilities descriptor (NodeCapabilitiesEvent), from which the
        // orchestrator/cache DERIVE the mask at ingest (AQ-70 §Q70-C). The heartbeat is telemetry-only again.
    }

    /// <summary>
    /// Published ONCE at join by <c>ClusterSlave</c> — the node's static capability token set
    /// (OpenGL-extension-style namespaced: <c>fdp.role.*</c> role tokens + feature tokens like
    /// <c>fdp.reliable-init</c>). Consumed by the egress translator to write the durable
    /// <c>NodeCapabilities</c> DDS topic, and (ingress) republished from that topic for the orchestrator/cache
    /// gather (AQ-70 §Q70-B/C, cluster-master §8). The <see cref="NodeRole"/> mask is DERIVED from the
    /// <c>fdp.role.*</c> subset at ingest via <see cref="NodeRoleTokens"/> — nobody publishes the mask, so it
    /// cannot drift (supersedes CE-282 roles-on-heartbeat).
    /// </summary>
    [EventId(9022)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct NodeCapabilitiesEvent
    {
        public int      NodeId;
        public string[] Capabilities;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> when the global cluster state transitions.
    /// Consumed by translators to write the DDS <c>ClusterStateTopic</c> topic.
    /// </summary>
    [EventId(9015)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ClusterStateTransitionedEvent
    {
        /// <summary>New cluster state.</summary>
        public ClusterState NewStateId;
        /// <summary>"Cluster" — identifies the global cluster state machine.</summary>
        public string SubsystemName;
        public Guid ExerciseId;
        /// <summary>⭐ C2 (R-4006 I5) — the master's cluster session.</summary>
        public Guid SessionId;
        /// <summary>⭐ C4 (R-4006 I4) — telemetry: completed rounds so far.</summary>
        public int TransactionEpoch;
        /// <summary>⭐ CE-3329 R-3058 — the replay session's time axis (rides every cluster-state publication).</summary>
        public ReplayAxis ReplayAxis;
        /// <summary>⭐ CE-4090 (AQ108 D14) — the run the cluster shows was started with recording OFF. Inverse polarity: default false = recorded
        /// (today's behaviour), so a publisher that does not set it changes nothing. Rides every cluster-state publication.</summary>
        public bool RunNotRecorded;
        /// <summary>⭐ CE-4090 — the cluster's <i>Record live runs</i> SETTING is OFF (what the next live load will do). Inverse polarity: default false = ON.</summary>
        public bool RecordLiveRunsOff;
        /// <summary>⭐ AQ108 D15 (CE-4100 ①) — the cluster's <i>Ask to keep recordings</i> SETTING is ON. Default false (a publisher that never sets it reads as OFF).</summary>
        public bool AskToKeepRecordings;
        /// <summary>⭐ <c>CE-3354</c> ③ — the NAMES of the nodes whose scenario load also waits for their built 3-D map (the master's run setting); null/empty = none.</summary>
        public string[]? MapInLoadNodes;
        /// <summary>⭐ CE-3341 (AQ108 D12a, R-3068) — the cluster's ARMED run mode. Default 0 = <see cref="ClusterArmedMode.Live"/>, so a publisher that never sets it reads as the default.</summary>
        public ClusterArmedMode ArmedMode;
        /// <summary>⭐ R-1043 — the mode the LAST scenario load went into (Live or Preview). Default 0 = Live.</summary>
        public ClusterArmedMode LastLoadMode;
        /// <summary>⭐⭐ <c>CE-3344</c> (R-3069) — what the cluster is still WAITING FOR before it is ready, as <i>"SimHost, Brain (CGF)"</i>: the mandatory subsystem names and roles
        /// no node has reported yet. <see langword="null"/> / empty = nothing is missing. Rides every cluster-state publication; ⭐ the ONE text — the load wait's
        /// "waiting for" reads the same master function.</summary>
        public string? WaitingFor;
        /// <summary>⭐⭐ <c>CE-3345</c> (R-3073, <c>DESIGN_Cluster_Load_Phase.md</c> §11.5) — the cluster ERROR: why it is broken (<i>"Brain (CGF, node 3) lost at sim 00:12:31"</i>), set while a mandatory
        /// node / role was lost with a world loaded and cleared only when the cluster reaches Idle. <see langword="null"/> / empty = no error. ⭐ Beside <see cref="WaitingFor"/>, never instead of it.</summary>
        public string? ErrorReason;
        /// <summary>⭐⭐ <c>CE-3345</c> part B (R-3070, AQ110 B) — CLUSTER PLAY READY: the master's combination over the PARTICIPANTS only (in sync ∧ not an observer, K6) — every one of them reports <see cref="NodePhase.PlayReady"/>.
        /// False while any of them is still loading, and in Idle (nobody has a world to play). Rides every cluster-state publication, beside <c>WaitingFor</c>.</summary>
        public bool ClusterPlayReady;
        /// <summary>⭐ <c>CE-3345</c> — which PARTICIPANTS are still loading and on what step (<i>"CGF (node 2): Terrain; SimHost (node 3): KnowledgeBase"</i>); <see langword="null"/> / empty = none is.</summary>
        public string? LoadProgress;
    }

    /// <summary>
    /// ⭐ Q86 §4-B (R-215) — published by the orchestrator's scenario-load handler when a scenario's context is
    /// restored. The cluster's network adapter writes it to the DDS <c>OrchestratorContextTopic</c> (the late-joiner
    /// Context Plane, <c>docs/designs/mgmt-1/DESIGN.md</c> §1.2); a host with no network simply has no reader.
    /// ⛔ The handler used to write that topic DIRECTLY — the one orchestrator piece that touched DDS (hexag-2 §1.4).
    /// </summary>
    [EventId(9063)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct OrchestratorContextChangedEvent
    {
        public string ScenarioId;
        public string SceneId;
    }

    /// <summary>
    /// ⭐⭐ <c>C6</c> (Q91 §4-N, <c>R-4014</c> K6) — the master's <c>InSync</c> set changed. The cluster's network adapter writes
    /// it to <c>OrchestratorContextTopic.RequiredNodeIdsJson</c> (durable, one producer); every node's cluster cache uses it so a
    /// creator's reliable-init barrier waits only for in-sync peers.
    /// </summary>
    [EventId(9064)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct InSyncSetChangedEvent
    {
        public int[] NodeIds;
    }

    /// <summary>⭐⭐ <c>C1</c> (Q91 §4-O, <c>R-4009</c>) — a process asks the master for a node id (computer name + process id).</summary>
    [EventId(9065)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct NodeIdRequestEvent
    {
        public string ComputerName;
        public int ProcessId;
        public int ForcedId;
        public int PreferredId;
    }

    /// <summary>⭐⭐ <c>C1</c> — the master's answer to a <see cref="NodeIdRequestEvent"/>.</summary>
    [EventId(9066)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct NodeIdGrantEvent
    {
        public string ComputerName;
        public int ProcessId;
        public int NodeId;
        public System.Guid SessionId;
        public bool Refused;
        public string Reason;
    }

    /// <summary>
    /// Published by <c>OrchestrationObserverTranslator</c> (DDS→bus) and by
    /// <c>ClusterMaster</c> (bus-mode) when the global cluster state transitions.
    /// Consumed by <c>ClusterUiCache</c> to update <c>CurrentState</c>.
    /// </summary>
    [EventId(9016)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ClusterStateUpdateEvent
    {
        /// <summary>New cluster state.</summary>
        public ClusterState CurrentState;
        public Guid ExerciseId;
        /// <summary>⭐ C2 (R-4006 I5) — the master's cluster session (empty from a sender that does not set it).</summary>
        public Guid SessionId;
        /// <summary>⭐ C2 — the master's cluster-state writer is GONE (its instance went not-alive): the session has ended.</summary>
        public bool MasterGone;
        /// <summary>⭐ CE-3329 R-3058 — the replay session's time axis, as the master last said.</summary>
        public ReplayAxis ReplayAxis;
        /// <summary>⭐ CE-4090 (AQ108 D14) — as <see cref="ClusterStateTransitionedEvent.RunNotRecorded"/>.</summary>
        public bool RunNotRecorded;
        /// <summary>⭐ CE-4090 — as <see cref="ClusterStateTransitionedEvent.RecordLiveRunsOff"/>.</summary>
        public bool RecordLiveRunsOff;
        /// <summary>⭐ AQ108 D15 (CE-4100 ①) — as <see cref="ClusterStateTransitionedEvent.AskToKeepRecordings"/>.</summary>
        public bool AskToKeepRecordings;
        /// <summary>⭐ <c>CE-3354</c> ③ — as <see cref="ClusterStateTransitionedEvent.MapInLoadNodes"/>.</summary>
        public string[]? MapInLoadNodes;
        /// <summary>⭐ CE-3341 — as <see cref="ClusterStateTransitionedEvent.ArmedMode"/>.</summary>
        public ClusterArmedMode ArmedMode;
        /// <summary>⭐ R-1043 — the mode the LAST scenario load went into (Live or Preview). Default 0 = Live.</summary>
        public ClusterArmedMode LastLoadMode;
        /// <summary>⭐⭐ <c>CE-3344</c> (R-3069) — as <see cref="ClusterStateTransitionedEvent.WaitingFor"/> (what <see cref="ClusterView.WaitingFor"/> folds).</summary>
        public string? WaitingFor;
        /// <summary>⭐⭐ <c>CE-3345</c> (R-3073) — as <see cref="ClusterStateTransitionedEvent.ErrorReason"/> (what <see cref="ClusterView.ErrorReason"/> folds).</summary>
        public string? ErrorReason;
        /// <summary>⭐⭐ <c>CE-3345</c> part B (R-3070, AQ110 B) — CLUSTER PLAY READY: the master's combination over the PARTICIPANTS only (in sync ∧ not an observer, K6) — every one of them reports <see cref="NodePhase.PlayReady"/>.
        /// False while any of them is still loading, and in Idle (nobody has a world to play). Rides every cluster-state publication, beside <c>WaitingFor</c>.</summary>
        public bool ClusterPlayReady;
        /// <summary>⭐ <c>CE-3345</c> — which PARTICIPANTS are still loading and on what step (<i>"CGF (node 2): Terrain; SimHost (node 3): KnowledgeBase"</i>); <see langword="null"/> / empty = none is.</summary>
        public string? LoadProgress;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> when the asset inventory is refreshed, and by
    /// <c>OrchestrationObserverTranslator</c> when a DDS <c>AssetInventoryTopic</c> arrives.
    /// Consumed by <c>ClusterOpMasterTranslator</c> to write the DDS inventory topic, and by
    /// <c>ClusterUiCache</c> to update <c>AvailableScenarios</c> / <c>AvailableExercises</c>.
    /// </summary>
    [EventId(9017)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct AssetInventoryUpdateEvent
    {
        public string[] LocalScenarios;
        public ExerciseInventoryItem[] LocalExercises;
        public ExerciseInventoryItem[] ArchivedExercises;
        public ExerciseInventoryItem[] UnarchivedLocalExercises;
    }

    /// <summary>
    /// Published by <c>EpisodeProcessManager</c> after the active episode set changes.
    /// Consumers (e.g. <c>ClusterUiCache</c>, tests) subscribe to this event instead of
    /// reading internal state from any process manager.
    /// </summary>
    [EventId(9018)]
    [DataPolicy(DataPolicy.NoReplay)]
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
    [DataPolicy(DataPolicy.NoReplay)]
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
