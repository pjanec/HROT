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
        // ⭐ CE-3329 P3 (R5) — During Action Review: a replay of the LIVE run in place, whose only exit is back to live.
        //   ⛔ PERMANENT WIRE VALUES (R-42), allocated 2026-10-26 in BOTH enums. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §3a.
        LoadingReview = 50,
        OperatingReview = 51,
        UnloadingReview = 52,
        Degraded = 99,
    }

	// keep in sync with ClusterOpType in orchestration toolkit!
	public enum ClusterOpType : int
    {
        /// <summary>⭐ AQ107 / CycloneDDS.NET 0.4: a default-initialised message carries 0, and Cyclone DDS 11 refuses to serialize an enum value
        /// the enum does not declare (DdsWriter.Write → BadParameter). Never sent on purpose; module-unique name (IDL scopes enum members by module).</summary>
        ClusterOpNone = 0,
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

        // ⛔ PERMANENT WIRE VALUES (R-42), allocated 2026-10-03 — the next free values in BOTH enums.
        // ⭐ CE-3021 — the explicit, user-triggered asset operations (docs/DESIGN_Asset_Management.md §5, C4/C5):
        //   publish an author's tree to NAS / refresh an author's tree from NAS. Payload: AssetOpPayloadDto.
        PublishAssets = 19,
        RefreshAssets = 20,

        // ⭐ CE-3299 B4 — restore checkpoint X on every node (mgmt-1 §9.3 named this request; no enum carried it).
        //   ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-22 — the next free value in BOTH enums.
        RestoreCheckpoint = 21,

        // ⭐ CE-3329 P1 (R-3058) — switch the replay's time axis (Sim | Wall) for the whole session, from any host.
        //   ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-26 — the next free value in BOTH enums. Payload: SetReplayAxisPayloadDto.
        SetReplayAxis = 22,

        // ⭐ CE-3329 §12n — set / clear the replay's LOOP range (a session setting of the master's replay clock), from any host.
        //   ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-26 — the next free value in BOTH enums. Payload: SetReplayLoopPayloadDto.
        SetReplayLoop = 23,

        // ⭐ CE-4090 (AQ108 D14) — set the cluster-wide run settings (today: Record live runs), held by the master, from any host.
        //   ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-28 — the next free value in BOTH enums. Payload: SetRunSettingsPayloadDto.
        SetRunSettings = 24,

        // ⭐ CE-4090 (AQ108 D15) — discard an UNARCHIVED recording (its ledger entry and staging copies), from any host. Payload: ArchivePayloadDto.
        //   ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-28 — the next free value in BOTH enums.
        DeleteExercise = 25,

        // ⭐ CE-4093 (AQ108 D20) — the CHECK ROUND: ask every node whether its whole world differs from its saved baseline, from any host.
        //   ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-28 — the next free value in BOTH enums. No payload; the answer is UnsavedCheckResult.
        //   ⚠ Named CheckUnsavedChanges, not CheckUnsaved: IDL scopes enum members by module, and NodeOpType.CheckUnsaved (35) is in the same module.
        CheckUnsavedChanges = 26,
    }

    /// <summary>Wire value 13 is replay seek on nodes; C# name avoids IDL literal clash with <see cref="ClusterOpType.ReplaySeek"/>.</summary>
    public enum NodeOpType : int
    {
        /// <summary>⭐ AQ107 / CycloneDDS.NET 0.4: a default-initialised message carries 0, and Cyclone DDS 11 refuses to serialize an enum value
        /// the enum does not declare (DdsWriter.Write → BadParameter). Never sent on purpose; module-unique name (IDL scopes enum members by module).</summary>
        NodeOpNone = 0,
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

        // ⭐ CE-3299 B3 (R-3044) — export a checkpoint keyframe as a one-frame recording. ⛔ PERMANENT WIRE VALUE (R-42).
        ExportCheckpoint = 31,

        // ⭐ CE-3299 B4 — phase 1 of a checkpoint restore: read keyframe X and stage it (nothing changes). Phase 2 is the dead
        //   `RestoreSnapshot = 5` slot, REUSED for its documented meaning (restore the ECS from a recording keyframe).
        //   ⛔ PERMANENT WIRE VALUE (R-42).
        PrepareRestore = 32,

        // ⭐ CE-3329 P3 — enter / leave a review on a node. ⛔ PERMANENT WIRE VALUES (R-42), allocated 2026-10-26 in BOTH enums.
        PrepareReview = 33,
        FinalizeReview = 34,

        // ⭐ CE-4093 (AQ108 D20) — the CHECK ROUND: every node answers whether its whole world differs from its baseline (the world's keyframe
        //   content hash against the hash taken at its load / completed save / catch-up). The node's response is the JSON STRING "changed" |
        //   "unchanged" | "no-baseline". ⛔ PERMANENT WIRE VALUE (R-42), allocated 2026-10-28 in BOTH enums.
        CheckUnsaved = 35,
    }

    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct ClusterStateTopic
    {
        public ClusterState CurrentState;
        public Guid ExerciseId;
        public long StateStartWallTicks;
        /// <summary>⭐ C4 (Q91 §4-I I4, R-4006) — telemetry only: incremented by the master on every completed round. ⛔ Never
        /// decides membership (the master's InSync set does).</summary>
        public int TransactionEpoch;
        /// <summary>⭐⭐ C2 (Q91 §4-I I5, R-4006) — THE CLUSTER SESSION: minted by the master when it starts (one master = one
        /// session; the master is not restartable). A node that sees a DIFFERENT session — or this writer gone — knows the
        /// world it belonged to has ended and drops to <c>Idle</c>.</summary>
        public Guid SessionId;
        /// <summary>⭐ CE-3329 P1 (R-3058) — the replay session's time axis: 0 = Sim (the default), 1 = Wall. The same on every node
        /// (what each host's timeline draws, what its seeks mean). Additive on the wire.</summary>
        public int ReplayAxis;
        /// <summary>⭐ CE-4090 (AQ108 D14) — 1 = the run the cluster shows was started with recording OFF (nothing is being, or was,
        /// recorded). 0 = recorded, or no run yet. ⭐ Inverse polarity on purpose: an older sender omits it, and 0 is today's behaviour.
        /// Held by the master, the same on every node — what every host's HTTP gates and the Live icon read. Additive on the wire.</summary>
        public int RunNotRecorded;
        /// <summary>⭐ CE-4090 — 1 = the cluster's <i>Record live runs</i> SETTING is OFF (what the next live load does); 0 = ON, or an older sender. Additive.</summary>
        public int RecordLiveRunsOff;
        /// <summary>⭐ CE-3341 (AQ108 D12a, R-3068) — the cluster's ARMED run mode: 0 = Live (also an older sender), 1 = Preview, 2 = AAR (<c>ClusterArmedMode</c>; ⛔ permanent, R-42). Held by the master, the same on every node. Additive on the wire.</summary>
        public int ArmedMode;
        /// <summary>⭐⭐ CE-3344 (R-3069) — what the cluster is still WAITING FOR before it is ready (<i>"SimHost, Brain (CGF)"</i>: the mandatory names and roles no node has reported);
        /// empty = nothing is missing. Held by the master, the same on every node. Additive on the wire: an older sender omits it, which reads as "nothing missing".</summary>
        [DdsManaged] public string WaitingFor;
        /// <summary>⭐⭐ CE-3345 (R-3073) — the cluster ERROR reason (<i>"Brain (CGF, node 3) lost at sim 00:12:31"</i>); empty = no error. Set by the master while a mandatory node / role was lost with a world loaded,
        /// cleared when the cluster reaches Idle; the same on every node. Additive on the wire: an older sender omits it, which reads as "no error".</summary>
        [DdsManaged] public string ErrorReason;
    }

    /// <summary>
    /// ⭐⭐ CE-3329 (rows 32, 45; DESIGN_Replay_Time_Axis_And_Review §9f) — THE REPLAY'S SESSION LAYER: the zero, the length, the axis,
    /// the cluster's wall ↔ sim map, the checkpoint marks, the review's id and live point — what every host's timeline draws the same.
    /// The master republishes it on every change; TransientLocal + KeepLast 1 hands the newest to a late joiner. A host matches it to
    /// <see cref="ClusterStateTopic"/> by <see cref="SessionId"/> / <see cref="ExerciseId"/>, never by arrival order.
    /// </summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct ReplaySessionTopic
    {
        public Guid SessionId;
        public Guid ExerciseId;
        /// <summary>The whole session, <c>Fdp.Toolkit.Orchestration.ReplaySession</c> as JSON (the map and the marks are variable-length).</summary>
        [DdsManaged] public string SessionJson;
    }

    /// <summary>
    /// ⭐⭐ <c>AQ107 C</c> (R-4094) — THE ORIGIN OF THE NED WIRE'S LOCAL FRAME. Every position on the NED wire is local ENU metres (X east,
    /// Y north, Z up); this retained, keyless sample says WHERE on the Earth that frame's (0, 0, 0) is, so a gateway (DIS / HLA) can convert
    /// to ECEF / geodetic: <c>ECEF = ecef(origin) + R(origin) · local</c>, the same exact transform every node uses (<c>WGS84Transform</c>).
    /// <para>⭐ Published by the CLUSTER MASTER only (one writer, not one per CGF): at a load it is the loaded terrain's declared origin
    /// (0, 0, 0 when the terrain declares none — R-229), at a replay entry the recording's own origin. TransientLocal + KeepLast 1 hands the
    /// newest to a late joiner. <see cref="Generation"/> goes up by 1 whenever a DIFFERENT origin is published; republishing the same origin
    /// keeps it, so a gateway re-bases its entities only when it changes. Nothing in HROT reads it (every node holds the terrain).</para>
    /// <para>📄 docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md §3, §4 row C; docs/reference/NED_DDS_Protocol.md §11.</para>
    /// </summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GeoReference
    {
        /// <summary>Geodetic latitude of the frame's origin, degrees (WGS84 ellipsoid).</summary>
        public double OriginLatitudeDeg;
        /// <summary>Geodetic longitude of the frame's origin, degrees (WGS84 ellipsoid).</summary>
        public double OriginLongitudeDeg;
        /// <summary>Altitude of the frame's origin above the WGS84 ellipsoid, metres.</summary>
        public double OriginAltitudeM;
        /// <summary>The frame kind; <see cref="NedWire.FrameEnuTangentPlane"/> (0) = an ENU tangent plane at the origin — the only value so far.</summary>
        public byte Frame;
        /// <summary>The terrain the origin belongs to (the recording's terrain in a replay); empty when none.</summary>
        [DdsManaged] public string TerrainName;
        /// <summary>+1 every time a DIFFERENT origin is published; 0 never appears on the wire (the first publish is 1).</summary>
        public int Generation;
        /// <summary>The NED wire version the publisher speaks — <see cref="NedWire.ProtocolVersion"/> — so a gateway can refuse an unknown wire.</summary>
        public int ProtocolVersion;
    }

    /// <summary>
    /// Published by the Orchestrator every 5 seconds. Carries the NAS/local asset lists
    /// so that any subscriber — including ExCon — can populate asset combo-boxes purely over DDS,
    /// with no direct reference to <see cref="Hrot.Orchestrator.ClusterMaster"/>
    /// or <see cref="Hrot.Orchestrator.StorageGatewayModule"/>.
    /// </summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable,
            Durability  = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast,
            HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
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

    // ⭐ CE-3023 — KeepAll: a QUEUE of requests (mgmt-1 DESIGN "consume ClusterOpRequest DDS queue"). The default depth-1
    //   history collapsed a same-frame burst to its newest sample (measured: ops lost). Volatile, so nothing is retained
    //   for late joiners — the NodeOpCommand shape.
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct ClusterOpRequest
    {
        public Guid RequestId;
        public ClusterOpType OperationType;
        [DdsManaged] public string PayloadJson;
    }

    // ⭐ CE-3023 — the reply leg had the same depth-1 collapse (measured: 1 of 5 same-frame statuses arrived), so a remote
    //   panel waited forever on an op whose completion was overwritten. ⚠ BOUNDED, not KeepAll: the topic is
    //   TransientLocal, so its history is what a late-joining panel replays; every reader filters by RequestId.
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 64)]
    public partial struct ClusterOpStatus
    {
        public Guid RequestId;
        public int StatusCode;
        [DdsManaged] public string ResultJson;
    }

    [DdsTopic]
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

    [DdsTopic]
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

    /// <summary>
    /// ⭐⭐ <c>C1</c> (Q91 §4-O, <c>R-4009</c>/<c>R-4016</c>) — a process with no configured node id asks the master for one, BEFORE it
    /// composes anything id-dependent. Identified by facts it already has: its computer name and process id (no random id).
    /// ⚠ Keyed by the process id plus a stable 64-bit hash of the computer name (<c>NodeIdKeys.ComputerHash</c>); the name
    /// itself rides as data.
    /// </summary>
    // ══ P6 part 2 (Q91 §4-E2, R-4002) — the DDS file transfer. Commands, not state: Reliable + Volatile (R-136 §1.1). ══

    /// <summary>⭐ The sender offers files to named targets: path relative to the node's staging root, length, mtime, SHA-256.</summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct FileOffer
    {
        [DdsKey] public long TransferId;
        /// <summary>The DDS partition the chunks of THIS transfer travel in (only its targets join it).</summary>
        [DdsManaged] public string Partition;
        /// <summary>JSON <c>int[]</c> — the nodes this transfer is for.</summary>
        [DdsManaged] public string TargetsJson;
        /// <summary>JSON array of <c>{ RelativePath, Length, LastWriteUtcTicks, Sha256 }</c>, indexed by file index.</summary>
        [DdsManaged] public string ItemsJson;
    }

    /// <summary>⭐ A target's answer to an offer: the file indices it lacks (the rest it already holds — same length + mtime).</summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct FileNeed
    {
        [DdsKey] public long TransferId;
        [DdsKey] public int NodeId;
        /// <summary>JSON <c>int[]</c> of file indices.</summary>
        [DdsManaged] public string NeededJson;
    }

    /// <summary>⭐ One 64 KB piece of one file, in the transfer's own partition.</summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct FileChunk
    {
        [DdsKey] public long TransferId;   // ⭐ the probe's shape: one instance per transfer, KeepAll holds every chunk
        public int FileIndex;
        public int Seq;
        public int SeqCount;
        [DdsManaged] public byte[] Data;
    }

    /// <summary>
    /// ⭐ A target's progress: the highest gap-free chunk of a file (every 8 chunks), or — with <c>FileIndex = -1</c> and
    /// <c>Done</c> — its verdict on the whole transfer (<c>Code</c> is an <c>OrchestrationStatusCode</c>).
    /// </summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct FileAck
    {
        [DdsKey] public long TransferId;
        [DdsKey] public int NodeId;
        [DdsKey] public int FileIndex;
        public int ContiguousSeq;
        public bool Done;
        public int Code;
        [DdsManaged] public string Message;
    }

    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NodeIdRequest
    {
        [DdsKey] public long ComputerHash;
        [DdsKey] public int ProcessId;
        [DdsManaged] public string ComputerName;
        /// <summary>The id the process insists on (0 = none: let the master choose).</summary>
        public int ForcedId;
        /// <summary>The id the process held before (0 = none) — granted again when free.</summary>
        public int PreferredId;
    }

    /// <summary>⭐⭐ <c>C1</c> — the master's answer, on the same key as the request.</summary>
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NodeIdGrant
    {
        [DdsKey] public long ComputerHash;
        [DdsKey] public int ProcessId;
        public int NodeId;
        public Guid SessionId;
        public bool Refused;
        [DdsManaged] public string Reason;
    }

    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    public partial struct NodeHeartbeat
    {
        [DdsKey] public int NodeId;
        /// <summary>⭐ C2 (Q91 §4-I I5) — the cluster session this node follows (empty until it has seen one).</summary>
        public Guid SessionId;
        [DdsManaged] public string SubsystemName;
        public ClusterState LocalClusterState;
        public long WallTicksUtc;
        /// <summary>⭐ AQ107 I — the node's CPU load as a 0…1 FRACTION (was a percent). The cluster cache's <c>NodeCapability.CpuUsagePercent</c> keeps percent; the ingest converts.</summary>
        public float CpuUsage;
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
    [DdsTopic]
    [DdsIdlFile("hrot-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1, AutodisposeUnregisteredInstances = false)]
    public partial struct NodeCapabilitiesTopic
    {
        [DdsKey] public int NodeId;
        /// <summary>JSON-serialised <c>string[]</c> of namespaced capability tokens (same wire convention as
        /// <see cref="NodeHeartbeat.SubsystemsJson"/> / <see cref="AssetInventoryTopic"/>). Empty JSON array for a
        /// node that advertises nothing (a valid fast-mode-only host).</summary>
        [DdsManaged] public string CapabilitiesJson;
    }

    [DdsTopic]
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

namespace Hrot.NED.Descriptors.Orchestration
{
    /// <summary>⭐ <c>C1</c> — the key of a <see cref="NodeIdRequest"/>/<see cref="NodeIdGrant"/>: one definition for both sides.</summary>
    public static class NodeIdKeys
    {
        /// <summary>A stable 64-bit FNV-1a hash of the upper-cased computer name (never <c>string.GetHashCode</c>, which is per-process).</summary>
        public static long ComputerHash(string computerName)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in (computerName ?? string.Empty).ToUpperInvariant())
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }
                return (long)h;
            }
        }
    }
}
