using System;
using System.Collections.Generic;
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
        private Dictionary<uint, string>? _pendingInterns;

        public void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives, StringInternMap? internMap = null)
        {
            _pending = primitives.ToArray();

            if (internMap != null)
            {
                _pendingInterns = new Dictionary<uint, string>();
                foreach (var kvp in internMap.Entries)
                    _pendingInterns[kvp.Key] = kvp.Value;
            }
        }

        public void PollAndApply(GizmoPrimitiveBuffer target)
        {
            if (_pending == null) return;

            foreach (ref readonly var prim in _pending.AsSpan())
                target.AppendRaw(in prim);

            if (_pendingInterns != null)
            {
                foreach (var kvp in _pendingInterns)
                    target.InternMap.Intern(kvp.Key, kvp.Value);
            }

            _pending = null;
            _pendingInterns = null;
        }

        public void Dispose() { /* no-op */ }
    }
}
