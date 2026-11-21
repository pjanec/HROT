using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐ U-25 / S4 (docs/UX/DESIGN_Initial_State.md §3, §4.3) — a named <b>Point</b> map graphic (<see cref="TkbEntityTypes.Point"/> = 8816) on the map:
    /// a diamond at its position, its name beside it, and — when it is ORIENTED (a rotation other than the identity) — a heading tick. Clickable
    /// (select / menu) through the shared pick box. ⭐ The same projector pattern as <see cref="TacticalAreaGizmo"/>: discovered by the gizmo registrar,
    /// so every map host draws it. 0 B steady: the label is cached per name change.
    /// </summary>
    [GizmoProjector(typeof(TkbIdentity), DrawsOffScreen = true)]
    public sealed class NamedPointGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 PointColor = new Rgba32(120, 200, 255, 235);
        private const float Half = 4f;        // the diamond's half-diagonal, world metres
        private const float TickLength = 12f; // the heading tick, world metres

        private readonly Dictionary<int, (FixedString64 Name, FixedString32 Label)> _labels = new();

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<TkbIdentity>(entity)) return;
            if (view.GetComponentRO<TkbIdentity>(entity).TkbType != TkbEntityTypes.Point) return;
            if (!view.HasComponent<SimTransform>(entity)) return;
            ref readonly var tr = ref view.GetComponentRO<SimTransform>(entity);
            var p = new Vector3(tr.Position.X, tr.Position.Y, tr.Position.Z);
            byte layer = DebugTraceLayers.TacticalGraphics;

            var n = p + new Vector3(0, Half, 0); var e = p + new Vector3(Half, 0, 0);
            var s = p + new Vector3(0, -Half, 0); var w = p + new Vector3(-Half, 0, 0);
            draw.DrawLine(n, e, PointColor, 2f, layer: layer); draw.DrawLine(e, s, PointColor, 2f, layer: layer);
            draw.DrawLine(s, w, PointColor, 2f, layer: layer); draw.DrawLine(w, n, PointColor, 2f, layer: layer);

            var q = tr.Rotation;
            bool oriented = !(q.X == 0f && q.Y == 0f && q.Z == 0f && (q.W == 1f || q.W == -1f));
            if (oriented)
            {
                float rad = q.HeadingDeg * (System.MathF.PI / 180f);   // compass: 0 = north, clockwise
                var dir = new Vector3(System.MathF.Sin(rad), System.MathF.Cos(rad), 0f);
                draw.DrawLine(p, p + dir * TickLength, PointColor, 2f, layer: layer);
            }

            int net = view.HasComponent<NetworkIdentity>(entity) ? view.GetComponentRO<NetworkIdentity>(entity).Value : 0;
            if (view.HasComponent<EntityInfo>(entity))
            {
                var name = view.GetComponentRO<EntityInfo>(entity).Name;
                int key = net != 0 ? net : entity.Index;
                if (!_labels.TryGetValue(key, out var c) || !c.Name.Equals(name)) _labels[key] = c = (name, new FixedString32(name.ToString()));
                draw.DrawText(p.X, p.Y, c.Label, PointColor, layer: layer, fontSizePx: 12f, lineOffsetPx: 14f);
            }
            EntityPresentationGizmoShared.EmitPickBox(draw, net, p, Half * 1.5f);   // layer 0: a pick target (CE-1160)
        }
    }
}
