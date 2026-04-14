using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == TransitionState</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9050)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct TransitionStateIntent
    {
        public Guid TransactionId;
        public ClusterState TargetState;
        /// <summary>Target wall-clock tick; 0 = not specified.</summary>
        public long TargetWallTicks;
        public string? ScenarioId;
        public string? ExerciseId;
        public string? TimeMode;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == ManageEpisode</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9051)]
    [DataPolicy(DataPolicy.NoRecord)]
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
    [DataPolicy(DataPolicy.NoRecord)]
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
    [DataPolicy(DataPolicy.NoRecord)]
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
        SaveScenario,
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a storage-related DDS
    /// <c>ClusterOpRequest</c> arrives (ExportArchive, ImportArchive, SaveScenario).
    /// Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9054)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ExecuteStorageOpIntent
    {
        public Guid RequestId;
        public StorageOpType Operation;
        public string? ExerciseId;
    }

    /// <summary>
    /// Published by <c>ClusterMaster</c> when a storage operation completes.
    /// Consumed by translators to write the DDS <c>ClusterOpStatus</c> topic.
    /// </summary>
    [EventId(9055)]
    [DataPolicy(DataPolicy.NoRecord)]
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
    [DataPolicy(DataPolicy.NoRecord)]
    public struct TakeCheckpointIntent
    {
        public Guid RequestId;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == LoadZone</c> arrives. Consumed by <c>ClusterMaster</c>.
    /// </summary>
    [EventId(9057)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct LoadZoneIntent
    {
        public Guid RequestId;
        public string? ZoneId;
    }
}
