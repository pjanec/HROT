using Hrot.Terrain.InputSet;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Random;

/// <summary>
/// ⭐ CE-6001 (docs/DESIGN_Terrain_Authoring.md §18l) — PURE: a <see cref="TownPlan"/> as a <see cref="SynthGround"/>, the type the random pipeline's own writer already takes. The relief is the plan's plain plus its hills
/// (<see cref="HeightField.FromFunction"/>), the roads its polylines (<see cref="RoadNetwork.FromPolylines"/>), the water its rivers (a centre line smoothed and buffered into one polygon each); no buildings, trees or scrub (the
/// houses are recipe blocks, the woods are the recipes' own). The same plan is the same ground, byte for byte. The ground lives in the synth's frame (centred on 0,0): <see cref="ToSynth"/> converts a terrain-local point.
/// </summary>
public static class TownPlanGround
{
    public static SynthOptions OptionsOf(TownPlan plan, string outDir) =>
        new("full", plan.Seed, outDir, plan.SizeMetres, plan.HeightSpacing, 0, 0, Extras: true, GroundOnly: true, ZRange: 0.0, WaterMode: "none", RoadScale: 0.0);

    /// <summary>A terrain-local point (south-west corner 0,0) in the ground's centred frame.</summary>
    public static (double X, double Y) ToSynth(TownPlan plan, double x, double y) => (x - plan.SizeMetres / 2, y - plan.SizeMetres / 2);

    public static SynthGround Build(TownPlan plan)
    {
        var p = OptionsOf(plan, "").Resolve();
        double dx = p.MinX, dy = p.MinY;   // local → synth: add the corner

        double Relief(double x, double y)
        {
            double h = plan.PlainHeight;
            foreach (var hill in plan.Hills)
            {
                double u = (x - dx - hill.X) / hill.Rx, v = (y - dy - hill.Y) / hill.Ry;
                h += hill.Height * Math.Exp(-(u * u + v * v) * 2.0);
            }
            return h;
        }
        var heights = HeightField.FromFunction(p with { ZMin = 0, ZMax = Math.Max(1, plan.PlainHeight + plan.Hills.Sum(h => h.Height)) }, Relief);

        var polylines = plan.Roads.Select(r => (Enum.Parse<RoadClass>(r.Class, true), (IReadOnlyList<(double X, double Y)>)r.Points.Select(q => (q[0] + dx, q[1] + dy)).ToList())).ToList();
        var roads = RoadNetwork.FromPolylines(p, polylines);

        var water = new WaterField(p, plan.Rivers.Select(r => new AreaFeature(River(r, dx, dy), "river")).ToList());
        return new SynthGround(p, heights, roads, water, Array.Empty<Building>(), Array.Empty<Opening>(),
            new TreeResult(Array.Empty<(double, double, double)>(), Array.Empty<AreaFeature>(), null), Array.Empty<AreaFeature>());
    }

    /// <summary>The scenario DOM of the plan: its blocks (as the random layout writes them) and its bridges.</summary>
    public static System.Text.Json.Nodes.JsonObject ScenarioDom(TownPlan plan, string groundName)
    {
        var blocks = plan.Blocks.Select((b, i) => new LayoutBlock(BlockLayout.GuidOf(plan.Seed, i), b.Recipe, b.Seed, (float)b.HeadingDeg, b.X + (-plan.SizeMetres / 2), b.Y + (-plan.SizeMetres / 2), 0f)).ToList();
        var dom = BlockLayout.ToScenarioDom(blocks, groundName, -plan.SizeMetres / 2, -plan.SizeMetres / 2);
        var entities = (System.Text.Json.Nodes.JsonObject)dom["entities"]!;
        foreach (var (guid, entity) in plan.BridgeEntities()) entities[guid.ToString("D")] = entity;
        return dom;
    }

    // a river's centre line, smoothed (Catmull-Rom through its points, a sample every ≈ 12 m) and buffered by half its width into one closed polygon (left bank forward, right bank back)
    internal static List<(double X, double Y)> River(TownRiver r, double dx, double dy)
    {
        var pts = r.Points.Select(q => (X: q[0] + dx, Y: q[1] + dy)).ToList();
        var line = new List<(double X, double Y)>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Math.Max(i - 1, 0)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(i + 2, pts.Count - 1)];
            double len = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
            int n = Math.Max(1, (int)Math.Ceiling(len / 12.0));
            for (int k = 0; k < n; k++)
            {
                double t = (double)k / n, t2 = t * t, t3 = t2 * t;
                double X = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double Y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                line.Add((X, Y));
            }
        }
        line.Add(pts[^1]);
        double half = r.Width / 2;
        var left = new List<(double X, double Y)>(); var right = new List<(double X, double Y)>();
        for (int i = 0; i < line.Count; i++)
        {
            var a = line[Math.Max(i - 1, 0)]; var b = line[Math.Min(i + 1, line.Count - 1)];
            double tx = b.X - a.X, ty = b.Y - a.Y, l = Math.Sqrt(tx * tx + ty * ty);
            if (l < 1e-9) { tx = 0; ty = 1; l = 1; }
            double nx = -ty / l, ny = tx / l;
            left.Add((Math.Round(line[i].X + nx * half, 2), Math.Round(line[i].Y + ny * half, 2)));
            right.Add((Math.Round(line[i].X - nx * half, 2), Math.Round(line[i].Y - ny * half, 2)));
        }
        right.Reverse();
        var ring = new List<(double X, double Y)>(left);
        ring.AddRange(right);
        double area = 0;
        for (int i = 0; i < ring.Count; i++) { var u = ring[i]; var v = ring[(i + 1) % ring.Count]; area += u.X * v.Y - v.X * u.Y; }
        if (area < 0) ring.Reverse();   // the input set wants a counter-clockwise ring
        ring.Add(ring[0]);
        return ring;
    }
}
