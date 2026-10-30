using System;
using System.Text.Json.Serialization;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Orchestration;

/// <summary>
/// A <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> variant that rejects numeric enum values.
/// Thin forwarding wrapper — the canonical implementation is
/// <see cref="Fdp.Core.Serialization.Converters.StrictStringEnumConverter"/>.
/// Retained here so that existing <c>[JsonConverter(typeof(StrictStringEnumConverter))]</c>
/// attributes on payload DTOs compile without change.
/// </summary>
public sealed class StrictStringEnumConverter : Fdp.Core.Serialization.Converters.StrictStringEnumConverter
{
    /// <summary>Initialises the converter with <c>allowIntegerValues = false</c>.</summary>
    public StrictStringEnumConverter() : base() { }
}

/// <summary>
/// Shared <see cref="System.Text.Json.JsonSerializerOptions"/> for all orchestration
/// payload DTOs.  Delegates to <see cref="Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed"/>
/// which enforces string-based enum serialisation, rejects integer enum values, and
/// suppresses null values.  Use these for all DDS payload round-trips to avoid silent
/// integer-as-enum bugs.
/// </summary>
public static class OrchestrationJsonOptions
{
    /// <summary>
    /// Options that enforce string-based enum serialisation, reject integer enum values,
    /// and suppress null values.
    /// Use these for all DDS payload round-trips to avoid silent integer-as-enum bugs.
    /// </summary>
    public static System.Text.Json.JsonSerializerOptions Default
        => Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
}

/// <summary>Payload DTO for <c>ClusterOpType.TransitionState</c> DDS requests.</summary>
public record TransitionPayloadDto(
    [property: JsonPropertyName("TargetState")]
    [property: JsonConverter(typeof(StrictStringEnumConverter))]
    ClusterState?  TargetState,

    [property: JsonPropertyName("ScenarioId")]
    string?        ScenarioId,

    [property: JsonPropertyName("ExerciseId")]
    Guid          ExerciseId,

    [property: JsonPropertyName("TimeMode")]
    string?        TimeMode,

    // ⭐ CE-3299 D17 (R-3045) — the recording mode of the live session this request starts: 0 / omitted = FULL (every frame), N > 0 =
    //   CHECKPOINTING (a keyframe every N s of sim time, nothing else). Additive on the wire.
    [property: JsonPropertyName("CheckpointingSeconds")]
    double         CheckpointingSeconds = 0,

    // ⭐ R-3055 — where a replay enters, in ticks after the replay's zero (C9). Additive on the wire. 📌 Measured on the five-process
    //   cluster: a POST /replay {atSeconds:30} sent from CGF entered at 0 — this field did not exist, and the master side wrote 0.
    [property: JsonPropertyName("TargetWallTicks")]
    long           TargetWallTicks = 0,

    // ⭐ CE-3329 P1 (R11) — a replay's entry point in RECORDED sim time; the master resolves it. Additive on the wire.
    [property: JsonPropertyName("TargetSimSeconds")]
    double?        TargetSimSeconds = null,

    // ⭐ CE-4090 (AQ108 D14) — a live load that records whatever the cluster's "Record live runs" setting says (the scenario verifier).
    //   Additive on the wire; omitted ⇒ false ⇒ follow the setting.
    [property: JsonPropertyName("ForceRecording")]
    bool           ForceRecording = false
)
{
    /// <summary>⭐ R-3055 — the ONE encoding of a transition on the wire (every node's egress).</summary>
    public static TransitionPayloadDto From(Fdp.Toolkit.Orchestration.TransitionStateIntent intent) => new(
        TargetState: (ClusterState)(int)intent.TargetState, ScenarioId: intent.ScenarioId, ExerciseId: intent.ExerciseId,
        TimeMode: intent.TimeMode, CheckpointingSeconds: intent.CheckpointingSeconds, TargetWallTicks: intent.TargetWallTicks,
        TargetSimSeconds: intent.TargetSimSeconds, ForceRecording: intent.ForceRecording);

    /// <summary>⭐ R-3055 — the ONE decoding (the master's translator and its request adapter): nothing on the wire is dropped.</summary>
    public Fdp.Toolkit.Orchestration.TransitionStateIntent ToIntent(Guid requestId) => new()
    {
        TransactionId        = requestId,
        TargetState          = (Fdp.Toolkit.Orchestration.ClusterState)(int)TargetState!.Value,
        ScenarioId           = ScenarioId,
        ExerciseId           = ExerciseId,
        TimeMode             = TimeMode,
        CheckpointingSeconds = CheckpointingSeconds,   // D17
        TargetWallTicks      = TargetWallTicks,
        TargetSimSeconds     = TargetSimSeconds,   // CE-3329 R11
        ForceRecording       = ForceRecording,     // CE-4090 D14
    };
}

/// <summary>
/// ⭐ CE-4090 (AQ108 D14) + ⭐ CE-3341 (D12a) — payload of <c>ClusterOpType.SetRunSettings</c>: the cluster-wide run settings. ⭐ EVERY field is optional:
/// absent ⇒ "no change", so one op sets either (an older sender that only knows <c>RecordLiveRuns</c>, or one that only sends <c>Armed</c>, both work).
/// </summary>
public record SetRunSettingsPayloadDto(
    [property: JsonPropertyName("RecordLiveRuns")]
    bool? RecordLiveRuns = null,
    [property: JsonPropertyName("Armed")]
    Fdp.Toolkit.Orchestration.ClusterArmedMode? Armed = null,
    [property: JsonPropertyName("AskToKeepRecordings")]
    bool? AskToKeepRecordings = null,   // ⭐ AQ108 D15 (CE-4100 ①) — appended; absent = no change
    [property: JsonPropertyName("MapInLoadNodes")]
    string[]? MapInLoadNodes = null)    // ⭐ CE-3354 ③ — appended; absent = no change, empty = none
{
    public static SetRunSettingsPayloadDto From(Fdp.Toolkit.Orchestration.SetRunSettingsIntent intent) => new(intent.RecordLiveRuns, intent.Armed, intent.AskToKeepRecordings, intent.MapInLoadNodes);

    /// <summary>⭐ The ONE decoding (the master's translator): an unreadable payload changes nothing (both fields null).
    /// ⚠ CE-3341 changed this: before it, an unreadable payload meant "recording ON"; a no-op is the safer answer now that the op also carries the armed mode.</summary>
    public static Fdp.Toolkit.Orchestration.SetRunSettingsIntent Decode(Guid requestId, string? json)
    {
        var intent = new Fdp.Toolkit.Orchestration.SetRunSettingsIntent { RequestId = requestId };
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (System.Text.Json.JsonSerializer.Deserialize<SetRunSettingsPayloadDto>(json, OrchestrationJsonOptions.Default) is { } dto)
                {
                    intent.RecordLiveRuns = dto.RecordLiveRuns;
                    intent.Armed = dto.Armed;
                    intent.AskToKeepRecordings = dto.AskToKeepRecordings;
                    intent.MapInLoadNodes = dto.MapInLoadNodes;
                }
            }
            catch (System.Text.Json.JsonException) { }
        }
        return intent;
    }
}

/// <summary>Payload DTO for <c>ClusterOpType.ManageEpisode</c> DDS requests.</summary>
public record ManageEpisodePayloadDto(
    [property: JsonPropertyName("IsStart")]
    bool           IsStart,

    [property: JsonPropertyName("EpisodeId")]
    Guid?          EpisodeId,

    [property: JsonPropertyName("ScenarioId")]
    string?        ScenarioId
);

/// <summary>Payload DTO for <c>ClusterOpType.ExportArchive</c> / <c>ImportArchive</c> DDS requests.</summary>
public record ArchivePayloadDto(
    [property: JsonPropertyName("ExerciseId")]
    Guid          ExerciseId
);

/// <summary>
/// Payload DTO for <c>ClusterOpType.LoadZone</c> DDS requests — ONE zone per request
/// (📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.3).
///
/// <para>⛔ Replaces the previous abuse of <see cref="ArchivePayloadDto"/>, whose
/// <c>ExerciseId</c> (a <see cref="Guid"/>) was being stringified into the zone id. That silently
/// restricted zone ids to GUID shape and named the field after a different domain entirely.</para>
/// </summary>
public record ZonePayloadDto(
    [property: JsonPropertyName("ZoneId")]
    string?       ZoneId
);

/// <summary>
/// Payload DTO for <c>ClusterOpType.BuildTerrainAsset</c> DDS requests.
/// <para><c>Kinds</c> names the asset kinds to rebuild; null or empty means "all".
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.1.</para>
/// </summary>
public record TerrainAssetBuildPayloadDto(
    [property: JsonPropertyName("Kinds")]
    string[]?     Kinds
);

/// <summary>
/// ⭐ CE-3021 — payload for <c>ClusterOpType.PublishAssets</c> / <c>RefreshAssets</c>: which behaviour-asset
/// <c>Kind</c> (<c>blueprint</c>, <c>btree</c>, <c>hsm</c>) and which authoring <c>NodeId</c>.
/// 📄 docs/DESIGN_Asset_Management.md §5, §7.3c.
/// </summary>
public record AssetOpPayloadDto(
    [property: JsonPropertyName("Kind")]   string Kind,
    [property: JsonPropertyName("NodeId")] int    NodeId
);

/// <summary>Payload DTO for <c>ClusterOpType.ReplaySeek</c> DDS requests.</summary>
public record SeekReplayPayloadDto(
    [property: JsonPropertyName("TargetWallTicks")]
    long           TargetWallTicks,

    // ⭐ CE-3329 P1 (R11) — the target in RECORDED sim time; the master resolves it. Additive on the wire.
    [property: JsonPropertyName("TargetSimSeconds")]
    double?        TargetSimSeconds = null
)
{
    /// <summary>⭐ CE-3329 P1 — the ONE encoding of a seek on the wire (the R-3055 shape of <see cref="TransitionPayloadDto"/>).</summary>
    public static SeekReplayPayloadDto From(Fdp.Toolkit.Orchestration.SeekReplayIntent intent)
        => new(intent.TargetWallTicks, intent.TargetSimSeconds);

    /// <summary>⭐ CE-3329 P1 — the ONE decoding (the master's translator, its request adapter, the cluster panel): a bare tick count
    /// (older senders) or this DTO; nothing on the wire is dropped.</summary>
    public static Fdp.Toolkit.Orchestration.SeekReplayIntent Decode(Guid requestId, string? json)
    {
        var intent = new Fdp.Toolkit.Orchestration.SeekReplayIntent { RequestId = requestId };
        if (string.IsNullOrWhiteSpace(json)) return intent;
        if (long.TryParse(json, out var bare)) { intent.TargetWallTicks = bare; return intent; }
        try
        {
            if (System.Text.Json.JsonSerializer.Deserialize<SeekReplayPayloadDto>(json, OrchestrationJsonOptions.Default) is { } dto)
            {
                intent.TargetWallTicks  = dto.TargetWallTicks;
                intent.TargetSimSeconds = dto.TargetSimSeconds;
            }
        }
        catch (System.Text.Json.JsonException) { }
        return intent;
    }
}

/// <summary>⭐ CE-3329 R-3058 — payload of <c>ClusterOpType.SetReplayAxis</c>: "Sim" or "Wall".</summary>
public record SetReplayAxisPayloadDto(
    [property: JsonPropertyName("Axis")]
    string Axis)
{
    /// <summary>The ONE encoding of an axis switch on the wire.</summary>
    public static SetReplayAxisPayloadDto From(Fdp.Toolkit.Orchestration.SetReplayAxisIntent intent) => new(intent.Axis.ToString());

    /// <summary>The ONE decoding (the master's translator and its request adapter). An unreadable axis is Sim — the default (R2).</summary>
    public static Fdp.Toolkit.Orchestration.SetReplayAxisIntent Decode(Guid requestId, string? json)
    {
        var axis = Fdp.Toolkit.Orchestration.ReplayAxis.Sim;
        try
        {
            if (!string.IsNullOrWhiteSpace(json)
                && System.Text.Json.JsonSerializer.Deserialize<SetReplayAxisPayloadDto>(json, OrchestrationJsonOptions.Default) is { } dto
                && Enum.TryParse<Fdp.Toolkit.Orchestration.ReplayAxis>(dto.Axis, ignoreCase: true, out var parsed))
                axis = parsed;
        }
        catch (System.Text.Json.JsonException) { }
        return new Fdp.Toolkit.Orchestration.SetReplayAxisIntent { RequestId = requestId, Axis = axis };
    }
}

/// <summary>⭐ CE-3329 §12n — payload of <c>ClusterOpType.SetReplayLoop</c>: wall bounds, sim bounds, or none (= clear).</summary>
public record SetReplayLoopPayloadDto(
    [property: JsonPropertyName("FromSeconds")]
    double? FromSeconds,
    [property: JsonPropertyName("ToSeconds")]
    double? ToSeconds,
    [property: JsonPropertyName("FromSimSeconds")]
    double? FromSimSeconds,
    [property: JsonPropertyName("ToSimSeconds")]
    double? ToSimSeconds)
{
    /// <summary>The ONE encoding of a loop request on the wire.</summary>
    public static SetReplayLoopPayloadDto From(Fdp.Toolkit.Orchestration.SetReplayLoopIntent intent)
        => new(intent.FromSeconds, intent.ToSeconds, intent.FromSimSeconds, intent.ToSimSeconds);

    /// <summary>The ONE decoding (the master's translator and its request adapter). Unreadable ⇒ all-null, i.e. a CLEAR.</summary>
    public static Fdp.Toolkit.Orchestration.SetReplayLoopIntent Decode(Guid requestId, string? json)
    {
        var intent = new Fdp.Toolkit.Orchestration.SetReplayLoopIntent { RequestId = requestId };
        try
        {
            if (!string.IsNullOrWhiteSpace(json)
                && System.Text.Json.JsonSerializer.Deserialize<SetReplayLoopPayloadDto>(json, OrchestrationJsonOptions.Default) is { } dto)
            {
                intent.FromSeconds = dto.FromSeconds;
                intent.ToSeconds = dto.ToSeconds;
                intent.FromSimSeconds = dto.FromSimSeconds;
                intent.ToSimSeconds = dto.ToSimSeconds;
            }
        }
        catch (System.Text.Json.JsonException) { }
        return intent;
    }
}

/// <summary>Payload DTO for <c>ClusterOpType.StepTime</c> DDS requests.</summary>
public record StepTimePayloadDto(
    [property: JsonPropertyName("FixedDelta")]
    float          FixedDelta
);

/// <summary>Payload DTO for <c>ClusterOpType.SetTimeScale</c> DDS requests.</summary>
public record SetTimeScalePayloadDto(
    [property: JsonPropertyName("TimeScale")]
    float          TimeScale
);

/// <summary>
/// Node-level transition payload DTO carried inside <c>NodeOpCommand.PayloadJson</c>
/// for <c>PrepareState</c>, <c>PrepareLive</c>, <c>PrepareReplay</c>, <c>PrepareEdit</c>,
/// and <c>FinalizeEdit</c> operations.
/// </summary>
public record NodeTransitionPayloadDto(
    [property: JsonPropertyName("TargetState")]
    [property: JsonConverter(typeof(StrictStringEnumConverter))]
    ClusterState?  TargetState,

    [property: JsonPropertyName("ScenarioId")]
    string?        ScenarioId,

    [property: JsonPropertyName("ExerciseId")]
    Guid          ExerciseId,

    // ⭐⭐⭐ L1 — the SHARED CONTENT NAME. See EditLoadHandlerPayload's remarks for why it rides the
    //    message: four of the five roles never open the scenario file, so they cannot read it out of it.
    // ⭐ Optional with a null default; an older peer that omits it decodes to null.
    // ⛔ R-4018 — no TkbName: the TKB's name travels with its file sync. An older peer's TkbName is ignored on read.
    [property: JsonPropertyName("TerrainName")]
    string?        TerrainName = null,

    // ⭐ CE-2101 — the load step entered from Idle (the world boundary): every node clears its world first.
    //   Additive on the wire; an older peer omits it and decodes to false.
    [property: JsonPropertyName("IsWorldBoundary")]
    bool           IsWorldBoundary = false,

    // ⭐⭐ C7 (Q91 §4-M) — a late joiner's catch-up load: universal parts only, no scenario. Additive; omitted ⇒ false.
    [property: JsonPropertyName("IsCatchUp")]
    bool           IsCatchUp = false,

    // ⭐ CE-3299 D17 (R-3045) — the session's recording mode (see TransitionPayloadDto). Additive; omitted ⇒ 0 ⇒ FULL.
    [property: JsonPropertyName("CheckpointingSeconds")]
    double         CheckpointingSeconds = 0,

    // ⭐ CE-4090 (AQ108 D14) — this live session must NOT be recorded ("Record live runs" was off when the master minted it). Additive;
    //   omitted ⇒ false ⇒ record, exactly as before.
    [property: JsonPropertyName("RecordingOff")]
    bool           RecordingOff = false,

    // ⭐ CE-3354 ③ — this node's load also waits for its built 3-D map (stamped per node by the master). Additive; omitted ⇒ false.
    [property: JsonPropertyName("MapInLoad")]
    bool           MapInLoad = false
);

/// <summary>Node-level episode payload DTO for <c>StartEpisode</c> / <c>StopEpisode</c>.</summary>
public record NodeEpisodePayloadDto(
    [property: JsonPropertyName("IsStart")]
    bool           IsStart,

    [property: JsonPropertyName("EpisodeId")]
    Guid?          EpisodeId,

    [property: JsonPropertyName("ScenarioId")]
    string?        ScenarioId
);

/// <summary>Node-level prefetch payload DTO for <c>PrefetchFiles</c>.</summary>
public record NodePrefetchPayloadDto(
    [property: JsonPropertyName("ScenarioId")]
    string?        ScenarioId
);

/// <summary>
/// Identifies a file to be pulled from a node's local storage to the central NAS.
/// Returned as <c>ResultPayload</c> in <see cref="Fdp.Toolkit.Orchestration.NodeOpCompletedEvent"/>
/// for the <c>SerializeLocal</c> operation.
/// </summary>
public sealed record FileManifestEntry
{
    /// <summary>
    /// UNC or fully-qualified source path of the file on the originating node
    /// (e.g. <c>\\NODE01\c$\FDP_Temp\checkpoint_a.fdp</c>).
    /// </summary>
    public string SourceUnc { get; init; } = string.Empty;

    /// <summary>
    /// Relative destination path under the NAS base directory into which the file
    /// should be written (e.g. <c>exercises\2026-03-29\checkpoint_a.fdp</c>).
    /// </summary>
    public string RelativeDest { get; init; } = string.Empty;

    /// <summary>
    /// CE-277(c1): the slice's <c>$meta.docType</c>, carried from the node's
    /// <c>FileManifestResult</c> through the JSON round-trip so the orchestrator can classify a pulled
    /// scenario slice (merge our format, route a foreign one verbatim). <c>null</c> for non-scenario paths
    /// (e.g. the <c>.fdp</c> archive), which are not merged.
    /// </summary>
    public string? DocType { get; init; }

    /// <summary>⭐ C1 (docs/DESIGN_Asset_Management.md §1 ②, §8) — the source file's length, so the receiver can tell an
    /// already-current copy without opening it. 0 with a null <see cref="LastWriteUtc"/> = not carried (older senders).</summary>
    public long Length { get; init; }

    /// <summary>⭐ C1 — the source file's last-write time (UTC). Null = not carried; the receiver then always copies.</summary>
    public DateTime? LastWriteUtc { get; init; }
}

/// <summary>
/// Payload DTO for <c>ClusterOpType.DumpDiagnostics</c> DDS requests.
/// Controls which diagnostic data each node collects and how it is packaged.
/// </summary>
public record DiagnosticDumpPayloadDto
{
    [JsonPropertyName("transaction_id")]
    public Guid TransactionId { get; init; }

    [JsonPropertyName("requested_at")]
    public DateTime RequestedAt { get; init; }

    [JsonPropertyName("target_node_ids")]
    public int[]? TargetNodeIds { get; init; }

    [JsonPropertyName("dump_events")]
    public bool DumpEvents { get; init; }

    [JsonPropertyName("dump_entities")]
    public bool DumpEntities { get; init; }

    [JsonPropertyName("dump_architecture")]
    public bool DumpArchitecture { get; init; }

    [JsonPropertyName("dump_logs")]
    public bool DumpLogs { get; init; }

    [JsonPropertyName("event_providers")]
    public string[]? EventProviders { get; init; }

    [JsonPropertyName("use_markdown")]
    public bool UseMarkdownWrapper { get; init; }

    [JsonPropertyName("max_age_hours")]
    public float MaxAgeHours { get; init; } = 24f;

    [JsonPropertyName("severity_threshold")]
    public int SeverityThreshold { get; init; }
}
