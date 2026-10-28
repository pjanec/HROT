using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Messages;
using Hrot.NED.Descriptors.Orchestration;
using NedNodeOpType   = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using FdpNodeOpType   = Fdp.Toolkit.Orchestration.NodeOpType;
using Fdp.Core.Serialization;

namespace Hrot.Common.Orchestration;

/// <summary>
/// Anti-Corruption Layer: reads seven DDS orchestration topics and publishes corresponding
/// CQRS events on an <see cref="FdpEventBus"/> for consumption by
/// <c>ClusterUiCache</c>.
///
/// <para>Constructor: <see cref="OrchestrationObserverTranslator(DdsParticipant, FdpEventBus)"/></para>
/// <para>Call <see cref="Tick"/> once per frame before calling <c>FdpEventBus.SwapBuffers()</c>
/// so that <c>ClusterUiCache.Update()</c> can see the translated events in the same frame.</para>
/// <para>
/// <b>NodeOpCommand messages are forwarded promiscuously</b> (all target nodes, not only the
/// local node) so <c>ClusterUiCache</c> can build a complete 2PC transaction history.
/// </para>
/// </summary>
public sealed class OrchestrationObserverTranslator : IDisposable
{
    private readonly DdsReader<ClusterStateTopic>      _stateReader;
    private readonly DdsReader<AssetInventoryTopic>   _inventoryReader;
    private readonly DdsReader<ReplaySessionTopic>    _sessionReader;   // ⭐ CE-3329 rows 32, 45
    private readonly DdsReader<NodeHeartbeat>         _heartbeatReader;
    private readonly DdsReader<SwitchTimeModeWireDto> _timeModeReader;
    private readonly DdsReader<ClusterOpStatus>       _sysOpStatusReader;
    private readonly DdsReader<NodeOpCommand>         _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus>          _nodeOpStatusReader;
    private readonly DdsReader<NodeCapabilitiesTopic> _capabilitiesReader;   // CE-285 (C-cap)
    private readonly FdpEventBus                      _bus;

    /// <summary>
    /// Creates a new translator that reads from <paramref name="participant"/> and
    /// publishes events to <paramref name="bus"/>.
    /// </summary>
    public OrchestrationObserverTranslator(DdsParticipant participant, FdpEventBus bus)
    {
        _bus                = bus ?? throw new ArgumentNullException(nameof(bus));
        _stateReader        = new DdsReader<ClusterStateTopic>(participant);
        _inventoryReader    = new DdsReader<AssetInventoryTopic>(participant);
        _sessionReader      = new DdsReader<ReplaySessionTopic>(participant);
        _heartbeatReader    = new DdsReader<NodeHeartbeat>(participant);
        _timeModeReader     = new DdsReader<SwitchTimeModeWireDto>(participant);
        _sysOpStatusReader  = new DdsReader<ClusterOpStatus>(participant);
        _nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader = new DdsReader<NodeOpStatus>(participant);
        _capabilitiesReader = new DdsReader<NodeCapabilitiesTopic>(participant);
    }

    /// <summary>
    /// Polls all DDS topics and publishes the translated CQRS events to the bus.
    /// Call once per frame, before the bus SwapBuffers.
    /// </summary>
    public void Tick()
    {
        // ClusterStateTopic → ClusterStateUpdateEvent
        using (var l = _stateReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(StateOf(s.Data));

        // AssetInventoryTopic → AssetInventoryUpdateEvent
        using (var l = _inventoryReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(InventoryOf(s.Data));

        // ⭐ CE-3329 — ReplaySessionTopic → ReplaySessionUpdateEvent (an observer — ExCon — draws the session layer only, §9e)
        using (var l = _sessionReader.Take())
            foreach (var s in l)
                if (s.IsValid && Hrot.Network.Orchestration.ReplaySessionWire.FromTopic(s.Data) is { } session)
                    _bus.PublishManaged(new ReplaySessionUpdateEvent { Session = session });

        // NodeHeartbeat → NodeHeartbeatEvent
        using (var l = _heartbeatReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new NodeHeartbeatEvent
                    {
                        NodeId        = s.Data.NodeId,
                        LocalStateId  = (int)s.Data.LocalClusterState,
                        WallTicksUtc  = s.Data.WallTicksUtc,
                        SubsystemName = s.Data.SubsystemName ?? string.Empty,
                        SessionId     = s.Data.SessionId,   // ⭐ C2
                        // CE-286 (C-roles): roles are derived from the NodeCapabilities tokens, not the heartbeat.
                    });

        // NodeCapabilities → NodeCapabilitiesEvent (CE-285 — durable static attributes, gathered by ClusterMaster)
        using (var l = _capabilitiesReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new NodeCapabilitiesEvent
                    {
                        NodeId       = s.Data.NodeId,
                        Capabilities = DeserializeStringArray(s.Data.CapabilitiesJson),
                    });

        // SwitchTimeModeWireDto → SwitchTimeModeEvent (unmanaged — use Publish, not PublishManaged)
        using (var l = _timeModeReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.Publish(s.Data.ToEvent());

        // ClusterOpStatus → ClusterOpCompletedEvent
        using (var l = _sysOpStatusReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                {
                    object? payload = null;
                    if (!string.IsNullOrWhiteSpace(s.Data.ResultJson))
                    {
                        try
                        {
                            payload = DecodeResult(s.Data.ResultJson);
                        }
                        catch
                        {
                            payload = s.Data.ResultJson;
                        }
                    }

                    _bus.PublishManaged(new ClusterOpCompletedEvent
                    {
                        RequestId  = s.Data.RequestId,
                        StatusCode = (OrchestrationStatusCode)s.Data.StatusCode,
                        ResultPayload = payload // <-- Assign the deserialized payload here
                    });
                }

        // NodeOpCommand → ExecuteNodeOpIntent (promiscuous — all target nodes)
        using (var l = _nodeOpCmdReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                {
                    var domainPayload = NodeOpSlaveTranslator.DeserializeNodePayload(
                        s.Data.Operation, s.Data.PayloadJson);
                    _bus.PublishManaged(new ExecuteNodeOpIntent
                    {
                        TransactionId = s.Data.TransactionId,
                        TargetNodeId  = s.Data.TargetNodeId,
                        Operation     = (FdpNodeOpType)(int)s.Data.Operation,
                        DomainPayload = domainPayload,
                    });
                }

        // NodeOpStatus → NodeOpCompletedEvent
        using (var l = _nodeOpStatusReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new NodeOpCompletedEvent
                    {
                        TransactionId   = s.Data.TransactionId,
                        Operation       = (FdpNodeOpType)(int)s.Data.Operation,
                        NodeId          = s.Data.NodeId,
                        StatusCode      = (OrchestrationStatusCode)s.Data.StatusCode,
                        IsParticipating = s.Data.IsParticipating,
                        ResultPayload   = s.Data.ResultJson,
                    });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stateReader.Dispose();
        _inventoryReader.Dispose();
        _sessionReader.Dispose();
        _heartbeatReader.Dispose();
        _timeModeReader.Dispose();
        _sysOpStatusReader.Dispose();
        _nodeOpCmdReader.Dispose();
        _nodeOpStatusReader.Dispose();
        _capabilitiesReader.Dispose();
    }

    /// <summary>
    /// The master's op result as the typed payload its readers expect: ⭐ CE-4093 an <see cref="Fdp.Toolkit.Orchestration.UnsavedCheckResult"/> (recognised by its
    /// one distinctive property — ⛔ the replay result below deserialises "successfully" from ANY JSON object and would swallow it), else the replay
    /// result <c>ClusterUiCache</c> reads. Throws <see cref="JsonException"/> for text that is not JSON (a refusal's reason): the caller keeps the text.
    /// </summary>
    internal static object? DecodeResult(string resultJson)
    {
        using (var doc = JsonDocument.Parse(resultJson))
            if (Fdp.Toolkit.Orchestration.UnsavedCheckResult.LooksLike(doc.RootElement))
                return JsonSerializer.Deserialize<Fdp.Toolkit.Orchestration.UnsavedCheckResult>(resultJson, FdpJsonOptionsRegistry.DefaultRelaxed);
        // Rehydrate the specific DTO expected by ClusterUiCache
        return JsonSerializer.Deserialize<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(resultJson, FdpJsonOptionsRegistry.DefaultRelaxed);
    }

    /// <summary>⭐ R-3055 — the ONE decoding of the master's inventory sample (this observer and every node's
    /// <see cref="NodeOpSlaveTranslator"/> use it).</summary>
    /// <summary>⭐ CE-3329 — the ONE decoding of the master's cluster-state broadcast (this translator and every node's
    /// <c>NodeOpSlaveTranslator</c>): a field added on the wire reaches every reader.</summary>
    internal static ClusterStateUpdateEvent StateOf(in ClusterStateTopic topic) => new()
    {
        CurrentState = (FdpClusterState)(int)topic.CurrentState,
        ExerciseId   = topic.ExerciseId,
        SessionId    = topic.SessionId,   // ⭐ C2
        ReplayAxis   = (Fdp.Toolkit.Orchestration.ReplayAxis)topic.ReplayAxis,   // ⭐ CE-3329 R-3058
        RunNotRecorded = topic.RunNotRecorded != 0,   // ⭐ CE-4090 D14
    };

    internal static AssetInventoryUpdateEvent InventoryOf(in AssetInventoryTopic data) => new()
    {
        LocalScenarios           = DeserializeStringArray(data.LocalScenariosJson),
        LocalExercises           = DeserializeExerciseArray(data.LocalExercisesJson),
        ArchivedExercises        = DeserializeExerciseArray(data.ArchivedExercisesJson),
        UnarchivedLocalExercises = DeserializeExerciseArray(data.UnarchivedLocalExercisesJson),
    };

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json, FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static ExerciseInventoryItem[] DeserializeExerciseArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ExerciseInventoryItem>();
        try { return JsonSerializer.Deserialize<ExerciseInventoryItem[]>(json, FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<ExerciseInventoryItem>(); }
        catch { return Array.Empty<ExerciseInventoryItem>(); }
    }
}
