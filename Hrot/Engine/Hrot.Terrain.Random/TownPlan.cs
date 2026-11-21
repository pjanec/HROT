using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Random;

/// <summary>A hill: an elliptical Gaussian bump (terrain-local metres; <see cref="Height"/> above the plain at the centre, the half-axes where it has fallen to ≈ 14 %).</summary>
public sealed record TownHill(double X, double Y, double Rx, double Ry, double Height);

/// <summary>A river: a centre line (terrain-local metres, running off the terrain at both ends) and its width; it becomes ONE water polygon in the ground.</summary>
public sealed record TownRiver(double Width, double[][] Points);

/// <summary>A road: a class (<c>secondary</c>, <c>primary</c>, <c>residential</c> …) and a centre line, each segment one edge.</summary>
public sealed record TownRoad(string Class, double[][] Points);

/// <summary>A recipe block put exactly here: the recipe id, its centre, heading and seed (the block's guid is derived from the plan seed and its index).</summary>
public sealed record TownBlock(string Recipe, double X, double Y, double HeadingDeg, uint Seed);

/// <summary>A bridge from one bank to the other (a Bridge terrain feature: the deck 1.5 m up, a ramp at each end) of the given width.</summary>
public sealed record TownBridge(double[] From, double[] To, double Width);

/// <summary>⭐ CE-6020 — an area of the plan by KIND (a polygon, terrain-local metres): the built-up kinds say how the streets through it are lined (<c>old-town</c> terraced, <c>c19</c> close rows, <c>family</c> detached,
/// <c>industry</c> halls); <c>estate</c> / <c>sports</c> / <c>park</c> are built-up but not lined; <c>wood</c> and <c>water</c> are ground features.</summary>
public sealed record TownArea(string Kind, double[][] Points)
{
    public static readonly string[] Kinds = { "old-town", "c19", "family", "industry", "estate", "sports", "park", "wood", "water" };
}

/// <summary>⭐ CE-6020 — a slab building (a block of flats, an industrial hall) where the map has one: its centre, the heading of its long side, length × depth and storeys; <c>Use</c> is <c>flats</c> or <c>industry</c>.</summary>
public sealed record TownSlab(double X, double Y, double HeadingDeg, double Length, double Depth, int Storeys, string? Use = null);

/// <summary>⭐ CE-6020 — a named landmark building (<c>church</c>, <c>town-hall</c>, <c>chateau</c>) at its place.</summary>
public sealed record TownLandmark(string Kind, string Name, double X, double Y, double HeadingDeg);

/// <summary>⭐ CE-6020 — the market square: its polygon (kept clear of the street frontage; the town hall stands on it).</summary>
public sealed record TownSquare(string Name, double[][] Points);

/// <summary>⭐ CE-6020 — a name drawn on the plan's sketch (a street, a quarter); not terrain.</summary>
public sealed record TownLabel(string Text, double X, double Y, bool Big = false);

/// <summary>
/// ⭐ CE-6001 (showcase world, docs/DESIGN_Terrain_Authoring.md §18l) — an AUTHORED TOWN PLAN: the one committed, re-runnable input that makes a hand-laid terrain with the EXISTING generator library (the ground by <c>Hrot.Terrain.Synth</c>, the
/// houses by recipe blocks, the bridges by Bridge features, one <c>TerrainBake.SaveAs</c>). It replaces what the random pipeline decides by seed (the river's course, the roads, where each block goes) with what the plan says, and nothing else:
/// every number is terrain-local metres (the south-west corner is 0,0, x east, y north). Pure data; <see cref="TownPlanGround"/> turns it into a ground, <see cref="TownPlanGenerator"/> into a terrain.
/// <para>⭐ CE-6020 (the traced town, §18l): <see cref="Areas"/>, <see cref="Slabs"/>, <see cref="Landmarks"/>, <see cref="Square"/>, <see cref="Labels"/> and <see cref="SizeYMetres"/> (a plan that is not square: <see cref="SizeMetres"/> is then west–east) are optional — a plan without them is the CE-6001 plan.</para>
/// </summary>
public sealed record TownPlan(string Name, double SizeMetres, double HeightSpacing, ulong Seed, double PlainHeight, TownHill[] Hills, TownRiver[] Rivers, TownRoad[] Roads, TownBlock[] Blocks, TownBridge[] Bridges,
                              TownArea[]? Areas = null, TownSlab[]? Slabs = null, TownLandmark[]? Landmarks = null, TownSquare? Square = null, TownLabel[]? Labels = null, double? SizeYMetres = null)
{
    /// <summary>⭐ CE-6020 — the north–south extent: <see cref="SizeYMetres"/> when the plan is not square, else <see cref="SizeMetres"/> (which is then the west–east extent).</summary>
    public double ExtentY => SizeYMetres ?? SizeMetres;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static TownPlan Load(string path) => Parse(File.ReadAllText(path));

    public static TownPlan Parse(string json)
    {
        var plan = JsonSerializer.Deserialize<TownPlan>(json, Json) ?? throw new InvalidDataException("the town plan is empty");
        if (string.IsNullOrWhiteSpace(plan.Name) || plan.Name.IndexOfAny(new[] { '/', '\\' }) >= 0) throw new InvalidDataException($"'{plan.Name}' is not a terrain name");
        if (!double.IsFinite(plan.SizeMetres) || plan.SizeMetres < 200 || plan.SizeMetres > 6000) throw new InvalidDataException($"the size must be 200 … 6 000 m, not {plan.SizeMetres}");
        if (!double.IsFinite(plan.ExtentY) || plan.ExtentY < 200 || plan.ExtentY > 6000) throw new InvalidDataException($"the north–south size must be 200 … 6 000 m, not {plan.ExtentY}");
        if (plan.HeightSpacing <= 0) throw new InvalidDataException("heightSpacing must be positive");
        foreach (var r in plan.Roads) if (r.Points.Length < 2 || !Enum.TryParse<RoadClass>(r.Class, true, out _)) throw new InvalidDataException($"a road needs ≥ 2 points and a known class, not '{r.Class}'");
        foreach (var r in plan.Rivers) if (r.Points.Length < 2 || r.Width <= 0) throw new InvalidDataException("a river needs ≥ 2 points and a width");
        foreach (var b in plan.Blocks) if (b.X < 0 || b.Y < 0 || b.X > plan.SizeMetres || b.Y > plan.ExtentY) throw new InvalidDataException($"block '{b.Recipe}' at ({b.X}, {b.Y}) is off the terrain");
        foreach (var a in plan.Areas ?? Array.Empty<TownArea>())
            if (a.Points.Length < 3 || Array.IndexOf(TownArea.Kinds, a.Kind) < 0) throw new InvalidDataException($"an area needs ≥ 3 points and a known kind ({string.Join(", ", TownArea.Kinds)}), not '{a.Kind}'");
        foreach (var s in plan.Slabs ?? Array.Empty<TownSlab>())
            if (s.Length <= 0 || s.Depth <= 0 || s.Storeys < 1) throw new InvalidDataException($"a slab at ({s.X}, {s.Y}) needs a length, a depth and ≥ 1 storey");
        if (plan.Square is { } sq && sq.Points.Length < 3) throw new InvalidDataException("the square needs ≥ 3 points");
        return plan;
    }

    /// <summary>The plan's JSON in its one canonical form (what the generator script writes).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(Json) { WriteIndented = true });

    /// <summary>The bridges as scenario entities (Bridge feature kind 10: the entity stands at the first bank, its polyline runs to the second, <c>Pad</c> is the width) — terrain-local metres.</summary>
    public IEnumerable<(Guid Guid, JsonObject Entity)> BridgeEntities()
    {
        for (int i = 0; i < Bridges.Length; i++)
        {
            var b = Bridges[i];
            double dx = b.To[0] - b.From[0], dy = b.To[1] - b.From[1];
            yield return (BlockLayout.GuidOf(Seed ^ 0xB81D6EUL, i), new JsonObject
            {
                ["TerrainFeature"] = new JsonObject { ["Kind"] = 10, ["Template"] = "", ["HeadingDeg"] = 0f, ["Pad"] = b.Width, ["Seed"] = 0 },
                ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(Math.Round(b.From[0], 2), Math.Round(b.From[1], 2), 0), ["Rotation"] = new JsonArray(0, 0, 0, 1) },
                ["EditablePolyline"] = new JsonObject
                {
                    ["Points"] = new JsonArray(new JsonObject { ["X"] = 0, ["Y"] = 0 }, new JsonObject { ["X"] = Math.Round(dx, 2), ["Y"] = Math.Round(dy, 2) }),
                    ["Version"] = 0,
                },
            });
        }
    }
}
