namespace Hrot.Tools.TerrainSynth;

/// <summary>
/// Makes the doors and windows of a clean building set look like measured data: heights off the floor, positions off the
/// wall, a few headings flipped and a few doors on no wall. Footprints, floors and roofs are not touched.
/// </summary>
public static class OpeningNoise
{
    // door height above its floor: knots (cumulative share, metres); median +0.1, p10 -0.3, p90 +1.0, clipped to [-1.2, +3.6]
    private static readonly (double U, double Z)[] DoorKnots =
    {
        (0.0, -1.2), (0.10, -0.3), (0.50, 0.1), (0.90, 1.0), (1.0, 3.6),
    };

    private static double DoorOffset(double u)
    {
        for (int i = 1; i < DoorKnots.Length; i++)
        {
            if (u <= DoorKnots[i].U)
            {
                var (u0, z0) = DoorKnots[i - 1];
                var (u1, z1) = DoorKnots[i];
                return z0 + (z1 - z0) * (u - u0) / (u1 - u0);
            }
        }
        return DoorKnots[^1].Z;
    }

    private static double Gauss(Rng rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static bool Inside(IReadOnlyList<(double X, double Y)> v, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                inside = !inside;
        return inside;
    }

    public static IReadOnlyList<Building> Apply(IReadOnlyList<Building> buildings, Rng rng)
    {
        var result = new List<Building>(buildings.Count);
        foreach (var b in buildings)
        {
            var openings = new List<Opening>(b.Openings.Count);
            foreach (var o in b.Openings)
            {
                // the same number of draws for every opening, whatever is decided
                double uz = rng.NextDouble(), uOn = rng.NextDouble(), g = Math.Abs(Gauss(rng)), uSide = rng.NextDouble();
                double uFlip = rng.NextDouble(), uLoose = rng.NextDouble(), uDist = rng.NextDouble();

                double floor = b.StoreyZ[o.Storey];
                double z = o.Type == "door" ? floor + DoorOffset(uz) : floor + 1.0 + (uz * 0.4 - 0.2);

                double rad = o.Heading * Math.PI / 180.0;
                double nx = Math.Sin(rad), ny = Math.Cos(rad);
                double x = o.X, y = o.Y;
                if (o.Type == "door" && uLoose < 0.01)
                {
                    // a door on no wall: 1 to 3 m outward, kept outside the footprint
                    double dist = 1.0 + 2.0 * uDist;
                    if (Inside(b.Footprint, o.X + nx * dist, o.Y + ny * dist)) dist = 1.0;
                    if (!Inside(b.Footprint, o.X + nx * dist, o.Y + ny * dist)) { x = o.X + nx * dist; y = o.Y + ny * dist; }
                }
                else if (uOn >= 0.5)
                {
                    double off = Math.Min(0.3, g * 0.1) * (uSide < 0.5 ? -1.0 : 1.0);
                    x = o.X + nx * off;
                    y = o.Y + ny * off;
                }

                double heading = o.Heading;
                if (uFlip < 0.03)
                {
                    heading = (heading + 180.0) % 360.0;
                    heading = Math.Round(heading, 1);
                }
                openings.Add(o with { X = Math.Round(x, 2), Y = Math.Round(y, 2), Z = Math.Round(z, 2), Heading = heading });
            }
            result.Add(b with { Openings = openings });
        }
        return result;
    }
}
