using System;
using System.Collections.Concurrent;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;

namespace GizmoMap.Example
{
    /// <summary>
    /// In-process loopback DDS transport. Uses DdsDebugPrimitivePublisher and
    /// DdsDebugPrimitiveSubscriber backed by a ConcurrentQueue, exercising the
    /// serialization path through those adapters without a live CycloneDDS participant.
    /// </summary>
    public sealed class DdsGizmoTransport : IGizmoTransport
    {
        private readonly ConcurrentQueue<DebugPrimitivesBatch> _queue = new();
        private readonly InMemoryDdsWriter _writer;
        private readonly InMemoryDdsReader _reader;
        private readonly DdsDebugPrimitiveSubscriber _subscriber;
        private uint _frameCounter;

        public DdsGizmoTransport()
        {
            _writer     = new InMemoryDdsWriter(_queue);
            _reader     = new InMemoryDdsReader(_queue);
            _subscriber = new DdsDebugPrimitiveSubscriber(_reader);
        }

        public void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives)
        {
            var arr   = primitives.ToArray();
            var batch = new DebugPrimitivesBatch
            {
                FrameNumber = ++_frameCounter,
                NodeId      = 1,
                Primitives  = arr,
            };
            _writer.Write(batch);
        }

        public void PollAndApply(DebugPrimitiveBuffer target)
        {
            // Drain all pending batches.
            while (_subscriber.PollAndApply(target)) { }
        }

        public void Dispose() { /* no-op for in-process queue */ }

        // ---- Private adapters -----------------------------------------------

        private sealed class InMemoryDdsWriter : IDdsWriter<DebugPrimitivesBatch>
        {
            private readonly ConcurrentQueue<DebugPrimitivesBatch> _q;
            public InMemoryDdsWriter(ConcurrentQueue<DebugPrimitivesBatch> q) => _q = q;
            public void Write(DebugPrimitivesBatch sample) => _q.Enqueue(sample);
        }

        private sealed class InMemoryDdsReader : IDdsReader<DebugPrimitivesBatch>
        {
            private readonly ConcurrentQueue<DebugPrimitivesBatch> _q;
            public InMemoryDdsReader(ConcurrentQueue<DebugPrimitivesBatch> q) => _q = q;
            public bool TryRead(out DebugPrimitivesBatch sample) => _q.TryDequeue(out sample);
        }
    }
}
