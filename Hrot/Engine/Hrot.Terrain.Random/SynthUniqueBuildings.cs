using System.Numerics;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.Import;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Random;

/// <summary>
/// ⭐ T-3 part 2 (CE-4430, docs/DESIGN_Terrain_Authoring.md §18i I-6) — the host's <see cref="IUniqueBuildings"/>: a block's streets go to the synth's SITE-ONLY building run (<see cref="SiteBuildings"/> — the same <c>BuildingGenerator</c>, no whole generated ground) and what it
/// places goes through the importer's <c>BuildingTemplateGenerator</c> (the ONE path from a generated building to a template: footprint, storeys, openings, stairs). Only ENTERABLE results come back (a solid prism has no door to enter by). Deterministic: the same request is the same templates, byte for byte.
/// </summary>
public sealed class SynthUniqueBuildings : IUniqueBuildings
{
    public IReadOnlyList<UniqueBuilding> Make(UniqueRequest request)
    {
        var streets = request.Streets.Select(s => new SiteStreet(s.A.X, s.A.Y, s.B.X, s.B.Y, s.Width)).ToList();
        // ask for more than wanted: some are solid (no stair fits) and are dropped
        var generated = SiteBuildings.Generate(streets, request.Count + request.Count / 2 + 2, request.MinStoreys, request.MaxStoreys, request.AreaP10, request.AreaP50, request.AreaP90, request.Seed);
        var built = BuildingTemplateGenerator.Generate(generated.ToList(), 0, 0, new ImportReport(), CancellationToken.None);
        var result = new List<UniqueBuilding>();
        foreach (var b in built)
        {
            if (b.Solid || b.TemplateJson == null) continue;
            result.Add(new UniqueBuilding(b.TemplateJson, new Vector2((float)b.Origin.X, (float)b.Origin.Y)));
            if (result.Count >= request.Count) break;
        }
        return result;
    }

    /// <summary>The spacing of the generated windows along a wall (m) and their height above each storey's floor.</summary>
    public const double WindowEvery = 3.5, WindowZ = 1.5, StoreyHeight = 3.0;

    /// <inheritdoc/>
    public UniqueBuilding? FromFootprint(IReadOnlyList<Vector2> footprint, float baseZ, float height, Vector2? door)
    {
        if (footprint.Count < 3) return null;
        var ring = footprint.Select(p => ((double)p.X, (double)p.Y)).ToList();
        double area2 = 0;
        for (int i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; area2 += a.Item1 * b.Item2 - b.Item1 * a.Item2; }
        if (area2 < 0) ring.Reverse();   // the input model is counter-clockwise
        int storeys = Math.Max(1, (int)Math.Round(Math.Max(height, StoreyHeight) / StoreyHeight));
        var storeyZ = Enumerable.Range(0, storeys).Select(s => baseZ + s * Math.Max(height, StoreyHeight) / storeys).ToList();
        var openings = new List<Hrot.Terrain.InputSet.Opening>();
        if (door is { } d) openings.Add(new Hrot.Terrain.InputSet.Opening("door", d.X, d.Y, baseZ, 0, 0));
        for (int s = 0; s < storeys; s++)
            for (int e = 0; e < ring.Count; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % ring.Count];
                double len = Math.Sqrt((b.Item1 - a.Item1) * (b.Item1 - a.Item1) + (b.Item2 - a.Item2) * (b.Item2 - a.Item2));
                int n = (int)Math.Floor(len / WindowEvery);
                for (int k = 0; k < n; k++)
                {
                    double t = (k + 0.5) / n;
                    openings.Add(new Hrot.Terrain.InputSet.Opening("window", a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, storeyZ[s] + WindowZ, 0, s));
                }
            }
        var closed = ring.Append(ring[0]).ToList();
        var building = new Hrot.Terrain.InputSet.Building("edit", closed, baseZ, Math.Max(height, StoreyHeight), "flat", null, storeyZ, openings, null, Enterable: true);
        var built = BuildingTemplateGenerator.Generate(new[] { building }, 0, 0, new ImportReport(), CancellationToken.None);
        var t0 = built.FirstOrDefault(b => !b.Solid && b.TemplateJson != null);
        return t0 == null ? null : new UniqueBuilding(t0.TemplateJson!, new Vector2((float)t0.Origin.X, (float)t0.Origin.Y));
    }
}
