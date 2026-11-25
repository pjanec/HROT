namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ <c>CE-5056</c> ④ (user `2026-11-25`: "the importer generates entrance steps - ok approved") — a ground-floor door whose sill stands more than
/// <see cref="MinRise"/> above the ground outside (a house on a slope: its floor at the pad's base, the ground falling away on the downhill side) gets an
/// ENTRANCE RAMP: as wide as the door, from the wall's CENTRELINE (where the floor slab ends, so it also covers the threshold) straight out and down to the
/// ground at <see cref="SlopeDegrees"/> — the world's existing ground-relative <c>ramp</c> feature, its foot buried in the ground by the parser (TH-K).
/// 📐 village-street: 17 ground doors were more than the infantry climb (0.4 m) above the ground outside; up to 2.9 m.
/// </summary>
internal static class EntranceSteps
{
    /// <summary>A rise below this needs no step (the infantry navmesh climbs 0.4 m; a margin for the cell quantisation).</summary>
    public const double MinRise = 0.3;
    /// <summary>The ramp's slope — a flight of steps' pitch; infantry walks up to 60°, so the steps are comfortably walkable.</summary>
    public const double SlopeDegrees = 33;
    /// <summary>A ramp never shorter than this (m): the foot must reach past the wall's outer face and the navmesh's erosion.</summary>
    public const double MinRun = 0.8;

    /// <summary>One ramp, terrain-local: the two TOP corners on the wall's centreline (their height above the ground under them = sill − ground) and the two
    /// FOOT corners on the ground (height 0).</summary>
    public sealed record Ramp(string Building, (double X, double Y) TopA, (double X, double Y) TopB, double TopAboveA, double TopAboveB,
                              (double X, double Y) FootA, (double X, double Y) FootB);

    /// <summary>The entrance ramps every enterable building of <paramref name="built"/> needs, on the final (levelled) ground <paramref name="groundAt"/>.</summary>
    public static List<Ramp> For(IReadOnlyList<BuiltBuilding> built, Func<double, double, double> groundAt, ImportReport rep)
    {
        var list = new List<Ramp>();
        double tan = Math.Tan(SlopeDegrees * Math.PI / 180);
        foreach (var b in built)
        {
            if (b.Solid) continue;
            int n = b.Ring.Count;
            foreach (var (edge, at, width) in b.GroundDoors)
            {
                var a = b.Ring[edge]; var c = b.Ring[(edge + 1) % n];
                double len = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y));
                if (len < 1e-6) continue;
                double dx = (c.X - a.X) / len, dy = (c.Y - a.Y) / len, ox = dy, oy = -dx;   // the ring is counter-clockwise: OUTWARD is the right-hand normal
                var p0 = (X: a.X + dx * at, Y: a.Y + dy * at); var p1 = (X: a.X + dx * (at + width), Y: a.Y + dy * (at + width));
                double sill = b.BaseZ;
                // the run: the rise to the ground at the foot, the foot where that rise ends at the slope — three passes settle a sloping ground
                double run = MinRun, rise = 0;
                for (int k = 0; k < 3; k++)
                {
                    double gFoot = Math.Min(groundAt(p0.X + ox * run, p0.Y + oy * run), groundAt(p1.X + ox * run, p1.Y + oy * run));
                    rise = sill - gFoot;
                    run = Math.Max(MinRun, rise / tan);
                }
                double above0 = sill - groundAt(p0.X, p0.Y), above1 = sill - groundAt(p1.X, p1.Y);
                if (rise <= MinRise || above0 < 0 || above1 < 0) continue;   // level enough, or the threshold is buried (the ground above the sill)
                list.Add(new Ramp(b.Id, p0, p1, Math.Round(above0, 3), Math.Round(above1, 3),
                    (Math.Round(p0.X + ox * run, 3), Math.Round(p0.Y + oy * run, 3)), (Math.Round(p1.X + ox * run, 3), Math.Round(p1.Y + oy * run, 3))));
            }
        }
        rep.EntranceSteps = list.Count;
        return list;
    }
}
