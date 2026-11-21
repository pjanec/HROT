using Hrot.Terrain.InputSet;

namespace Hrot.Tools.TerrainSynth;

/// <summary>A street of a site: its centre line and carriageway width (metres, any local frame).</summary>
public readonly record struct SiteStreet(double Ax, double Ay, double Bx, double By, double Width);

/// <summary>
/// ⭐ T-3 part 2 (CE-4430, docs/DESIGN_Terrain_Authoring.md §18i I-6) — the SITE-ONLY call of the building generator: unique houses along the given streets, on flat ground, in the streets' own frame — no generated ground (no heights, no whole road
/// net, no profile file). It runs the SAME <see cref="BuildingGenerator"/> (footprint rect / L / U, storeys, doors and windows, setback, the 2 m gap between houses, 3 m off every street) over a profile cut to the site: storeys drawn uniformly from
/// <c>minStoreys..maxStoreys</c>, flat roofs, no loose openings. Deterministic from <paramref name="seed"/>.
/// </summary>
public static class SiteBuildings
{
    public static IReadOnlyList<Building> Generate(IReadOnlyList<SiteStreet> streets, int count, int minStoreys, int maxStoreys, double areaP10, double areaP50, double areaP90, ulong seed)
    {
        if (streets.Count == 0 || count <= 0) return Array.Empty<Building>();
        double minX = streets.Min(s => Math.Min(s.Ax, s.Bx)) - 60, maxX = streets.Max(s => Math.Max(s.Ax, s.Bx)) + 60;
        double minY = streets.Min(s => Math.Min(s.Ay, s.By)) - 60, maxY = streets.Max(s => Math.Max(s.Ay, s.By)) + 60;
        int lo = Math.Clamp(minStoreys, 1, 6), hi = Math.Clamp(Math.Max(maxStoreys, minStoreys), lo, 6);
        var shares = new double[6];
        for (int i = lo; i <= hi; i++) shares[i - 1] = 1.0 / (hi - lo + 1);
        var p = SynthProfile.Full with
        {
            Name = "site", MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, NodataRects = Array.Empty<Rect>(),
            BuildingCount = count, FootprintAreaP10 = areaP10, FootprintAreaP50 = areaP50, FootprintAreaP90 = areaP90,
            StoreyShares = shares, PitchedShare = 0, UnassignedPerHouse = 0,
        };
        var rng = new Rng(seed);
        var net = RoadNetwork.FromStreets(p, streets.Select(s => (s.Ax, s.Ay, s.Bx, s.By, s.Width)).ToList());
        var (buildings, _) = BuildingGenerator.Generate(p, rng, HeightField.Flat(p), net);
        return buildings;
    }
}
