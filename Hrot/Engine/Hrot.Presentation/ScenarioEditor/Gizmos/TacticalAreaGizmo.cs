using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.Map.Common;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// Stateless gizmo projector that emits the polygon outline of tactical-graphics
    /// area entities (<see cref="Hrot.Map.Common.TkbEntityTypes.TacGraphic_Area"/> = 8803).
    /// The area boundary is drawn as a closed polyline using vertices from the entity's
    /// <see cref="EditablePolyline.Points"/> list.
    /// </summary>
    [GizmoProjector(typeof(TkbIdentity))]
    public sealed class TacticalAreaGizmo : IStatelessGizmo
    {
        // Olive-yellow outline to visually distinguish areas from routes.
        private static readonly Rgba32 AreaColor = new Rgba32(200, 180, 0, 230);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<TkbIdentity>(entity)) return;

            ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);
            if (tkb.TkbType != TkbEntityTypes.TacGraphic_Area) return;

            if (!view.HasManagedComponent<EditablePolyline>(entity)) return;

            var polyline = view.GetManagedComponentRO<EditablePolyline>(entity);
            if (polyline.Points == null || polyline.Points.Count < 2) return;

            // ⭐⭐⭐ BP-517 — EditablePolyline.Points are RELATIVE offsets from SimTransform, so the
            //   origin MUST be added. ⛔ This gizmo used to pass Vector2.Zero on the strength of a
            //   comment claiming the points were absolute; that comment was false for shipped data.
            //   Because an area entity can carry BOTH TkbIdentity and MapOverlayStyle, and
            //   GizmoReflectionRegistrar runs every matching [GizmoProjector], the raw-Points version
            //   drew such an entity TWICE — once here and once at origin+Points from MapOverlayGizmo —
            //   with picking off by the same distance. 📄 DESIGN_Terrain_Zones_And_Assets.md §2.2.
            var origin = Vector2.Zero;
            if (view.HasComponent<SimTransform>(entity))
            {
                ref readonly var simTr = ref view.GetComponentRO<SimTransform>(entity);
                origin = new Vector2(simTr.Position.X, simTr.Position.Y);
            }

            // ⭐ E1 — through the SHARED outline helper, so this loop exists once. BP-517 lived in a copy
            //   of it, and TerrainZoneGizmo draws the same geometry with a state-driven stroke.
            EntityPresentationGizmoShared.DrawClosedPolylineOutline(
                draw, polyline.Points, origin, AreaColor);

            // ⭐⭐⭐ CE-259ae — make the boundary CLICKABLE (select on left-click, context menu on
            //   right-click). ⚠ The SAME origin the drawing used, or a click on the drawn outline
            //   misses by exactly the transform.
            EntityPresentationGizmoShared.EmitPickSegments(
                draw, view, entity, polyline.Points, origin, isClosed: true);
        }
    }
}
