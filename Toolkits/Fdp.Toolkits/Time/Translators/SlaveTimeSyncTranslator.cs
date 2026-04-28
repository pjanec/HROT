using System;
using System.Diagnostics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Time.Translators
{
    /// <summary>
    /// Slave-side NTP clock-sync translator.
    /// <para>
    /// <b>Egress:</b> drains <see cref="TimeSyncRequest"/> from the <see cref="FdpEventBus"/>,
    /// <b>overwrites</b> <c>ClientSendTicks</c> at the exact moment the packet hits the
    /// network (bypassing event-bus buffering delay), and writes to the DDS topic.
    /// </para>
    /// <para>
    /// <b>Ingress:</b> reads <see cref="TimeSyncResponse"/> from DDS; captures <c>t4</c>
    /// immediately on receipt; performs the full NTP formula here at the network boundary;
    /// publishes a <see cref="TimeSyncOffsetCalculatedEvent"/> onto the bus.
    /// Computing the offset inside the translator — rather than inside
    /// <see cref="Controllers.SlaveSyncController"/> — eliminates the ±1-frame jitter
    /// that the double-buffered event bus would otherwise inject into the RTT measurement.
    /// </para>
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="participant"/> in unit-test environments;
    /// all DDS operations become safe no-ops.
    /// </para>
    /// </summary>
    public sealed class SlaveTimeSyncTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "TimeSyncResponse";
        private const long   OrdinalValue   = (long)TimeDescriptorType.TimeSyncResponse;

        private readonly DdsWriter<TimeSyncRequest>?  _requestWriter;
        private readonly DdsReader<TimeSyncResponse>? _responseReader;
        private readonly FdpEventBus _eventBus;
        private readonly int         _localNodeId;
        private readonly Func<long>  _getTick;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Bidirectional;

        /// <summary>Creates the translator.</summary>
        /// <param name="participant">DDS domain participant. Pass <see langword="null"/> for unit tests.</param>
        /// <param name="eventBus">Event bus shared with <see cref="Controllers.SlaveSyncController"/>.</param>
        /// <param name="localNodeId">This slave's node ID — used to filter incoming responses.</param>
        /// <param name="tickSource">
        /// Optional tick source override (<c>HighResUtcClock.GetTicks</c> by default).
        /// Inject a controlled counter in unit tests.
        /// </param>
        public SlaveTimeSyncTranslator(
            DdsParticipant? participant,
            FdpEventBus     eventBus,
            int             localNodeId,
            Func<long>?     tickSource = null)
        {
            _eventBus    = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;
            _getTick     = tickSource ?? HighResUtcClock.GetTicks;

            if (participant is not null)
            {
                _requestWriter  = new DdsWriter<TimeSyncRequest>(participant);
                _responseReader = new DdsReader<TimeSyncResponse>(participant);
            }
        }

        /// <summary>
        /// Drains <see cref="TimeSyncRequest"/> from the bus and writes to DDS.
        /// <c>ClientSendTicks</c> is overwritten with the current tick at the moment of
        /// transmission, eliminating event-bus buffering from the t1 timestamp.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var requests = _eventBus.Read<TimeSyncRequest>();
            foreach (var request in requests)
            {
                if (_requestWriter is null) continue;

                // Overwrite t1 at the network boundary — not when the controller first
                // published the request — so the RTT excludes event-bus delay.
                var outgoing = request;
                outgoing.ClientSendTicks = _getTick();
                _requestWriter.Write(outgoing);
                SentSampleCount++;
            }
        }

        /// <summary>
        /// Reads <see cref="TimeSyncResponse"/> from DDS; captures <c>t4</c> immediately;
        /// computes the NTP offset and publishes <see cref="TimeSyncOffsetCalculatedEvent"/>
        /// onto the bus so <see cref="Controllers.SlaveSyncController"/> can apply it.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_responseReader is null) return;

            using var samples = _responseReader.Take();
            foreach (var sample in samples)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var response = sample.Data;
                if (response.ClientNodeId != _localNodeId) continue;

                // Capture t4 immediately at the network boundary.
                long t4 = _getTick();

                var (offset, rtt) = NtpCompute(
                    response.ClientSendTicks,
                    response.MasterReceiveTicks,
                    response.MasterTransmitTicks,
                    t4);

                _eventBus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = rtt, NewOffset = offset });
            }
        }

        /// <summary>
        /// Computes the NTP clock offset and round-trip time from the four NTP timestamps.
        /// <para>
        /// <c>offset = ((t2 - t1) + (t3 - t4)) / 2</c><br/>
        /// <c>rtt    = (t4 - t1) - (t3 - t2)</c>
        /// </para>
        /// Exposed <see langword="internal"/> for unit-test coverage of the formula.
        /// </summary>
        internal static (long offset, long rtt) NtpCompute(long t1, long t2, long t3, long t4)
        {
            long rtt    = (t4 - t1) - (t3 - t2);
            long offset = ((t2 - t1) + (t3 - t4)) / 2;
            return (offset, rtt);
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
