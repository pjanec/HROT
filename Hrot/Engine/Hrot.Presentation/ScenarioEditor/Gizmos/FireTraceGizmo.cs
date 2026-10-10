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
    /// ⭐⭐⭐ <c>CE-3153</c> — <b>how long a trace stays on the map (s of SIM time), fading the whole way.</b>
    /// <para>🔒 User ruling, <c>2026-10-09</c>: <i>"traces can not be persistent, they need to disappear after short
    /// time after being drawn for the first time"</i> — then <i>"the lines should fade out (getting more translucent
    /// with time until disappear) - to recognize older from newer; same with explosions."</i></para>
    /// <para>⛔⛔ <b>MY FIRST TWO ATTEMPTS BOTH RE-INVENTED MACHINERY THAT ALREADY EXISTED.</b> I wrote a private
    /// <c>now</c>/age/fade with its own <c>VisibleSeconds</c> + <c>FadeSeconds</c> tail, and its own alpha helper.
    /// 📐 <see cref="DebugTraceClock"/> already did all of it — <c>Now(repo)</c>, and <c>Fade(age, shown)</c> whose
    /// own summary is <i>"1 at age 0, falling to 0 at shown; negative when the trace is not shown"</i>, i.e. the
    /// progressive fade the user asked for AND the negative-age case that made a reload redraw the previous run.
    /// ⭐ Every sibling trace gizmo (<c>DetonationGizmo</c>, hearing, paths) was already using it; <b>this gizmo was
    /// the only one that was not</b>, which is exactly why it was the only one with these defects.</para>
    /// <para>⭐⭐ <b>The DISPLAY expires; the DATA does not.</b> The 64-slot ring is untouched, so
    /// <c>CE-3117</c>/<c>R-226</c>'s acceptance — <i>"a recorded run, seeked to just after a detonation, draws its
    /// rays"</i> — still holds: after a seek the age is small and the trace is inside the window.</para>
    /// <para>⚠ Tune here. <c>DetonationGizmo.Shown</c> is 1.0 s for a burst; traces get longer because a shot is a
    /// thin line that is easier to miss than an expanding ring.</para>
    /// </summary>
    public const double Shown = 3.0;

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

        // ⭐ CE-3153 — the family's own clock, the same one DetonationGizmo ages a burst against.
        double now = DebugTraceClock.Now(repo);

        for (int i = 0; i < traces.Count; i++)
        {
            ref readonly var s = ref slots[i];

            // ⭐⭐⭐ ONE call covers all three requirements: the progressive fade (1 → 0 across `Shown`, so an older
            //   line is plainly more translucent than a newer one), the hard cutoff, AND the negative age that a
            //   scenario RELOAD produces — nothing clears the ShotTraces ring on load while sim time restarts near
            //   0, so last run's traces sit in the future and must not be drawn.
            float fade = DebugTraceClock.Fade(now - s.Time, Shown);
            if (fade < 0f) continue;

            var color = s.Outcome switch
            {
                ShotOutcome.Hit              => HitColor,
                ShotOutcome.StoppedByTerrain => StoppedColor,
                ShotOutcome.Expired          => ExpiredColor,
                _                            => FlyingColor,
            };
            color = DetonationGizmo.WithAlpha(color, fade);
            draw.DrawLine(s.Muzzle, s.End, color, 1.5f, layer: FireTraceLayer,
                style: s.Outcome == ShotOutcome.InFlight ? LineStyle.Dashed : LineStyle.Solid);
            if (s.HasEnd != 0) draw.DrawSphere(s.End, 0.25f, color, layer: FireTraceLayer, fillColor: color);
            for (int c = 0; c < s.CrossingCount; c++)
            {
                var at = s.Crossings[c].At;
                // ⭐ CE-3153 — the crossing marks fade with their own trace, or the line vanishes and its
                //   green dots / orange crosses stay behind, which is the same clutter in smaller pieces.
                var passed  = DetonationGizmo.WithAlpha(PassedColor,  fade);
                var stopped = DetonationGizmo.WithAlpha(StoppedColor, fade);
                if (s.Crossings[c].Passed != 0) draw.DrawSphere(at, 0.15f, passed, layer: FireTraceLayer, fillColor: passed);
                else
                {
                    draw.DrawLine(at + new Vector3(-0.4f, -0.4f, 0), at + new Vector3(0.4f, 0.4f, 0), stopped, 2f, layer: FireTraceLayer);
                    draw.DrawLine(at + new Vector3(-0.4f, 0.4f, 0), at + new Vector3(0.4f, -0.4f, 0), stopped, 2f, layer: FireTraceLayer);
                }
            }
        }
    }

}
