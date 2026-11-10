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
    public const double Reach = 0.6, GroundDoorReach = 1.5, DoorWidth = 0.9, WindowWidth = 1.2;

    public readonly record struct Counts(int Placed, int OffWall, int OnParty, int Overlap, int Other);

    public static (List<PlacedOpening> Placed, Counts Counts) Place(
        IReadOnlyList<(double X, double Y)> ring, string buildingId, IReadOnlyList<Opening> openings, int storeys, PartyWalls party)
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
        return (placed, new Counts(placed.Count, off, onParty, overlap, other));
    }
}
