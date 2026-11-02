using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ SR-192 (docs/DESIGN_Scenario_Macros.md §14.4) — the lines from a SELECTED macro instance to the entities it expanded: the squad gizmo's pattern
/// (<see cref="SquadGizmo"/>), one thin line from the instance to every entity whose <see cref="MacroOrigin.Instance"/> resolves to it, in the macro
/// marker's colour (<see cref="MacroMarker.LineColour"/> — ⛔ never a side colour, so it never reads as a command line).
/// <para>⭐ DIRECT members only: a nested instance is itself a member with its own marker — select it for its lines.</para>
/// <para>⭐ A FAILED expansion (<see cref="MacroExpansionState.Failed"/>) draws no lines and the reason over the marker (SR-182).</para>
/// <para>⭐ Keyed on the instance's UNMANAGED status (<see cref="MacroInstance"/> is a managed record). Family <see cref="AiOverlayFlags.MacroMembers"/>:
/// drawn for the selected instance (the family is not a Brain family — <c>GizmoFamilies.IsBrainFamily</c>). Layer <see cref="DebugTraceLayers.SquadCommand"/>
/// (leader → member lines: its checkbox is the nearest honest one; a layer of its own would move <c>FirstUntoggledLayer</c> and the layer DTO for
/// four lines).</para>
/// </summary>
[GizmoProjector(typeof(MacroExpansionStatus), typeof(SimTransform), Family = AiOverlayFlags.MacroMembers)]
public sealed class MacroMembersGizmo : IStatelessGizmo
{
    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (!draw.IsLayerActive(DebugTraceLayers.SquadCommand)) return;   // a hidden layer costs no work
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        var status = view.GetComponentRO<MacroExpansionStatus>(entity);

        if (status.State == MacroExpansionState.Failed)
        {
            string reason = status.Reason;
            draw.DrawTextLong(at.X, at.Y, reason.Length == 0 ? "macro failed" : "macro failed: " + reason, MacroMarker.Colour,
                fontSizePx: 11f, lineOffsetPx: -18f, layer: DebugTraceLayers.SquadCommand);
            return;
        }

        if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<MacroOrigin>()) return;
        foreach (var member in view.Query().With<MacroOrigin>().Build())
        {
            if (member == entity || !view.HasComponent<SimTransform>(member)) continue;
            if (view.GetComponentRO<MacroOrigin>(member).Instance.Resolve(repo) != entity) continue;   // the stale-checked resolver, not a bare id compare
               // the stale-checked resolver, not a bare id compare
            draw.DrawLine(at, view.GetComponentRO<SimTransform>(member).Position, MacroMarker.LineColour, 1f, layer: DebugTraceLayers.SquadCommand);
        }
    }
}
