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

    /// <summary>
    /// ⭐⭐⭐ <c>CE-3153</c> — <b>how long a trace stays on screen, in SIM seconds.</b>
    /// 🔒 User ruling, <c>2026-10-09</c>: <i>"traces can not be persistent, they need to disappear after short time
    /// after being drawn for the first time."</i> ⛔ Before this the gizmo drew all 64 ring slots every frame forever,
    /// so a duel fought through walls left the map full of orange <c>StoppedByTerrain</c> lines.
    /// <para>⭐⭐ <b>The DISPLAY expires; the DATA does not.</b> The ring is left exactly as it was, which is what keeps
    /// <c>CE-3117</c>/<c>R-226</c>'s acceptance intact — <i>"a recorded run, seeked to just after a detonation, draws
    /// its rays"</i> still holds, because after a seek <c>now − Time</c> is small and the trace is inside the window.
    /// ⛔ Expiring the ring itself would have deleted that capability to fix a display annoyance.</para>
    /// <para>⚠ Tune here: <see cref="VisibleSeconds"/> is the hard cutoff, <see cref="FadeSeconds"/> the tail over
    /// which alpha falls to zero so a trace does not vanish mid-frame.</para>
    /// </summary>
    public const double VisibleSeconds = 3.0;

    /// <inheritdoc cref="VisibleSeconds"/>
    public const double FadeSeconds = 1.0;

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

        // ⭐ CE-3153 — the same time source the recorder stamps with (CombatTraceSystem.cs:23).
        //   ⚠ A world with no GlobalTime draws EVERYTHING, as before: degrading to "show all" is right, because
        //   degrading to "show none" would silently remove the layer on any host that lacks the singleton.
        bool haveTime = repo.HasSingleton<GlobalTime>();
        double now = haveTime ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;

        for (int i = 0; i < traces.Count; i++)
        {
            ref readonly var s = ref slots[i];

            float ageFade = 1f;
            if (haveTime)
            {
                double age = now - s.Time;
                // ⚠ A backwards replay seek makes age negative — treat that as fresh, never as expired.
                if (age > VisibleSeconds) continue;
                if (age > VisibleSeconds - FadeSeconds && FadeSeconds > 0d)
                    ageFade = (float)((VisibleSeconds - age) / FadeSeconds);
                if (ageFade < 0f) ageFade = 0f;
                if (ageFade > 1f) ageFade = 1f;
            }

            var color = s.Outcome switch
            {
                ShotOutcome.Hit              => HitColor,
                ShotOutcome.StoppedByTerrain => StoppedColor,
                ShotOutcome.Expired          => ExpiredColor,
                _                            => FlyingColor,
            };
            color = Fade(color, ageFade);
            draw.DrawLine(s.Muzzle, s.End, color, 1.5f, layer: FireTraceLayer,
                style: s.Outcome == ShotOutcome.InFlight ? LineStyle.Dashed : LineStyle.Solid);
            if (s.HasEnd != 0) draw.DrawSphere(s.End, 0.25f, color, layer: FireTraceLayer, fillColor: color);
            for (int c = 0; c < s.CrossingCount; c++)
            {
                var at = s.Crossings[c].At;
                // ⭐ CE-3153 — the crossing marks fade with their own trace, or the line vanishes and its
                //   green dots / orange crosses stay behind, which is the same clutter in smaller pieces.
                var passed  = Fade(PassedColor,  ageFade);
                var stopped = Fade(StoppedColor, ageFade);
                if (s.Crossings[c].Passed != 0) draw.DrawSphere(at, 0.15f, passed, layer: FireTraceLayer, fillColor: passed);
                else
                {
                    draw.DrawLine(at + new Vector3(-0.4f, -0.4f, 0), at + new Vector3(0.4f, 0.4f, 0), stopped, 2f, layer: FireTraceLayer);
                    draw.DrawLine(at + new Vector3(-0.4f, 0.4f, 0), at + new Vector3(0.4f, -0.4f, 0), stopped, 2f, layer: FireTraceLayer);
                }
            }
        }
    }

    /// <summary>⭐ CE-3153 — scales a colour's ALPHA only, so the fade never shifts a hue the outcome encodes.</summary>
    private static Rgba32 Fade(Rgba32 c, float f)
        => f >= 1f ? c : new Rgba32(c.R, c.G, c.B, (byte)(c.A * (f < 0f ? 0f : f)));
}
