using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Random;

/// <summary>One block the layout placed: its stable guid, the recipe, the seed it expands with, the heading and the centre in SYNTH metres (the ground's own frame, centred on 0,0).</summary>
public sealed record LayoutBlock(Guid Guid, string Recipe, uint Seed, float HeadingDeg, double X, double Y, float Radius);

/// <summary>
/// ⭐ CE-4428 F-3 (docs/DESIGN_Terrain_Authoring.md §18f) — PURE: where the recipe blocks of a preset go over a generated ground. At each synth <see cref="Village"/> it places N blocks from the preset's recipe mix,
/// each ALONG one of the nearest roads (its centre a block radius off the road's edge, its heading along the road), off roads, water and the other blocks. Everything comes from the layout seed — guids included, never
/// <see cref="Guid.NewGuid"/> — so the same inputs are the same layout, byte for byte. Plain terrain places nothing.
/// ⚠ The houses of a block are placed by the expander, which knows base buildings and water but NOT roads (<c>AlignRoad</c> is unbuilt): the block's CENTRE and disc keep off the roads, a house inside the disc may still
/// meet a street where a village's own grid runs through it.
/// </summary>
public static class BlockLayout
{
    private readonly record struct Mix(int MinPerVillage, int MaxPerVillage, double ExtraPerKm2, (string Recipe, double Weight)[] Recipes);

    private static Mix MixOf(TerrainPreset p) => p switch
    {
        TerrainPreset.Plain => new Mix(0, 0, 0, Array.Empty<(string, double)>()),
        TerrainPreset.OpenCountryside => new Mix(1, 3, 2.0, new[] { ("farmstead", 0.6), ("copse", 0.4) }),
        TerrainPreset.Villages => new Mix(2, 4, 0, new[] { ("hamlet", 0.8), ("copse", 0.2) }),
        TerrainPreset.SmallTown => new Mix(4, 7, 0, new[] { ("hamlet", 0.9), ("farmstead", 0.1) }),
        TerrainPreset.TownWithSuburbs => new Mix(7, 11, 0, new[] { ("hamlet", 0.85), ("farmstead", 0.15) }),
        _ => new Mix(3, 6, 1.0, new[] { ("hamlet", 0.5), ("farmstead", 0.3), ("copse", 0.2) }),
    };

    private const int AttemptsPerBlock = 60;
    private const double RoadMargin = 3.0;   // m between a block's centre clearance and a road's edge

    /// <summary>How far a block's CENTRE stays from every road's edge (m).</summary>
    public static double Clearance(float radius) => 0.3 * radius + RoadMargin;

    /// <param name="radiusOf">a recipe id → its block radius (m); the houses and woods stay inside it</param>
    public static IReadOnlyList<LayoutBlock> Place(TerrainPreset preset, SynthGround ground, ulong layoutSeed, Func<string, float> radiusOf)
    {
        var mix = MixOf(preset);
        var blocks = new List<LayoutBlock>();
        if (mix.Recipes.Length == 0) return blocks;

        var p = ground.Profile;
        var roads = ground.Roads;
        var water = ground.Water;
        var nodes = roads.Nodes.ToDictionary(n => n.Id);
        // the roads blocks stand along: not the motorway or the railway
        var edges = roads.Edges.Where(e => e.Class != RoadClass.Motorway && e.Class != RoadClass.Rail && e.Class != RoadClass.Trunk).Select(e => (E: e, A: nodes[e.From], B: nodes[e.To])).ToList();
        if (edges.Count == 0) return blocks;

        var rng = new Rng(layoutSeed);
        double total = mix.Recipes.Sum(r => r.Weight);
        string PickRecipe(Rng r)
        {
            double t = r.NextDouble() * total;
            foreach (var (recipe, w) in mix.Recipes) { if ((t -= w) < 0) return recipe; }
            return mix.Recipes[^1].Recipe;
        }

        bool Free(double x, double y, float radius)
        {
            if (x - radius < p.MinX || x + radius > p.MaxX || y - radius < p.MinY || y + radius > p.MaxY) return false;
            if (roads.DistanceToRoad(x, y) < Clearance(radius)) return false;   // the centre keeps a block-radius-proportional clearance from every road
            if (water != null)
            {
                if (water.Near(x, y)) return false;
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4;
                    if (water.Near(x + 0.6 * radius * Math.Cos(a), y + 0.6 * radius * Math.Sin(a))) return false;
                }
            }
            foreach (var b in blocks)
            {
                double d = Math.Sqrt((b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y));
                if (d < 0.8 * (b.Radius + radius)) return false;
            }
            return true;
        }

        // one block: a random one of the nearest roads to (cx, cy), a point along it, a side, a radius off its edge
        bool TryPlace(Rng r, double cx, double cy, double reach, string recipe)
        {
            float radius = radiusOf(recipe);
            var near = edges.Select(e => (e, d: MidDistance(e.A, e.B, cx, cy))).Where(t => t.d <= reach).OrderBy(t => t.d).Take(8).ToList();
            if (near.Count == 0) return false;
            for (int attempt = 0; attempt < AttemptsPerBlock; attempt++)
            {
                var (e, _) = near[r.Int(0, near.Count)];
                double t = r.Range(0.1, 0.9);
                double ax = e.A.X, ay = e.A.Y, bx = e.B.X, by = e.B.Y;
                double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;
                double nx = -dy / len, ny = dx / len;
                double side = r.NextDouble() < 0.5 ? -1 : 1;
                double off = e.E.WidthM / 2 + Clearance(radius) + radius * r.Range(0.0, 0.3);
                double x = ax + dx * t + side * nx * off, y = ay + dy * t + side * ny * off;
                if (!Free(x, y, radius)) continue;
                double heading = Math.Round(Math.Atan2(dy, dx) * 180 / Math.PI, 1);
                blocks.Add(new LayoutBlock(GuidOf(layoutSeed, blocks.Count), recipe, (uint)(r.NextU64() & 0xFFFFFFFFUL), (float)heading, Math.Round(x, 2), Math.Round(y, 2), radius));
                return true;
            }
            return false;
        }

        // 1. at each village, N blocks along its nearest roads
        for (int v = 0; v < roads.Villages.Count; v++)
        {
            var village = roads.Villages[v];
            var vr = rng.Fork("layout.village." + v);
            int n = vr.Int(mix.MinPerVillage, mix.MaxPerVillage + 1);
            for (int i = 0; i < n; i++) TryPlace(vr, village.X, village.Y, village.Radius + 80, PickRecipe(vr));
        }
        // 2. the countryside: a few more along any road, anywhere (Open countryside, Mixed)
        double km2 = p.Width * p.Height / 1e6;
        int extra = (int)Math.Round(mix.ExtraPerKm2 * km2);
        var er = rng.Fork("layout.extra");
        for (int i = 0; i < extra; i++)
        {
            double cx = er.Range(p.MinX, p.MaxX), cy = er.Range(p.MinY, p.MaxY);
            TryPlace(er, cx, cy, 200, PickRecipe(er));
        }
        return blocks;
    }

    private static double MidDistance(RoadNode a, RoadNode b, double x, double y)
    {
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        return Math.Sqrt((mx - x) * (mx - x) + (my - y) * (my - y));
    }

    /// <summary>A guid derived from the layout seed and the block's index — deterministic, shaped like a v4 guid.</summary>
    public static Guid GuidOf(ulong layoutSeed, int index)
    {
        var r = new Rng(layoutSeed).Fork("layout.guid." + index);
        ulong a = r.NextU64(), b = r.NextU64();
        var bytes = new byte[16];
        BitConverter.GetBytes(a).CopyTo(bytes, 0); BitConverter.GetBytes(b).CopyTo(bytes, 8);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>
    /// The scenario DOM of a layout: <c>header.terrainName</c> and one entity per block with <c>TerrainBlock</c> and <c>SimTransform.Position</c> — the JSON the overlay compiler reads (<c>OverlayCompiler</c>). The
    /// positions are the TERRAIN's local metres (the south-west corner of the ground is 0,0, as the importer makes it), so the blocks stand where the preview drew them. Entities are ordered by guid: the same layout is the
    /// same bytes.
    /// </summary>
    public static JsonObject ToScenarioDom(IReadOnlyList<LayoutBlock> blocks, string terrainName, double cornerX, double cornerY)
    {
        var entities = new JsonObject();
        foreach (var b in blocks.OrderBy(b => b.Guid.ToString("D"), StringComparer.Ordinal))
        {
            entities[b.Guid.ToString("D")] = new JsonObject
            {
                ["TerrainBlock"] = new JsonObject { ["Recipe"] = b.Recipe, ["Seed"] = b.Seed, ["HeadingDeg"] = b.HeadingDeg, ["Fit"] = Fdp.Toolkit.Terrain.TerrainBlock.FitAll },
                ["SimTransform"] = new JsonObject
                {
                    ["Position"] = new JsonArray(Math.Round(b.X - cornerX, 2), Math.Round(b.Y - cornerY, 2), 0),
                    ["Rotation"] = new JsonArray(0, 0, 0, 1),
                },
            };
        }
        return new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
            ["header"] = new JsonObject { ["subsystemType"] = "Hrot.Scenario", ["schemaVersion"] = "1.0", ["terrainName"] = terrainName },
            ["entities"] = entities,
        };
    }

    /// <summary>The DOM as the bytes written to <c>scenario.json</c> (indented, one canonical form).</summary>
    public static string Serialize(JsonObject dom) => dom.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
