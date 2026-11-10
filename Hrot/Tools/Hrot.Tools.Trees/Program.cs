// hrot-trees — CE-4369 (L-7, docs/DESIGN_Terrain_World.md §11 W-7, UTM §9.6): tree points → woods polygons + lone trees.
//
//   hrot-trees --trees trees.csv --offset X Y --size W H [--roads roads.json] --out trees.features.json
//
// trees.csv: "x,y,…" in the input set's frame (a header line); --offset is subtracted (the bounds' south-west corner ⇒ terrain-local metres);
// the terrain spans 0..W × 0..H. roads.json: [[ax, ay, bx, by, halfWidth], …] in local metres — the woods leave these open.
// Writes a JSON {"features": [...]}: forest surfaces (grid-aligned rectangles with "density") and "tree" points. Deterministic.
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Fdp.Toolkit.Terrain;

string? treesPath = null, roadsPath = null, outPath = null; float ox = 0, oy = 0, w = 0, h = 0;
static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--trees": treesPath = args[++i]; break;
        case "--roads": roadsPath = args[++i]; break;
        case "--out": outPath = args[++i]; break;
        case "--offset": ox = F(args[++i]); oy = F(args[++i]); break;
        case "--size": w = F(args[++i]); h = F(args[++i]); break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}"); return 2;
    }
}
if (treesPath == null || outPath == null || !(w > 0) || !(h > 0))
{ Console.Error.WriteLine("usage: hrot-trees --trees CSV --offset X Y --size W H [--roads JSON] --out FILE"); return 2; }

var sw = System.Diagnostics.Stopwatch.StartNew();
var pts = new List<Vector2>();
foreach (var line in File.ReadLines(treesPath).Skip(1))
{
    var p = line.Split(',');
    if (p.Length < 2) continue;
    var v = new Vector2(F(p[0]) - ox, F(p[1]) - oy);
    if (v.X >= 0 && v.Y >= 0 && v.X <= w && v.Y <= h) pts.Add(v);
}
var roads = new List<TerrainWoods.RoadSegment>();
if (roadsPath != null)
    foreach (var s in JsonDocument.Parse(File.ReadAllText(roadsPath)).RootElement.EnumerateArray())
        roads.Add(new TerrainWoods.RoadSegment(new Vector2(s[0].GetSingle(), s[1].GetSingle()), new Vector2(s[2].GetSingle(), s[3].GetSingle()), s[4].GetSingle()));

var r = TerrainWoods.Cluster(pts.ToArray(), Vector2.Zero, new Vector2(w, h), roads);

static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
var sb = new StringBuilder("{\"features\":[\n");
bool first = true;
void Add(string f) { if (!first) sb.Append(",\n"); sb.Append(f); first = false; }
int wi = 0;
foreach (var wr in r.Woods)
    Add($"{{\"type\":\"Feature\",\"properties\":{{\"kind\":\"surface\",\"surface\":\"forest\",\"label\":\"wood {wi++}\",\"density\":{N(wr.Density)}}}," +
        $"\"geometry\":{{\"type\":\"Polygon\",\"coordinates\":[[[{N(wr.Min.X)},{N(wr.Min.Y)}],[{N(wr.Max.X)},{N(wr.Min.Y)}],[{N(wr.Max.X)},{N(wr.Max.Y)}],[{N(wr.Min.X)},{N(wr.Max.Y)}],[{N(wr.Min.X)},{N(wr.Min.Y)}]]]}}}}");
foreach (int i in r.LoneTrees)
    Add($"{{\"type\":\"Feature\",\"properties\":{{\"kind\":\"tree\"}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{N(pts[i].X)},{N(pts[i].Y)}]}}}}");
sb.Append("\n]}\n");
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"trees: {pts.Count} → {r.Woods.Count} woods polygons ({r.WoodCells} cells, {r.WoodCells * 0.0625:F0} ha, {r.WoodTrees} trees), " +
                  $"{r.LoneTrees.Count} lone trees; {r.ClearedCells} cells left open for {roads.Count} road pieces ({r.DroppedTrees} trees dropped) — {sw.ElapsedMilliseconds} ms");
return 0;
