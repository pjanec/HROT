using System;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    // ECS-free stateful gizmo. No entity binding, no ISimulationView in the hot path.
    // Constructors establish invariants; IDisposable tears them down.
    // The host (GizmoInteractionManager) owns the lifecycle.
    public interface IStatefulGizmo : IGizmoInteractionHandler, IDisposable
    {
        // Called once per frame. The gizmo emits its visual primitives via draw.
        // The manager calls UpdateAndDraw for every registered tool before emitting
        // InputCaptureBinding on the exclusive-focus holder's behalf.
        void UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw);
    }
}
