using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IGizmoTransport : IDisposable
    {
        void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives);
        void PollAndApply(GizmoPrimitiveBuffer target);
    }
}
