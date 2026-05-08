using System;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;

namespace GizmoMap.Example
{
    /// <summary>
    /// Live CycloneDDS transport. Instantiates a real participant and binds
    /// to the network, proving out the full serialization and socket layer.
    /// </summary>
    public sealed class DdsGizmoTransport : IGizmoTransport
    {
        private readonly DdsParticipant? _ownedParticipant;
        private readonly IDdsWriter<DebugPrimitivesBatch> _writer;
        private readonly DdsDebugPrimitiveSubscriber _subscriber;
        private uint _frameCounter;

        // Production constructor: creates a real DDS participant bound to the domain.
        public DdsGizmoTransport(uint domainId = 0)
        {
            _ownedParticipant = new DdsParticipant(domainId);
            _writer     = new LiveDdsWriter(_ownedParticipant);
            _subscriber = new DdsDebugPrimitiveSubscriber(new LiveDdsReader(_ownedParticipant));
        }

        // Test constructor: accepts pre-built writer/reader adapters; does not own a DDS participant.
        internal DdsGizmoTransport(IDdsWriter<DebugPrimitivesBatch> writer, IDdsReader<DebugPrimitivesBatch> reader)
        {
            _writer     = writer  ?? throw new ArgumentNullException(nameof(writer));
            _subscriber = new DdsDebugPrimitiveSubscriber(reader ?? throw new ArgumentNullException(nameof(reader)));
        }

        public void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives)
        {
            if (primitives.IsEmpty) return;

            _writer.Write(new DebugPrimitivesBatch
            {
                FrameNumber    = ++_frameCounter,
                NodeId         = 1,
                // Zero-overhead projection matching the network tier strategy.
                PrimitivesData = MemoryMarshal.AsBytes(primitives).ToArray(),
            });
        }

        public void PollAndApply(GizmoPrimitiveBuffer target)
        {
            // Drain all pending batches from the socket.
            while (_subscriber.PollAndApply(target)) { }
        }

        public void Dispose()
        {
            // Strict teardown to prevent socket leaks.
            (_writer as IDisposable)?.Dispose();
            _ownedParticipant?.Dispose();
        }

        // ---- Private live DDS adapters --------------------------------------

        private sealed class LiveDdsWriter : IDdsWriter<DebugPrimitivesBatch>, IDisposable
        {
            private readonly DdsWriter<DebugPrimitivesBatch> _inner;
            public LiveDdsWriter(DdsParticipant participant) => _inner = new DdsWriter<DebugPrimitivesBatch>(participant);
            public void Write(DebugPrimitivesBatch sample) => _inner.Write(sample);
            public void Dispose() => _inner.Dispose();
        }

        private sealed class LiveDdsReader : IDdsReader<DebugPrimitivesBatch>
        {
            private readonly DdsReader<DebugPrimitivesBatch> _inner;
            public LiveDdsReader(DdsParticipant participant) => _inner = new DdsReader<DebugPrimitivesBatch>(participant);

            public bool TryRead(out DebugPrimitivesBatch sample)
            {
                using var loan = _inner.Take(maxSamples: 1);
                if (loan.Count > 0)
                {
                    sample = loan[0];
                    return true;
                }
                sample = default;
                return false;
            }
        }
    }
}
