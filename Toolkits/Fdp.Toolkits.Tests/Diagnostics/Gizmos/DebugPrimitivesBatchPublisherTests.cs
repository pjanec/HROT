// SC-GZ033: DebugPrimitivesBatchPublisherSystem tests.
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // =========================================================================
    // Capturing mock IDdsWriter<DebugPrimitivesBatch>
    // =========================================================================

    internal sealed class CapturingBatchWriter : IDdsWriter<DebugPrimitivesBatch>
    {
        public readonly List<DebugPrimitivesBatch> Written = new();
        public void Write(DebugPrimitivesBatch sample) => Written.Add(sample);
    }

    // =========================================================================
    // SC-GZ033: DebugPrimitivesBatchPublisherSystem
    // =========================================================================

    public class DebugPrimitivesBatchPublisherTests
    {
        // Adds N sphere primitives to the buffer so GetFrame() returns N entries.
        private static void FillBuffer(DebugPrimitiveBuffer buf, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var p = default(DebugPrimitive);
                p.Shape        = DebugPrimitiveShape.Sphere;
                p.TargetView   = PipelineTarget.Map2D;
                p.SphereRadius = 1f;
                buf.Append(p);
            }
        }

        // SC-GZ033-1: Buffer with N primitives -> exactly one Write call, Primitives.Length == N.
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public void SC_GZ033_1_NonEmptyBuffer_WritesOneCallWithCorrectCount(int n)
        {
            var buffer = new DebugPrimitiveBuffer(64);
            FillBuffer(buffer, n);

            var writer = new CapturingBatchWriter();
            var sys = new DebugPrimitivesBatchPublisherSystem(buffer, nodeId: 1, writer: writer);

            sys.Execute(null!, 0f);

            Assert.Single(writer.Written);
            Assert.Equal(n, writer.Written[0].Primitives.Length);
        }

        // SC-GZ033-2: Empty buffer -> zero Write calls.
        [Fact]
        public void SC_GZ033_2_EmptyBuffer_NoWriteCalls()
        {
            var buffer = new DebugPrimitiveBuffer(64);
            var writer = new CapturingBatchWriter();
            var sys = new DebugPrimitivesBatchPublisherSystem(buffer, nodeId: 0, writer: writer);

            sys.Execute(null!, 0f);

            Assert.Empty(writer.Written);
        }

        // SC-GZ033-3: Null writer -> Execute returns without exception.
        [Fact]
        public void SC_GZ033_3_NullWriter_NoException()
        {
            var buffer = new DebugPrimitiveBuffer(64);
            FillBuffer(buffer, 3);
            var sys = new DebugPrimitivesBatchPublisherSystem(buffer, nodeId: 0, writer: null);

            // Must not throw.
            sys.Execute(null!, 0f);
        }

        // SC-GZ033-4: FrameNumber increments by 1 per Execute call.
        [Fact]
        public void SC_GZ033_4_FrameNumber_IncrementsPerExecute()
        {
            var buffer = new DebugPrimitiveBuffer(64);
            var writer = new CapturingBatchWriter();
            var sys = new DebugPrimitivesBatchPublisherSystem(buffer, nodeId: 0, writer: writer);

            // Execute three times with a non-empty buffer each time.
            for (int call = 0; call < 3; call++)
            {
                buffer.Clear();
                FillBuffer(buffer, 1);
                sys.Execute(null!, 0f);
            }

            Assert.Equal(3, writer.Written.Count);
            Assert.Equal(0u, writer.Written[0].FrameNumber);
            Assert.Equal(1u, writer.Written[1].FrameNumber);
            Assert.Equal(2u, writer.Written[2].FrameNumber);
        }
    }
}
