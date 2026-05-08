using System;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// Polls the DDS <see cref="DebugPrimitivesBatch"/> topic and applies the most recent
    /// batch to the local <see cref="DebugPrimitiveBuffer"/>, replacing its contents.
    /// Called from the Raylib render-loop thread (not the ECS thread).
    /// </summary>
    public sealed class DebugPrimitivesIngressTranslator
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsReader<DebugPrimitivesBatch>? _reader;
        private readonly byte? _filterNodeId;

        /// <param name="buffer">Target buffer to populate.</param>
        /// <param name="reader">DDS reader; null disables network ingress (local-only mode).</param>
        /// <param name="filterNodeId">When set, only batches with matching NodeId are applied.</param>
        public DebugPrimitivesIngressTranslator(
            DebugPrimitiveBuffer buffer,
            IDdsReader<DebugPrimitivesBatch>? reader = null,
            byte? filterNodeId = null)
        {
            _buffer       = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _reader       = reader;
            _filterNodeId = filterNodeId;
        }

        /// <summary>
        /// Drains all pending DDS samples, selects the latest matching one, and replaces the
        /// buffer contents with its primitives. Called every render tick.
        /// </summary>
        public void PollAndApply()
        {
            if (_reader == null) return;

            DebugPrimitivesBatch? latest = null;
            while (_reader.TryRead(out var batch))
            {
                if (_filterNodeId.HasValue && batch.NodeId != _filterNodeId.Value)
                    continue;
                latest = batch;
            }

            if (!latest.HasValue) return;

            _buffer.Clear();
            var primitives = latest.Value.Primitives;
            if (primitives == null) return;

            for (int i = 0; i < primitives.Length; i++)
                _buffer.AppendRaw(in primitives[i]);
        }
    }
}
