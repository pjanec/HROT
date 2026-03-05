using System;
using Bagira.BDC.SSTM;
using CycloneDDS.Runtime;
using FDP.Toolkit.DER;
using FDP.Kernel.Logging;

namespace Bagira.IOS.Services;

/// <summary>
/// DDS ingress handler that enqueues MapClickEvent samples for IOS processing.
/// </summary>
public sealed class MapClickIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MapClickEvent> _reader;
    private readonly IEventQueue<MapClickEvent> _queue;
    private readonly int _maxSamples;

    public MapClickIngressHandler(DdsParticipant participant, IEventQueue<MapClickEvent> queue, int maxSamples = 10)
    {
        _reader = new DdsReader<MapClickEvent>(participant, "MapClickEvent");
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            FdpLog<MapClickIngressHandler>.Debug(
                "[TRACE-IOS] MapClickEvent ingress ctx={0}", sample.Data.InteractionContextId);
            _queue.Enqueue(sample.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS ingress handler that enqueues SelectionChangedEvent samples for IOS processing.
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
/// <see cref="Bagira.IOS.Services.MissionEditorService"/> can resolve pending
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
