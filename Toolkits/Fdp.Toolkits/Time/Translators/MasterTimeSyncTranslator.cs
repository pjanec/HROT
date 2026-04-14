using System;
using System.Diagnostics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost_Core.Abstractions;

namespace FDP.Toolkit.Time.Translators
{
    /// <summary>
    /// Master-side NTP clock-sync translator.
    /// <para>
    /// <b>Ingress only:</b> reads <see cref="TimeSyncRequest"/> samples from DDS, records the
    /// master receive timestamp, constructs a <see cref="TimeSyncResponse"/>, records the
    /// transmit timestamp, and writes the response back to DDS — all without touching the
    /// event bus.
    /// </para>
    /// <para>
    /// <b>Egress:</b> no-op.  The master does not send requests.
    /// </para>
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="participant"/> in unit-test environments;
    /// all DDS operations become safe no-ops.
    /// </para>
    /// </summary>
    public sealed class MasterTimeSyncTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "TimeSyncRequest";
        private const long   OrdinalValue   = 205L;

        private readonly DdsReader<TimeSyncRequest>?  _requestReader;
        private readonly DdsWriter<TimeSyncResponse>? _responseWriter;
        private readonly Func<long>                   _getTick;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;

        /// <summary>Creates the translator.</summary>
        /// <param name="participant">DDS domain participant. Pass <see langword="null"/> for unit tests.</param>
        /// <param name="tickSource">
        /// Optional tick source override (<c>Stopwatch.GetTimestamp</c> by default).
        /// Inject a controlled counter in unit tests.
        /// </param>
        public MasterTimeSyncTranslator(DdsParticipant? participant, Func<long>? tickSource = null)
        {
            _getTick = tickSource ?? Stopwatch.GetTimestamp;

            if (participant is not null)
            {
                _requestReader  = new DdsReader<TimeSyncRequest>(participant);
                _responseWriter = new DdsWriter<TimeSyncResponse>(participant);
            }
        }

        /// <summary>No-op — master does not send sync requests.</summary>
        public void ScanAndPublish(ISimulationView view) { }

        /// <summary>
        /// Reads all pending <see cref="TimeSyncRequest"/> samples; for each, builds and writes
        /// a <see cref="TimeSyncResponse"/> with master-side receive and transmit timestamps.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_requestReader is null || _responseWriter is null) return;

            using var samples = _requestReader.Take();
            foreach (var sample in samples)
            {
                if (!sample.IsValid) continue;
                var request = sample.Data;

                long masterReceiveTicks = _getTick();

                var response = new TimeSyncResponse
                {
                    ClientNodeId        = request.ClientNodeId,
                    ClientSendTicks     = request.ClientSendTicks,
                    MasterReceiveTicks  = masterReceiveTicks,
                    MasterTransmitTicks = 0, // filled in after
                };

                long masterTransmitTicks = _getTick();
                response.MasterTransmitTicks = masterTransmitTicks;

                _responseWriter.Write(response);

                FDP.Kernel.Logging.FdpLog<MasterTimeSyncTranslator>.Trace(
                    "[TC3][Master] SyncResponse sent. Node={0}, RTT_approx={1} ticks",
                    request.ClientNodeId,
                    masterTransmitTicks - request.ClientSendTicks);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
