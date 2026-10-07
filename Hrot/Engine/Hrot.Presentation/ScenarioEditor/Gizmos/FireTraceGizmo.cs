using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ Tuning T-5 — the <b>fire traces</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5): the last rounds fired on this node,
/// muzzle → where each ended, coloured by outcome (hit red, stopped by terrain orange, expired grey, in flight yellow), with a mark at
/// the end and every terrain crossing it passed (green) or that stopped it (orange cross). It reads the SAME <see cref="ShotLog"/>
/// <c>GET /combat/shots</c> serves, so the map and the API agree; a node that never fired draws nothing (uniform membership — no host
/// check). Toggled by the <c>FireTraces</c> bit of the layer control.
/// </summary>
[GizmoProjector]
public sealed class FireTraceGizmo : IGlobalStatelessGizmo
{
    /// <summary>The debug layer the traces draw on (<c>LayerControlDto.FireTraces</c>).</summary>
    public const byte FireTraceLayer = 3;
    /// <summary>How many of the newest records are drawn.</summary>
    public const int Drawn = 64;

    private static readonly Rgba32 HitColor     = new(220, 40, 40, 230);
    private static readonly Rgba32 StoppedColor = new(240, 140, 20, 230);
    private static readonly Rgba32 ExpiredColor = new(140, 140, 140, 160);
    private static readonly Rgba32 FlyingColor  = new(240, 220, 40, 200);
    private static readonly Rgba32 PassedColor  = new(60, 180, 60, 230);

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || ShotLog.Peek(repo) is not { } log) return;
        foreach (var s in log.Recent(Drawn))
        {
            var color = s.Outcome switch
            {
                ShotOutcome.Hit              => HitColor,
                ShotOutcome.StoppedByTerrain => StoppedColor,
                ShotOutcome.Expired          => ExpiredColor,
                _                            => FlyingColor,
            };
            var end = s.EndPoint ?? s.Aim;
            draw.DrawLine(s.Muzzle, end, color, 1.5f, layer: FireTraceLayer,
                style: s.Outcome == ShotOutcome.InFlight ? LineStyle.Dashed : LineStyle.Solid);
            if (s.EndPoint is { } p) draw.DrawSphere(p, 0.25f, color, layer: FireTraceLayer, fillColor: color);
            foreach (var c in s.Crossings)
            {
                if (c.Passed) draw.DrawSphere(c.At, 0.15f, PassedColor, layer: FireTraceLayer, fillColor: PassedColor);
                else
                {
                    draw.DrawLine(c.At + new Vector3(-0.4f, -0.4f, 0), c.At + new Vector3(0.4f, 0.4f, 0), StoppedColor, 2f, layer: FireTraceLayer);
                    draw.DrawLine(c.At + new Vector3(-0.4f, 0.4f, 0), c.At + new Vector3(0.4f, -0.4f, 0), StoppedColor, 2f, layer: FireTraceLayer);
                }
            }
        }
    }
}
