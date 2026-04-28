using System;
using System.Text.Json.Serialization;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Orchestration;

/// <summary>
/// A <see cref="JsonStringEnumConverter"/> variant that rejects numeric enum values,
/// throwing <see cref="System.Text.Json.JsonException"/> when an integer is encountered.
/// This prevents the silent integer-as-enum parsing bug in the wire protocol.
/// </summary>
public sealed class StrictStringEnumConverter : JsonStringEnumConverter
{
    /// <summary>Initialises the converter with <c>allowIntegerValues = false</c>.</summary>
    public StrictStringEnumConverter() : base(allowIntegerValues: false) { }
}

/// <summary>
/// Shared <see cref="System.Text.Json.JsonSerializerOptions"/> for all orchestration
/// payload DTOs.  Enums are serialised as strings; numeric enum values are rejected;
/// null properties are omitted.
/// </summary>
public static class OrchestrationJsonOptions
{
    /// <summary>
    /// Options that enforce string-based enum serialisation, reject integer enum values,
    /// and suppress null values.
    /// Use these for all DDS payload round-trips to avoid silent integer-as-enum bugs.
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions Default = new()
    {
        Converters = { new StrictStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };
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
