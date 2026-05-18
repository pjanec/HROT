using System;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Network
{
    // Stateless transport adapter that reads DebugPrimitivesBatch from DDS
    // and unpacks the primitives into a target GizmoPrimitiveBuffer.
    // No ECS dependencies.
    public sealed class DdsDebugPrimitiveSubscriber
    {
        private readonly IDdsReader<DebugPrimitivesBatch> _reader;

        public DdsDebugPrimitiveSubscriber(IDdsReader<DebugPrimitivesBatch> reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        // Reads one pending DebugPrimitivesBatch and appends its primitives into 'target'.
        // Returns true if a sample was consumed, false if the reader was empty.
        public bool PollAndApply(GizmoPrimitiveBuffer target)
        {
            if (!_reader.TryRead(out var batch))
                return false;

            if (batch.PrimitivesData == null)
                return true;

            // Zero-allocation memory reinterpret cast back to DebugPrimitive span.
            var primitives = MemoryMarshal.Cast<byte, DebugPrimitive>(batch.PrimitivesData.AsSpan());
            foreach (ref readonly var primitive in primitives)
                target.AppendRaw(in primitive);

            return true;
        }
    }
}
