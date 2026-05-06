using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Hrot.Network.NED.Gizmos;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    // ── Test helper ───────────────────────────────────────────────────────────

    internal sealed class QueuedReader : IDdsReader<DebugPrimitivesBatch>
    {
        private readonly Queue<DebugPrimitivesBatch> _items;
        public QueuedReader(params DebugPrimitivesBatch[] items)
            => _items = new Queue<DebugPrimitivesBatch>(items);
        public bool TryRead(out DebugPrimitivesBatch sample)
        {
            if (_items.TryDequeue(out sample)) return true;
            sample = default;
            return false;
        }
    }

    // ── SC-GZ038 tests ────────────────────────────────────────────────────────

    public class GizmoIngressTranslatorTests
    {
        // SC-GZ038-1: Most recent batch replaces buffer contents.
        [Fact]
        public void SC_GZ038_1_PollAndApply_UsesLatestBatch()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);

            // Two batches with different primitive counts.
            var batch1 = new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 1,
                Primitives = new DebugPrimitive[1] };
            var batch2 = new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 2,
                Primitives = new DebugPrimitive[3] };

            var reader = new QueuedReader(batch1, batch2);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader);
            translator.PollAndApply();

            // Buffer should contain 3 primitives from batch2, not 1 from batch1.
            Assert.Equal(3, buffer.GetFrame().Length);
        }

        // SC-GZ038-3: Null reader — no-op.
        [Fact]
        public void SC_GZ038_3_NullReader_NoOp()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader: null);
            translator.PollAndApply(); // must not throw; buffer unchanged
            Assert.Equal(0, buffer.GetFrame().Length);
        }

        // SC-GZ038-4: Filter by NodeId skips other nodes.
        [Fact]
        public void SC_GZ038_4_FilterNodeId_SkipsOtherNodes()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);

            var fromNode5 = new DebugPrimitivesBatch { NodeId = 5, FrameNumber = 1,
                Primitives = new DebugPrimitive[2] };
            var fromNode9 = new DebugPrimitivesBatch { NodeId = 9, FrameNumber = 2,
                Primitives = new DebugPrimitive[4] };

            var reader = new QueuedReader(fromNode5, fromNode9);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader, filterNodeId: 9);
            translator.PollAndApply();

            // Only node 9's batch (4 primitives) should be applied.
            Assert.Equal(4, buffer.GetFrame().Length);
        }
    }
}
