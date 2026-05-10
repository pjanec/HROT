using System;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    // Publishes the current frame's debug primitive buffer to the DebugPrimitivesBatch DDS topic
    // so that remote subscribers (viewers, recorders) can render gizmos without running the
    // simulation locally. Runs in the Export phase after all gizmo projectors have populated
    // the buffer.
    [UpdateInPhase(SystemPhase.Export)]
    public sealed class DebugPrimitivesBatchPublisherSystem : IEcsModuleSystem
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsWriter<DebugPrimitivesBatch> _writer;
        private readonly byte _nodeId;

        public long SentSampleCount { get; private set; }

        public DebugPrimitivesBatchPublisherSystem(
            DebugPrimitiveBuffer buffer,
            IDdsWriter<DebugPrimitivesBatch> writer,
            byte nodeId)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _nodeId = nodeId;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var frame = _buffer.GetFrame();

            // Skip publishing when the buffer is empty to avoid flooding the network.
            if (frame.Length == 0) return;

            var batch = new DebugPrimitivesBatch
            {
                FrameNumber    = view.Tick,
                NodeId         = _nodeId,
                PrimitivesData = MemoryMarshal.AsBytes(frame).ToArray(),
            };

            _writer.Write(batch);
            SentSampleCount++;
        }
    }
}
