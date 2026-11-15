using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-4621</c> (docs/designs/vehicle-tactics/DESIGN_Motorcycle.md PF-1, U-2; mockup <c>motorcycle-visuals.svg</c> panels C and D) — the FIRING SEAT's arc, drawn
    /// on the 2-D and the 3-D map for the selected and pinned vehicles (the pillion's: both sides and the rear): a wedge from the seat's place, its two edges and an arc of
    /// segments, in amber, ground level. A seat that states <c>TraverseMinDeg</c> … <c>TraverseMaxDeg</c> and holds no station (a station's arc is the gun's own, drawn with
    /// its turret). The arc is read counter-clockwise from Min to Max (<see cref="Hrot.UI.Common.Map3D.PossessionStation.InArc"/> — the same reading). Family
    /// <c>Channels</c> ("Actions"): like the action status lines, the selected and pinned units by default. From the TKB's seats — nothing is recorded.
    /// </summary>
    [GizmoProjector(typeof(TkbIdentity), typeof(SimTransform), Family = AiOverlayFlags.Channels)]
    public sealed class SeatArcGizmo : IStatelessGizmo
    {
        /// <summary>The wedge's reach (m) and the arc's segment step (degrees).</summary>
        public const float Range = 30f, StepDegrees = 10f;

        private static readonly Rgba32 Amber = new(242, 192, 51, 230);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Actions)) return;
            if (view is not EntityRepository repo || !repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return;
            long type = repo.GetComponentRO<TkbIdentity>(entity).TkbType;
            if (!repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>()!.TryGetByType(type, out var template) || template == null) return;
            var seats = template.GetDescriptor<CarrierDefDto>()?.Seats;
            if (seats == null) return;
            var tf = repo.GetComponentRO<SimTransform>(entity);
            Vector3 position = tf.Position;
            for (int i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                if (seat.TraverseMinDeg is not { } min || seat.TraverseMaxDeg is not { } max || seat.Role == SeatRole.Station) continue;
                var origin = position + Vector3.Transform(new Vector3(seat.X, seat.Y, 0f), tf.Rotation);
                Arc(draw, origin, tf.Rotation, min, max);
            }
        }

        /// <summary>The wedge of <paramref name="min"/> … <paramref name="max"/> (degrees, counter-clockwise from the vehicle's forward, Min round to Max): the two edges and an arc of segments (public for the rail).</summary>
        public static void Arc(IDebugDrawBuilder draw, Vector3 origin, Quaternion rotation, float minDeg, float maxDeg)
        {
            float span = (maxDeg - minDeg) % 360f;
            if (span <= 0f) span += 360f;
            Vector3 At(float deg) => origin + Vector3.Transform(new Vector3(MathF.Cos(deg * MathF.PI / 180f), MathF.Sin(deg * MathF.PI / 180f), 0f) * Range, rotation);
            if (span < 359.9f)
            {
                draw.DrawLine(origin, At(minDeg), Amber, thickness: 1.6f, layer: DebugTraceLayers.Actions);
                draw.DrawLine(origin, At(minDeg + span), Amber, thickness: 1.6f, layer: DebugTraceLayers.Actions);
            }
            int steps = Math.Max(1, (int)MathF.Ceiling(span / StepDegrees));
            var from = At(minDeg);
            for (int s = 1; s <= steps; s++)
            {
                var to = At(minDeg + span * s / steps);
                draw.DrawLine(from, to, Amber, thickness: 1.2f, layer: DebugTraceLayers.Actions, style: LineStyle.Dashed);
                from = to;
            }
        }
    }
}
