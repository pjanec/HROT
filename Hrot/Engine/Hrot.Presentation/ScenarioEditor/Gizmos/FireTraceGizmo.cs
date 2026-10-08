using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ Tuning T-5 — the <b>fire traces</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5): the last rounds fired on this node,
/// muzzle → where each ended, coloured by outcome (hit red, stopped by terrain orange, expired grey, in flight yellow), with a mark at
/// the end and every terrain crossing it passed (green) or that stopped it (orange cross). ⭐ <c>CE-3117</c>: it reads the recorded
/// <see cref="ShotTraces"/> mirrored from the <see cref="ShotLog"/> <c>GET /combat/shots</c> serves, so the map, the API and a replay agree; a node that never fired draws nothing (uniform membership — no host
/// check). Toggled by the <c>FireTraces</c> bit of the layer control.
/// </summary>
[GizmoProjector]
public sealed class FireTraceGizmo : IGlobalStatelessGizmo
{
    /// <summary>The debug layer the traces draw on (<c>LayerControlDto.FireTraces</c>).</summary>
    public const byte FireTraceLayer = 3;
    /// <summary>How many of the newest records are drawn.</summary>
    public const int Drawn = ShotTraces.Capacity;

    private static readonly Rgba32 HitColor     = new(220, 40, 40, 230);
    private static readonly Rgba32 StoppedColor = new(240, 140, 20, 230);
    private static readonly Rgba32 ExpiredColor = new(140, 140, 140, 160);
    private static readonly Rgba32 FlyingColor  = new(240, 220, 40, 200);
    private static readonly Rgba32 PassedColor  = new(60, 180, 60, 230);

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        // ⭐ CE-3117 (R-226) — the RECORDED ShotTraces singleton (mirrored from ShotLog by CombatTraceSystem), so a replay seek
        //   shows the shots again. Same shapes as before; the text explanation stays on GET /combat/shots.
        if (view is not EntityRepository repo || !repo.HasSingletonUnmanaged<ShotTraces>()) return;
        var traces = repo.GetSingletonUnmanaged<ShotTraces>();
        var slots = traces.SlotsRO();
        for (int i = 0; i < traces.Count; i++)
        {
            ref readonly var s = ref slots[i];
            var color = s.Outcome switch
            {
                ShotOutcome.Hit              => HitColor,
                ShotOutcome.StoppedByTerrain => StoppedColor,
                ShotOutcome.Expired          => ExpiredColor,
                _                            => FlyingColor,
            };
            draw.DrawLine(s.Muzzle, s.End, color, 1.5f, layer: FireTraceLayer,
                style: s.Outcome == ShotOutcome.InFlight ? LineStyle.Dashed : LineStyle.Solid);
            if (s.HasEnd != 0) draw.DrawSphere(s.End, 0.25f, color, layer: FireTraceLayer, fillColor: color);
            for (int c = 0; c < s.CrossingCount; c++)
            {
                var at = s.Crossings[c].At;
                if (s.Crossings[c].Passed != 0) draw.DrawSphere(at, 0.15f, PassedColor, layer: FireTraceLayer, fillColor: PassedColor);
                else
                {
                    draw.DrawLine(at + new Vector3(-0.4f, -0.4f, 0), at + new Vector3(0.4f, 0.4f, 0), StoppedColor, 2f, layer: FireTraceLayer);
                    draw.DrawLine(at + new Vector3(-0.4f, 0.4f, 0), at + new Vector3(0.4f, -0.4f, 0), StoppedColor, 2f, layer: FireTraceLayer);
                }
            }
        }
    }
}
