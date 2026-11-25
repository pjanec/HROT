namespace Hrot.Terrain.Import;

/// <summary>A door or window mapped onto a wall of a template: <see cref="At"/> is the opening's START along the wall.</summary>
internal sealed record PlacedOpening(string Kind, int Edge, int Storey, double At, double Width, string? DoorId);

/// <summary>
/// ⭐ I-e: each input point → the nearest footprint edge within <see cref="Reach"/> (ground-floor doors <see cref="GroundDoorReach"/>, recessed entrances) (0.6 m — the real noise is ≤ 0.3 m off-wall; the heading is
/// NOT trusted, 3 % are flipped); <c>at</c> = the projection along the wall; door 0.9 m, window 1.2 m; doors are placed before windows, and an
/// opening overlapping one already on that wall and storey is dropped; one beyond reach, on a party wall, or on a wall too short is dropped and counted.
/// </summary>
internal static class OpeningPlacer
{
    /// <summary>⭐ <c>DoorWidth</c> 1.2 m (CE-5056, user `2026-11-25`: "ok to 1,2"; was 0.9): the infantry navmesh keeps 0.3 m off each jamb, so a 0.9 m door left a
    /// 0.3 m channel (two 0.15 m cells) that broke up on a wall rotated off the grid — 📐 village-street: 234 of 582 ground doors had no path through at 0.9 m,
    /// 118 at 1.0 m, 72 at 1.2 m (the rest: a stair behind the door, now kept clear — <see cref="StairPlanner.DoorApron"/>). The source gives no door widths.</summary>
    public const double Reach = 0.6, GroundDoorReach = 1.5, DoorWidth = 1.2, WindowWidth = 1.2;

    /// <param name="AddedDoor">⭐ CE-5029 — the ≥ 1 door rule added the ground-floor door (the input had none that fit).</param>
    /// <param name="Doorless">⭐ CE-5029 — no ground-floor door, and no free wall to put one on (every wall shared): REPORTED, never silent.</param>
    public readonly record struct Counts(int Placed, int OffWall, int OnParty, int Overlap, int Other, bool AddedDoor = false, bool Doorless = false);

    /// <summary>
    /// ⭐ CE-5029 (📄 docs/DESIGN_Terrain_Base_Edits.md D-6) — places the input's doors and windows (I-e), then guarantees an ENTERABLE house its door: when no ground-floor
    /// door fit, one is added in the middle of a FREE wall — the one facing the nearest road (<paramref name="nearestRoad"/>, when the caller knows the roads), else the
    /// longest — shifted off a ground-floor window it would overlap; a footprint with every wall shared is reported (<see cref="Counts.Doorless"/>). ⭐ One rule for the
    /// importer and the editor's "make enterable" (ruling 9). <paramref name="doorHint"/> (the editor's "add door": a point on a wall) puts the door on THAT wall, there.
    /// </summary>
    public static (List<PlacedOpening> Placed, Counts Counts) Place(
        IReadOnlyList<(double X, double Y)> ring, string buildingId, IReadOnlyList<Opening> openings, int storeys, PartyWalls party,
        Func<(double X, double Y), (double X, double Y)?>? nearestRoad = null, (double X, double Y)? doorHint = null)
    {
        var placed = new List<PlacedOpening>();
        int off = 0, onParty = 0, overlap = 0, other = 0, doors = 0;
        int n = ring.Count;
        // doors first (stable otherwise): a window listed before an overlapping door must not take the door's place
        foreach (var o in openings.OrderBy(o => o.Type == "door" ? 0 : 1))
        {
            if (o.Storey < 0 || o.Storey >= storeys || (o.Type != "door" && o.Type != "window")) { other++; continue; }
            double reach = o.Type == "door" && o.Storey == 0 ? GroundDoorReach : Reach;   // a recessed entrance sits up to 1.5 m inside
            int best = -1; double bestD = reach + 1e-9, bestT = 0;
            for (int e = 0; e < n; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                if (l2 < 1e-6) continue;
                double t = Math.Clamp(((o.X - a.X) * dx + (o.Y - a.Y) * dy) / l2, 0, 1);
                double px = a.X + t * dx, py = a.Y + t * dy, d = Math.Sqrt((o.X - px) * (o.X - px) + (o.Y - py) * (o.Y - py));
                if (d < bestD) { bestD = d; best = e; bestT = t * Math.Sqrt(l2); }
            }
            if (best < 0) { off++; continue; }
            if (party.IsShared(buildingId, best)) { onParty++; continue; }
            var p = ring[best]; var q = ring[(best + 1) % n];
            double len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            double width = o.Type == "door" ? DoorWidth : WindowWidth;
            if (len < width + 0.1) { other++; continue; }
            double at = Math.Clamp(bestT - width / 2, 0.05, len - width - 0.05);
            if (placed.Any(x => x.Edge == best && x.Storey == o.Storey && at < x.At + x.Width + 1e-3 && x.At < at + width + 1e-3)) { overlap++; continue; }
            placed.Add(new PlacedOpening(o.Type, best, o.Storey, Math.Round(at, 3), width, o.Type == "door" ? "d" + doors++ : null));
        }
        bool added = false, doorless = false;
        if (storeys > 0 && !placed.Any(x => x.Kind == "door" && x.Storey == 0))
        {
            if (AddDoor(ring, buildingId, party, placed, nearestRoad, doorHint, ref doors)) added = true; else doorless = true;
        }
        return (placed, new Counts(placed.Count, off, onParty, overlap, other, added, doorless));
    }

    /// <summary>The ≥ 1 door rule: a ground-floor door on the hinted wall, else the free wall facing the nearest road, else the longest free wall.</summary>
    private static bool AddDoor(IReadOnlyList<(double X, double Y)> ring, string id, PartyWalls party, List<PlacedOpening> placed,
                                Func<(double X, double Y), (double X, double Y)?>? nearestRoad, (double X, double Y)? hint, ref int doors)
    {
        int n = ring.Count;
        double Len(int e) { var a = ring[e]; var b = ring[(e + 1) % n]; return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); }
        (double X, double Y) Mid(int e) { var a = ring[e]; var b = ring[(e + 1) % n]; return ((a.X + b.X) / 2, (a.Y + b.Y) / 2); }
        bool Free(int e) => !party.IsShared(id, e) && Len(e) >= DoorWidth + 0.3;
        int edge = -1; double atCentre = 0;
        if (hint is { } h)
        {
            double bestD = 1.5;
            for (int e = 0; e < n; e++)
            {
                if (!Free(e)) continue;
                var a = ring[e]; var b = ring[(e + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = Math.Clamp(((h.X - a.X) * dx + (h.Y - a.Y) * dy) / l2, 0, 1);
                double px = a.X + t * dx, py = a.Y + t * dy, d = Math.Sqrt((h.X - px) * (h.X - px) + (h.Y - py) * (h.Y - py));
                if (d < bestD) { bestD = d; edge = e; atCentre = t * Math.Sqrt(l2); }
            }
        }
        if (edge < 0 && nearestRoad != null)
        {
            double bestD = double.MaxValue;
            for (int e = 0; e < n; e++)
            {
                if (!Free(e)) continue;
                var m = Mid(e);
                if (nearestRoad(m) is not { } r) continue;
                double d = Math.Sqrt((r.X - m.X) * (r.X - m.X) + (r.Y - m.Y) * (r.Y - m.Y));
                if (d < bestD) { bestD = d; edge = e; atCentre = Len(e) / 2; }
            }
        }
        if (edge < 0)
            for (int e = 0; e < n; e++)
                if (Free(e) && (edge < 0 || Len(e) > Len(edge))) { edge = e; atCentre = Len(e) / 2; }
        if (edge < 0) return false;
        double len = Len(edge);
        double at = Math.Clamp(atCentre - DoorWidth / 2, 0.05, len - DoorWidth - 0.05);
        // a ground-floor window where the door goes: the door takes the place (a house with no door is the worse outcome)
        placed.RemoveAll(x => x.Edge == edge && x.Storey == 0 && at < x.At + x.Width + 1e-3 && x.At < at + DoorWidth + 1e-3);
        placed.Add(new PlacedOpening("door", edge, 0, Math.Round(at, 3), DoorWidth, "d" + doors++));
        return true;
    }
}
