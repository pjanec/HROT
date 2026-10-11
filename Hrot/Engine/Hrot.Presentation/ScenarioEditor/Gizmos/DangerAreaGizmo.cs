using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Squad.DangerArea;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> — the <b>danger areas</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a): every box of every
/// <see cref="DangerAreaCognitiveBuffer"/> (the danger-along-route sensor's answer, <c>CE-3072</c>) as its oriented rectangle,
/// coloured from yellow (low threat) to red (high), with its kind. A global gizmo: the buffer sits on the SENSOR CHILD, which is never
/// the selected entity. The buffer is an ordinary recorded component, so replay shows it too. Drawn on the <c>AiHelpers</c> layer (2).
/// </summary>
[GizmoProjector]
public sealed class DangerAreaGizmo : IGlobalStatelessGizmo
{
    public const byte Layer = 2;

    private EntityRepository? _queryRepo;
    private EntityQuery? _buffers;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<DangerAreaCognitiveBuffer>()) return;
        if (!ReferenceEquals(repo, _queryRepo))
        {
            _queryRepo = repo;
            _buffers = repo.Query().With<DangerAreaCognitiveBuffer>().Build();
        }
        foreach (var e in _buffers!)
        {
            var buffer = repo.GetComponentRO<DangerAreaCognitiveBuffer>(e);
            var areas = buffer.GetSpanRO();
            for (int i = 0; i < Math.Min(buffer.Count, areas.Length); i++)
            {
                ref readonly var a = ref areas[i];
                var color = DetonationGizmo.Mix(new Rgba32(240, 220, 40, 220), new Rgba32(230, 40, 40, 220), a.ThreatRating);
                // ⭐ CE-1033 S2 (§3.9 H3) — the area's HEIGHT BAND, which it always had and never drew: bottom and top outlines at
                //   ZFloor / ZCeiling joined at the corners (a bridge deck vs the street below are two areas). In 2-D the outlines lie
                //   on top of each other; a band of zero thickness draws the old single outline.
                if (a.ZCeiling > a.ZFloor + 0.05f)
                    Band(draw, a.Center, a.ExtentsXY, a.AngleRad, a.ZFloor, a.ZCeiling, color);
                else
                    Box(draw, a.Center, a.ExtentsXY, a.AngleRad, color);
                draw.DrawText(a.Center.X, a.Center.Y, new Fdp.Core.FixedString32($"{a.Kind} {a.ThreatRating:0.00}"), color, layer: Layer);
            }
        }
    }

    /// <summary>⭐ CE-1033 S2 (H3) — the wire prism of an oriented footprint between two absolute heights.</summary>
    public static void Band(IDebugDrawBuilder draw, Vector3 c, Vector2 half, float yaw, float zFloor, float zCeiling, Rgba32 color)
    {
        Box(draw, c with { Z = zFloor }, half, yaw, color);
        Box(draw, c with { Z = zCeiling }, half, yaw, color);
        var ax = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * half.X;
        var ay = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f) * half.Y;
        Span<Vector3> corners = stackalloc Vector3[] { -ax - ay, ax - ay, ax + ay, -ax + ay };
        foreach (var corner in corners)
        {
            var p = c + corner;
            draw.DrawLine(p with { Z = zFloor }, p with { Z = zCeiling }, color, 1.5f, layer: Layer);
        }
    }

    /// <summary>The oriented rectangle of half-extents <paramref name="half"/> turned by <paramref name="yaw"/> about <paramref name="c"/>.</summary>
    public static void Box(IDebugDrawBuilder draw, Vector3 c, Vector2 half, float yaw, Rgba32 color)
    {
        var ax = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * half.X;
        var ay = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f) * half.Y;
        var p0 = c - ax - ay; var p1 = c + ax - ay; var p2 = c + ax + ay; var p3 = c - ax + ay;
        draw.DrawLine(p0, p1, color, 1.5f, layer: Layer);
        draw.DrawLine(p1, p2, color, 1.5f, layer: Layer);
        draw.DrawLine(p2, p3, color, 1.5f, layer: Layer);
        draw.DrawLine(p3, p0, color, 1.5f, layer: Layer);
    }
}
