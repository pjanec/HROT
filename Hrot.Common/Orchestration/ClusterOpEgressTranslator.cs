using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using NedClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.Common.Orchestration;

/// <summary>
/// Anti-Corruption Layer egress translator for cluster-level commands.
///
/// <para>Consumes <see cref="ClusterOpIntent"/> events from the <see cref="FdpEventBus"/>
/// (published by <c>ClusterScenarioPanel</c>) and writes the corresponding
/// <see cref="ClusterOpRequest"/> DDS messages to the Orchestrator.</para>
///
/// <para>This is the <b>only</b> class in the ExCon cluster-op egress stack that
/// is permitted to call <c>System.Text.Json.JsonSerializer</c>.</para>
///
/// <para>Call <see cref="Tick"/> once per frame after the bus <c>SwapBuffers</c>
/// so that intents published in the previous frame are dispatched in this frame.</para>
/// </summary>
public sealed class ClusterOpEgressTranslator : IDisposable
{
    private readonly FdpEventBus                  _bus;
    private readonly DdsWriter<ClusterOpRequest>  _writer;

    /// <summary>
    /// Creates a new translator that consumes from <paramref name="bus"/> and writes
    /// to a <see cref="DdsWriter{T}"/> created on <paramref name="participant"/>.
    /// </summary>
    public ClusterOpEgressTranslator(FdpEventBus bus, DdsParticipant participant)
    {
        _bus    = bus    ?? throw new ArgumentNullException(nameof(bus));
        _writer = new DdsWriter<ClusterOpRequest>(participant
            ?? throw new ArgumentNullException(nameof(participant)));
    }

    /// <summary>
    /// Drains all queued <see cref="ClusterOpIntent"/> events from the bus and writes
    /// one <see cref="ClusterOpRequest"/> DDS message per intent.
    /// Call once per frame after the bus <c>SwapBuffers</c>.
    /// </summary>
    public void Tick()
    {
        foreach (var intent in _bus.ConsumeManaged<ClusterOpIntent>())
        {
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.RequestId,
                OperationType = (NedClusterOpType)(int)intent.OperationType,
                PayloadJson   = SerializePayload(intent.DomainPayload),
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _writer.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Converts <paramref name="payload"/> to a JSON string for the DDS wire message.
    /// <list type="bullet">
    ///   <item>If <paramref name="payload"/> is already a <see cref="string"/>, it is passed
    ///   through verbatim (allows direct JSON string forwarding from the panel).</item>
    ///   <item>If <paramref name="payload"/> is a non-null object, it is serialised via
    ///   <see cref="JsonSerializer.Serialize(object?, Type, JsonSerializerOptions?)"/>.</item>
    ///   <item>If <paramref name="payload"/> is <c>null</c>, <see cref="string.Empty"/> is
    ///   returned.</item>
    /// </list>
    /// </summary>
    private static string SerializePayload(object? payload)
    {
        return payload switch
        {
            null       => string.Empty,
            string s   => s,
            _ => JsonSerializer.Serialize(payload, payload.GetType()),
        };
    }
}
