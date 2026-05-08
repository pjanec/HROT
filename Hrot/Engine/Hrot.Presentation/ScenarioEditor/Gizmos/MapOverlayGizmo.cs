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
        }
    }
}
