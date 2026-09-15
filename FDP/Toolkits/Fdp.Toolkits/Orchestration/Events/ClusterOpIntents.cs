using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == TransitionState</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9050)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct TransitionStateIntent
    {
        public Guid TransactionId;
        public ClusterState TargetState;
        /// <summary>Target wall-clock tick; 0 = not specified.</summary>
        public long TargetWallTicks;
        public string? ScenarioId;
        public Guid ExerciseId;
        public string? TimeMode;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == ManageEpisode</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9051)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ManageEpisodeIntent
    {
        public Guid TransactionId;
        public bool IsStart;
        public Guid EpisodeId;
        public string? ScenarioId;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == ReplaySeek</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9052)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct SeekReplayIntent
    {
        public Guid RequestId;
        public long TargetWallTicks;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == CancelOperation</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9053)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct CancelOperationIntent
    {
        public Guid TargetRequestId;
    }

    /// <summary>
    /// Discriminator for storage operations dispatched via <see cref="ExecuteStorageOpIntent"/>.
    /// </summary>
    public enum StorageOpType
    {
        Export,
        Import,

        // CE-278: the legacy .fdp-archive scenario-save op (op=2) was retired; the name is reclaimed below
        // by the declarative JSON scenario save (formerly SaveScenarioJson), the operation CGF-1 always meant.

        /// <summary>
        /// ⭐⭐⭐ CE-275 ③ — the DECLARATIVE scenario save (per-node gated <c>ScenarioSerializer</c> JSON).
        /// CE-278: reclaims the name <c>SaveScenario</c> (was <c>SaveScenarioJson</c>) after the legacy
        /// .fdp-archive op=2 was retired — this is the operation CGF-1 always meant by "SaveScenario".
        /// Carries a <see cref="ExecuteStorageOpIntent.ScenarioName"/>; the fan-out runs the ONE gated
        /// scenario save handler on EVERY host (IG included — it can author persistable entities; it just
        /// usually owns nothing savable, so its file is empty BY THE GATE, not by a missing handler) so
        /// each host writes exactly the slice it owns.
        /// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
        /// </summary>
        SaveScenario,
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a storage-related DDS
    /// <c>ClusterOpRequest</c> arrives (ExportArchive, ImportArchive, SaveScenario).
    /// Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9054)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ExecuteStorageOpIntent
    {
        public Guid RequestId;
        public StorageOpType Operation;
        public Guid ExerciseId;

        /// <summary>
        /// The relative scenario name / subfolder under the NAS scenarios root to write to. Used ONLY by
        /// <see cref="StorageOpType.SaveScenario"/> (CE-275 ③). ⛔ The operator never picks a full
        /// filesystem path — at most a subfolder of the standard scenarios folder — so this is a name, not
        /// a path. Null/ignored for every other operation.
        /// </summary>
        public string? ScenarioName;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> when a storage operation completes.
    /// Consumed by translators to write the DDS <c>ClusterOpStatus</c> topic.
    /// </summary>
    [EventId(9055)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct StorageOpCompletedEvent
    {
        public Guid RequestId;
        public OrchestrationStatusCode StatusCode;
        public int SuccessCount;
        public int FailureCount;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == TakeCheckpoint</c> arrives. Contains no payload fields
    /// beyond <see cref="RequestId"/> — the checkpoint operation requires no parameters.
    /// </summary>
    [EventId(9056)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct TakeCheckpointIntent
    {
        public Guid RequestId;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == LoadZone</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9057)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct LoadZoneIntent
    {
        public Guid RequestId;
        public string? ZoneId;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == DumpDiagnostics</c> arrives. Consumed by
    /// <c>DiagnosticsDumpProcessManager</c> which orchestrates the per-node collection.
    /// <para><c>PayloadJson</c> is a JSON-serialised <c>DiagnosticDumpPayloadDto</c>.</para>
    /// </summary>
    [EventId(9058)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ExecuteDiagnosticDumpIntent
    {
        public Guid   RequestId;
        /// <summary>JSON-serialised <c>DiagnosticDumpPayloadDto</c> from the ExCon.</summary>
        public string PayloadJson;
    }
}
