using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Common.Events;

namespace Hrot.Common.Interactions
{
    public static class InteractionEventRegistry
    {
        public static void RegisterAll(FdpEventBus bus)
        {
            // Unmanaged Hardware and Gizmo Events
            bus.Register<GizmoInteractionStartedEvent>();
            bus.Register<GizmoDragUpdateEvent>();
            bus.Register<GizmoInteractionCommitEvent>();
            bus.Register<GizmoInteractionCancelEvent>();
            bus.Register<GizmoMenuActionEvent>();
            bus.Register<GizmoMouseEvent>();
            bus.Register<GizmoKeyEvent>();
            bus.Register<GizmoComponentActivatedEvent>();
            bus.Register<GlobalActionRequestedEvent>();
            bus.Register<OpenLayerEditorEvent>();

            // Managed UI Events
            bus.RegisterManaged<GizmoStructUpdateEvent>();
        }
    }
}
