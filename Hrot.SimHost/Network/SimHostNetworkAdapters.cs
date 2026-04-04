using System;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost.Network
{
    // ─── DDS-backed adapter: polls the DDS reader for incoming CreateEntityRequest ─────────────

    public sealed class DdsCreateEntityRequestSource : ICreateEntityRequestSource
    {
        private readonly DdsReader<CreateEntityRequest> _reader;

        public DdsCreateEntityRequestSource(DdsParticipant participant)
            => _reader = new DdsReader<CreateEntityRequest>(participant);

        public void ProcessRequests(Action<CreateEntityRequest> processor)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);
        }
    }

    // ─── DDS-backed adapter: writes CreateUpdateDeleteEntityAck responses ────────────────────

    public sealed class DdsCreateUpdateDeleteEntityAckSink : ICreateUpdateDeleteEntityAckSink
    {
        private readonly DdsWriter<CreateUpdateDeleteEntityAck> _writer;

        public DdsCreateUpdateDeleteEntityAckSink(DdsParticipant participant)
            => _writer = new DdsWriter<CreateUpdateDeleteEntityAck>(participant);

        public void WriteAck(CreateUpdateDeleteEntityAck ack) => _writer.Write(ack);
    }

    // ─── DDS-backed adapter: polls the DDS reader for incoming DeleteEntityRequest ──────────

    public sealed class DdsDeleteEntityRequestSource : IDeleteEntityRequestSource
    {
        private readonly DdsReader<DeleteEntityRequest> _reader;

        public DdsDeleteEntityRequestSource(DdsParticipant participant)
            => _reader = new DdsReader<DeleteEntityRequest>(participant);

        public void ProcessRequests(Action<DeleteEntityRequest> processor)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);
        }
    }
}
