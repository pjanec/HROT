using System;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using FDP.Toolkit.DER;
using FDP.Kernel.Logging;
using FDP.Toolkit.Time.Messages;

namespace Hrot.ExCon.Services;

/// <summary>
/// DDS ingress handler that enqueues MapClickEvent samples for ExCon processing.
/// </summary>
public sealed class MapClickIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MapClickEvent> _reader;
    private readonly IEventQueue<MapClickEvent> _queue;
    private readonly int _maxSamples;
    private readonly long _localNodeId;

    public MapClickIngressHandler(DdsParticipant participant, IEventQueue<MapClickEvent> queue, int maxSamples = 10, long localNodeId = 0)
    {
        _reader = new DdsReader<MapClickEvent>(participant, "MapClickEvent");
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _maxSamples = Math.Max(1, maxSamples);
        _localNodeId = localNodeId;
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            FdpLog<MapClickIngressHandler>.Debug(
                "[Node-{0}] MapClickEvent ingress ctx={1}", _localNodeId, sample.Data.InteractionContextId);
            _queue.Enqueue(sample.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS ingress handler that enqueues SelectionChangedEvent samples for ExCon processing.
/// </summary>
public sealed class SelectionChangedIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SelectionChangedEvent> _reader;
    private readonly IEventQueue<SelectionChangedEvent> _queue;
    private readonly int _maxSamples;

    public SelectionChangedIngressHandler(DdsParticipant participant, IEventQueue<SelectionChangedEvent> queue, int maxSamples = 10)
    {
        _reader = new DdsReader<SelectionChangedEvent>(participant, "SelectionChangedEvent");
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            _queue.Enqueue(sample.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS ingress handler that enqueues MissionControlAck samples so that
/// <see cref="Hrot.ExCon.Services.MissionEditorService"/> can resolve pending
/// commit <see cref="System.Threading.Tasks.Task"/>s without timing out.
/// </summary>
public sealed class MissionControlAckIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MissionControlAck> _reader;
    private readonly IEventQueue<MissionControlAck> _queue;
    private readonly int _maxSamples;

    public MissionControlAckIngressHandler(DdsParticipant participant, IEventQueue<MissionControlAck> queue, int maxSamples = 10)
    {
        _reader = new DdsReader<MissionControlAck>(participant, "MissionControlAck");
        _queue  = queue ?? throw new ArgumentNullException(nameof(queue));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            _queue.Enqueue(sample.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS ingress handler that enqueues <see cref="CreateUpdateDeleteEntityAck"/> samples so that
/// <see cref="Hrot.ExCon.ExConLogic"/> can process two-phase entity lifecycle acknowledgments,
/// manage the pending-entity set, and surface errors to the operator.
/// </summary>
public sealed class CreateUpdateDeleteEntityAckIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<CreateUpdateDeleteEntityAck> _reader;
    private readonly IEventQueue<CreateUpdateDeleteEntityAck> _queue;
    private readonly int _maxSamples;
    private readonly long _localNodeId;

    public CreateUpdateDeleteEntityAckIngressHandler(DdsParticipant participant, IEventQueue<CreateUpdateDeleteEntityAck> queue, int maxSamples = 10, long localNodeId = 0)
    {
        _reader = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");
        _queue  = queue ?? throw new ArgumentNullException(nameof(queue));
        _maxSamples = Math.Max(1, maxSamples);
        _localNodeId = localNodeId;
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            FdpLog<CreateUpdateDeleteEntityAckIngressHandler>.Debug(
                "[Node-{0}] CreateUpdateDeleteEntityAck ingress req={1} entityId={2} status={3}",
                _localNodeId, sample.Data.RequestId, sample.Data.EntityId, sample.Data.StatusCode);
            _queue.Enqueue(sample.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}
/// <summary>
/// DDS ingress handler that enqueues <see cref="MapCommandAck"/> samples so that
/// <see cref="Hrot.ExCon.ExConLogic"/> can correlate command responses and close
/// in-flight <c>CMD_PLACE_ENTITY</c> / <c>CMD_START_AUTHORING</c> transactions.
/// </summary>
public sealed class MapCommandAckIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MapCommandAck> _reader;
    private readonly IEventQueue<MapCommandAck> _queue;
    private readonly int _maxSamples;
    private readonly long _localNodeId;

    public MapCommandAckIngressHandler(DdsParticipant participant, IEventQueue<MapCommandAck> queue, int maxSamples = 10, long localNodeId = 0)
    {
        _reader = new DdsReader<MapCommandAck>(participant, "MapCommandAck");
        _queue  = queue ?? throw new ArgumentNullException(nameof(queue));
        _maxSamples = Math.Max(1, maxSamples);
        _localNodeId = localNodeId;
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            FdpLog<MapCommandAckIngressHandler>.Debug(
                "[Node-{0}] MapCommandAck ingress req={1} status={2}",
                _localNodeId, sample.Data.RequestId, sample.Data.StatusCode);
            _queue.Enqueue(sample.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS ingress handler that forwards SwitchTimeModeWireDto samples to ExConLogic.
/// </summary>
public sealed class TimeModeIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SwitchTimeModeWireDto>  _reader;
    private readonly Action<SwitchTimeModeWireDto>     _onMode;

    public TimeModeIngressHandler(DdsParticipant participant, Action<SwitchTimeModeWireDto> onMode)
    {
        _reader  = new DdsReader<SwitchTimeModeWireDto>(participant);
        _onMode  = onMode ?? throw new ArgumentNullException(nameof(onMode));
    }

    public void Poll()
    {
        using var loan = _reader.Take();
        foreach (var s in loan)
        {
            if (!s.IsValid) continue;
            _onMode(s.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}