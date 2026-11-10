using System.Numerics;
using System.Text.Json;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm.Tests;
using Xunit;

namespace Hrot.Terrain.Import.Tests;

public class ImporterTests : IDisposable
{
    private readonly Env _env = new();
    public void Dispose() => _env.Dispose();
    private static Opening Door(double x, double y, int s = 0, double heading = 0) => new("door", x, y, 10 + 3 * s, heading, s);
    private static Opening Window(double x, double y, int s = 0, double heading = 0) => new("window", x, y, 11 + 3 * s, heading, s);

    private async Task<(string Folder, TerrainWorld World, ImportReport Rep)> Import(MiniSet mini, string name = "mini", string? dir = null)
    {
        string set = mini.Write(dir ?? Path.Combine(_env.Root, "set-" + Guid.NewGuid().ToString("N")[..6]));
        var imp = _env.Importer();
        string folder = await imp.ImportAsync(set, name, false, null, default);
        return (folder, Env.Load(folder, name), imp.LastReport!);
    }

    private static JsonDocument Template(string folder, string id) => JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "buildings", id + ".building.json")));

    // ---- the strongest check: the terrain LOADS
    [Fact]
    public async Task TheSynthSet_Imports_AndLoadsThroughTheRealTerrainParser()
    {
        string set = _env.Synth();
        var imp = _env.Importer();
        string folder = await imp.ImportAsync(set, "synth", false, null, default);
        var world = Env.Load(folder, "synth");
        var input = InputSetReader.Open(set);
        var rep = imp.LastReport!;
        Assert.Equal(input.ReadBuildings().Count(b => b.Enterable) - rep.SolidBecauseNoStair, world.Buildings.Count(b => !b.Solid));
        Assert.Equal(rep.OpeningsPlaced, world.Panels.Sum(p => p.Openings.Count));
        Assert.NotEmpty(world.Doors);
        Assert.Equal(rep.Stairs, world.Walkables.Count(w => w.Kind == TerrainWalkableKind.Ramp));
        Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Forest);
        Assert.NotNull(world.Height);
        Assert.Equal(new Vector2(0, 0), world.BoundsMin); Assert.Equal(new Vector2((float)(input.Manifest.MaxX - input.Manifest.MinX), (float)(input.Manifest.MaxY - input.Manifest.MinY)), world.BoundsMax);
    }

    [Fact]
    public async Task TheOsmTown_Imports_AndLoads_ThroughTheRealPipelineOutput()
    {
        string set = await _env.OsmFixtureSet();
        var imp = _env.Importer();
        string folder = await imp.ImportAsync(set, "town", false, null, default);
        var world = Env.Load(folder, "town");
        var rep = imp.LastReport!;
        Assert.Equal(11, rep.BuildingsEnterable + rep.SolidBecauseNoStair);
        Assert.Equal(rep.BuildingsEnterable, world.Buildings.Count(b => !b.Solid));
        Assert.Equal(rep.OpeningsPlaced, world.Panels.Sum(p => p.Openings.Count));
        Assert.Equal(4, rep.BuildingsSolid - rep.SolidBecauseNoStair);            // garage, shed, 20 m² yes, ruins (R-4193)
        Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Water);
        Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Forest && s.Density == 115f);
        Assert.Equal(6, world.Prisms.Count(p => p.Kind == TerrainPrismKind.Tree));
        Assert.Equal(5, rep.Walls);
        Assert.Contains("OpenStreetMap contributors", File.ReadAllText(Path.Combine(folder, "town.world.geojson")));
    }

    // ---- every enterable building: a template file and a Point
    [Fact]
    public async Task EveryEnterableBuilding_HasItsTemplateFile_AndAPoint()
    {
        string set = await _env.OsmFixtureSet();
        var imp = _env.Importer();
        string folder = await imp.ImportAsync(set, "town", false, null, default);
        var input = InputSetReader.Open(set).ReadBuildings();
        using var world = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "town.world.geojson")));
        var points = world.RootElement.GetProperty("features").EnumerateArray()
            .Where(f => f.GetProperty("geometry").GetProperty("type").GetString() == "Point" && f.GetProperty("properties").GetProperty("kind").GetString() == "building")
            .ToDictionary(f => f.GetProperty("properties").GetProperty("label").GetString()!, f => f.GetProperty("properties").GetProperty("template").GetString()!);
        Assert.Equal(imp.LastReport!.BuildingsEnterable, points.Count);
        foreach (var (label, template) in points)
        {
            Assert.True(File.Exists(Path.Combine(folder, "buildings", template + ".building.json")), template);
            Assert.Contains(input, b => b.Id == label && b.Enterable);
        }
        // and the solid ones are prisms, with no template file
        Assert.Equal(points.Count, Directory.GetFiles(Path.Combine(folder, "buildings")).Length);
    }

    // ---- terrain-local = input − the bounds' SW corner; the net's anchor = sumoAnchor + the corner
    [Fact]
    public async Task TerrainLocal_IsTheInputMinusTheSouthWestCorner_AndTheNetAnchorFollows()
    {
        var mini = new MiniSet { MinX = -80, MinY = -60, MaxX = 120, MaxY = 140, AnchorX = 5, AnchorY = 7, Net = true };
        mini.Buildings.Add(MiniSet.Rect("b1", -30, -20, -20, -10));
        var (folder, world, _) = await Import(mini);
        var b = world.Buildings.Single();
        Assert.Equal(50f, b.Position.X, 3); Assert.Equal(40f, b.Position.Y, 3);   // (−30 − −80, −20 − −60)
        using var t = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "terrain.json")));
        var anchor = t.RootElement.GetProperty("sumoNet").GetProperty("anchor");
        Assert.Equal(5 - 80, anchor[0].GetDouble()); Assert.Equal(7 - 60, anchor[1].GetDouble());
        Assert.True(File.Exists(Path.Combine(folder, t.RootElement.GetProperty("sumoNet").GetProperty("file").GetString()!)));
        Assert.Equal(200f, world.BoundsMax.X); Assert.Equal(200f, world.BoundsMax.Y);
    }

    [Fact]
    public async Task TerrainJson_CarriesTheGeoOriginAndTheExactFrame_AndTheDefinitionParserTakesIt()
    {
        var mini = new MiniSet();
        var (folder, _, _) = await Import(mini);
        var def = TerrainDefinitionParser.Parse(File.ReadAllText(Path.Combine(folder, "terrain.json")));
        var (lat, lon) = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(500000 - 100, 5430000 - 100, 33, false);
        Assert.Equal(lat, def.Origin!.Value.Lat, 9); Assert.Equal(lon, def.Origin.Value.Lon, 9);
        using var t = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "terrain.json")));
        var utm = t.RootElement.GetProperty("frame").GetProperty("utm");
        Assert.Equal(33, utm.GetProperty("zone").GetInt32()); Assert.Equal("N", utm.GetProperty("hemisphere").GetString());
        Assert.Equal(500000, utm.GetProperty("e0").GetDouble()); Assert.Null(def.SumoNet);   // no roads in this set
    }

    // ---- heights
    [Fact]
    public async Task TheHeightStore_AtAFewPosts_EqualsTheInputGrid()
    {
        string set = _env.Synth();
        var imp = _env.Importer();
        string folder = await imp.ImportAsync(set, "synth", false, null, default);
        var world = Env.Load(folder, "synth");
        var input = InputSetReader.Open(set);
        var (info, z) = HeightsGrid.ReadAll(set);
        foreach (var (c, r) in new[] { (3, 3), (60, 70), (100, 20), (info.Cols - 2, info.Rows - 2), (40, 40) })
        {
            double x = info.OriginX + c * info.CellSize - input.Manifest.MinX, y = info.OriginY + r * info.CellSize - input.Manifest.MinY;
            Assert.Equal(z[r * info.Cols + c], world.Height!.Sample((float)x, (float)y), 2);
        }
        Assert.Equal(1f, world.Height!.CellSize);
    }

    // ---- openings
    [Fact]
    public async Task OpeningsOnWalls_ArePlaced_NoisyOnesWithin06m_TheHeadingIgnored_FarOnesDropped()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 10, 8, openings: new[]
        {
            Door(5, 0.0), Window(2, 0.3, heading: 90), Window(8, -0.5, heading: 270),   // on the south wall: exact, 0.3 off with a WRONG heading, 0.5 off
            Window(10.4, 4),                                                         // east wall, 0.4 off
            Window(10.9, 6),                                                         // 0.9 off: dropped
            Window(5.2, 0.0),                                                        // overlaps the door: dropped
        }));
        var (_, world, rep) = await Import(mini);
        Assert.Equal(4, rep.OpeningsPlaced); Assert.Equal(1, rep.OpeningsDroppedOffWall); Assert.Equal(1, rep.OpeningsDroppedOverlap);
        Assert.Equal(4, world.Panels.Sum(p => p.Openings.Count));
        Assert.Single(world.Doors);
    }

    [Fact]
    public async Task ADoor_WinsOverAnOverlappingWindow_ListedBeforeIt()
    {
        // real sets list windows before doors; the door is the gameplay-critical opening, so it keeps its place
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 10, 8, openings: new[] { Window(5.2, 0.0), Door(5, 0.0) }));
        var (_, world, rep) = await Import(mini);
        Assert.Equal(1, rep.OpeningsPlaced); Assert.Equal(1, rep.OpeningsDroppedOverlap);
        Assert.Single(world.Doors);
    }

    [Fact]
    public async Task ARecessedGroundDoor_UpTo15mInside_SnapsToItsWall_ACourtyardDoorDeeperIn_IsDropped()
    {
        // real sets put a recessed entrance's door point up to ≈ 1.5 m inside the outline; courtyard doors lie deeper, with no outline wall
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 20, 16, storeys: 2, openings: new[]
        {
            Door(5, 1.2),          // ground, 1.2 m in: placed
            Door(10, 8),           // ground, 8 m in (courtyard): dropped
            Door(15, 1.2, s: 1),   // upper storey, 1.2 m in: dropped (the door reach is for the ground floor only)
            Window(12, 1.2),       // a window 1.2 m in: dropped (windows keep 0.6 m)
        }));
        var (_, world, rep) = await Import(mini);
        Assert.Equal(1, rep.OpeningsPlaced); Assert.Equal(3, rep.OpeningsDroppedOffWall);
        Assert.Single(world.Doors);
    }

    [Fact]
    public async Task OpeningsAreMappedPerStorey_OnTheirOwnFloor()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 12, 9, storeys: 2, openings: new[] { Door(6, 0), Window(3, 0, 1), Window(9, 9, 1) }));
        var (_, world, rep) = await Import(mini);
        Assert.Equal(3, rep.OpeningsPlaced);
        Assert.Equal(new[] { 0, 1, 1 }, world.Panels.Where(p => p.Openings.Count > 0).SelectMany(p => p.Openings.Select(_ => p.Storey)).OrderBy(s => s).ToArray());
    }

    // ---- party walls
    [Fact]
    public async Task ASharedEdge_IsOneWall_InExactlyOneTemplate_AndCarriesNoOpenings()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("a", 0, 0, 8, 10, openings: new[] { Door(4, 0), Window(8.0, 5, heading: 90) }));   // a window the input put on the party wall
        mini.Buildings.Add(MiniSet.Rect("b", 8, 0, 16, 10, openings: new[] { Door(12, 0) }));
        var (folder, world, rep) = await Import(mini);
        Assert.Equal(1, rep.SharedWallsMerged);
        int walls = 0;
        foreach (var id in new[] { "a", "b" })
        {
            using var t = Template(folder, id);
            var ox = world.Buildings.Single(b => b.Label == id).Position;
            foreach (var wall in t.RootElement.GetProperty("storeys")[0].GetProperty("walls").EnumerateArray())
            {
                double x0 = wall.GetProperty("from")[0].GetDouble() + ox.X, x1 = wall.GetProperty("to")[0].GetDouble() + ox.X;
                if (Math.Abs(x0 - 108) < 0.05 && Math.Abs(x1 - 108) < 0.05) walls++;   // the shared edge x = 8 (terrain-local 108)
            }
        }
        Assert.Equal(1, walls);
        Assert.Equal(2, rep.OpeningsPlaced);
        Assert.Equal(1, rep.OpeningsDroppedOnPartyWall);
    }

    [Fact]
    public void PartyWalls_MatchEdgesWithin5cm_AndTheFirstIdWritesThem()
    {
        var a = new (double, double)[] { (0, 0), (8, 0), (8, 10), (0, 10) };
        var b = new (double, double)[] { (8.04, 0.03), (16, 0), (16, 10), (8.0, 10.04) };
        var p = new PartyWalls(new[] { ("a", (IReadOnlyList<(double, double)>)a), ("b", (IReadOnlyList<(double, double)>)b) });
        Assert.Equal(1, p.SharedEdges);
        Assert.True(p.IsShared("a", 1) && p.IsShared("b", 3));
        Assert.True(p.Writes("a", 1)); Assert.False(p.Writes("b", 3));
        Assert.True(p.Writes("b", 1));   // an unshared edge is always written
    }

    // ---- stairs
    [Fact]
    public async Task EachStoreyAboveGround_HasAStair_AndTheFloorAboveHasNoHole()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("tower", 0, 0, 16, 12, storeys: 3));
        var (folder, world, rep) = await Import(mini);
        Assert.Equal(0, rep.SolidBecauseNoStair);
        Assert.Equal(2, rep.Stairs);
        Assert.Equal(2, world.Walkables.Count(w => w.Kind == TerrainWalkableKind.Ramp));
        using var t = Template(folder, "tower");
        var storeys = t.RootElement.GetProperty("storeys");
        for (int k = 0; k < 2; k++)
        {
            var st = storeys[k].GetProperty("stairs");
            Assert.Equal(1, st.GetArrayLength());
            Assert.Equal(k + 1, st[0].GetProperty("toStorey").GetInt32());
            // the floor above: ONE ring (no hole), the stairwell is open, the landing beyond it is floor
            var rings = storeys[k + 1].GetProperty("floor");
            Assert.Equal(1, rings.GetArrayLength());
            var ring = rings[0].EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray();
            double fx = st[0].GetProperty("from")[0].GetDouble(), fy = st[0].GetProperty("from")[1].GetDouble(), tx = st[0].GetProperty("to")[0].GetDouble(), ty = st[0].GetProperty("to")[1].GetDouble();
            var mid = new Vector2((float)((fx + tx) / 2), (float)((fy + ty) / 2));
            Assert.False(PolygonMath.Contains(ring, mid), "the stairwell is open in the floor above");
            double len = Math.Sqrt((tx - fx) * (tx - fx) + (ty - fy) * (ty - fy));
            var beyond = new Vector2((float)(tx + (tx - fx) / len * 0.4), (float)(ty + (ty - fy) / len * 0.4));
            Assert.True(PolygonMath.Contains(ring, beyond), "the landing beyond the stair is floor");
            Assert.True(Math.Abs(PolygonMath.SignedArea2(ring)) / 2 < 16 * 12 - 4, "a notch was cut");
        }
        // the 2nd stair does not start in the 1st one's stairwell: its start lies in the floor of storey 1
        var ring1 = storeys[1].GetProperty("floor")[0].EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray();
        var s2 = storeys[1].GetProperty("stairs")[0].GetProperty("from");
        Assert.True(PolygonMath.Contains(ring1, new Vector2(s2[0].GetSingle(), s2[1].GetSingle())));
    }

    [Fact]
    public async Task ATooSmallFootprint_BecomesSolid_AndIsCounted()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("tiny", 0, 0, 3, 4, storeys: 2, openings: new[] { Door(1.5, 0) }));   // 2 storeys: the straight flight needs 6.1 m of wall, the switch-back (CE-4379) 4.55 m
        var (folder, world, rep) = await Import(mini);
        Assert.Equal(1, rep.SolidBecauseNoStair); Assert.Equal(0, rep.BuildingsEnterable); Assert.Equal(1, rep.BuildingsSolid);
        Assert.Empty(world.Buildings.Where(b => !b.Solid));
        Assert.Contains(world.Prisms, p => p.Kind == TerrainPrismKind.Building && p.Label == "tiny");
        Assert.False(File.Exists(Path.Combine(folder, "buildings", "tiny.building.json")));
        Assert.Equal(0, rep.OpeningsPlaced);
    }


    // ---- ⭐ CE-4379: the switch-back stair, tried when no wall takes the straight flight
    private static double Dist2(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    [Fact]
    public async Task AFootprintTooShortForAStraightFlight_GetsASwitchBack_AndStaysEnterable()
    {
        var mini = SmallHouse();
        var (folder, world, rep) = await Import(mini);
        Assert.Equal(0, rep.SolidBecauseNoStair); Assert.Equal(1, rep.BuildingsEnterable); Assert.Equal(0, rep.BuildingsSolid);
        Assert.Equal(1, rep.StairsSwitchBack); Assert.Equal(2, rep.Stairs);
        Assert.False(world.Buildings.Single().Solid);
        Assert.Equal(2, world.Walkables.Count(w => w.Kind == TerrainWalkableKind.Ramp));
        using var t = Template(folder, "small");
        var st = t.RootElement.GetProperty("storeys")[0];
        Assert.Equal(2, st.GetProperty("stairs").GetArrayLength());
        Assert.Equal(1, st.GetProperty("landings").GetArrayLength());
        // the floor above: ONE ring, the well open
        var rings = t.RootElement.GetProperty("storeys")[1].GetProperty("floor");
        Assert.Equal(1, rings.GetArrayLength());
    }

    private static MiniSet SmallHouse()
    {
        var mini = new MiniSet { MinX = -20, MinY = -20, MaxX = 30, MaxY = 30 };
        mini.Buildings.Add(MiniSet.Rect("small", 0, 0, 5, 5, storeys: 2, openings: new[] { Door(2.5, 5) }));   // north door: opens onto the free floor, not into the well
        return mini;
    }

    [Fact]
    public async Task AFootprintThatFitsAStraightFlight_StillGetsTheStraightOne()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("roomy", 0, 0, 7, 7, storeys: 2));   // 7 m wall >= 5.1 + 1.0
        var (folder, world, rep) = await Import(mini);
        Assert.Equal(0, rep.StairsSwitchBack); Assert.Equal(1, rep.Stairs); Assert.Equal(0, rep.SolidBecauseNoStair);
        using var t = Template(folder, "roomy");
        Assert.False(t.RootElement.GetProperty("storeys")[0].TryGetProperty("landings", out _));
        Assert.False(t.RootElement.GetProperty("storeys")[0].GetProperty("stairs")[0].TryGetProperty("toRise", out _));
    }

    [Fact]
    public async Task TwoStoreysOfSwitchBack_TakeWellsThatDoNotOverlap()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("tri", 0, 0, 6, 6, storeys: 3));
        var (folder, _, rep) = await Import(mini);
        Assert.Equal(0, rep.SolidBecauseNoStair); Assert.Equal(2, rep.StairsSwitchBack);
        using var t = Template(folder, "tri");
        var storeys = t.RootElement.GetProperty("storeys");
        (double x0, double y0, double x1, double y1) Well(int k)
        {
            var pts = new List<(double X, double Y)>();
            foreach (var p in storeys[k].GetProperty("landings")[0].GetProperty("ring").EnumerateArray()) pts.Add((p[0].GetDouble(), p[1].GetDouble()));
            foreach (var r in storeys[k].GetProperty("stairs").EnumerateArray())
            { pts.Add((r.GetProperty("from")[0].GetDouble(), r.GetProperty("from")[1].GetDouble())); pts.Add((r.GetProperty("to")[0].GetDouble(), r.GetProperty("to")[1].GetDouble())); }
            return (pts.Min(p => p.X) - 0.5, pts.Min(p => p.Y) - 0.5, pts.Max(p => p.X) + 0.5, pts.Max(p => p.Y) + 0.5);   // the lanes are 1 m wide
        }
        var a = Well(0); var b = Well(1);
        bool disjoint = a.x1 <= b.x0 || b.x1 <= a.x0 || a.y1 <= b.y0 || b.y1 <= a.y0;
        Assert.True(disjoint, $"wells overlap: {a} vs {b}");
        // the 2nd stair starts on the 1st one's upper floor, not in its well
        var ring1 = storeys[1].GetProperty("floor")[0].EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray();
        var s2 = storeys[1].GetProperty("stairs")[0].GetProperty("from");
        Assert.True(PolygonMath.Contains(ring1, new Vector2(s2[0].GetSingle(), s2[1].GetSingle())));
    }

    [Fact]
    public async Task TheSwitchBack_LoadsThroughTheRealParser_RampsConnectTheSlabs_AndNoRunIsSteeperThanTheStraightRamp()
    {
        var (_, world, _) = await Import(SmallHouse());
        var b = world.Buildings.Single();
        float floor0 = b.StoreyZ[0], floor1 = b.StoreyZ[1], landingZ = floor0 + (floor1 - floor0) / 2;
        var ramps = world.Walkables.Where(w => w.Kind == TerrainWalkableKind.Ramp).OrderBy(w => w.MinZ).ToList();
        Assert.Equal(2, ramps.Count);
        Assert.Equal(floor0, ramps[0].MinZ, 2); Assert.Equal(landingZ, ramps[0].MaxZ, 2);   // run A: the floor -> the landing
        Assert.Equal(landingZ, ramps[1].MinZ, 2); Assert.Equal(floor1, ramps[1].MaxZ, 2);   // run B: the landing -> the floor above
        var landing = world.Walkables.Single(w => w.Kind == TerrainWalkableKind.Slab && Math.Abs(w.MinZ - landingZ) < 0.01f && Math.Abs(w.MaxZ - landingZ) < 0.01f);
        // run A's head and run B's foot lie on the landing slab's edge; run B's head meets the floor slab above
        Vector2 Plan(Vector3 v) => new(v.X, v.Y);
        foreach (var (ramp, z) in new[] { (ramps[0], landingZ), (ramps[1], landingZ) })
            foreach (var v in ramp.Vertices.Where(v => Math.Abs(v.Z - z) < 0.01f))
                Assert.True(PolygonOnEdge(landing.Vertices.Select(Plan).ToArray(), Plan(v)) || PolygonMath.Contains(landing.Vertices.Select(Plan).ToArray(), Plan(v)), $"{v} is off the landing");
        var floorAbove = world.Walkables.Where(w => w.Kind == TerrainWalkableKind.Slab && Math.Abs(w.MinZ - floor1) < 0.01f && Math.Abs(w.MaxZ - floor1) < 0.01f).ToList();
        Assert.NotEmpty(floorAbove);
        foreach (var v in ramps[1].Vertices.Where(v => Math.Abs(v.Z - floor1) < 0.01f))
            Assert.True(floorAbove.Any(f => f.Vertices.Any(fv => Vector2.Distance(Plan(fv), Plan(v)) < 0.01f) || PolygonMath.Contains(f.Vertices.Select(Plan).ToArray(), Plan(v))
                                              || PolygonOnEdge(f.Vertices.Select(Plan).ToArray(), Plan(v))), $"{v} does not meet the floor above");
        // slope: the straight ramp rises 1 / 1.7
        foreach (var r in ramps)
        {
            var lo = r.Vertices.Where(v => Math.Abs(v.Z - r.MinZ) < 0.01f).ToArray(); var hi = r.Vertices.Where(v => Math.Abs(v.Z - r.MaxZ) < 0.01f).ToArray();
            double run = Dist2((lo[0].X + lo[1].X) / 2, (lo[0].Y + lo[1].Y) / 2, (hi[0].X + hi[1].X) / 2, (hi[0].Y + hi[1].Y) / 2);
            Assert.True((r.MaxZ - r.MinZ) / run <= 1 / StairPlanner.RunPerHeight + 1e-3, $"run steeper than the straight ramp: {(r.MaxZ - r.MinZ) / run}");
        }
    }

    private static bool PolygonOnEdge(Vector2[] ring, Vector2 p)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Length]; var ab = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            if (Vector2.Distance(a + ab * t, p) < 0.02f) return true;
        }
        return false;
    }

    [Fact]
    public async Task TheSwitchBack_IsWalkable_ANavmeshPathReachesTheUpperStorey_ThroughTheWell()
    {
        var (_, world, _) = await Import(SmallHouse());
        AssertPathReachesUpstairs(world);
    }

    private static void AssertPathReachesUpstairs(TerrainWorld world)
    {
        var nav = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry, TileCache = null }.Build(world)!;
        float floor1 = world.Buildings.Single().StoreyZ[1];
        var fp = world.Buildings.Single().Footprint; var o = new Vector2(fp.Min(p => p.X), fp.Min(p => p.Y));   // the house's south-west corner in the world
        var goal = new Vector3(o.X + 2.5f, o.Y + 4.0f, floor1);   // upstairs, on the free floor beside the well
        var start = new Vector3(o.X + 2.5f, o.Y + 7.0f, world.GroundHeightAt(o.X + 2.5f, o.Y + 7.0f));
        Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
        int n = nav.PlanPath(start, goal, wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 2, $"no path to the upper storey ({n} waypoints)");
        Assert.True(nav.PathExists(start, goal, (uint)NavLayerMask.Infantry), "the path to the upper storey is partial");
        Assert.InRange(wps[n - 1].Position.Z, floor1 - 0.4f, floor1 + 0.4f);
        float f0 = world.Buildings.Single().StoreyZ[0];
        bool between = false;
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            if (p.Z > f0 + 0.5f && p.Z < floor1 - 0.5f) { between = true; Assert.True(p.X > o.X && p.X < o.X + 4.2f && p.Y > o.Y && p.Y < o.Y + 2.6f, $"waypoint {i} {p} is between floors outside the well"); }
        }
        Assert.True(between, "the path never climbed through the well");
    }

    // ---- roofs and solids
    [Fact]
    public async Task APitchedRoof_HasACapPrism_ToTheRidge()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("hip", 0, 0, 12, 10, storeys: 2, roof: "pitched", ridgeRise: 3.5, baseZ: 10));
        var (_, world, rep) = await Import(mini);
        Assert.Equal(1, rep.RoofCaps);
        var cap = world.Prisms.Single(p => p.Label == "hip-roof");
        Assert.Equal(10f + 6f, cap.BaseZ, 3); Assert.Equal(10f + 6f + 3.5f, cap.TopZ, 3);
        var flat = new MiniSet(); flat.Buildings.Add(MiniSet.Rect("fl", 0, 0, 12, 10, storeys: 2));
        var (_, w2, r2) = await Import(flat, "flat");
        Assert.Equal(0, r2.RoofCaps); Assert.DoesNotContain(w2.Prisms, p => p.Label == "fl-roof");
    }

    [Fact]
    public async Task SolidBuildings_AreWorldPrisms_WithTheirHeight_NoTemplate()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("garage", 0, 0, 6, 3, enterable: false, baseZ: 12));
        mini.Buildings.Add(MiniSet.Rect("shed", 20, 0, 24, 5, enterable: false, roof: "pitched", ridgeRise: 1.5, baseZ: 12));
        var (folder, world, rep) = await Import(mini);
        Assert.Equal(2, rep.BuildingsSolid); Assert.Equal(0, rep.BuildingsEnterable);
        var g = world.Prisms.Single(p => p.Label == "garage");
        Assert.Equal(12f, g.BaseZ); Assert.Equal(15f, g.TopZ);
        Assert.Equal(12f + 3f + 1.5f, world.Prisms.Single(p => p.Label == "shed").TopZ, 3);   // a pitched solid reaches its ridge
        Assert.False(Directory.Exists(Path.Combine(folder, "buildings")));
    }

    [Fact]
    public async Task BuildingMaterials_AreMappedToTheLibrary()
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("a", 0, 0, 10, 8, material: "wood"));
        mini.Buildings.Add(MiniSet.Rect("b", 20, 0, 30, 8, enterable: false, material: "stone"));
        var (_, world, _) = await Import(mini);
        Assert.Contains(world.Panels, p => p.Label == "a" && p.Material.Name == "fence-wood");
        Assert.Equal("concrete", world.Prisms.Single(p => p.Label == "b").Material.Name);
    }

    // ---- vegetation, water, walls
    [Fact]
    public async Task WoodsPolygons_WithDensity_BecomeForestSurfaces_AndPointsAreLoneTrees()
    {
        var mini = new MiniSet
        {
            Woods = new() { new AreaFeature(new (double, double)[] { (-50, -50), (0, -50), (0, 0), (-50, 0), (-50, -50) }, null, 80.0) },
            Water = new() { new AreaFeature(new (double, double)[] { (20, 20), (40, 20), (40, 40), (20, 40), (20, 20) }, "lake") },
            Scrub = new() { new AreaFeature(new (double, double)[] { (60, 60), (70, 60), (70, 70), (60, 70), (60, 60) }, "scrub") },
            Trees = { (10, 10, 0), (-20, -20, 0), (500, 500, 0) },
        };
        var (_, world, rep) = await Import(mini);
        var forest = world.Surfaces.Single(s => s.Type == TerrainSurfaceType.Forest);
        Assert.Equal(80f, forest.Density); Assert.Equal(new Vector2(50, 50), forest.Min); Assert.Equal(new Vector2(100, 100), forest.Max);   // −(−100)
        Assert.Single(world.Surfaces, s => s.Type == TerrainSurfaceType.Water);
        Assert.Equal(1, rep.WoodsPolygons); Assert.Equal(0, rep.WoodsFromPoints); Assert.Equal(1, rep.ScrubSkipped);
        Assert.Equal(2, world.Prisms.Count(p => p.Kind == TerrainPrismKind.Tree));   // the tree outside the bounds is dropped
    }

    [Fact]
    public async Task WithoutWoodsPolygons_PointsAreClusteredIntoWoods_ByTerrainWoodsCluster()
    {
        var mini = new MiniSet();
        for (int i = 0; i < 40; i++) for (int j = 0; j < 40; j++) mini.Trees.Add((-90 + i * 1.5, -90 + j * 1.5, 0));   // a 60 m square at ~ 4 400 trees / ha
        mini.Trees.Add((80, 80, 0));                                                                                 // one lone tree
        var (_, world, rep) = await Import(mini);
        Assert.True(rep.WoodsFromPoints > 0); Assert.Equal(0, rep.WoodsPolygons);
        Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Forest && s.Density >= 45f);
        Assert.Equal(1, rep.LoneTrees);
        Assert.Single(world.Prisms, p => p.Kind == TerrainPrismKind.Tree);
    }

    [Theory]
    [InlineData("stone", "brick")] [InlineData("wall", "brick")] [InlineData("mud", "earth-berm")] [InlineData("fence", "fence-wood")] [InlineData("hedge", "hedge")]
    [InlineData("retaining_wall", "concrete")] [InlineData("city_wall", "concrete")]
    public async Task WallKinds_MapToTheMaterialLibrary(string kind, string material)
    {
        var mini = new MiniSet();
        mini.Walls.Add(new WallFeature(new (double, double)[] { (0, 0), (20, 0) }, 2.0, 0.4, kind));
        var (_, world, rep) = await Import(mini);
        Assert.Equal(1, rep.Walls);
        Assert.Contains(world.Panels, p => p.Material.Name == material && p.Building == -1);
        Assert.Equal(material, TerrainImporter.WallMaterial(kind));
    }

    // ---- all or nothing
    [Fact]
    public async Task Replace_False_RefusesAnExistingTerrain_Replace_True_ReplacesIt()
    {
        var mini = new MiniSet(); mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 10, 8));
        string set = mini.Write(Path.Combine(_env.Root, "s1"));
        var imp = _env.Importer();
        string folder = await imp.ImportAsync(set, "t", false, null, default);
        File.WriteAllText(Path.Combine(folder, "marker.txt"), "old");
        await Assert.ThrowsAsync<InvalidOperationException>(() => imp.ImportAsync(set, "t", false, null, default));
        Assert.True(File.Exists(Path.Combine(folder, "marker.txt")));   // untouched
        await imp.ImportAsync(set, "t", true, null, default);
        Assert.False(File.Exists(Path.Combine(folder, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "terrain.json")));
        Assert.Empty(Directory.GetDirectories(_env.Terrains).Where(d => Path.GetFileName(d) != "t"));
    }

    [Fact]
    public async Task ACancelledImport_LeavesNoFolder_NoTempEither()
    {
        string set = _env.Synth();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Stage == "roads") cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _env.Importer().ImportAsync(set, "c", false, progress, cts.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_env.Terrains));
    }

    [Fact]
    public async Task AFailedImport_LeavesNothing_AndKeepsTheOldTerrainWhenReplacing()
    {
        var mini = new MiniSet(); mini.Buildings.Add(MiniSet.Rect("b", 0, 0, 10, 8));
        string good = mini.Write(Path.Combine(_env.Root, "good"));
        var imp = _env.Importer();
        await imp.ImportAsync(good, "t", false, null, default);
        string bad = mini.Write(Path.Combine(_env.Root, "bad"));
        File.AppendAllText(Path.Combine(bad, "buildings", "buildings.geojson"), " ");   // the manifest's hash no longer matches
        await Assert.ThrowsAsync<InvalidDataException>(() => imp.ImportAsync(bad, "t", true, null, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => imp.ImportAsync(bad, "u", false, null, default));
        Assert.Equal(new[] { "t" }, Directory.GetFileSystemEntries(_env.Terrains).Select(Path.GetFileName).ToArray());
        Env.Load(Path.Combine(_env.Terrains, "t"), "t");   // the old one still loads
    }

    [Theory]
    [InlineData("")] [InlineData("../x")] [InlineData("a b")] [InlineData("a/b")]
    public async Task BadTerrainNames_AreRefused(string name) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _env.Importer().ImportAsync("nowhere", name, false, null, default));

    // ---- ScenariosUsing
    [Fact]
    public void ScenariosUsing_ReadsHeaderTerrainName_OfEveryScenario()
    {
        void Scn(string folder, string terrain)
        {
            Directory.CreateDirectory(Path.Combine(_env.Scenarios, folder));
            File.WriteAllText(Path.Combine(_env.Scenarios, folder, "scenario.json"), "{ \"$meta\": {}, \"header\": { \"subsystemType\": \"Hrot.Scenario\", \"terrainName\": \"" + terrain + "\" }, \"entities\": {} }");
        }
        Scn("a", "alpha"); Scn("b", "beta"); Scn("c", "alpha"); Scn("d", "alpha2");
        Directory.CreateDirectory(Path.Combine(_env.Scenarios, "empty"));
        Assert.Equal(new[] { "a", "c" }, _env.Importer().ScenariosUsing("alpha"));
        Assert.Empty(_env.Importer().ScenariosUsing("gamma"));
    }

    [Fact]
    public void ScenariosUsing_FindsTheShippedScenarios_OfTheShippedTerrain()
    {
        var used = new TerrainImporter().ScenariosUsing("cqb-village");   // the repo's own scenarios/ and assets/terrain/
        Assert.Contains("pc-defend-dry", used);
    }

}
