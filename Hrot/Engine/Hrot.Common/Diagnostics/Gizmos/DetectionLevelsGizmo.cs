using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-8613</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-8, mockup ④ in <c>agg-ui-visuals.svg</c>) — the DETECTION LEVELS of a
    /// stochastic observer (its <see cref="UnitDetections"/>, agg-backend ST-2): a line to each target it is detecting and, at the line's middle, the pair's level
    /// as 5 boxes (the threshold — <see cref="UnitDetections.ContactAt"/> — is the fifth): GREEN solid = contact reported; AMBER dashed = accumulating (seen this
    /// step); GREY dashed = fading (out of sight). Tactical Map Layers ▸ AI ▸ Detection levels, OFF by default, 2-D only. 0 B.
    /// </summary>
    [GizmoProjector(typeof(UnitDetections), typeof(SimTransform))]
    public sealed class DetectionLevelsGizmo : IStatelessGizmo
    {
        public static readonly Rgba32 Contact = new(91, 208, 106, 255), Building = new(242, 192, 51, 255), Fading = new(160, 160, 170, 255);

        /// <summary>Seen within this long ⇒ still accumulating; longer ⇒ fading (one sensing step is <see cref="StochasticCombat.StepSeconds"/>).</summary>
        public const double SeenWithinSeconds = 2.0 * StochasticCombat.StepSeconds + 0.5;

        public enum PairState : byte { Building, Contact, Fading }

        /// <summary>The pair's state and its filled boxes (0..5).</summary>
        public static (PairState State, int Boxes) StateOf(in DetectionPair p, double now)
        {
            bool seen = now - p.LastSeenAt.Seconds <= SeenWithinSeconds;
            int boxes = Math.Clamp((int)MathF.Ceiling(p.Level / UnitDetections.ContactAt * 5f - 0.001f), 0, 5);
            if (p.Reported != 0) return (seen ? PairState.Contact : PairState.Fading, seen ? 5 : boxes);
            return (seen ? PairState.Building : PairState.Fading, boxes);
        }

        private static readonly Fdp.Toolkit.Diagnostics.Gizmos.FixedString32[] Meters =
        {
            new("meter/0"), new("meter/1"), new("meter/2"), new("meter/3"), new("meter/4"), new("meter/5"),
        };

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.DetectionLevels)) return;
            ref readonly var d = ref view.GetComponentRO<UnitDetections>(entity);
            if (d.Count == 0) return;
            double now = view is EntityRepository repo && repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanaged<GlobalTime>().TotalTime : 0.0;
            var from = view.GetComponentRO<SimTransform>(entity).Position;
            for (int i = 0; i < d.Count; i++)
            {
                ref readonly var p = ref d.Pairs[i];
                if (p.Target.IsNull || !view.IsAlive(p.Target) || !view.HasComponent<SimTransform>(p.Target)) continue;
                var to = view.GetComponentRO<SimTransform>(p.Target).Position;
                var (state, boxes) = StateOf(p, now);
                var colour = state == PairState.Contact ? Contact : state == PairState.Building ? Building : Fading;
                var a = new System.Numerics.Vector2(from.X, from.Y); var b = new System.Numerics.Vector2(to.X, to.Y);
                draw.DrawLine(new System.Numerics.Vector3(a, 0f), new System.Numerics.Vector3(b, 0f), colour, 2f, SizeMode.ScreenPixels,
                    PipelineTarget.Map2D | PipelineTarget.FlatOnly, DebugTraceLayers.DetectionLevels, state == PairState.Contact ? LineStyle.Solid : LineStyle.Dashed);
                var m = (a + b) * 0.5f;
                var meter = default(DebugPrimitive);
                meter.Shape = DebugPrimitiveShape.Icon;
                meter.Space = CoordinateSpace.World;
                meter.TargetView = PipelineTarget.Map2D | PipelineTarget.FlatOnly;
                meter.DebugLayer = DebugTraceLayers.DetectionLevels;
                meter.Color = colour;
                meter.IconWorldPosX = m.X; meter.IconWorldPosY = m.Y;
                meter.TextContent = Meters[boxes];
                draw.EmitRaw(in meter);
            }
        }
    }
}
