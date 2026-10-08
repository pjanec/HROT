using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> — the <b>blast / fragments</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a): for
/// <see cref="Shown"/> s of sim time after a burst, its fragment and blast-injury radii, a ray from the burst to every body point it
/// rated (green where the fragments got through, shading to orange as less did, an ✕ at the first obstacle on a blocked ray) and each
/// target's exposure and damage. Reads the RECORDED <see cref="DetonationTraces"/> singleton, so a replay seek shows the burst again;
/// a node with no area effect draws nothing. Toggled by the <c>Blast</c> bit of the layer control.
/// </summary>
[GizmoProjector]
public sealed class DetonationGizmo : IGlobalStatelessGizmo
{
    /// <summary>How long a burst stays on the map (s of sim time).</summary>
    public const double Shown = 1.0;

    private static readonly Rgba32 FragmentRing = new(240, 140, 20, 255);
    private static readonly Rgba32 BlastRing    = new(220, 40, 40, 255);
    private static readonly Rgba32 Clear        = new(60, 200, 60, 255);
    private static readonly Rgba32 Blocked      = new(240, 140, 20, 255);
    private static readonly Rgba32 Label        = new(20, 20, 20, 255);

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonUnmanaged<DetonationTraces>()) return;
        double now = DebugTraceClock.Now(repo);
        var traces = repo.GetSingletonUnmanaged<DetonationTraces>();
        var slots = traces.SlotsRO();
        for (int i = 0; i < traces.Count; i++)
        {
            ref readonly var d = ref slots[i];
            float fade = DebugTraceClock.Fade(now - d.Time, Shown);
            if (fade < 0f) continue;

            draw.DrawSphere(d.Burst, d.FragmentRadius, WithAlpha(FragmentRing, fade * 0.8f), thickness: 1.5f,
                layer: DebugTraceLayers.Blast, style: LineStyle.Dashed);
            draw.DrawSphere(d.Burst, d.BlastInjuryRadius, WithAlpha(BlastRing, fade * 0.8f), thickness: 1.5f,
                layer: DebugTraceLayers.Blast);
            draw.DrawSphere(d.Burst, 0.4f, WithAlpha(BlastRing, fade), layer: DebugTraceLayers.Blast, fillColor: WithAlpha(BlastRing, fade));

            foreach (ref readonly var ray in d.RaysRO())
            {
                var color = WithAlpha(Mix(Blocked, Clear, ray.Transmission), fade);
                if (ray.HasStop == 0 || ray.Transmission >= 0.999f)
                {
                    draw.DrawLine(d.Burst, ray.To, color, 1f, layer: DebugTraceLayers.Blast);
                    continue;
                }
                draw.DrawLine(d.Burst, ray.StopAt, color, 1f, layer: DebugTraceLayers.Blast);
                if (ray.Transmission > 0f)
                    draw.DrawLine(ray.StopAt, ray.To, color, 1f, layer: DebugTraceLayers.Blast, style: LineStyle.Dashed);
                else
                    Cross(draw, ray.StopAt, 0.3f, WithAlpha(Blocked, fade));
            }

            foreach (ref readonly var t in d.TargetsRO())
                draw.DrawText(t.At.X, t.At.Y + 1.2f, new Fdp.Core.FixedString32($"{t.Exposure * 100f:0}% {t.Damage:0.#}"), WithAlpha(Label, fade),
                    layer: DebugTraceLayers.Blast);
        }
    }

    internal static void Cross(IDebugDrawBuilder draw, Vector3 at, float half, Rgba32 color)
    {
        draw.DrawLine(at + new Vector3(-half, -half, 0), at + new Vector3(half, half, 0), color, 2f, layer: DebugTraceLayers.Blast);
        draw.DrawLine(at + new Vector3(-half, half, 0), at + new Vector3(half, -half, 0), color, 2f, layer: DebugTraceLayers.Blast);
    }

    internal static Rgba32 WithAlpha(Rgba32 c, float a) => new(c.R, c.G, c.B, (byte)(System.Math.Clamp(a, 0f, 1f) * c.A));

    internal static Rgba32 Mix(Rgba32 a, Rgba32 b, float t)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        return new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }
}
