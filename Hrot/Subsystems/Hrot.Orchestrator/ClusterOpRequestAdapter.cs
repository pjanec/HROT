using System;
using System.Text.Json;
using Hrot.NED.Descriptors.Orchestration;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpClusterState = FDP.Toolkit.Orchestration.ClusterState;

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

        FdpClusterState targetState = default;
        long   targetWallTicks = 0;
        string? scenarioId    = null;
        string? exerciseId    = null;
        string? timeMode      = null;

        if (int.TryParse(req.PayloadJson, out var rawInt))
        {
            targetState = (FdpClusterState)rawInt;
        }
        else
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(req.PayloadJson); }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"[ClusterOpRequestAdapter] TransitionState payload is not valid JSON: {ex.Message}", ex);
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("TargetState", out var tsProp))
                    throw new InvalidOperationException(
                        "[ClusterOpRequestAdapter] TransitionState JSON missing required 'TargetState'.");

                targetState = (FdpClusterState)tsProp.GetInt32();

                if (doc.RootElement.TryGetProperty("TargetWallTicks", out var twProp))
                    targetWallTicks = twProp.GetInt64();

                if (doc.RootElement.TryGetProperty("ScenarioId", out var sidProp))
                    scenarioId = sidProp.GetString();

                if (doc.RootElement.TryGetProperty("ExerciseId", out var eidProp))
                    exerciseId = eidProp.GetString();

                if (doc.RootElement.TryGetProperty("TimeMode", out var tmProp))
                    timeMode = tmProp.GetString();
            }
        }

        return new TransitionStateIntent
        {
            TransactionId  = req.RequestId,
            TargetState    = targetState,
            TargetWallTicks = targetWallTicks,
            ScenarioId     = scenarioId,
            ExerciseId     = exerciseId,
            TimeMode       = timeMode,
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

        JsonDocument doc;
        try { doc = JsonDocument.Parse(req.PayloadJson); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[ClusterOpRequestAdapter] ManageEpisode payload is not valid JSON: {ex.Message}", ex);
        }

        string? mode;
        Guid    episodeId  = Guid.Empty;
        string? scenarioId = null;

        using (doc)
        {
            mode       = doc.RootElement.TryGetProperty("Mode",      out var mp) ? mp.GetString()   : null;
            scenarioId = doc.RootElement.TryGetProperty("ScenarioId", out var sp) ? sp.GetString()  : null;

            if (doc.RootElement.TryGetProperty("EpisodeId", out var ep))
                Guid.TryParse(ep.GetString(), out episodeId);
        }

        if (string.IsNullOrWhiteSpace(mode))
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] ManageEpisode payload missing 'Mode'.");
        if (episodeId == Guid.Empty)
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] ManageEpisode payload missing or invalid 'EpisodeId'.");

        bool isStart = string.Equals(mode, "Start", StringComparison.OrdinalIgnoreCase);
        if (isStart && string.IsNullOrWhiteSpace(scenarioId))
            throw new InvalidOperationException(
                "[ClusterOpRequestAdapter] ManageEpisode Mode:Start missing 'ScenarioId'.");

        return new ManageEpisodeIntent
        {
            TransactionId = req.RequestId,
            IsStart       = isStart,
            EpisodeId     = episodeId,
            ScenarioId    = scenarioId,
        };
    }

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == ExportArchive</c>
    /// or <c>ImportArchive</c> or <c>SaveScenario</c> to an <see cref="ExecuteStorageOpIntent"/>.
    /// </summary>
    public static ExecuteStorageOpIntent ToExecuteStorageOpIntent(ClusterOpRequest req)
    {
        string? exerciseId = ExtractString(req.PayloadJson, "ExerciseId");
        string? scenarioId = ExtractString(req.PayloadJson, "ScenarioId");

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
            ExerciseId = exerciseId ?? scenarioId,
        };
    }

    /// <summary>
    /// Converts a <see cref="ClusterOpRequest"/> with <c>OperationType == ReplaySeek</c>
    /// to a <see cref="SeekReplayIntent"/>.
    /// </summary>
    public static SeekReplayIntent ToSeekReplayIntent(ClusterOpRequest req)
    {
        long ticks = 0;
        if (!string.IsNullOrWhiteSpace(req.PayloadJson) && long.TryParse(req.PayloadJson, out var t))
            ticks = t;

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

    // ── Internal JSON helpers ────────────────────────────────────────────────

    private static string? ExtractString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty(key, out var el)) return el.GetString();
        }
        catch (JsonException) { }
        return null;
    }
}
