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
    private readonly DdsReader<NodeHeartbeat>         _heartbeatReader;
    private readonly DdsReader<SwitchTimeModeWireDto> _timeModeReader;
    private readonly DdsReader<ClusterOpStatus>       _sysOpStatusReader;
    private readonly DdsReader<NodeOpCommand>         _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus>          _nodeOpStatusReader;
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
        _heartbeatReader    = new DdsReader<NodeHeartbeat>(participant);
        _timeModeReader     = new DdsReader<SwitchTimeModeWireDto>(participant);
        _sysOpStatusReader  = new DdsReader<ClusterOpStatus>(participant);
        _nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader = new DdsReader<NodeOpStatus>(participant);
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
                    _bus.PublishManaged(new ClusterStateUpdateEvent
                    {
                        CurrentState = (FdpClusterState)(int)s.Data.CurrentState,
                        ExerciseId   = s.Data.ExerciseId,
                    });

        // AssetInventoryTopic → AssetInventoryUpdateEvent
        using (var l = _inventoryReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new AssetInventoryUpdateEvent
                    {
                        LocalScenarios           = DeserializeStringArray(s.Data.LocalScenariosJson),
                        LocalExercises           = DeserializeStringArray(s.Data.LocalExercisesJson),
                        ArchivedExercises        = DeserializeStringArray(s.Data.ArchivedExercisesJson),
                        UnarchivedLocalExercises = DeserializeStringArray(s.Data.UnarchivedLocalExercisesJson),
                    });

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
                            // Rehydrate the specific DTO expected by ClusterUiCache
                            payload = JsonSerializer.Deserialize<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(
                                s.Data.ResultJson,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
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
        _heartbeatReader.Dispose();
        _timeModeReader.Dispose();
        _sysOpStatusReader.Dispose();
        _nodeOpCmdReader.Dispose();
        _nodeOpStatusReader.Dispose();
    }

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
