using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network
{
    // Stateless transport adapter that packs a DebugPrimitiveBuffer into a
    // DebugPrimitivesBatch and writes it via the injected DDS writer.
    // No ECS dependencies; pure data transformation.
    public sealed class DdsDebugPrimitivePublisher
    {
        private readonly IDdsWriter<DebugPrimitivesBatch> _writer;

        public DdsDebugPrimitivePublisher(IDdsWriter<DebugPrimitivesBatch> writer)
        {
            _writer = writer ?? throw new System.ArgumentNullException(nameof(writer));
        }

        // Packs all primitives from 'buffer' into a DebugPrimitivesBatch and publishes it.
        public void Publish(DebugPrimitiveBuffer buffer, uint frameNumber, byte nodeId)
        {
            var frame = buffer.GetFrame();
            var primitives = new DebugPrimitive[frame.Length];
            for (int i = 0; i < frame.Length; i++)
                primitives[i] = frame[i];

            var batch = new DebugPrimitivesBatch
            {
                FrameNumber = frameNumber,
                NodeId      = nodeId,
                Primitives  = primitives,
            };
            _writer.Write(batch);
        }
    }
}
