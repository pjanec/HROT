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

	// keep in sync with ClusterOpType in orchestration toolkit!
	public enum ClusterOpType : int
    {
        TransitionState = 1,
        // 2 — RESERVED gap: the legacy SaveScenario op (CE-278) was retired; wire value 2 is not reused.
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
        DumpDiagnostics = 16,
        SaveScenario = 17,   // CE-277(c0): distributed JSON scenario save; name in PayloadJson {"ScenarioName":...}. CE-278: renamed from SaveScenarioJson (wire value 17 unchanged).

        // ⛔ PERMANENT WIRE VALUE (R-42). Ruled 2026-09-17.
        // ⚠ Originally specified as 17 — that was WRONG and caught before it shipped: SaveScenario
        //   above already holds 17. 18 is the next genuinely free value in THIS (authoritative) enum.
        BuildTerrainAsset = 18,
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
        CollectDiagnostics = 28,
        StartEpisode = 20,
        StopEpisode = 21,
        ReplayEpisode = 22,
        ForgetEpisode = 23,
        LoadEpisodeAssets = 24,
        PrefetchFiles = 25,

        // ── Terrain asset build (29–30) ───────────────────────────────────────────────────────
        // ⛔ PERMANENT WIRE VALUES (R-42). Ruled 2026-09-17. Must stay identical to the FDP mirror
        //    in Fdp.Toolkit.Orchestration.NodeOpType.
        // ⛔ The gaps at 6 / 17 / 18 / 19 are historical holes, not reservations — leave them empty.
        PrepareTerrainAsset = 29,
        CommitTerrainAsset = 30,
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
        // CE-286 (C-roles): the per-tick RolesMask field was REMOVED — roles are static and now travel as
        // fdp.role.* tokens on the durable NodeCapabilities descriptor (AQ-70 §Q70-C). The heartbeat is
        // telemetry-only again. History: CE-282 carried `int RolesMask` here.
    }

    /// <summary>
    /// Host static attributes — the OpenGL-extension-style namespaced capability token set a node advertises
    /// ONCE at join (AQ-70 §Q70-B, cluster-master §8). Durable (Reliable + TransientLocal + KeepLast 1) so the
    /// last set per node is retained and delivered to late joiners and to the orchestrator whenever it polls.
    /// ⛔ Deliberately SEPARATE from the per-tick <see cref="NodeHeartbeat"/>: capabilities are static, so they
    /// must not ride a per-tick message. Carries <c>fdp.role.*</c> role tokens (the bit-backed subset, derived to
    /// a <c>NodeRole</c> mask at ingest — superseding CE-282's <see cref="NodeHeartbeat.RolesMask"/>) plus feature
    /// tokens such as <c>fdp.reliable-init</c>.
    /// </summary>
    [DdsTopic("NodeCapabilities")]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NodeCapabilitiesTopic
    {
        [DdsKey] public int NodeId;
        /// <summary>JSON-serialised <c>string[]</c> of namespaced capability tokens (same wire convention as
        /// <see cref="NodeHeartbeat.SubsystemsJson"/> / <see cref="AssetInventoryTopic"/>). Empty JSON array for a
        /// node that advertises nothing (a valid fast-mode-only host).</summary>
        [DdsManaged] public string CapabilitiesJson;
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
