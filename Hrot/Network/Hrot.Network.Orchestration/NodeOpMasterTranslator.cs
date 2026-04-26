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
    /// <c>CommitState</c> payloads (boxed <c>int</c>) are written as a raw integer string.
    /// </summary>
    private string SerializeNodePayload(FdpNodeOpType operation, object? domainPayload)
    {
        if (domainPayload is null) return string.Empty;

        return domainPayload switch
        {
            CommitStatePayload      csp => csp.TargetStateId.ToString(),
            ReplaySeekPayload       rsp => rsp.TargetWallTicks.ToString(),
            AbortTransactionPayload atp => atp.TargetTransactionId.ToString(),

            EditLoadHandlerPayload p => JsonSerializer.Serialize(
                new NodeTransitionPayloadDto(
                    TargetState: p.TargetState != 0
                        ? ((Hrot.NED.Descriptors.Orchestration.ClusterState)p.TargetState).ToString()
                        : null,
                    ScenarioId:  p.ScenarioId,
                    ExerciseId:  p.ExerciseId),
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
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return entries;
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
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    return resultJson;
                }
            }
            default:
                return resultJson;
        }
    }
}
