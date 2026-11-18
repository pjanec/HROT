// CE-4651 (UL-14) — hrot-roadscene: a terrain's SUMO road scene as mesh tiles for the Unreal image generator.
//
//   hrot-roadscene <terrain folder> <out folder> [--tile 500] [--lift 0.08]
//
// The scene is HROT's OWN: SumoNetTerrainLoader.Prepare — the call the editor makes when it loads the terrain — parses the net, places it
// (SumoNetPlacement) and builds TrafficRoadScene (SumoSharp's Sim.Scene: lane ribbons, junction fills, lane markings, crosswalks; the
// sidewalks split out). Nothing here re-derives road geometry. Each mesh is laid on OUR ground (TerrainWorld.GroundHeightAt) plus a
// small lift per kind (markings above asphalt above the ground) so the Unreal landscape — exactly our ground — never covers it.
//
// Output, per kind and per tile:  <name>.road_<kind>_<tx>_<ty>.bin  (HrotTerrain.BuildMesh: int32 vertices, int32 triangles,
// float32 xyz in Unreal cm relative to the tile origin, int32 a b c, uint8 slot 0)  +  <name>.roads-mesh.json (the tiles, their origins).
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Fdp.Toolkit.Terrain;
using Hrot.Traffic;
using Sim.Scene;

static int Fail(string msg) { Console.Error.WriteLine(msg); return 1; }

if (args.Length < 2) return Fail("usage: hrot-roadscene <terrain folder> <out folder> [--tile 500] [--lift 0.08]");
string folder = Path.GetFullPath(args[0]), outDir = Path.GetFullPath(args[1]);
double tile = 500, lift = 0.08;
for (int i = 2; i + 1 < args.Length; i += 2)
{
    if (args[i] == "--tile") tile = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
    else if (args[i] == "--lift") lift = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
}
string defPath = Path.Combine(folder, "terrain.json");
var definition = TerrainFolderWorld.ReadDefinition(defPath);
string name = definition.Name ?? Path.GetFileName(folder);
var world = TerrainFolderWorld.ReadWorld(defPath, definition, name);
if (world is null) return Fail($"{folder}: no world");
var sw = System.Diagnostics.Stopwatch.StartNew();
if (new SumoNetTerrainLoader().Prepare(definition, folder, name, world, null) is not TrafficNetCache cache) return Fail($"{folder}: the terrain declares no SUMO net");
Console.WriteLine($"road scene built in {sw.ElapsedMilliseconds} ms: {cache.Roads.TriangleCount} triangles");
Directory.CreateDirectory(outDir);

// the lift per kind (m above our ground): the paint sits above the surface it is painted on
static double KindLift(TrafficRoadMesh m, double l) => m.Kind switch
{
    SceneKind.LaneMarking or SceneKind.Crosswalk => l + 0.03,
    SceneKind.Junction => l + 0.01,
    _ => m.Sidewalk ? l + 0.02 : l,
};
static string KindName(TrafficRoadMesh m) => m.Sidewalk ? "Sidewalk" : m.Kind.ToString();

var tiles = new List<object>();
var counts = new Dictionary<string, int>();
foreach (var mesh in cache.Roads.Meshes)
{
    if (mesh.Kind == SceneKind.AmbientGround) continue;   // the floor beyond our footprint: Unreal has OUR landscape
    string kind = KindName(mesh);
    double l = KindLift(mesh, lift);
    // triangles by the tile of their centroid
    var byTile = new Dictionary<(int, int), List<int>>();
    for (int t = 0; t < mesh.Indices.Length; t += 3)
    {
        Vector3 a = mesh.Vertices[mesh.Indices[t]], b = mesh.Vertices[mesh.Indices[t + 1]], c = mesh.Vertices[mesh.Indices[t + 2]];
        var key = ((int)Math.Floor(((a.X + b.X + c.X) / 3 - world.BoundsMin.X) / tile), (int)Math.Floor(((a.Y + b.Y + c.Y) / 3 - world.BoundsMin.Y) / tile));
        if (!byTile.TryGetValue(key, out var list)) byTile[key] = list = new List<int>();
        list.Add(t);
    }
    foreach (var ((tx, ty), tris) in byTile)
    {
        double ox = world.BoundsMin.X + tx * tile, oy = world.BoundsMin.Y + ty * tile, oz = double.MaxValue;
        var remap = new Dictionary<int, int>();
        var verts = new List<(double X, double Y, double Z)>();
        var idx = new List<int>();
        foreach (int t in tris)
            for (int k = 0; k < 3; k++)
            {
                int vi = mesh.Indices[t + k];
                if (!remap.TryGetValue(vi, out int ni))
                {
                    var v = mesh.Vertices[vi];
                    double z = world.GroundHeightAt(v.X, v.Y) + l;
                    remap[vi] = ni = verts.Count; verts.Add((v.X, v.Y, z)); oz = Math.Min(oz, z);
                }
                idx.Add(ni);
            }
        string file = $"{name}.road_{kind}_{tx}_{ty}.bin";
        using (var bw = new BinaryWriter(File.Create(Path.Combine(outDir, file))))
        {
            bw.Write(verts.Count); bw.Write(idx.Count / 3);
            foreach (var (X, Y, Z) in verts) { bw.Write((float)((X - ox) * 100.0)); bw.Write((float)(-(Y - oy) * 100.0)); bw.Write((float)((Z - oz) * 100.0)); }
            // Sim.Scene winds counter-clockwise seen from above in a right-handed (x east, y north) frame; Unreal's Y points south
            // (mirrored) ⇒ the order is reversed so the faces stay up-facing there
            for (int t = 0; t < idx.Count; t += 3) { bw.Write(idx[t]); bw.Write(idx[t + 2]); bw.Write(idx[t + 1]); }
            for (int t = 0; t < idx.Count / 3; t++) bw.Write((byte)0);
        }
        tiles.Add(new { file, kind, tile = new[] { tx, ty }, locationCm = new[] { ox * 100.0, -oy * 100.0, oz * 100.0 }, triangles = idx.Count / 3 });
        counts[kind] = counts.GetValueOrDefault(kind) + idx.Count / 3;
    }
}
File.WriteAllText(Path.Combine(outDir, $"{name}.roads-mesh.json"),
    JsonSerializer.Serialize(new { terrain = name, tileMetres = tile, lift, triangles = counts, tiles }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"wrote {tiles.Count} tiles: " + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
return 0;
