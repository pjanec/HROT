using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ Stage 7a (<c>CE-3134</c>, §3l C7) — the terrain's COVER DATABASE: every point of the <see cref="ICoverProvider"/> world
/// singleton that terrain residency publishes (<see cref="TerrainCoverProvider"/>), cover green and window firing positions blue,
/// a dot sized by stance (prone · crouch · stand) at the point's own Z, and a short tick toward the wall it faces. Nothing showed
/// cover before, so a wrong point was invisible. Has data = can draw: SimHost, Editor, CGF, IG, and the Replay Browser (which
/// mirrors the provider with the terrain). ⭐ <c>CE-3143</c>: plus the points beside every standing vehicle, lighter green (live, never in
/// the database). Toggled by the <c>Cover</c> layer (off by default — a town holds hundreds of points).
/// 📄 docs/DESIGN_Building_Interiors.md §3l.
/// </summary>
[GizmoProjector]
public sealed class CoverPointsGizmo : IGlobalStatelessGizmo
{
    public static readonly Rgba32 CoverColor  = new(0, 200, 80, 220);
    public static readonly Rgba32 WindowColor = new(0, 140, 255, 230);

    /// <summary>⭐ <c>CE-3143</c> — a point beside a STANDING vehicle (P-7a O5): live, gone when the vehicle drives off.</summary>
    public static readonly Rgba32 VehicleColor = new(150, 255, 150, 220);

    private readonly System.Collections.Generic.List<CoverPoint> _vehicle = new();

    /// <summary>The tick toward the wall, metres.</summary>
    public const float FacingTick = 0.6f;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonManaged<ICoverProvider>()) return;
        // ⭐ Only the terrain database enumerates its points; a designer-placed provider (tests) draws nothing.
        if (repo.GetSingletonManaged<ICoverProvider>() is not TerrainCoverProvider cover) return;
        foreach (var p in cover.Points)
            Dot(draw, p, p.Kind == CoverKind.WindowFiring ? WindowColor : CoverColor);

        // ⭐ CE-3143 — the points beside standing vehicles, from the SAME producer the cover query uses (VehicleCover.StandingPoints):
        //   they are in no database (a vehicle is read live, R-243/R-245), so without this the map showed less cover than the AI saw.
        _vehicle.Clear();
        if (VehicleCover.StandingPoints(view, EqsTerrainSight.World(view), _vehicle))
            foreach (var p in _vehicle) Dot(draw, p, VehicleColor);
    }

    private static void Dot(IDebugDrawBuilder draw, in CoverPoint p, Rgba32 color)
    {
        var at = new Vector3(p.PositionX, p.PositionY, p.PositionZ + 0.05f);
        draw.DrawSphere(at, RadiusOf(p.StanceHeight), color, layer: DebugTraceLayers.Cover, fillColor: color);
        draw.DrawLine(at, at + new Vector3(p.DirectionX, p.DirectionY, 0f) * FacingTick, color, 2f, SizeMode.ScreenPixels,
            layer: DebugTraceLayers.Cover);
    }

    /// <summary>The dot's radius by stance: prone 0.15 · crouch 0.25 · stand 0.35 m.</summary>
    public static float RadiusOf(byte stance) => stance switch { 0 => 0.15f, 1 => 0.25f, _ => 0.35f };
}
