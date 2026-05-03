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
    string?        TimeMode
);

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

/// <summary>Payload DTO for <c>ClusterOpType.ReplaySeek</c> DDS requests.</summary>
public record SeekReplayPayloadDto(
    [property: JsonPropertyName("TargetWallTicks")]
    long           TargetWallTicks
);

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
    Guid          ExerciseId
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
