using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ058: mirrors MapOverlayRenderLayer border rendering via StatelessGizmoSystem.
    // Emits Line primitives for each polygon edge of EditablePolyline entities.
    [GizmoProjector(typeof(SimTransform), typeof(MapOverlayStyle))]
    public sealed class MapOverlayGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasManagedComponent<EditablePolyline>(entity)) return;

            var polyline = view.GetManagedComponentRO<EditablePolyline>(entity);
            if (polyline.Points == null || polyline.Points.Count < 2) return;

            ref readonly var style = ref view.GetComponentRO<MapOverlayStyle>(entity);
            ref readonly var simTr = ref view.GetComponentRO<SimTransform>(entity);

            // Absolute positions: relative Points + SimTransform origin (X=East, Y=North).
            var origin = new Vector2(simTr.Position.X, simTr.Position.Y);
            var borderColor = new Rgba32(style.BorderR, style.BorderG, style.BorderB, style.BorderA);

            int n = polyline.Points.Count;
            int segCount = style.IsClosed ? n : n - 1;

            // ⭐ CE-3014 (2026-10-03) — the fill colour the overlay always carried, finally drawn: a closed
            //   area with a visible FillColor is triangulated and filled UNDER its border.
            //   📄 docs/DESIGN_Terrain_World.md §7.1 W2.
            if (style.IsClosed && style.FillColor.A > 0 && n >= 3)
            {
                var abs = new Vector2[n];
                for (int i = 0; i < n; i++) abs[i] = origin + polyline.Points[i];
                var tris = Fdp.Toolkit.Terrain.PolygonMath.Triangulate(abs);
                var fill = new Rgba32(style.FillColor.R, style.FillColor.G, style.FillColor.B, style.FillColor.A);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    draw.DrawFilledTriangle(abs[tris[t]], abs[tris[t + 1]], abs[tris[t + 2]], fill);
            }

            for (int i = 0; i < segCount; i++)
            {
                var a = origin + polyline.Points[i];
                var b = origin + polyline.Points[(i + 1) % n];
                draw.DrawLine(
                    new Vector3(a.X, a.Y, 0f),
                    new Vector3(b.X, b.Y, 0f),
                    borderColor,
                    style.LineThickness,
                    SizeMode.WorldMeters);
            }

            // ⭐⭐⭐ CE-259ae — make those edges CLICKABLE: left-click selects this entity, right-click
            //   opens its context menu (the AREA/ROUTE menus already existed in
            //   ContextMenuProjectorGizmo and were simply unreachable). 🔒 User, 2026-09-11: "polygon
            //   areas and routes entities should be selectable by clicking on their lines, also context
            //   menu by right clicking them."
            //   ⭐ One shared implementation for both this gizmo and TacticalAreaGizmo — see its notes
            //     for why SubElementId is 0 and why the z-order already favours an active tool's handles.
            EntityPresentationGizmoShared.EmitPickSegments(
                draw, view, entity, polyline.Points, origin, style.IsClosed);
        }
    }
}
