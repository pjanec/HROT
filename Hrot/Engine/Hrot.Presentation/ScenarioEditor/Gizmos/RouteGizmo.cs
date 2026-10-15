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
                draw.DrawLine(PointOf(view, plan.Waypoints[i]), PointOf(view, plan.Waypoints[(i + 1) % n]), NormalColor, 1f, SizeMode.ScreenPixels);
        }

        /// <summary>
        /// ⭐⭐ CE-2172 (R-182; docs/designs/air-attack/DESIGN_Air_Operations.md §5, air hand-off U1) — where a waypoint is DRAWN: the route
        /// is Z-up like every world position (X east, Y north — what the draw tool writes, <c>ScenarioSpawnAdapter</c>), so the 2-D map shows
        /// it where it was drawn. ⭐ U2 — and at its height in 3-D: a flyer's waypoint (<see cref="RouteWaypoint.HeightAgl"/> &gt; 0) that high
        /// above the ground at its point; otherwise its own Z — 0 for a ground route, which the 3-D map drapes on the surface (§3.9 group B).
        /// ⛔ Before, all three route gizmos read <c>Position.Z</c> as north.
        /// </summary>
        public static Vector3 PointOf(ISimulationView view, in RouteWaypoint wp)
        {
            if (wp.HeightAgl <= 0f) return wp.Position;
            float ground = Fdp.Toolkit.World.WorldQuery.Of(view)?.GroundHeightAt(wp.Position.X, wp.Position.Y) ?? 0f;
            return new Vector3(wp.Position.X, wp.Position.Y, ground + wp.HeightAgl);
        }
    }
}
