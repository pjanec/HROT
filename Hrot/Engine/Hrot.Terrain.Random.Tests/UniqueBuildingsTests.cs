using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.Terrain.Random.Tests;

/// <summary>
/// ⭐⭐ T-3 part 2 (CE-4430, docs/DESIGN_Terrain_Authoring.md §18i I-5, I-6) — UNIQUE 3–6-storey houses for a town block's grid: the host's <see cref="SynthUniqueBuildings"/> (the synth's site-only building run + the importer's template generator) behind <see cref="IUniqueBuildings"/>.
/// Two compiles are byte-identical, generated templates included; the overlay loads and its houses are enterable; the live overlay and the compiler agree; with none passed the grid falls back to the shipped templates.
/// </summary>
public sealed class UniqueBuildingsTests : IDisposable
{
    private const string Key = "b1000000-0000-4000-8000-000000000009";
    private static readonly Vector2 At = new(345f, 180f);
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ub-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _terrains = ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    private JsonObject Dom(uint seed = 21) => new()
    {
        ["header"] = new JsonObject { ["terrainName"] = "test-town" },
        ["entities"] = new JsonObject { [Key] = new JsonObject
        {
            ["TerrainBlock"] = new JsonObject { ["Recipe"] = "town-block", ["Seed"] = seed, ["HeadingDeg"] = 0, ["Fit"] = 15 },
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(At.X, At.Y, 0) },
        } },
    };

    private OverlayCompileResult Compile(string name, IUniqueBuildings? unique)
    {
        string dir = Path.Combine(_tmp, name, "scn"); Directory.CreateDirectory(dir);   // the same folder NAME: the header records it
        var catalog = new TerrainCatalog(new[] { _terrains });
        var r = OverlayCompiler.Compile(Dom(), dir, catalog.ResolveDefinition, bakeNavtiles: false, unique: unique);
        Assert.True(r.Wrote);
        return r;
    }

    private static int Generated(OverlayCompileResult r) => Directory.Exists(Path.Combine(r.Folder!, "buildings")) ? Directory.GetFiles(Path.Combine(r.Folder!, "buildings")).Length : 0;

    /// <summary>🔴 Red-proof: draw from a non-deterministic source in <c>SiteBuildings</c> / <c>SynthUniqueBuildings</c> ⇒ the two folders differ ⇒ red.</summary>
    [Fact]
    public void TwoCompilesWithUniqueHouses_AreByteIdentical_GeneratedTemplatesIncluded()
    {
        var a = Compile("a", new SynthUniqueBuildings()); var b = Compile("b", new SynthUniqueBuildings());
        Assert.True(Generated(a) >= 6, $"the grid asked for generated houses ({Generated(a)} templates)");
        Assert.Equal(Env.Hashes(a.Folder!), Env.Hashes(b.Folder!));
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.Contains(Env.Hashes(a.Folder!).Keys, k => k.StartsWith("buildings/u", StringComparison.Ordinal));
    }

    /// <summary>
    /// The overlay LOADS and its generated houses are ENTERABLE: not solid, 3–6 storeys, each with a door the merged world defines. The overlay's own <c>buildings/</c> resolves BEFORE the shared ones (a shared template of the same name is not read), and
    /// <c>OfOverlay</c> hashes <c>buildings/*</c> (editing a template moves the hash). 🔴 Red-proof: skip the <c>buildings/</c> write in <c>OverlayCompiler</c> ⇒ the template is not found ⇒ red.
    /// </summary>
    [Fact]
    public void TheOverlayLoads_ItsUniqueHousesAreEnterable_TheOverlaysBuildingsResolveFirst_AndAreHashed()
    {
        var r = Compile("a", new SynthUniqueBuildings());
        string shared = TerrainAssets.RecipesSibling(Path.Combine(_terrains, "test-town"));
        var overlay = TerrainFolderWorld.ReadOverlay(r.Folder!, "test-town", shared);
        var houses = overlay.World.Buildings.Where(b => b.Label.StartsWith(Key + "/", StringComparison.Ordinal)).ToList();
        Assert.True(houses.Count >= 6, $"{houses.Count} houses");
        Assert.All(houses, h => { Assert.False(h.Solid, $"{h.Label} is solid"); Assert.InRange(h.Storeys, 3, 7); });   // 3–6 storeys (+ the roof storey of a flat roof)
        Assert.True(overlay.World.Doors.Count >= houses.Count, $"doors {overlay.World.Doors.Count} for {houses.Count} houses");

        // the real load merges it
        var catalog = new TerrainCatalog(new[] { _terrains });
        var world = new EntityRepository();
        var residency = new TerrainResidency(catalog, new CarKinem.Road.RoadNetworkHolder());
        residency.Commit(world, residency.Prepare("test-town", r.Folder));
        var merged = world.GetSingletonManaged<TerrainWorld>()!;
        Assert.True(merged.Buildings.Count(b => b.Label.StartsWith(Key + "/", StringComparison.Ordinal)) == houses.Count);

        // resolution order: a SHARED template of the same name does not win over the overlay's own
        string tname = houses[0].Template!;
        string fakeShared = Path.Combine(_tmp, "fake-shared"); Directory.CreateDirectory(fakeShared);
        File.WriteAllText(Path.Combine(fakeShared, tname + TerrainAssets.TemplateSuffix), """{"footprint":[[0,0],[2,0],[2,2],[0,2]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[2,0]},{"from":[2,0],"to":[2,2]},{"from":[2,2],"to":[0,2]},{"from":[0,2],"to":[0,0]}]}]}""");
        var again = TerrainFolderWorld.ReadOverlay(r.Folder!, "test-town", fakeShared);
        var same = again.World.Buildings.First(b => b.Label == houses[0].Label);
        Assert.Equal(houses[0].Footprint.Length, same.Footprint.Length);
        Assert.True(MathF.Abs(Area(same.Footprint) - Area(houses[0].Footprint)) < 0.01f && Area(same.Footprint) > 8f, "the overlay's own template, not the shared 2 × 2 m one");

        // the content hash covers buildings/*
        string before = TerrainContentHash.OfOverlay(r.Folder!);
        string file = Directory.GetFiles(Path.Combine(r.Folder!, "buildings"))[0];
        File.AppendAllText(file, " ");
        Assert.NotEqual(before, TerrainContentHash.OfOverlay(r.Folder!));
    }

    private static float Area(System.Numerics.Vector2[] p) { float a = 0; for (int i = 0; i < p.Length; i++) { var q = p[(i + 1) % p.Length]; a += p[i].X * q.Y - q.X * p[i].Y; } return MathF.Abs(a) / 2f; }

    /// <summary>
    /// The live overlay (given the same implementation) and the compiler agree: the same parts (labels), the same boxes — and the live world's houses are the generated, enterable ones. 🔴 Red-proof: give the live overlay NO implementation
    /// (<c>Unique = null</c>) ⇒ it falls back to the shipped pool ⇒ other houses ⇒ red.
    /// </summary>
    [Fact]
    public void TheLiveOverlayAndTheCompiler_AgreeOnUniqueHouses()
    {
        var r = Compile("a", new SynthUniqueBuildings());
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(r.Folder!, "overlay.world.geojson")))!["features"]!.AsArray().Select(f => (JsonObject)f!).ToList();
        var fromCompiler = file.Select(f => (Label: OverlayExpansion.Label(f), Box: OverlayExpansion.BoundsOf(f))).OrderBy(t => t.Label, StringComparer.Ordinal).ToList();

        var catalog = new TerrainCatalog(new[] { _terrains });
        var world = new EntityRepository();
        SimHostLikeRegistry(world);
        var residency = new TerrainResidency(catalog, new CarKinem.Road.RoadNetworkHolder());
        residency.Commit(world, residency.Prepare("test-town"));
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(At.X, At.Y, 0), Rotation = Quaternion.Identity });
        world.AddComponent(e, new TerrainBlock { Recipe = new FixedString32("town-block"), Seed = 21, HeadingDeg = 0, Fit = TerrainBlock.FitAll });
        world.AddComponent(e, new EntityGuid(Guid.Parse(Key)));
        var live = new LiveOverlay { Unique = new SynthUniqueBuildings() };
        var (@base, version, folder) = residency.ExpansionBase();
        var result = live.Expand(live.Snapshot(world), @base, version, folder)!;
        var fromLive = result.Parts.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();

        Assert.Equal(fromCompiler.Select(t => t.Label), fromLive.Select(p => p.Key));
        for (int i = 0; i < fromLive.Count; i++)
        {
            Assert.Equal(fromCompiler[i].Box.Min.X, fromLive[i].Min.X, 2); Assert.Equal(fromCompiler[i].Box.Min.Y, fromLive[i].Min.Y, 2);
            Assert.Equal(fromCompiler[i].Box.Max.X, fromLive[i].Max.X, 2); Assert.Equal(fromCompiler[i].Box.Max.Y, fromLive[i].Max.Y, 2);
        }
        Assert.All(result.World.Buildings, b => Assert.False(b.Solid));
    }

    private static void SimHostLikeRegistry(EntityRepository world)
    {
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TerrainBlock>();
        world.RegisterComponent<TerrainFeature>();
        world.RegisterComponent<EntityGuid>();
        world.RegisterManagedComponent<Hrot.Map.Common.Components.EditablePolyline>();
    }

    /// <summary>With no implementation passed the grid falls back to the shipped templates: the compile succeeds, writes no <c>buildings/</c>, and still makes houses.</summary>
    [Fact]
    public void WithNoImplementation_TheGridFallsBackToTheShippedTemplates()
    {
        var r = Compile("none", null);
        Assert.Equal(0, Generated(r));
        var feats = JsonNode.Parse(File.ReadAllText(Path.Combine(r.Folder!, "overlay.world.geojson")))!["features"]!.AsArray();
        Assert.True(feats.Count(f => (string?)f!["properties"]!["kind"] == "building") >= 8);
    }
}
