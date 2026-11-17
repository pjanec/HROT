using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-8615</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md UI-9, mockup ⑤) — a man a split has MATERIALISED but still HOLDS (D-P4, agg-backend
    /// ST-5: <see cref="AggregateChild.Held"/> = 1 — not in the world yet) is a DASHED RING in his unit's side colour at his post: the instructor sees the split
    /// building before the handover. Gone at the handover (Held → 0). 2-D only, on the Unit cards layer (it belongs to the unit's picture). 0 B.
    /// </summary>
    [GizmoProjector(typeof(AggregateChild), typeof(SimTransform))]
    public sealed class HeldManGizmo : IStatelessGizmo
    {
        public const float RingPx = 4f;
        /// <summary>⭐ <c>CE-8307</c> (review F10) — a held UNIT child (a platoon's squad before the handover) is drawn as a unit: a wider double ring.</summary>
        public const float UnitRingPx = 10f;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var child = ref view.GetComponentRO<AggregateChild>(entity);
            if (child.Held == 0) return;
            var side = !child.Unit.IsNull && view.IsAlive(child.Unit) && view.HasComponent<EntityInfo>(child.Unit) ? view.GetComponentRO<EntityInfo>(child.Unit).ForceId : ForceId.Neutral;
            var p = view.GetComponentRO<SimTransform>(entity).Position;
            bool unit = view.HasComponent<AggregateUnit>(entity);   // ⭐ CE-8307 — a held squad, not a held man
            draw.DrawSphere(p, unit ? UnitRingPx : RingPx, SidePalette.Of(side), 1.5f, SizeMode.ScreenPixels, PipelineTarget.Map2D | PipelineTarget.FlatOnly,
                DebugTraceLayers.UnitCards, default, LineStyle.Dashed);
            if (unit)
                draw.DrawSphere(p, UnitRingPx - 3f, SidePalette.Of(side), 1.5f, SizeMode.ScreenPixels, PipelineTarget.Map2D | PipelineTarget.FlatOnly,
                    DebugTraceLayers.UnitCards, default, LineStyle.Dashed);
        }
    }
}
