using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ058: mirrors RouteRenderLayer rendering logic via the StatelessGizmoSystem.
    // Emits Line primitives for each consecutive waypoint pair in route entities.
    [GizmoProjector(typeof(TkbIdentity))]
    public sealed class RouteGizmo : IStatelessGizmo
    {
        // #4488FF — same blue as RouteRenderLayer.NormalColor.
        private static readonly Rgba32 NormalColor = new Rgba32(0x44, 0x88, 0xFF, 0xFF);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<TkbIdentity>(entity)) return;

            ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);
            if (tkb.TkbType != TkbEntityTypes.TacGraphic_Route) return;

            if (!view.HasManagedComponent<RoutePlan>(entity)) return;

            var plan = view.GetManagedComponentRO<RoutePlan>(entity);
            if (plan.Waypoints == null || plan.Waypoints.Count == 0) return;

            int n        = plan.Waypoints.Count;
            int segCount = plan.IsLoop ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                // RouteWaypoint uses X=East, Z=North (canvas Y).
                var a = new Vector3(plan.Waypoints[i].Position.X,             plan.Waypoints[i].Position.Z,             0f);
                var b = new Vector3(plan.Waypoints[(i + 1) % n].Position.X,   plan.Waypoints[(i + 1) % n].Position.Z,   0f);
                draw.DrawLine(a, b, NormalColor, 1f, SizeMode.ScreenPixels);
            }
        }
    }
}
