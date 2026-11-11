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
}
