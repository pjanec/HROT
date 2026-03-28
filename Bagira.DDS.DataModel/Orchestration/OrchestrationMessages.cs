using CycloneDDS.Schema;
using Bagira.DDS.DM;

namespace Bagira.BDC.SSTD.Orchestration
{
    public enum DSMState : int
    {
        Standby = 0,
        LoadingEdit = 10, RunningEdit = 11, UnloadingEdit = 12,
        LoadingDryRun = 20, RunningDryRun = 21, UnloadingDryRun = 22,
        LoadingLive = 30, RunningLive = 31, UnloadingLive = 32,
        LoadingReplay = 40, RunningReplay = 41, UnloadingReplay = 42,
        Degraded = 99,
    }

    public enum SysOpType : int
    {
        TransitionState = 1, SaveScenario = 2, LoadBattlespace = 3,
        TakeCheckpoint = 4, CollectCheckpoint = 5, ExportArchive = 6,
        ImportArchive = 7, ManageStory = 8, ReplaySeek = 9,
        PauseTime = 10, ResumeTime = 11,
    }

    public enum OpStatus : int
    {
        Pending = 0, InProgress = 1, Success = 2, Failure = 3, Rejected = 4
    }

    /// <summary>Wire value 13 is replay seek on nodes; C# name avoids IDL literal clash with <see cref="SysOpType.ReplaySeek"/>.</summary>
    public enum NodeOpType : int
    {
        PrepareState = 1, CommitState = 2, AbortTransaction = 3,
        TakeSnapshot = 4, RestoreSnapshot = 5,
        PrepareBattlespace = 7, CommitBattlespace = 8,
        PrepareLive = 9, FinalizeLive = 10,
        PrepareReplay = 11, FinalizeReplay = 12,
        NodeReplaySeek = 13, UploadChunk = 14,
        SerializeLocal = 15, CleanupTempFiles = 16,
        StartStory = 20, StopStory = 21,
        ReplayStory = 22, ForgetStory = 23,
        LoadStoryAssets = 24,
    }

    [DdsTopic("SystemState")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct SystemStateTopic
    {
        public DSMState CurrentState;
        public Guid DrillId;
        public long StateStartWallTicks;
        public int TransactionEpoch;
    }

    [DdsTopic("SysOpRequest")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct SysOpRequest
    {
        public Guid RequestId;
        public SysOpType OperationType;
        [DdsManaged] public string PayloadJson;
    }

    [DdsTopic("SysOpStatus")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct SysOpStatus
    {
        public Guid RequestId;
        public OpStatus Status;
        public int ErrorCode;
        [DdsManaged] public string ResultJson;
    }

    [DdsTopic("NodeOpCommand")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct NodeOpCommand
    {
        public Guid TransactionId;
        public NodeOpType Operation;
        [DdsManaged] public string PayloadJson;
    }

    [DdsTopic("NodeOpStatus")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct NodeOpStatus
    {
        public Guid TransactionId;
        public int NodeId;
        public OpStatus Status;
        public bool IsParticipating;
        public int ErrorCode;
        [DdsManaged] public string ResultJson;
    }

    [DdsTopic("NodeHeartbeat")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NodeHeartbeat
    {
        [DdsKey] public int NodeId;
        [DdsManaged] public string SubsystemName;
        public DSMState LocalDsmState;
        public long WallTicksUtc;
        public float CpuUsagePercent;
        public long RamUsedBytes;
        public bool SimTickAdvancing;
        [DdsManaged] public string SubsystemsJson;
    }

    [DdsTopic("OrchestratorContext")]
    [DdsIdlFile("bdc-sst-orchestration")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct OrchestratorContextTopic
    {
        public DSMState CurrentState;
        public Guid DrillId;
        public int TransactionEpoch;
        [DdsManaged] public string ScenarioId;
        [DdsManaged] public string ArchiveBasePath;
        [DdsManaged] public string RequiredNodeIdsJson;
        public long StateStartWallTicks;
    }
}
