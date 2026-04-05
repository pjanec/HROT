using CycloneDDS.Schema;
using Hrot.NED.Common;

namespace Hrot.NED.Descriptors.Orchestration
{
    public enum ClusterState : int
    {
        Idle = 0,
        LoadingEdit = 10,
        OperatingEdit = 11,
        UnloadingEdit = 12,
        LoadingPreview = 20,
        OperatingPreview = 21,
        UnloadingPreview = 22,
        LoadingLive = 30,
        OperatingLive = 31,
        UnloadingLive = 32,
        LoadingReplay = 40,
        OperatingReplay = 41,
        UnloadingReplay = 42,
        Degraded = 99,
    }

    public enum ClusterOpType : int
    {
        TransitionState = 1,
        SaveScenario = 2,
        LoadZone = 3,
        TakeCheckpoint = 4,
        CollectCheckpoint = 5,
        ExportArchive = 6,
        ImportArchive = 7,
        ManageEpisode = 8,
        ReplaySeek = 9,
        PauseTime = 10,
        ResumeTime = 11,
        PrefetchScenario = 12,
        CancelOperation = 13,
        StepTime        = 14,
        SetTimeScale    = 15,
    }

    /// <summary>Wire value 13 is replay seek on nodes; C# name avoids IDL literal clash with <see cref="ClusterOpType.ReplaySeek"/>.</summary>
    public enum NodeOpType : int
    {
        PrepareState = 1,
        CommitState = 2,
        AbortTransaction = 3,
        TakeSnapshot = 4,
        RestoreSnapshot = 5,
        PrepareZone = 7,
        CommitZone = 8,
        PrepareLive = 9,
        FinalizeLive = 10,
        PrepareReplay = 11,
        FinalizeReplay = 12,
        NodeReplaySeek = 13,
        UploadChunk = 14,
        SerializeLocal = 15,
        CleanupTempFiles = 16,
        PrepareEdit = 26,
        FinalizeEdit = 27,
        StartEpisode = 20,
        StopEpisode = 21,
        ReplayEpisode = 22,
        ForgetEpisode = 23,
        LoadEpisodeAssets = 24,
        PrefetchFiles = 25,
    }

    [DdsTopic("SystemState")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct SystemStateTopic
    {
        public ClusterState CurrentState;
        public Guid ExerciseId;
        public long StateStartWallTicks;
        public int TransactionEpoch;
    }

    /// <summary>
    /// Published by the Orchestrator every 5 seconds. Carries the NAS/local asset lists
    /// so that any subscriber — including ExCon — can populate asset combo-boxes purely over DDS,
    /// with no direct reference to <see cref="Hrot.Orchestrator.ClusterMaster"/>
    /// or <see cref="Hrot.Orchestrator.StorageGatewayModule"/>.
    /// </summary>
    [DdsTopic("AssetInventory")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable,
            Durability  = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast,
            HistoryDepth = 1)]
    public partial struct AssetInventoryTopic
    {
        /// <summary>Key: 0 = singleton cluster orchestrator.</summary>
        [DdsKey] public int NodeId;

        /// <summary>JSON-serialised <c>string[]</c> of locally available scenario directory names.</summary>
        [DdsManaged] public string LocalScenariosJson;

        /// <summary>JSON-serialised <c>string[]</c> of locally recorded exercise directory names.</summary>
        [DdsManaged] public string LocalExercisesJson;

        /// <summary>JSON-serialised <c>string[]</c> of exercise directory names archived on NAS.</summary>
        [DdsManaged] public string ArchivedExercisesJson;

        /// <summary>JSON-serialised <c>string[]</c> of local exercises that are NOT yet on NAS.</summary>
        [DdsManaged] public string UnarchivedLocalExercisesJson;
    }

    [DdsTopic("ClusterOpRequest")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct ClusterOpRequest
    {
        public Guid RequestId;
        public ClusterOpType OperationType;
        [DdsManaged] public string PayloadJson;
    }

    [DdsTopic("SysOpStatus")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal)]
    public partial struct ClusterOpStatus
    {
        public Guid RequestId;
        public int StatusCode;
        [DdsManaged] public string ResultJson;
    }

    [DdsTopic("NodeOpCommand")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct NodeOpCommand
    {
        /// <summary>
        /// Per-node delivery key.  Orchestrator writes one sample per roster entry with this
        /// field set to the target node's <see cref="NodeHeartbeat.NodeId"/>.  ClusterSlave
        /// readers apply a client-side filter (<c>cmd.TargetNodeId == _nodeId</c>) so each
        /// node only processes commands addressed to it.
        /// </summary>
        [DdsKey] public int TargetNodeId;
        public Guid TransactionId;
        public NodeOpType Operation;
        [DdsManaged] public string PayloadJson;
    }

    [DdsTopic("NodeOpStatus")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct NodeOpStatus
    {
        public Guid TransactionId;
        public NodeOpType Operation;
        public int NodeId;
        public int StatusCode;
        public bool IsParticipating;
        [DdsManaged] public string ResultJson;
    }

    [DdsTopic("NodeHeartbeat")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NodeHeartbeat
    {
        [DdsKey] public int NodeId;
        [DdsManaged] public string SubsystemName;
        public ClusterState LocalClusterState;
        public long WallTicksUtc;
        public float CpuUsagePercent;
        public long RamUsedBytes;
        public bool SimTickAdvancing;
        [DdsManaged] public string SubsystemsJson;
    }

    [DdsTopic("OrchestratorContext")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct OrchestratorContextTopic
    {
        public ClusterState CurrentState;
        public Guid ExerciseId;
        public int TransactionEpoch;
        [DdsManaged] public string ScenarioId;
        [DdsManaged] public string ArchiveBasePath;
        [DdsManaged] public string RequiredNodeIdsJson;
        public long StateStartWallTicks;
        /// <summary>
        /// JSON-serialized <c>string[]</c> of active episode IDs (Guid strings) injected into
        /// the running exercise.  Published by <c>ClusterMaster</c> after each
        /// <see cref="ClusterOpType.ManageEpisode"/> Start/Stop operation (CGF1-S0308).
        /// Empty string when no episodes are active.
        /// </summary>
        [DdsManaged] public string ActiveEpisodesJson;
    }
}
