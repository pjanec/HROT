using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Infrastructure;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using NedClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NedNodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType    = Fdp.Toolkit.Orchestration.NodeOpType;
using FdpClusterState  = Fdp.Toolkit.Orchestration.ClusterState;

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
        IncludeFields = true
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
        foreach (var hb in _bus.ReadManaged<NodeHeartbeatEvent>())
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
        foreach (var ev in _bus.ReadManaged<NodeOpCompletedEvent>())
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
                var dto = JsonSerializer.Deserialize<NodeTransitionPayloadDto>(payloadJson!, OrchestrationJsonOptions.Default);
                return new EditLoadHandlerPayload(
                    ScenarioId:    dto?.ScenarioId,
                    IsNewScenario: false,
                    TargetState:   dto?.TargetState.HasValue == true ? (FdpClusterState)(int)dto.TargetState.Value : default,
                    ExerciseId:    dto?.ExerciseId ?? Guid.Empty);
            }

            case NedNodeOpType.StartEpisode:
            case NedNodeOpType.StopEpisode:
            case NedNodeOpType.ForgetEpisode:
            {
                if (!hasPayload) return null;
                var dto = JsonSerializer.Deserialize<NodeEpisodePayloadDto>(payloadJson!, OrchestrationJsonOptions.Default);
                return new EpisodeHandlerPayload(
                    EpisodeId:  dto?.EpisodeId ?? Guid.Empty,
                    ScenarioId: dto?.ScenarioId,
                    IsStart:    dto?.IsStart ?? false);
            }

            case NedNodeOpType.PrefetchFiles:
            {
                if (!hasPayload) return null;
                var dto = JsonSerializer.Deserialize<NodePrefetchPayloadDto>(payloadJson!, OrchestrationJsonOptions.Default);
                return new PrefetchHandlerPayload(dto?.ScenarioId);
            }

            case NedNodeOpType.SerializeLocal:
            {
                if (!hasPayload) return null;
                var dto = JsonSerializer.Deserialize<ArchivePayloadDto>(payloadJson!, OrchestrationJsonOptions.Default);
                return new ArchiveHandlerPayload(dto?.ExerciseId ?? Guid.Empty);
            }

            case NedNodeOpType.CommitState:
            {
                if (!hasPayload) return null;
                try
                {
                    return JsonSerializer.Deserialize<CommitStatePayload>(payloadJson!, DefaultOptions);
                }
                catch { return null; }
            }

            case NedNodeOpType.NodeReplaySeek:
            {
                if (!hasPayload) return null;
                try
                {
                    return JsonSerializer.Deserialize<ReplaySeekPayload>(payloadJson!, DefaultOptions);
                }
                catch { return null; }
            }

            case NedNodeOpType.AbortTransaction:
            {
                if (!hasPayload) return null;
                try
                {
                    return JsonSerializer.Deserialize<AbortTransactionPayload>(payloadJson!, DefaultOptions);
                }
                catch { return null; }
            }

            default:
                return null;
        }
    }

    // ── JSON helpers (avoid taking a dependency on Hrot.Orchestrator DTOs) ─

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
