using System;
using System.Text.Json.Serialization;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Orchestrator.Translators.Payloads;

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
    string?        ExerciseId,

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
    string?        ExerciseId
);

/// <summary>Payload DTO for <c>ClusterOpType.ReplaySeek</c> DDS requests.</summary>
public record SeekReplayPayloadDto(
    [property: JsonPropertyName("TargetWallTicks")]
    long           TargetWallTicks
);

/// <summary>
/// Node-level transition payload DTO carried inside <c>NodeOpCommand.PayloadJson</c>
/// for <c>PrepareState</c>, <c>PrepareLive</c>, <c>PrepareReplay</c>, <c>PrepareEdit</c>,
/// and <c>FinalizeEdit</c> operations.
/// <c>TargetState</c> is stored as a string to avoid silent integer-as-enum parsing.
/// </summary>
public record NodeTransitionPayloadDto(
    [property: JsonPropertyName("TargetState")]
    string?        TargetState,   // ClusterState as string (e.g. "LoadingLive")

    [property: JsonPropertyName("ScenarioId")]
    string?        ScenarioId,

    [property: JsonPropertyName("ExerciseId")]
    string?        ExerciseId
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
