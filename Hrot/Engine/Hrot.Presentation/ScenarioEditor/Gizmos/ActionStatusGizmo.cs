using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3136</c> (T3, approved `2026-10-09`) — the <b>action status</b> gizmo: under the unit, one short line per channel that
/// is busy — <c>W aiming 0.4/0.8s</c> · <c>W hold: not seen</c> (amber) · <c>L moving 12m</c> — from <see cref="ActionStatus"/>, which
/// the executors write. Family <c>Channels</c>: by default the selected and the pinned units; switchable to every unit in the layer
/// panel, pinnable per unit. A row older than <see cref="FreshSeconds"/> is not drawn (an action that stopped). 📄
/// docs/DESIGN_Ai_Action_Status_Gizmo.md.
/// </summary>
[GizmoProjector(typeof(ActionStatus), typeof(SimTransform), Family = AiOverlayFlags.Channels)]
public sealed class ActionStatusGizmo : IStatelessGizmo
{
    /// <summary>A row last written longer ago than this (sim seconds) is stale and not drawn.</summary>
    public const double FreshSeconds = 1.0;

    private static readonly Rgba32 Busy = new(60, 140, 220, 255);
    private static readonly Rgba32 Hold = new(230, 150, 20, 255);

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        var at = view.GetComponentRO<SimTransform>(entity).Position;
        ref readonly var s = ref view.GetComponentRO<ActionStatus>(entity);
        // sim time for the staleness check; a view that cannot say (not a repository) draws every row
        double? now = view is EntityRepository repo && repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : null;
        float line = 0f;
        Line(draw, at, 'L', in s.Locomotion, now, ref line);
        Line(draw, at, 'W', in s.Weapon, now, ref line);
        Line(draw, at, 'I', in s.Interaction, now, ref line);
    }

    /// <summary>The text of one row, or null when there is nothing to say (public for the rail and the debug API's wording).</summary>
    public static string? Text(char channel, in ActionStatusRow row, double? now)
    {
        if (row.Reason == ActionReason.None || (now is double t && t - row.At > FreshSeconds)) return null;
        string label = ActionStatusOf.Label(row.Reason);
        return row.Reason switch
        {
            ActionReason.Aiming or ActionReason.Reloading => $"{channel} {label} {row.Progress:0.0}/{row.Needed:0.0}s",
            ActionReason.Cooldown                         => $"{channel} {label} {row.Progress:0.0}s",
            ActionReason.Moving when row.Progress > 0f    => $"{channel} {label} {row.Progress:0}m",
            _                                             => $"{channel} {label}",
        };
    }

    private static void Line(IDebugDrawBuilder draw, System.Numerics.Vector3 at, char channel, in ActionStatusRow row, double? now,
        ref float line)
    {
        var text = Text(channel, in row, now);
        if (text == null) return;
        draw.DrawText(at.X, at.Y, new Fdp.Core.FixedString32(text), ActionStatusOf.IsHold(row.Reason) ? Hold : Busy,
            fontSizePx: 11f, lineOffsetPx: -14f - line);
        line += 13f;
    }
}
