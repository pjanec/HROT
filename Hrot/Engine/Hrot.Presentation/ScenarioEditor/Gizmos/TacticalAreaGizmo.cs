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

            int n = polyline.Points.Count;

            // Draw a closed polygon: connect each consecutive pair and close the loop.
            for (int i = 0; i < n; i++)
            {
                var a = new Vector3(polyline.Points[i].X,           polyline.Points[i].Y,           0f);
                var b = new Vector3(polyline.Points[(i + 1) % n].X, polyline.Points[(i + 1) % n].Y, 0f);
                draw.DrawLine(a, b, AreaColor, 1.5f, SizeMode.ScreenPixels);
            }
        }
    }
}
