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

    /// <summary>The point SUMO is sent for the terrain-local <paramref name="local"/>, in terrain-local metres; <paramref name="onNet"/> is true
    /// when that is <paramref name="local"/> itself. False when no drivable lane lies within <see cref="MaxSearchMetres"/>.</summary>
    public static bool Near(NetworkModel model, SumoNetPlacement placement, Vector2 local, out Vector2 road, out bool onNet)
    {
        road = local; onNet = true;
        var (nx, ny) = placement.ToNet(local.X, local.Y);
        if (!NearestOnNet(model, nx, ny, out double rx, out double ry, out double dist)) { onNet = false; return false; }
        if (dist <= SnapRadius) return true;
        onNet = false;
        var (lx, ly) = placement.ToLocal(rx, ry);
        road = new Vector2((float)lx, (float)ly);
        return true;
    }

    /// <summary>The nearest point on a drivable, non-internal lane to the net point (<paramref name="x"/>, <paramref name="y"/>) — the lanes
    /// H13 accepts as a destination — searched in growing rings up to <see cref="MaxSearchMetres"/>.</summary>
    public static bool NearestOnNet(NetworkModel model, double x, double y, out double rx, out double ry, out double dist)
    {
        var index = Indexes.GetValue(model, m => new LaneSpatialIndex(m));
        var near = _scratch ??= new List<int>();
        rx = x; ry = y; dist = double.PositiveInfinity;
        for (double r = SnapRadius; ; r = Math.Min(r * 4.0, MaxSearchMetres))
        {
            index.LanesNear(x, y, r, near);
            Lane? best = null; double bestOffset = 0;
            foreach (int h in near)
            {
                var lane = model.LanesByHandle[h];
                if (!lane.AllowsRoadVehicle || lane.Shape.Count < 2 || lane.EdgeId.StartsWith(":", StringComparison.Ordinal)) continue;
                var (offset, _, d) = LaneProjection.Project(lane.Shape, x, y);
                if (d < dist) { dist = d; best = lane; bestOffset = offset; }
            }
            if (best is not null)
            {
                var at = LaneGeometry.PositionAtOffset(best.Shape, bestOffset);
                rx = at.X; ry = at.Y;
                return true;
            }
            if (r >= MaxSearchMetres) return false;
        }
    }
}
