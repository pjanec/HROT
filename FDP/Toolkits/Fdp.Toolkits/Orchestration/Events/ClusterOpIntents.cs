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
        /// <summary>⭐ CE-3299 D17 (R-3045) — the live session's recording mode: 0 = FULL, N &gt; 0 = a keyframe every N s of sim time.</summary>
        public double CheckpointingSeconds;
        /// <summary>⭐ CE-3329 P1 (R-3057 R11) — a replay's entry point in the RECORDED sim time (<c>atSimSeconds</c>); the master turns it
        /// into wall ticks with the cluster's time map. Wins over <see cref="TargetWallTicks"/> when set.</summary>
        public double? TargetSimSeconds;
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
        /// <summary>⭐ CE-3329 P1 (R-3057 R11) — the target in the RECORDED sim time (<c>atSimSeconds</c>): the master resolves it to wall
        /// ticks with the cluster's time map (a paused instant lands at its end — the pause's changes shown). Nodes are unchanged.</summary>
        public double? TargetSimSeconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> P1 (R-3058) — the replay's time AXIS, a property of the cluster's replay session: it decides what the
    /// timeline draws, what a seek means and how playback runs — on <see cref="ReplayAxis.Sim"/> a paused stretch is jumped over, on
    /// <see cref="ReplayAxis.Wall"/> it plays out. Any host asks; the master's <c>ReplayClockProcessManager</c> holds it.
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §5 R2.
    /// </summary>
    [EventId(9069)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct SetReplayAxisIntent
    {
        public Guid RequestId;
        public ReplayAxis Axis;
    }

    /// <summary>⭐ CE-3329 R-3058 — the two views of the ONE replay clock (wall). <see cref="Sim"/> is the default (R2).</summary>
    public enum ReplayAxis
    {
        /// <summary>The run's own clock: every paused stretch collapsed to the instant it ended.</summary>
        Sim = 0,

        /// <summary>The wall clock the nodes align on (R-4012): pauses play out.</summary>
        Wall = 1,
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
    /// with <c>OperationType == TakeCheckpoint</c> arrives (and by local panels). ⭐ CE-3299 B3 (R-3044): it names a keyframe
    /// every node's recording writes at one wall time; the optional <see cref="Label"/> rides the request's <c>PayloadJson</c>.
    /// </summary>
    [EventId(9056)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct TakeCheckpointIntent
    {
        public Guid RequestId;
        /// <summary>⭐ CE-3299 B3 — the checkpoint's name; null ⇒ the master names it after its wall time.</summary>
        public string? Label;
    }

    /// <summary>
    /// ⭐ CE-3299 B3 (R-3044 "collect") — export checkpoint <see cref="Label"/> on every node as a one-frame recording beside that
    /// node's recording (a bug report's state). Published by <c>ClusterOpMasterTranslator</c> for a DDS <c>CollectCheckpoint</c>.
    /// </summary>
    [EventId(9067)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct CollectCheckpointIntent
    {
        public Guid RequestId;
        public string? Label;
    }

    /// <summary>
    /// ⭐ CE-3299 B4 — restore checkpoint <see cref="Label"/> on every node and go on live, paused, as a NEW exercise (D18).
    /// Published by <c>ClusterOpMasterTranslator</c> for a DDS <c>RestoreCheckpoint</c>.
    /// </summary>
    [EventId(9068)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct RestoreCheckpointIntent
    {
        public Guid RequestId;
        public string? Label;
    }

    /// <summary>
    /// Published by <c>ClusterOpMasterTranslator</c> when a DDS <c>ClusterOpRequest</c>
    /// with <c>OperationType == LoadZone</c> arrives.
    /// <para>
    /// ✅ CONSUMED since `2026-09-17` by <c>ClusterMaster.ProcessLoadZoneIntents</c>, which starts ONE
    /// <c>PrepareZone</c> → <c>CommitZone</c> round per zone
    /// (<c>docs/DESIGN_Terrain_Zones_And_Assets.md</c> §9.3).
    /// ⚠ HISTORY: this comment previously claimed "Consumed by <c>ClusterMaster</c>" while nothing
    /// subscribed at all — the publish genuinely dead-ended (§4, dead edge 1). It is true again now,
    /// and the rail that keeps it true is <c>ClusterMasterZoneRoundTests</c>.
    /// </para>
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

    /// <summary>
    /// ⭐⭐ <c>E4</c> — published when a <c>ClusterOpRequest</c> with
    /// <c>OperationType == BuildTerrainAsset</c> arrives. Consumed by
    /// <c>ClusterMaster.ProcessBuildTerrainAssetIntents</c>, which runs ONE
    /// <c>PrepareTerrainAsset</c> → <c>CommitTerrainAsset</c> round over every active node.
    ///
    /// <para>⭐ <c>Kinds</c> names the asset kinds to rebuild; null or empty means ALL. ⚠ Empty is
    /// "everything", not "nothing" — an op that asked for nothing would never be published.</para>
    ///
    /// <para>⛔ <b>EVENT ID 9061 IS PERMANENT (<c>R-42</c>), and it was chosen by measuring the whole
    /// 9xxx range rather than taking "the next one after 9058".</b> 📐 That sweep found <b>9059 already
    /// used TWICE</b> — <c>StagingRemapPublishedEvent</c> (Fdp.Toolkits.Orchestration) and
    /// <c>MergeLogsIntent</c> (Hrot.Orchestrator), both production — and 9060 taken by
    /// <c>MergeLogsCompletedEvent</c>. ⇒ 9061 is the first genuinely free value. The 9059 collision is
    /// PRE-EXISTING and reported separately; it is not this intent's to fix.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.1, §9.2 U3, §8.3 N5.
    /// </summary>
    [EventId(9061)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct BuildTerrainAssetIntent
    {
        public Guid      RequestId;
        /// <summary>Asset kinds to rebuild; <c>null</c>/empty means all.</summary>
        public string[]? Kinds;
    }

    /// <summary>
    /// ⭐ CE-3021 — an explicit, user-triggered asset operation: PUBLISH an author's <see cref="Kind"/> tree to NAS, or
    /// REFRESH it from NAS (docs/DESIGN_Asset_Management.md §5, §7.3c, §10 D7). Carried on the wire as
    /// <c>ClusterOpType.PublishAssets</c> / <c>RefreshAssets</c> with an <c>AssetOpPayloadDto</c>.
    /// </summary>
    [EventId(9062)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct AssetOpIntent
    {
        public Guid    RequestId;
        /// <summary>false = publish (node→NAS), true = refresh (NAS→the author's folder).</summary>
        public bool    Refresh;
        /// <summary>The behaviour-asset kind id (<c>blueprint</c>, <c>btree</c>, <c>hsm</c>).</summary>
        public string? Kind;
        /// <summary>The authoring node.</summary>
        public int     NodeId;
    }
}
