using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.SimHost.Gizmos
{
    // Zero-byte ECS marker component. Adding this to an entity signals to
    // DataDrivenGizmoSystem that the operator wants to interactively rotate
    // the entity. The system instantiates EntityRotatorGizmo automatically.
    // Removed by EntityRotatorGizmo.onRemove when the interaction ends.
    [ComponentId(HrotComponentIds.ActiveRotationToolRequest)]
    public struct ActiveRotationToolRequest { }
}
