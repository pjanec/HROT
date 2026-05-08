using System;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace GizmoMap.Example
{
    /// <summary>
    /// In-process transport: copies primitives directly into the target buffer.
    /// No DDS involved. Suitable for unit tests and local CI runs.
    /// </summary>
    public sealed class LocalGizmoTransport : IGizmoTransport
    {
        private DebugPrimitive[]? _pending;

        public void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives)
        {
            _pending = primitives.ToArray();
        }

        public void PollAndApply(DebugPrimitiveBuffer target)
        {
            if (_pending == null) return;

            foreach (ref readonly var prim in _pending.AsSpan())
                target.AppendRaw(in prim);

            _pending = null;
        }

        public void Dispose() { /* no-op */ }
    }
}
