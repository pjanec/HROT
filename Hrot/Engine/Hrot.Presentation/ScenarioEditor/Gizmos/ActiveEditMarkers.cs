using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Zero-byte ECS marker. Adding this to an entity signals DataDrivenGizmoSystem
    // to instantiate VertexEditGizmo for the entity's EditablePolyline.
    // Removed by VertexEditGizmoDefinition.onRemove when the interaction ends.
    [ComponentId(HrotComponentIds.ActiveVertexEditRequest)]
    public struct ActiveVertexEditRequest { }

    // Zero-byte ECS marker. Adding this to an entity signals DataDrivenGizmoSystem
    // to instantiate RouteWaypointGizmo for the entity's RoutePlan.
    // Removed by RouteWaypointGizmoDefinition.onRemove when the interaction ends.
    [ComponentId(HrotComponentIds.ActiveRouteEditRequest)]
    public struct ActiveRouteEditRequest { }
}
