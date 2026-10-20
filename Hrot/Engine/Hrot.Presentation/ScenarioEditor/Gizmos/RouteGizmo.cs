using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ058: mirrors RouteRenderLayer rendering logic via the StatelessGizmoSystem.
    // ⭐ CE-1233 (docs/designs/routes-1/ROUTES1-DESIGN.md §8.1, as built): the route as the design drew it — line segments between
    //   consecutive waypoints AND a small circle at each waypoint, blue; brighter when the route entity is selected. 🔒 User,
    //   2026-10-20: "routes are not rendered as waypoints connect as lines". 📐 Before: 1 px segments only, no waypoint mark — on
    //   uc-convoy-hit the route lies on Avenue E's SUMO lanes and could not be told from them.
    [GizmoProjector(typeof(TkbIdentity))]
    public sealed class RouteGizmo : IStatelessGizmo
    {
        // #4488FF — same blue as RouteRenderLayer.NormalColor.
        public static readonly Rgba32 NormalColor = new Rgba32(0x44, 0x88, 0xFF, 0xFF);
        /// <summary>⭐ CE-1233 — a SELECTED route (its <see cref="SelectionState"/>): a light cyan, so it stands out from every other route.</summary>
        public static readonly Rgba32 SelectedColor = new Rgba32(0x7F, 0xE0, 0xFF, 0xFF);

        /// <summary>⭐ CE-1233 — the segments' stroke, screen pixels (was 1 — a hairline under the road lanes).</summary>
        public const float LinePixels = 2.5f;
        /// <summary>⭐ CE-1233 — a waypoint's circle radius, screen pixels (constant on screen, like a vertex handle).</summary>
        public const float WaypointPixels = 4f;
        /// <summary>⭐ CE-1233 — the line and the circles when the route is selected.</summary>
        public const float SelectedLinePixels = 3.5f, SelectedWaypointPixels = 5.5f;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<TkbIdentity>(entity)) return;

            ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);
            if (tkb.TkbType != TkbEntityTypes.TacGraphic_Route) return;

            if (!view.HasManagedComponent<RoutePlan>(entity)) return;

            var plan = view.GetManagedComponentRO<RoutePlan>(entity);
            if (plan.Waypoints == null || plan.Waypoints.Count == 0) return;

            bool selected = IsSelected(view, entity);
            var color = selected ? SelectedColor : NormalColor;
            float line = selected ? SelectedLinePixels : LinePixels;
            float dot = selected ? SelectedWaypointPixels : WaypointPixels;

            int n        = plan.Waypoints.Count;
            int segCount = plan.IsLoop ? n : n - 1;

            for (int i = 0; i < segCount; i++)
                draw.DrawLine(PointOf(view, plan.Waypoints[i]), PointOf(view, plan.Waypoints[(i + 1) % n]), color, line, SizeMode.ScreenPixels);
            for (int i = 0; i < n; i++)
                draw.DrawSphere(PointOf(view, plan.Waypoints[i]), dot, color, sizeMode: SizeMode.ScreenPixels, fillColor: color);
        }

        private static bool IsSelected(ISimulationView view, Entity entity)
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<SelectionState>())
               && view.HasComponent<SelectionState>(entity) && view.GetComponentRO<SelectionState>(entity).IsSelected;

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
