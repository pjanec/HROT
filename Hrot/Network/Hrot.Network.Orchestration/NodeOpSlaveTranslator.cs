using System.Linq;
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
using Fdp.Core.Serialization;

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
    private readonly DdsWriter<NodeCapabilitiesTopic>? _capabilitiesWriter;   // CE-285 (C-cap): null in tests / offline.

    /// <summary>⭐ P6 N5 — tokens describing how this node can be REACHED (the share it advertises); added to whatever the host advertises.</summary>
    private readonly string[] _transportCapabilities;
    private readonly DdsReader<ClusterStateTopic>?     _clusterStateReader;   // ⭐ C2: null in tests / offline.
    private readonly DdsReader<AssetInventoryTopic>?   _inventoryReader;      // ⭐ R-3055: what the cluster can replay, on every node
    private readonly DdsReader<ReplaySessionTopic>?    _sessionReader;        // ⭐ CE-3329 rows 32, 45: the replay's session layer
    private readonly FdpEventBus                _bus;
    private readonly int                        _nodeId;
    private readonly JsonSerializerOptions      _jsonOptions;

    // CE-3187 (R-2005 / R-2007): the shared options — enums as names, fields, null-omitting, case-insensitive.
    private static readonly JsonSerializerOptions DefaultOptions = FdpJsonOptionsRegistry.DefaultRelaxed;

    /// <summary>Initialises a new <see cref="NodeOpSlaveTranslator"/>.</summary>
    public NodeOpSlaveTranslator(
        DdsReader<NodeOpCommand>   commandReader,
        DdsWriter<NodeOpStatus>    statusWriter,
        DdsWriter<NodeHeartbeat>   heartbeatWriter,
        FdpEventBus                bus,
        int                        nodeId,
        JsonSerializerOptions?     jsonOptions = null,
        DdsWriter<NodeCapabilitiesTopic>? capabilitiesWriter = null,
        DdsReader<ClusterStateTopic>?     clusterStateReader = null,
        DdsReader<AssetInventoryTopic>?   inventoryReader = null,
        string[]?                         transportCapabilities = null,
        DdsReader<ReplaySessionTopic>?    sessionReader = null)
    {
        _clusterStateReader = clusterStateReader;
        _inventoryReader    = inventoryReader;
        _sessionReader      = sessionReader;
        _transportCapabilities = transportCapabilities ?? Array.Empty<string>();
        _commandReader   = commandReader   ?? throw new ArgumentNullException(nameof(commandReader));
        _statusWriter    = statusWriter    ?? throw new ArgumentNullException(nameof(statusWriter));
        _heartbeatWriter = heartbeatWriter ?? throw new ArgumentNullException(nameof(heartbeatWriter));
        _capabilitiesWriter = capabilitiesWriter;
        _bus             = bus             ?? throw new ArgumentNullException(nameof(bus));
        _nodeId          = nodeId;
        _jsonOptions     = jsonOptions ?? DefaultOptions;
    }

    /// <summary>
    /// Processes one frame: ingests DDS commands, publishes heartbeats, and writes status updates.
    /// </summary>
    public void Tick()
    {
        // ⭐ R-3055 — the master's exercise inventory, on every node's bus (its ClusterSlave keeps the last one): the same answer to
        //   GET /exercises on CGF, SimHost, IG as on the editor and the orchestrator.
        if (_inventoryReader != null)
        {
            using var inventoryScope = _inventoryReader.Take();
            foreach (var s in inventoryScope)
                if (s.IsValid) _bus.PublishManaged(OrchestrationObserverTranslator.InventoryOf(s.Data));
        }

        // ⭐ CE-3329 — the replay's session layer (§9f) on every node's bus: its time facade and cluster cache fold it
        if (_sessionReader != null)
        {
            using var sessionScope = _sessionReader.Take();
            foreach (var s in sessionScope)
                if (s.IsValid && Hrot.Network.Orchestration.ReplaySessionWire.FromTopic(s.Data) is { } session)
                    _bus.PublishManaged(new ReplaySessionUpdateEvent { Session = session });
        }

        // ── ⭐⭐ C2 (Q91 §4-I I5, R-4006) — Cluster-state ingress: DDS ClusterStateTopic → bus ClusterStateUpdateEvent, so the
        //   node's ClusterSlave knows which SESSION it follows. A not-alive instance means the master's writer is gone.
        if (_clusterStateReader != null)
        {
            using var stateScope = _clusterStateReader.Take();
            foreach (var s in stateScope)
            {
                if (s.IsValid)
                    _bus.PublishManaged(OrchestrationObserverTranslator.StateOf(s.Data));   // ⭐ CE-3329 — the one decoding
                else if (s.Info.InstanceState != DdsInstanceState.Alive)
                    _bus.PublishManaged(new ClusterStateUpdateEvent { MasterGone = true });
            }
        }

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
                SessionId         = hb.SessionId,   // ⭐ C2
                SubsystemName     = hb.SubsystemName ?? string.Empty,
                LocalClusterState = (NedClusterState)hb.LocalStateId,
                WallTicksUtc      = hb.WallTicksUtc,
                CpuUsagePercent   = 0f,
                RamUsedBytes      = 0L,
                SimTickAdvancing  = false,
                SubsystemsJson    = string.Empty,
                // CE-286 (C-roles): heartbeat no longer carries the role mask — roles travel as fdp.role.*
                // capability tokens on the durable NodeCapabilities descriptor.
            });
        }

        // ── Capabilities egress: Bus NodeCapabilitiesEvent → durable DDS NodeCapabilities (CE-285) ──
        // Published once at join by ClusterSlave; the durable descriptor retains the last sample per node.
        if (_capabilitiesWriter != null)
        {
            foreach (var caps in _bus.ReadManaged<NodeCapabilitiesEvent>())
            {
                _capabilitiesWriter.Write(new NodeCapabilitiesTopic
                {
                    NodeId           = caps.NodeId,
                    CapabilitiesJson = JsonSerializer.Serialize(
                        (caps.Capabilities ?? Array.Empty<string>()).Concat(_transportCapabilities).Distinct().ToArray(),
                        FdpJsonOptionsRegistry.DefaultRelaxed),
                });
            }
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
        _capabilitiesWriter?.Dispose();
        _inventoryReader?.Dispose();
        _sessionReader?.Dispose();
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
                    ExerciseId:    dto?.ExerciseId ?? Guid.Empty,
                    // ⭐ L1 — null when the sender omitted it (an older peer, or a scenario naming none).
                    //   The load steps treat a null name as "this scenario names none", which is legal.
                    TerrainName:   dto?.TerrainName,
                    IsWorldBoundary: dto?.IsWorldBoundary ?? false,    // CE-2101
                    IsCatchUp:       dto?.IsCatchUp ?? false,          // C7
                    CheckpointingSeconds: dto?.CheckpointingSeconds ?? 0);   // D17
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
                // ⭐ CE-279 Layer B — SerializeLocal carries EITHER the scenario save (ScenarioId = the scenario
                //   name) OR the .fdp archive (ExerciseId). Discriminate so the scenario payload survives the wire
                //   as a ScenarioSaveHandlerPayload instead of being flattened to an empty ArchiveHandlerPayload.
                var dto = JsonSerializer.Deserialize<NodeTransitionPayloadDto>(payloadJson!, OrchestrationJsonOptions.Default);
                if (!string.IsNullOrWhiteSpace(dto?.ScenarioId))
                    return new Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload(dto!.ScenarioId!);
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

            case NedNodeOpType.CollectDiagnostics:
            {
                if (!hasPayload) return null;
                try
                {
                    return JsonSerializer.Deserialize<DiagnosticDumpPayloadDto>(payloadJson!, DefaultOptions);
                }
                catch { return null; }
            }

            // ⭐ CE-3299 B3 (R-3044) — a checkpoint: its label and the wall time every node writes it at; an export names it.
            case NedNodeOpType.TakeSnapshot:
            {
                if (!hasPayload) return null;
                try { return JsonSerializer.Deserialize<CheckpointPayload>(payloadJson!, DefaultOptions); }
                catch { return null; }
            }

            case NedNodeOpType.ExportCheckpoint:
            {
                if (!hasPayload) return null;
                try { return JsonSerializer.Deserialize<ExportCheckpointPayload>(payloadJson!, DefaultOptions); }
                catch { return null; }
            }

            // ⭐ CE-3299 B4 — both phases of a checkpoint restore carry the label and the exercise the node records on as.
            case NedNodeOpType.PrepareRestore:
            case NedNodeOpType.RestoreSnapshot:
            {
                if (!hasPayload) return null;
                try { return JsonSerializer.Deserialize<RestoreCheckpointPayload>(payloadJson!, DefaultOptions); }
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
