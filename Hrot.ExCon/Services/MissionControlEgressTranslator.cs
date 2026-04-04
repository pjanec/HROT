using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Hrot.Common.Events;
using Hrot.NED.Messages;

namespace Hrot.ExCon.Services;

/// <summary>
/// Anti-Corruption Layer egress translator for mission-control commands.
///
/// <para>Consumes <see cref="MissionControlIntent"/> events from the <see cref="FdpEventBus"/>
/// (published by <c>MissionEditorService</c>) and writes the corresponding
/// <see cref="MissionControlRequest"/> DDS messages to SimHost.</para>
///
/// <para>This is the <b>only</b> class in the ExCon mission stack that may call
/// <c>System.Text.Json.JsonSerializer</c>.</para>
/// </summary>
public sealed class MissionControlEgressTranslator : IDisposable
{
    private readonly FdpEventBus                      _bus;
    private readonly DdsWriter<MissionControlRequest> _writer;

    /// <summary>
    /// Creates a new translator that consumes from <paramref name="bus"/> and writes
    /// to a <see cref="DdsWriter{T}"/> created on <paramref name="participant"/>
    /// with the default topic name.
    /// </summary>
    public MissionControlEgressTranslator(FdpEventBus bus, DdsParticipant participant)
    {
        _bus    = bus    ?? throw new ArgumentNullException(nameof(bus));
        _writer = new DdsWriter<MissionControlRequest>(
            participant ?? throw new ArgumentNullException(nameof(participant)));
    }

    /// <summary>
    /// Drains all queued <see cref="MissionControlIntent"/> events and writes one
    /// <see cref="MissionControlRequest"/> DDS message per intent.
    /// Call once per frame after the bus <c>SwapBuffers</c>.
    /// </summary>
    public void Tick()
    {
        foreach (var intent in _bus.ConsumeManaged<MissionControlIntent>())
        {
            _writer.Write(new MissionControlRequest
            {
                RequestId      = intent.RequestId,
                TargetEntityId = intent.TargetEntityId,
                BaseVersion    = intent.BaseVersion,
                Payload        = intent.Payload,
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _writer.Dispose();
}
