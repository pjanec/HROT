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
        if (!draw.IsLayerActive(DebugTraceLayers.Utility)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
        int net = EntityCardRows.CardIdOf(view, entity);   // ⭐ CE-4582 (§6am D-7) — rows of HIS card, never free text at the unit
        if (net == 0) return;
        var card = draw.Card(net, DebugTraceLayers.Utility);
        if (card.IsInert) return;   // Labels off — no orphan card (D-6)
        var log = view.GetComponentRO<UtilityDecisionLog>(entity);
        int row = EntityCardRows.UtilityFirst;
        foreach (ref readonly var slot in log.SlotsRO())
        {
            if (slot.DecisionId == 0) continue;
            if (row > EntityCardRows.UtilityLast) break;
            card.Row((byte)row++).Text(Line(in slot), TextColor, 11f);
        }
    }

    /// <summary>One decision's line (public for the rail): its id, winner and margin — ⭐ R-4221, invariant.</summary>
    public static string Line(in UtilityLogSlot slot)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"D{slot.DecisionId}: {slot.Winner} Δ{slot.Margin:0.00}");
}
