using System;
using System.Collections.Generic;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using NedNodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using Fdp.Core.Serialization;

namespace Hrot.Network.Orchestration;

/// <summary>
/// Anti-Corruption Layer translator for the ClusterMaster (orchestrator) side.
/// <para>Egress: drains <see cref="ExecuteNodeOpIntent"/> from the bus, serialises
/// <c>DomainPayload</c> to JSON, and writes <see cref="NodeOpCommand"/> to per-node
/// DDS writers created via <paramref name="commandWriterFactory"/>.</para>
/// <para>Ingress: reads <see cref="NodeOpStatus"/> from DDS and publishes
/// <see cref="NodeOpCompletedEvent"/> on the bus.</para>
/// </summary>
public sealed class NodeOpMasterTranslator
{
    private readonly Func<int, DdsWriter<NodeOpCommand>> _commandWriterFactory;
    private readonly DdsReader<NodeOpStatus>             _statusReader;
    private readonly FdpEventBus                         _bus;
    private readonly JsonSerializerOptions               _jsonOptions;

    /// <summary>
    /// Constructs a <see cref="NodeOpMasterTranslator"/> using a factory that returns a
    /// per-node <see cref="DdsWriter{NodeOpCommand}"/>.
    /// </summary>
    public NodeOpMasterTranslator(
        Func<int, DdsWriter<NodeOpCommand>> commandWriterFactory,
        DdsReader<NodeOpStatus>             statusReader,
        FdpEventBus                         bus,
        JsonSerializerOptions?              jsonOptions = null)
    {
        _commandWriterFactory = commandWriterFactory ?? throw new ArgumentNullException(nameof(commandWriterFactory));
        _statusReader         = statusReader         ?? throw new ArgumentNullException(nameof(statusReader));
        _bus                  = bus                  ?? throw new ArgumentNullException(nameof(bus));
        _jsonOptions          = jsonOptions ?? OrchestrationJsonOptions.Default;
    }

    /// <summary>
    /// Convenience constructor that accepts a pre-built node-id→writer dictionary.
    /// </summary>
    public NodeOpMasterTranslator(
        Dictionary<int, DdsWriter<NodeOpCommand>> commandWriters,
        DdsReader<NodeOpStatus>                   statusReader,
        FdpEventBus                               bus,
        JsonSerializerOptions?                    jsonOptions = null)
        : this(nodeId =>
               {
                   if (!commandWriters.TryGetValue(nodeId, out var w))
                       throw new InvalidOperationException(
                           $"[NodeOpMasterTranslator] No DDS writer registered for node {nodeId}.");
                   return w;
               },
               statusReader, bus, jsonOptions)
    { }

    /// <summary>
    /// Processes one frame: publishes queued node commands to DDS and ingests status replies.
    /// </summary>
    public void Tick()
    {
        // ── Egress: Bus ExecuteNodeOpIntent → DDS NodeOpCommand ──────────────
        foreach (var intent in _bus.ReadManaged<ExecuteNodeOpIntent>())
        {
            var payloadJson = SerializeNodePayload(intent.Operation, intent.DomainPayload);
            var writer      = _commandWriterFactory(intent.TargetNodeId);
            writer.Write(new NodeOpCommand
            {
                TargetNodeId  = intent.TargetNodeId,
                TransactionId = intent.TransactionId,
                Operation     = (NedNodeOpType)(int)intent.Operation,
                PayloadJson   = payloadJson,
            });
        }

        // ── Ingress: DDS NodeOpStatus → Bus NodeOpCompletedEvent ─────────────
        using var scope = _statusReader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            var status        = sample.Data;
            var fdpOp         = (FdpNodeOpType)(int)status.Operation;
            var resultPayload = DeserializeResultPayload(fdpOp, status.ResultJson);
            _bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = status.TransactionId,
                Operation       = fdpOp,
                NodeId          = status.NodeId,
                StatusCode      = (OrchestrationStatusCode)status.StatusCode,
                IsParticipating = status.IsParticipating,
                ResultPayload   = resultPayload,
            });
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Serialises a typed <paramref name="domainPayload"/> to a JSON string suitable for
    /// <see cref="NodeOpCommand.PayloadJson"/>.
    /// </summary>
    private string SerializeNodePayload(FdpNodeOpType operation, object? domainPayload)
    {
        if (domainPayload is null) return string.Empty;

        if (operation == FdpNodeOpType.CollectDiagnostics)
            return JsonSerializer.Serialize(domainPayload, _jsonOptions);

        return domainPayload switch
        {
            CommitStatePayload      csp => JsonSerializer.Serialize(csp, _jsonOptions),
            ReplaySeekPayload       rsp => JsonSerializer.Serialize(rsp, _jsonOptions),
            AbortTransactionPayload atp => JsonSerializer.Serialize(atp, _jsonOptions),

            // ⭐ L1 — the shared content names cross the wire with the rest of the transition payload.
            EditLoadHandlerPayload p => JsonSerializer.Serialize(
                new NodeTransitionPayloadDto(
                    TargetState: p.TargetState != 0
                        ? (Hrot.NED.Descriptors.Orchestration.ClusterState?)p.TargetState
                        : null,
                    ScenarioId:  p.ScenarioId,
                    ExerciseId:  p.ExerciseId,
                    TerrainName: p.TerrainName,
                    IsWorldBoundary: p.IsWorldBoundary,    // CE-2101
                    IsCatchUp:       p.IsCatchUp,          // C7
                    CheckpointingSeconds: p.CheckpointingSeconds),   // D17
                _jsonOptions),

            EpisodeHandlerPayload p => JsonSerializer.Serialize(
                new NodeEpisodePayloadDto(
                    IsStart:    p.IsStart,
                    EpisodeId:  p.EpisodeId == Guid.Empty ? null : p.EpisodeId,
                    ScenarioId: p.ScenarioId),
                _jsonOptions),

            PrefetchHandlerPayload p => JsonSerializer.Serialize(
                new NodePrefetchPayloadDto(p.ScenarioId), _jsonOptions),

            ArchiveHandlerPayload p => JsonSerializer.Serialize(
                new NodeTransitionPayloadDto(
                    TargetState: null,
                    ScenarioId:  null,
                    ExerciseId:  p.ExerciseId),
                _jsonOptions),

            // ⭐ CE-279 Layer B — the distributed scenario save shares the SerializeLocal op with the .fdp
            //   archive. Carry the scenario NAME (as ScenarioId) so the remote node reconstructs a
            //   ScenarioSaveHandlerPayload, not an (empty) ArchiveHandlerPayload. Without this the scenario
            //   payload was silently rebuilt as an archive payload on the wire and no slice was ever written.
            Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload p => JsonSerializer.Serialize(
                new NodeTransitionPayloadDto(
                    TargetState: null,
                    ScenarioId:  p.ScenarioName,
                    ExerciseId:  Guid.Empty),
                _jsonOptions),

            _ => JsonSerializer.Serialize(domainPayload, domainPayload.GetType(), _jsonOptions),
        };
    }

    /// <summary>
    /// Deserialises the <c>ResultJson</c> from a <see cref="NodeOpStatus"/> into a domain
    /// <summary>
    /// Deserialises the <c>ResultJson</c> from a <see cref="NodeOpStatus"/> into a typed domain
    /// result object based on the operation type.
    /// </summary>
    private static object? DeserializeResultPayload(FdpNodeOpType operation, string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;

        switch (operation)
        {
            case FdpNodeOpType.SerializeLocal:
            {
                try
                {
                    var entries = System.Text.Json.JsonSerializer.Deserialize<List<FileManifestEntry>>(
                        resultJson!,
                        FdpJsonOptionsRegistry.DefaultRelaxed);
                    return entries;
                }
                catch
                {
                    return resultJson;
                }
            }

            case FdpNodeOpType.CollectDiagnostics:
            {
                try
                {
                    return JsonSerializer.Deserialize<List<FileManifestEntry>>(
                        resultJson!,
                        FdpJsonOptionsRegistry.DefaultRelaxed);
                }
                catch
                {
                    return resultJson;
                }
            }

            case FdpNodeOpType.PrepareReplay:
            {
                try
                {
                    return JsonSerializer.Deserialize<ReplayPrepareResult>(
                        resultJson!,
                        FdpJsonOptionsRegistry.DefaultRelaxed);
                }
                catch
                {
                    return resultJson;
                }
            }

            // ⭐ CE-3299 B4 — a node's restore answer: the clock its checkpoint was written at (the master snaps to the earliest).
            //   ⛔ Without this case it fell to `default: null` and the cluster never moved its clock back.
            case FdpNodeOpType.RestoreSnapshot:
            {
                try { return JsonSerializer.Deserialize<CheckpointRestoreResult>(resultJson!, FdpJsonOptionsRegistry.DefaultRelaxed); }
                catch { return resultJson; }
            }

            // ⭐ CE-3312 — a node's live-branch answer: the replay clock at the branch (the master restores time and snaps to it).
            //   ⛔ Without this case it fell to `default: null`: on a multi-process cluster a branch left time frozen.
            case FdpNodeOpType.PrepareLive:
            {
                try { return JsonSerializer.Deserialize<LiveBranchResult>(resultJson!, FdpJsonOptionsRegistry.DefaultRelaxed); }
                catch { return resultJson; }
            }

            // ⭐ CE-3299 B3 — a node's export answer is the path it wrote (a JSON string).
            case FdpNodeOpType.ExportCheckpoint:
            {
                try { return JsonSerializer.Deserialize<string>(resultJson!, FdpJsonOptionsRegistry.DefaultRelaxed); }
                catch { return resultJson; }
            }

            case FdpNodeOpType.NodeReplaySeek:
            {
                try
                {
                    return JsonSerializer.Deserialize<ReplaySeekResult>(
                        resultJson!,
                        FdpJsonOptionsRegistry.DefaultRelaxed);
                }
                catch
                {
                    return resultJson;
                }
            }

            default:
                return null;
        }
    }
}
