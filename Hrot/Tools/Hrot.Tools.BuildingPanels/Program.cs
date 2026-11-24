// CE-11052 — hrot-building-panels: a terrain's wall panels and openings, in the order HROT's damage records index them.
//
//   hrot-building-panels <terrain folder> <out.json>
//
// The world is HROT's OWN: TerrainFolderWorld.ReadWorld — the one folder -> world step the node's load (TerrainResidency.Prepare) and the importer's navmesh prefill
// use — and the dump is BuildingPanelDump (BuildingPanelIndex rank order, the key BuildingDamages.KeyOf). Nothing here re-derives the wall split.
// The base terrain only: a scenario's overlay (its own blocks / features) is not merged. No simulation, no network.
using Fdp.Toolkit.Terrain;

static int Fail(string msg) { Console.Error.WriteLine(msg); return 1; }

if (args.Length != 2) return Fail("usage: hrot-building-panels <terrain folder> <out.json>");
string folder = Path.GetFullPath(args[0]), outPath = Path.GetFullPath(args[1]);
string defPath = Path.Combine(folder, "terrain.json");
if (!File.Exists(defPath)) return Fail($"{folder}: no terrain.json");
var sw = System.Diagnostics.Stopwatch.StartNew();
var definition = TerrainFolderWorld.ReadDefinition(defPath);
string name = definition.Name ?? Path.GetFileName(folder);
var world = TerrainFolderWorld.ReadWorld(defPath, definition, name);
if (world is null) return Fail($"{folder}: the terrain declares no world");
long loaded = sw.ElapsedMilliseconds;
string? dir = Path.GetDirectoryName(outPath);
if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
using (var fs = File.Create(outPath)) BuildingPanelDump.Write(world, fs);
int groups = WallGroups.Count(world);
Console.WriteLine($"{name}: {world.Buildings.Count} buildings, {groups - world.Buildings.Count} wall lines, {world.Panels.Count} panels; loaded in {loaded} ms, written in {sw.ElapsedMilliseconds - loaded} ms -> {outPath}");
return 0;
