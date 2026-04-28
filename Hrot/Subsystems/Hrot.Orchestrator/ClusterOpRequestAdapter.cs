using System;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// Converts legacy <see cref="ClusterOpRequest"/> (DDS-path) objects into strongly-typed
/// intent structs understood by the bus-path processing methods in <see cref="ClusterMaster"/>.
///
/// <para>All JSON parsing is isolated in this class so that <c>ClusterMaster.cs</c> and
/// <c>TransitionPlanner.cs</c> remain free of <c>System.Text.Json</c> references.</para>
/// </summary>
internal static class ClusterOpRequestAdapter
{
    /// <summary>Extracts the raw payload string (passthrough for time-control events).</summary>
    public static string GetPayloadString(ClusterOpRequest req) => req.PayloadJson ?? string.Empty;

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == TransitionState</c>
    /// to a <see cref="TransitionStateIntent"/>.
    /// Parses <c>PayloadJson</c> to extract <c>TargetState</c>, <c>ScenarioId</c>,
    /// <c>TargetWallTicks</c>, <c>TimeMode</c>, and <c>ExerciseId</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>PayloadJson</c> is empty, non-parseable, or missing required fields.
    /// </exception>
    public static TransitionStateIntent ToTransitionStateIntent(ClusterOpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PayloadJson))
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] TransitionState payload is empty — " +
                "a valid target state is required.");

        // Legacy path: bare integer string (e.g. "30") used by some tests and headless paths.
        if (int.TryParse(req.PayloadJson, out var rawInt))
        {
            return new TransitionStateIntent
            {
                TransactionId = req.RequestId,
                TargetState   = (FdpClusterState)rawInt,
            };
        }

        TransitionPayloadDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<TransitionPayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default)
                  ?? throw new InvalidOperationException(
                      "[ClusterOpRequestAdapter] TransitionState payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[ClusterOpRequestAdapter] TransitionState payload is not valid JSON: {ex.Message}", ex);
        }

        if (dto.TargetState == null)
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] TransitionState JSON missing required 'TargetState'.");

        return new TransitionStateIntent
        {
            TransactionId   = req.RequestId,
            TargetState     = (FdpClusterState)(int)dto.TargetState.Value,
            TargetWallTicks = 0,
            ScenarioId      = dto.ScenarioId,
            ExerciseId      = dto.ExerciseId,
            TimeMode        = dto.TimeMode,
        };
    }

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == ManageEpisode</c>
    /// to a <see cref="ManageEpisodeIntent"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the JSON payload is missing required fields.
    /// </exception>
    public static ManageEpisodeIntent ToManageEpisodeIntent(ClusterOpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PayloadJson))
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] ManageEpisode payload is empty.");

        ManageEpisodePayloadDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManageEpisodePayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default)
                  ?? throw new InvalidOperationException(
                      "[ClusterOpRequestAdapter] ManageEpisode payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[ClusterOpRequestAdapter] ManageEpisode payload is not valid JSON: {ex.Message}", ex);
        }

        Guid episodeId = dto.EpisodeId ?? Guid.Empty;
        if (episodeId == Guid.Empty)
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] ManageEpisode payload missing or invalid 'EpisodeId'.");

        if (dto.IsStart && string.IsNullOrWhiteSpace(dto.ScenarioId))
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] ManageEpisode Start missing 'ScenarioId'.");

        return new ManageEpisodeIntent
        {
            TransactionId = req.RequestId,
            IsStart       = dto.IsStart,
            EpisodeId     = episodeId,
            ScenarioId    = dto.ScenarioId,
        };
    }

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == ExportArchive</c>
    /// or <c>ImportArchive</c> or <c>SaveScenario</c> to an <see cref="ExecuteStorageOpIntent"/>.
    /// </summary>
    public static ExecuteStorageOpIntent ToExecuteStorageOpIntent(ClusterOpRequest req)
    {
        Guid exerciseId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(req.PayloadJson))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<ArchivePayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default);
                exerciseId = dto?.ExerciseId ?? Guid.Empty;
            }
            catch (JsonException) { }
        }

        var opType = req.OperationType switch
        {
            ClusterOpType.ExportArchive  => StorageOpType.Export,
            ClusterOpType.ImportArchive  => StorageOpType.Import,
            ClusterOpType.SaveScenario   => StorageOpType.SaveScenario,
            _ => StorageOpType.SaveScenario,
        };

        return new ExecuteStorageOpIntent
        {
            RequestId  = req.RequestId,
            Operation  = opType,
            ExerciseId = exerciseId,
        };
    }

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == ReplaySeek</c>
    /// to a <see cref="SeekReplayIntent"/>.
    /// </summary>
    public static SeekReplayIntent ToSeekReplayIntent(ClusterOpRequest req)
    {
        long ticks = 0;
        if (!string.IsNullOrWhiteSpace(req.PayloadJson))
        {
            if (!long.TryParse(req.PayloadJson, out ticks))
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<SeekReplayPayloadDto>(req.PayloadJson, OrchestrationJsonOptions.Default);
                    ticks = dto?.TargetWallTicks ?? 0;
                }
                catch { }
            }
        }

        return new SeekReplayIntent { RequestId = req.RequestId, TargetWallTicks = ticks };
    }

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == CancelOperation</c>
    /// to a <see cref="CancelOperationIntent"/>.
    /// </summary>
    public static CancelOperationIntent ToCancelOperationIntent(ClusterOpRequest req)
    {
        Guid targetId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(req.PayloadJson))
            Guid.TryParse(req.PayloadJson.Trim(), out targetId);
        return new CancelOperationIntent { TargetRequestId = targetId };
    }

}
