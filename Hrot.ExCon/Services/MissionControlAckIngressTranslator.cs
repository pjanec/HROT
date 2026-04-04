using System;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Hrot.Common.Events;
using Hrot.NED.Messages;

namespace Hrot.ExCon.Services;

/// <summary>
/// Anti-Corruption Layer ingress translator for mission-control ACKs.
///
/// <para>Polls a <see cref="DdsReader{T}"/> for incoming <see cref="MissionControlAck"/>
/// DDS messages from SimHost and publishes <see cref="MissionControlAckEvent"/> events
/// onto the <see cref="FdpEventBus"/> for consumption by
/// <c>MissionEditorService</c>.</para>
///
/// <para>Call <see cref="Tick"/> once per frame before calling <c>FlpEventBus.SwapBuffers</c>
/// so that ACK events are visible to consumers in the same frame.</para>
/// </summary>
public sealed class MissionControlAckIngressTranslator : IDisposable
{
    private readonly DdsReader<MissionControlAck> _reader;
    private readonly FdpEventBus                  _bus;

    /// <summary>
    /// Creates a new translator that reads from <paramref name="participant"/> and
    /// publishes events to <paramref name="bus"/>.
    /// </summary>
    public MissionControlAckIngressTranslator(DdsParticipant participant, FdpEventBus bus)
    {
        _reader = new DdsReader<MissionControlAck>(
            participant ?? throw new ArgumentNullException(nameof(participant)));
        _bus    = bus    ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <summary>
    /// Polls the DDS reader for incoming ACKs and publishes <see cref="MissionControlAckEvent"/>
    /// to the bus for each valid sample.
    /// Call once per frame before <c>FdpEventBus.SwapBuffers</c>.
    /// </summary>
    public void Tick()
    {
        using var scope = _reader.Take();
        foreach (var sample in scope)
        {
            if (!sample.IsValid) continue;
            _bus.Publish(new MissionControlAckEvent
            {
                RequestId  = sample.Data.RequestId,
                ErrorCode  = sample.Data.ErrorCode,
                NewVersion = sample.Data.NewVersion,
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _reader.Dispose();
}
