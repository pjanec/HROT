using CycloneDDS.Schema;

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

    [DdsTopic("ClusterState")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct ClusterStateTopic
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

namespace Hrot.NED.Messages
{
    /*
    **Status Codes** :

    | Code | Name                              | Description                    |
    | ---- | --------------------------------- | ------------------------------ |
    | 0    | SUCCESS                           | Operation completed            |
    | 1    | IN_PROGRESS                       | Request accepted, handshake in progress |
    | 2    | ERR_UNKNOWN_DESCRIPTOR_TYPE       | Descriptor type not supported  |
    | 3    | ERR_ENTITY_NOT_FOUND              | EntityMaster not ALIVE         |
    | 4    | ERR_DESCRIPTOR_INSTANCE_NOT_FOUND | Instance ID invalid            |
    | 5    | ERR_NOT_OWNER                     | Request reached non-owner      |
    | 6    | ERR_VALIDATION_FAILED             | Invalid value/state transition |
    | 7    | ERR_NOT_SUPPORTED                 | Descriptor updates forbidden   |
    | 8    | ERR_VERSION_CONFLICT              | currentVersion mismatch        |
    */

    /// <summary>
    /// Strongly-typed, centralised status codes for all SST request/response protocols
    /// (Create, Update, Delete, Mission, ...).  Cast to <c>int</c> at the DDS boundary.
    /// </summary>
    public enum NedStatusCode : int
    {
        /// <summary>Operation completed successfully.</summary>
        Success = 0,

        /// <summary>Request accepted; distributed handshake in progress. A terminal ACK will follow.</summary>
        InProgress = 1,

        /// <summary>The requested descriptor type is not handled by this node.</summary>
        UnknownDescriptorType = 2,

        /// <summary>No live <c>EntityMaster</c> found for the requested entity ID.</summary>
        EntityNotFound = 3,

        /// <summary>The requested descriptor instance ID does not exist.</summary>
        DescriptorInstanceNotFound = 4,

        /// <summary>This node does not own the targeted descriptor.</summary>
        NotOwner = 5,

        /// <summary>The provided value fails application-level validation.</summary>
        ValidationFailed = 6,

        /// <summary>Descriptor updates are not permitted for this descriptor type.</summary>
        NotSupported = 7,

        /// <summary>The provided <c>currentVersion</c> does not match the live version (optimistic locking).</summary>
        VersionConflict = 8,
    }
}
