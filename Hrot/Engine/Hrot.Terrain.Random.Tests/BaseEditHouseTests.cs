using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Hrot.Terrain.Random.Tests;

/// <summary>
/// ⭐⭐ <c>CE-5029</c> BE-3 part 2 (📄 docs/DESIGN_Terrain_Base_Edits.md D-6, D-7) — with the host's REAL generator (<see cref="SynthUniqueBuildings.FromFootprint"/>, the importer's
/// <c>BuildingTemplateGenerator</c>): a SOLID 22 m² shed made enterable is an enterable house on its footprint — a door, windows, a floor — and the solid block is gone; a doorless
/// templated house given a door hint has its door THERE. 🔴 Red-proof: return no openings from <c>FromFootprint</c>'s window loop and drop the door ⇒ the generator's ≥ 1 door rule
/// still gives a door (the rule's own rail) — so the hint rail is the one that reds: ignore the hint ⇒ the door lands on the longest wall ⇒ red.
/// </summary>
public sealed class BaseEditHouseTests
{
    private static TerrainWorld World(string features) => TerrainWorldParser.Parse(
        "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,120,60],\"groundZ\":0},\"features\":[" + features + "]}", "range");

    // a solid shed 5 × 4.4 m (22 m²), 3 m tall
    private const string Shed = "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"S\",\"height\":3},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[70,10],[75,10],[75,14.4],[70,14.4],[70,10]]]}}";

    // a doorless templated house 12 × 6 m, one storey, a window on the south wall
    private const string Doorless = "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"T\",\"building\":{\"footprint\":[[0,0],[12,0],[12,6],[0,6]],\"storeys\":[{\"height\":3,\"walls\":[" +
        "{\"from\":[0,0],\"to\":[12,0],\"openings\":[{\"kind\":\"window\",\"at\":2,\"width\":1.2}]},{\"from\":[12,0],\"to\":[12,6]},{\"from\":[12,6],\"to\":[0,6]},{\"from\":[0,6],\"to\":[0,0]}]}]}}," +
        "\"geometry\":{\"type\":\"Point\",\"coordinates\":[20,20]}}";

    private static TerrainWorld Edited(TerrainWorld @base, string action, string target, Vector2 at)
    {
        var parts = FeatureExpander.OfKind(TerrainFeature.Edit, "e1", action, 0f, 0f, 0u, at, System.Array.Empty<Vector2>(), @base, new TerrainAssets(),
            System.Array.Empty<string?>(), target: target, unique: new SynthUniqueBuildings());
        Assert.Equal(3, parts.Count);   // remove, the levelled ground, the new house
        var overlay = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(@base, parts).ToJsonString(), "range", new TerrainAssets(), @base.Height);
        return TerrainWorldMerge.Merge(@base, overlay);
    }

    [Fact]
    public void ASolidShed_MadeEnterable_IsAHouseWithADoorWindowsAndAFloor_OnItsFootprint()
    {
        var b = World(Shed);
        Assert.Empty(b.Buildings);   // solid: a block, no record
        var m = Edited(b, "enterable", "S", new Vector2(72f, 12f));
        Assert.DoesNotContain(m.Prisms, p => p.Label == "S");                    // the block is gone
        var house = Assert.Single(m.Buildings);
        Assert.True(house.Footprint.All(v => v.X >= 69.9f && v.X <= 75.1f && v.Y >= 9.9f && v.Y <= 14.5f), "on the shed's footprint");
        Assert.NotEmpty(m.Doors);                                                // ≥ 1 door
        Assert.Contains(m.Panels, p => p.Openings.Any(o => o.Kind == TerrainOpeningKind.Window));
        Assert.Contains(m.Walkables, w => w.Building == 0);                      // its floor
    }

    [Fact]
    public void ADoorlessHouse_GivenADoorHint_HasItsDoorThere()
    {
        var b = World(Doorless);
        Assert.Empty(b.Doors);
        var hint = new Vector2(30f, 26f);                                         // on the NORTH wall (y 26), 10 m along x — not the longest-wall middle of the south one
        var m = Edited(b, "door", "T", hint);
        var door = Assert.Single(m.Doors);
        Assert.True(Vector2.Distance(door.Center, hint) < 0.8f, $"the door at {door.Center}, the hint {hint}");
    }
}
