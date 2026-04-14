using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Infrastructure;
using Hrot.NED.Descriptors.Orchestration;
using NedClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NedNodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType    = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Common.Orchestration;

/// <summary>
/// Anti-Corruption Layer translator for the SimHost (slave) side.
/// <para>Ingress: reads <see cref="NodeOpCommand"/> from DDS, deserialises <c>PayloadJson</c>
/// to a typed <c>DomainPayload</c> and publishes <see cref="ExecuteNodeOpIntent"/> on the bus.</para>
/// <para>Heartbeat egress: reads <see cref="NodeHeartbeatEvent"/> from the bus and writes
/// <see cref="NodeHeartbeat"/> to DDS.</para>
/// <para>Status egress: reads <see cref="NodeOpCompletedEvent"/> from the bus and writes
/// <see cref="NodeOpStatus"/> to DDS.</para>
/// </summary>
public sealed class NodeOpSlaveTranslator : IOrchestrationTranslator
{
    private readonly DdsReader<NodeOpCommand>   _commandReader;
    private readonly DdsWriter<NodeOpStatus>    _statusWriter;
    private readonly DdsWriter<NodeHeartbeat>   _heartbeatWriter;
    private readonly FdpEventBus                _bus;
    private readonly int                        _nodeId;
    private readonly JsonSerializerOptions      _jsonOptions;

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Initialises a new <see cref="NodeOpSlaveTranslator"/>.</summary>
    public NodeOpSlaveTranslator(
        DdsReader<NodeOpCommand>   commandReader,
        DdsWriter<NodeOpStatus>    statusWriter,
        DdsWriter<NodeHeartbeat>   heartbeatWriter,
        FdpEventBus                bus,
        int                        nodeId,
        JsonSerializerOptions?     jsonOptions = null)
    {
        _commandReader   = commandReader   ?? throw new ArgumentNullException(nameof(commandReader));
        _statusWriter    = statusWriter    ?? throw new ArgumentNullException(nameof(statusWriter));
        _heartbeatWriter = heartbeatWriter ?? throw new ArgumentNullException(nameof(heartbeatWriter));
        _bus             = bus             ?? throw new ArgumentNullException(nameof(bus));
        _nodeId          = nodeId;
        _jsonOptions     = jsonOptions ?? DefaultOptions;
    }

    /// <summary>
    /// Processes one frame: ingests DDS commands, publishes heartbeats, and writes status updates.
    /// </summary>
    public void Tick()
    {
        // ── Ingress: DDS NodeOpCommand → Bus ExecuteNodeOpIntent ─────────────
        using var scope = _commandReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            var cmd = sample.Data;

            // Only process commands addressed to this node.
            if (cmd.TargetNodeId != _nodeId) continue;

            var domainPayload = DeserializeNodePayload(cmd.Operation, cmd.PayloadJson);
            _bus.PublishManaged(new ExecuteNodeOpIntent
            {
                TransactionId = cmd.TransactionId,
                TargetNodeId  = cmd.TargetNodeId,
                Operation     = (FdpNodeOpType)(int)cmd.Operation,
                DomainPayload = domainPayload,
            });
        }

        // ── Heartbeat egress: Bus NodeHeartbeatEvent → DDS NodeHeartbeat ─────
        foreach (var hb in _bus.ConsumeManaged<NodeHeartbeatEvent>())
        {
            _heartbeatWriter.Write(new NodeHeartbeat
            {
                NodeId            = hb.NodeId,
                SubsystemName     = hb.SubsystemName ?? string.Empty,
                LocalClusterState = (NedClusterState)hb.LocalStateId,
                WallTicksUtc      = hb.WallTicksUtc,
                CpuUsagePercent   = 0f,
                RamUsedBytes      = 0L,
                SimTickAdvancing  = false,
                SubsystemsJson    = string.Empty,
            });
        }

        // ── Status egress: Bus NodeOpCompletedEvent → DDS NodeOpStatus ───────
        foreach (var ev in _bus.ConsumeManaged<NodeOpCompletedEvent>())
        {
            _statusWriter.Write(new NodeOpStatus
            {
                TransactionId   = ev.TransactionId,
                Operation       = (NedNodeOpType)(int)ev.Operation,
                NodeId          = ev.NodeId,
                StatusCode      = (int)ev.StatusCode,
                IsParticipating = ev.IsParticipating,
                ResultJson      = SerializeResultPayload(ev.ResultPayload),
            });
        }
    }

    /// <inheritdoc/>
    /// <remarks>Calls <see cref="Tick"/> to pump the DDS read/publish cycle.</remarks>
    public void Update() => Tick();

    /// <inheritdoc/>
    public void Dispose()
    {
        _commandReader.Dispose();
        _statusWriter.Dispose();
        _heartbeatWriter.Dispose();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Deserialises <paramref name="payloadJson"/> into a typed domain payload object
    /// based on the <paramref name="operation"/> discriminator.
    /// Returns <c>null</c> for operations that carry no payload.
    /// </summary>
    internal static object? DeserializeNodePayload(NedNodeOpType operation, string? payloadJson)
    {
        bool hasPayload = !string.IsNullOrWhiteSpace(payloadJson);

        switch (operation)
        {
            case NedNodeOpType.PrepareState:
            case NedNodeOpType.PrepareLive:
            case NedNodeOpType.PrepareReplay:
            case NedNodeOpType.PrepareEdit:
            case NedNodeOpType.FinalizeEdit:
            {
                if (!hasPayload) return null;
                string? scenarioId    = GetString(payloadJson!, "ScenarioId");
                string? exerciseId    = GetString(payloadJson!, "ExerciseId");
                int     targetStateInt = 0;
                string? tsStr         = GetString(payloadJson!, "TargetState");
                if (!string.IsNullOrWhiteSpace(tsStr) &&
                    Enum.TryParse<NedClusterState>(tsStr, out var cs))
                {
                    targetStateInt = (int)cs;
                }
                return new EditLoadHandlerPayload(
                    ScenarioId:     scenarioId,
                    IsNewScenario:  false,
                    TargetState:    targetStateInt,
                    ExerciseId:     exerciseId);
            }

            case NedNodeOpType.StartEpisode:
            case NedNodeOpType.StopEpisode:
            case NedNodeOpType.ForgetEpisode:
            {
                if (!hasPayload) return null;
                bool   isStart    = GetBool(payloadJson!, "IsStart");
                Guid   episodeId  = GetGuid(payloadJson!, "EpisodeId");
                string? scenarioId = GetString(payloadJson!, "ScenarioId");
                return new EpisodeHandlerPayload(
                    EpisodeId:  episodeId,
                    ScenarioId: scenarioId,
                    IsStart:    isStart);
            }

            case NedNodeOpType.PrefetchFiles:
            {
                if (!hasPayload) return null;
                string? scenarioId = GetString(payloadJson!, "ScenarioId");
                return new PrefetchHandlerPayload(scenarioId);
            }

            case NedNodeOpType.SerializeLocal:
            {
                if (!hasPayload) return null;
                string? exerciseId = GetString(payloadJson!, "ExerciseId");
                return new ArchiveHandlerPayload(exerciseId);
            }

            case NedNodeOpType.CommitState:
            {
                // CommitState carries the new state ID as a raw int string.
                if (hasPayload && int.TryParse(payloadJson!.Trim(), out var stateId))
                    return new CommitStatePayload(stateId);
                return null;
            }

            case NedNodeOpType.NodeReplaySeek:
            {
                if (hasPayload && long.TryParse(payloadJson!.Trim(), out var ticks))
                    return new ReplaySeekPayload(ticks);
                return null;
            }

            case NedNodeOpType.AbortTransaction:
            {
                if (hasPayload && Guid.TryParse(payloadJson!.Trim(), out var txId))
                    return new AbortTransactionPayload(txId);
                return null;
            }

            default:
                return null;
        }
    }

    // ── JSON helpers (avoid taking a dependency on Hrot.Orchestrator DTOs) ─

    private static string? GetString(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }

    private static bool GetBool(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var el) && el.GetBoolean();
        }
        catch { return false; }
    }

    private static Guid GetGuid(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var el))
            {
                var str = el.GetString();
                if (str != null && Guid.TryParse(str, out var g)) return g;
            }
            return Guid.Empty;
        }
        catch { return Guid.Empty; }
    }

    /// <summary>Serialises <paramref name="resultPayload"/> to a JSON string, or empty string if null.</summary>
    private string SerializeResultPayload(object? resultPayload)
    {
        if (resultPayload is null) return string.Empty;
        try
        {
            return JsonSerializer.Serialize(resultPayload, resultPayload.GetType(), _jsonOptions);
        }
        catch
        {
            return string.Empty;
        }
    }
}
