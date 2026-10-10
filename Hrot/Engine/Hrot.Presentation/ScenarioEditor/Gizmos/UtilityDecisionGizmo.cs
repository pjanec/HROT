using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Utility;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3121</c> (R-227) — the <b>utility</b> gizmo: AT the unit, one line per decision it logged — the decision id, its winner and
/// the margin over the runner-up (<see cref="UtilityDecisionLog"/>, the record <c>GET /entities/{id}/utility</c> serves, so the map and
/// the API agree). Family <c>UtilityDecision</c>: by default the selected and the pinned units. ⚠ Folded from the dormant
/// <c>UtilityDecisionOverlaySource</c>, which drew one text line at the map ORIGIN; written from its intent, not ported.
/// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
/// </summary>
[GizmoProjector(typeof(UtilityDecisionLog), typeof(SimTransform), Family = AiOverlayFlags.UtilityDecision)]
public sealed class UtilityDecisionGizmo : IStatelessGizmo
{
    private static readonly Rgba32 TextColor = new(0, 160, 90, 255);

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        var log = view.GetComponentRO<UtilityDecisionLog>(entity);
        float line = 0f;
        foreach (ref readonly var slot in log.SlotsRO())
        {
            if (slot.DecisionId == 0) continue;
            draw.DrawText(at.X, at.Y, new Fdp.Core.FixedString32($"D{slot.DecisionId}: {slot.Winner} Δ{slot.Margin:0.00}"), TextColor,
                fontSizePx: 11f, lineOffsetPx: 14f + line);
            line += 13f;
        }
    }
}
