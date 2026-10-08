using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3121</c> (R-227) — the <b>squad</b> gizmo, on a squad commander (<see cref="UnitRoster"/> + <see cref="SquadCognitiveState"/>):
/// a line to every member coloured by its element, <c>E#R#</c> (element, role) at the member, the phase at the commander, the squad's
/// shared contacts, and the danger area it is crossing. Family <c>SquadAssignment</c>: by default the selected and the pinned squads.
/// ⚠ Folded from the dormant <c>SquadAssignmentOverlaySource</c> and <c>SquadCoordinationOverlaySource</c>, which drew their text at
/// the map origin, member lines from origin to origin and the danger box Y-up in a Z-up world; written from their intent.
/// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
/// </summary>
[GizmoProjector(typeof(UnitRoster), typeof(SquadCognitiveState), typeof(SimTransform), Family = AiOverlayFlags.SquadAssignment)]
public sealed class SquadGizmo : IStatelessGizmo
{
    private static readonly Rgba32[] ElementColors =
    {
        new(64, 128, 255, 220), new(255, 64, 64, 220), new(64, 200, 64, 220), new(230, 200, 0, 220),
    };
    private static readonly Rgba32 PhaseColor   = new(20, 20, 20, 255);
    private static readonly Rgba32 ContactColor = new(220, 60, 220, 220);
    private static readonly Rgba32 DangerColor  = new(255, 128, 0, 220);

    /// <summary>The size of <see cref="SquadContactPoolSlots"/> (its <c>[InlineArray(16)]</c>; the pool names no constant).</summary>
    private const int ContactCapacity = 16;

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        var state = view.GetComponentRO<SquadCognitiveState>(entity);
        var roster = view.GetComponentRO<UnitRoster>(entity);

        draw.DrawText(at.X, at.Y, new Fdp.Core.FixedString32($"SQUAD {roster.Count} ph{state.PhaseId}"), PhaseColor,
            fontSizePx: 11f, lineOffsetPx: -16f);

        var elements = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<MemberElementIndexArray, byte>(ref Unsafe.AsRef(in state.Elements.MemberElements)), UnitRoster.Capacity);
        var roles = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), UnitRoster.Capacity);
        for (int i = 0; i < Math.Min(roster.Count, UnitRoster.Capacity); i++)
        {
            var member = roster.SubordinateEntities[i];
            if (!view.IsAlive(member) || !view.HasComponent<SimTransform>(member)) continue;
            var m = view.GetComponentRO<SimTransform>(member).Position;
            var color = ElementColors[elements[i] % ElementColors.Length];
            draw.DrawLine(at, m, color, 1.5f);
            draw.DrawText(m.X, m.Y, new Fdp.Core.FixedString32($"E{elements[i]}R{roles[i].RoleId}"), color, fontSizePx: 10f, lineOffsetPx: 12f);
        }

        var contacts = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref Unsafe.AsRef(in state.Contacts.Contacts)), ContactCapacity);
        for (int c = 0; c < Math.Min(state.Contacts.Count, contacts.Length); c++)
            draw.DrawSphere(new Vector3(contacts[c].PositionX, contacts[c].PositionY, contacts[c].PositionZ), 1.5f, ContactColor, thickness: 1.5f);

        if (state.ActiveFeatureId != 0 && view.HasComponent<DangerAreaCognitiveBuffer>(entity))
        {
            var buffer = view.GetComponentRO<DangerAreaCognitiveBuffer>(entity);
            var areas = buffer.GetSpanRO();
            for (int d = 0; d < Math.Min(buffer.Count, areas.Length); d++)
                if (areas[d].FeatureId == state.ActiveFeatureId)
                    DangerAreaGizmo.Box(draw, areas[d].Center, areas[d].ExtentsXY, areas[d].AngleRad, DangerColor);
        }
    }
}
