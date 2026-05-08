using System;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network
{
    // Stateless transport adapter that packs a GizmoPrimitiveBuffer into a
    // DebugPrimitivesBatch and writes it via the injected DDS writer.
    // No ECS dependencies; pure data transformation.
    public sealed class DdsDebugPrimitivePublisher
    {
        private readonly IDdsWriter<DebugPrimitivesBatch> _writer;

        public DdsDebugPrimitivePublisher(IDdsWriter<DebugPrimitivesBatch> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        // Packs all primitives from 'buffer' as raw bytes into a DebugPrimitivesBatch and publishes it.
        public void Publish(GizmoPrimitiveBuffer buffer, uint frameNumber, byte nodeId)
        {
            var frame = buffer.GetFrame();
            var batch = new DebugPrimitivesBatch
            {
                FrameNumber    = frameNumber,
                NodeId         = nodeId,
                // Zero-overhead projection into bytes, followed by the requisite heap array allocation for DDS.
                PrimitivesData = MemoryMarshal.AsBytes(frame).ToArray(),
            };
            _writer.Write(batch);
        }
    }
}
