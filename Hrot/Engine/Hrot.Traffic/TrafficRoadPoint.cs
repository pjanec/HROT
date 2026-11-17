using System.Numerics;
using System.Runtime.CompilerServices;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ CE-9020 (mode 1 → 2) — WHERE SUMO CAN TAKE A DESTINATION: the destination itself when a drivable lane passes within SUMO's own snap
/// radius of it (<c>LiveCitySim.ExternalVehicleSnapRadius</c>, the radius SumoSharp H13 looks for a destination lane in), else the nearest
/// point on a drivable lane. One function, called by BOTH sides of a hand-over so they agree by construction: the traffic node sends SUMO
/// this point (<see cref="TrafficHandoverSystem.Gather"/>), and the body's mover reads the shadow's vanishing there as the traffic leg's
/// arrival (<see cref="TrafficShadowFollowSystem"/>). The move's executor then drives the rest of the way (<c>MoveToExecutor</c>).
/// </summary>
/// <remarks>Read-only, allocation-free per query after the first on a net (the lane grid is built once per parsed net, the scratch list is
/// per thread).</remarks>
public static class TrafficRoadPoint
{
    /// <summary>SumoSharp H13's destination snap radius — a destination nearer a lane than this is taken as it is.</summary>
    public const double SnapRadius = Sim.LiveCity.LiveCitySim.ExternalVehicleSnapRadius;

    /// <summary>How far from the destination the search for a lane goes (net metres). Past it SUMO is not asked at all.</summary>
    public const double MaxSearchMetres = 2000.0;

    private static readonly ConditionalWeakTable<NetworkModel, LaneSpatialIndex> Indexes = new();
    [ThreadStatic] private static List<int>? _scratch;

    /// <summary>How much farther than the nearest lane another lane may be and still be chosen for its DIRECTION — the other carriageway
    /// of a divided road (📌 demo-city's avenue: 57 m to the northbound lane, 67 m to the southbound one a convoy drives).</summary>
    public const double DirectionMarginMetres = 20.0;

    /// <summary>The point SUMO is sent for the terrain-local <paramref name="local"/>, in terrain-local metres; <paramref name="onNet"/> is true
    /// when that is <paramref name="local"/> itself. With <paramref name="from"/> (where the vehicle is), a lane within
    /// <see cref="DirectionMarginMetres"/> of the nearest one that runs the way the vehicle approaches the destination wins: ⛔ the nearest
    /// lane alone can be the OPPOSITE carriageway, and SUMO then drives on past the destination looking for a way round (measured,
    /// <c>cv-mode-1-to-2</c>). False when no drivable lane lies within <see cref="MaxSearchMetres"/>.</summary>
    public static bool Near(NetworkModel model, SumoNetPlacement placement, Vector2 local, out Vector2 road, out bool onNet, Vector2? from = null)
    {
        road = local; onNet = true;
        var (nx, ny) = placement.ToNet(local.X, local.Y);
        double hx = 0, hy = 0;
        if (from is { } f)
        {
            var (fx, fy) = placement.ToNet(f.X, f.Y);
            hx = nx - fx; hy = ny - fy;
        }
        if (!NearestOnNet(model, nx, ny, 0, 0, out _, out _, out double nearest)) { onNet = false; return false; }
        if (nearest <= SnapRadius) return true;   // on the net: sent as it is (SUMO's own nearest lane)
        onNet = false;
        NearestOnNet(model, nx, ny, hx, hy, out double rx, out double ry, out _);
        var (lx, ly) = placement.ToLocal(rx, ry);
        road = new Vector2((float)lx, (float)ly);
        return true;
    }

    /// <summary>How far <paramref name="local"/> lies from the nearest drivable lane (metres; <see cref="double.PositiveInfinity"/> past
    /// <see cref="MaxSearchMetres"/>). The shadow's mover reads a traffic leg as arrived within this + <see cref="DirectionMarginMetres"/>
    /// + the arrival slack of the destination — whichever lane the hand-over chose.</summary>
    public static double OffNet(NetworkModel model, SumoNetPlacement placement, Vector2 local)
    {
        var (nx, ny) = placement.ToNet(local.X, local.Y);
        return NearestOnNet(model, nx, ny, 0, 0, out _, out _, out double dist) ? dist : double.PositiveInfinity;
    }

    /// <summary>The point on a drivable, non-internal lane (the lanes H13 accepts as a destination) nearest the net point
    /// (<paramref name="x"/>, <paramref name="y"/>) — or, with a heading (<paramref name="hx"/>, <paramref name="hy"/>) ≠ 0, the one best aligned
    /// with it among the lanes within <see cref="DirectionMarginMetres"/> of the nearest. Searched in growing rings up to <see cref="MaxSearchMetres"/>.</summary>
    public static bool NearestOnNet(NetworkModel model, double x, double y, double hx, double hy, out double rx, out double ry, out double dist)
    {
        var index = Indexes.GetValue(model, m => new LaneSpatialIndex(m));
        var near = _scratch ??= new List<int>();
        rx = x; ry = y; dist = double.PositiveInfinity;
        double nearest = double.PositiveInfinity;
        for (double r = SnapRadius; ; r = Math.Min(r * 4.0, MaxSearchMetres))
        {
            index.LanesNear(x, y, r, near);
            foreach (int h in near)
                if (Drivable(model.LanesByHandle[h])) nearest = Math.Min(nearest, LaneProjection.Project(model.LanesByHandle[h].Shape, x, y).Distance);
            if (!double.IsPositiveInfinity(nearest)) break;
            if (r >= MaxSearchMetres) return false;
        }
        double hl = Math.Sqrt(hx * hx + hy * hy);
        double reach = hl > 1e-6 ? nearest + DirectionMarginMetres : nearest;
        index.LanesNear(x, y, reach, near);
        double bestScore = double.NegativeInfinity;
        foreach (int h in near)
        {
            var lane = model.LanesByHandle[h];
            if (!Drivable(lane)) continue;
            var (offset, _, d) = LaneProjection.Project(lane.Shape, x, y);
            if (d > reach) continue;
            var at = LaneGeometry.PositionAtOffset(lane.Shape, offset);
            double score;
            if (hl > 1e-6)
            {
                double a = at.AngleDeg * Math.PI / 180.0;   // naviDegree: 0 = +y, clockwise
                score = (Math.Sin(a) * hx + Math.Cos(a) * hy) / hl - d * 1e-3;   // aligned first, then nearer
            }
            else score = -d;
            if (score > bestScore) { bestScore = score; rx = at.X; ry = at.Y; dist = d; }
        }
        return true;
    }

    private static bool Drivable(Lane lane)
        => lane.AllowsRoadVehicle && lane.Shape.Count >= 2 && !lane.EdgeId.StartsWith(":", StringComparison.Ordinal);
}
