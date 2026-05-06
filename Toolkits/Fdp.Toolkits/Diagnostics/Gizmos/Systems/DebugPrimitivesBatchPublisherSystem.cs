using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// Post-simulation system that reads the current frame from <see cref="DebugPrimitiveBuffer"/>
    /// and publishes a <see cref="DebugPrimitivesBatch"/> DDS sample.
    /// When no DDS writer is provided, the system is a no-op (local-only mode).
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class DebugPrimitivesBatchPublisherSystem : IEcsModuleSystem
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsWriter<DebugPrimitivesBatch>? _writer;
        private readonly byte _nodeId;
        private uint _frameNumber;

        public DebugPrimitivesBatchPublisherSystem(
            DebugPrimitiveBuffer buffer,
            byte nodeId,
            IDdsWriter<DebugPrimitivesBatch>? writer = null)
        {
            _buffer  = buffer  ?? throw new System.ArgumentNullException(nameof(buffer));
            _nodeId  = nodeId;
            _writer  = writer;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_writer == null) return;

            var frame = _buffer.GetFrame();
            if (frame.Length == 0) return;

            var primitives = new DebugPrimitive[frame.Length];
            frame.CopyTo(primitives);

            _writer.Write(new DebugPrimitivesBatch
            {
                FrameNumber = _frameNumber++,
                NodeId      = _nodeId,
                Primitives  = primitives,
            });
        }
    }
}
