using System.Linq;
using System;
using System.IO;
using System.Text;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// C5 — the TERRAIN loader. Mirrors <c>TkbLoadClusterStateHandlerTests</c>'s fixture, because the handler
/// mirrors that handler.
///
/// <para>⚠ The <b>rail that matters</b> is not "it loads" — it is
/// <see cref="ANodeWithNoAuthoringDeps_StillGetsTheTerrainLoader"/>: a pure MuscleGround node registers
/// NONE of the scenario-LOAD handlers (they sit behind an authoring-deps conditional), and the muscle is
/// exactly the role that consumes the road network.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ②a ③ ④.
/// </summary>
public sealed class TerrainLoadStepTests : IDisposable
{
    private readonly string _stagingRoot;
    private readonly string _tkbDir;
    private readonly string _terrainDir;

    public TerrainLoadStepTests()
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "TerrainHandlerTest_" + Guid.NewGuid().ToString("N")[..8]);
        _tkbDir      = Path.Combine(_stagingRoot, "TKB");
        _terrainDir  = Path.Combine(_stagingRoot, "Terrain");
        Directory.CreateDirectory(_tkbDir);
        Directory.CreateDirectory(_terrainDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_stagingRoot)) Directory.Delete(_stagingRoot, recursive: true);
    }

    // ── fixture helpers ───────────────────────────────────────────────────────────────────────

    private void WriteScenarioHeader(string? terrainName)
    {
        // The staged header lives beside the TKB artifact — one staged header per node.
        string content = terrainName == null
            ? "{\"SubsystemType\":\"SimHost\"}"
            : $"{{\"$meta\":{{\"docType\":\"Hrot.Scenario\",\"schemaVersion\":1}},\"TerrainName\":\"{terrainName}\"}}";
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), content, new UTF8Encoding(false));
    }

    private string WriteTerrainDefinition(string name, string? roadNetworkRelative = null)
    {
        string roads = roadNetworkRelative == null ? "[]" : $"[\"{roadNetworkRelative}\"]";
        // ⭐ W12 (2026-10-03) — a terrain is a FOLDER: {staging}/Terrain/<name>/terrain.json.
        string folder = Path.Combine(_terrainDir, name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, TerrainCatalog.DefinitionFileName);
        File.WriteAllText(path,
            $"{{\"schemaVersion\":1,\"name\":\"{name}\",\"roadNetworks\":{roads}}}",
            new UTF8Encoding(false));
        return path;
    }

    /// <summary>A two-node road network in the JSON shape <c>RoadNetworkLoader</c> reads.</summary>
    private string WriteRoadNetwork(string fileName, string terrainName = "kandahar")
    {
        // The road network lives in the terrain's own folder, beside its definition (W12).
        string folder = Path.Combine(_terrainDir, terrainName);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        // ⚠ The property names are lowercase on purpose — RoadNetworkJson carries explicit
        //   [JsonPropertyName("nodes")] / ("position") / ("p0") … and System.Text.Json is case-SENSITIVE
        //   by default, so PascalCase here binds to nothing and yields a silently EMPTY network.
        File.WriteAllText(path, """
            {
              "nodes": [
                { "id": 0, "position": { "x": 0.0, "y": 0.0 } },
                { "id": 1, "position": { "x": 100.0, "y": 0.0 } }
              ],
              "segments": [
                {
                  "id": 0,
                  "startNodeId": 0,
                  "endNodeId": 1,
                  "controlPoints": {
                    "p0": { "x": 0.0,   "y": 0.0 },
                    "t0": { "x": 50.0,  "y": 0.0 },
                    "p1": { "x": 100.0, "y": 0.0 },
                    "t1": { "x": 50.0,  "y": 0.0 }
                  },
                  "speedLimit": 13.9, "laneWidth": 3.5, "laneCount": 2
                }
              ]
            }
            """, new UTF8Encoding(false));
        return path;
    }

    private static EntityRepository NewWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<TerrainDefinition>();
        return repo;
    }

    /// <summary>
    /// ⭐ <c>L3</c> — a step is driven by a LOAD-PHASE CONTEXT. ⚠ <c>terrainName: null</c> means "the
    /// message named none", which sends the step to the staged-header fallback these cases were written
    /// against; a non-null name exercises the message path the orchestrator now uses.
    /// </summary>
    private static LoadPhaseContext Intent(string? terrainName = null) => new(
        TransactionId: Guid.NewGuid(),
        TargetState:   ClusterState.LoadingLive,
        ScenarioId:    "scn",
        TerrainName:   terrainName,
        ExerciseId:    (Fdp.Core.ExerciseId)(Guid.Empty),
        IsNewScenario: false);

    /// <summary>
    /// Runs PrepareAsync to completion. ⚠ Deliberately a helper rather than an inline
    /// <c>.GetAwaiter().GetResult()</c> in each test: the handler's prepare is pure file I/O with no
    /// synchronization context, so blocking is safe, and keeping it in one place stops the pattern from
    /// being copied into a test that later gains a real async dependency.
    /// </summary>
    private static void Prepare(TerrainLoadStep handler, LoadPhaseContext intent)
        => handler.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();

    private (TerrainLoadStep Handler, RoadNetworkHolder Holder) NewHandler()
    {
        var holder = new RoadNetworkHolder();
        return (new TerrainLoadStep(new TerrainResidency(_stagingRoot, holder), _stagingRoot), holder);
    }

    /// <summary>
    /// A handler wired the way PRODUCTION wires it — the world INJECTED, not handed in at commit.
    /// See <see cref="ItPublishesThroughTheINJECTEDWorld_BecauseClusterSlaveAlwaysCommitsWithNullRepo"/>.
    /// </summary>
    /// <summary>
    /// ⚠ The world is no longer injected into the step — <c>LoadPhaseChain</c> owns it and hands it to
    /// every step at commit, which is what makes the null-repo trap impossible to re-introduce per host.
    /// These cases therefore commit through the world explicitly, exactly as the chain does.
    /// </summary>
    private (TerrainLoadStep Handler, RoadNetworkHolder Holder) NewHandlerWithWorld(
        EntityRepository world)
        => NewHandler();

    // ⛔ "it claims the right ops" MOVED to LoadPhaseChainTests: a step claims NOTHING now — the
    //    chain claims once for the whole node, which is the defect fix itself.

    // ── graceful absence ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AScenarioWithNoTerrainName_LoadsAnyway()
    {
        // ⭐ Same contract as TkbName: no opinion is legal, not an error.
        WriteScenarioHeader(terrainName: null);
        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world);

            Assert.False(world.HasSingletonManaged<TerrainDefinition>());
            Assert.False(world.HasSingleton<ZoneEnvironmentData>());
        }
    }

    [Fact]
    public void ANamedTerrainWhoseDefinitionIsMissing_FailsLOUDLY()
    {
        // ⛔ Loading the terrain a scenario NAMES is mandatory — a missing definition is a broken
        //    configuration, not a terrain-less scenario, and the two must not look the same.
        WriteScenarioHeader("kandahar");
        var (handler, holder) = NewHandler();
        using (holder)
        {
            var ex = Assert.Throws<FileNotFoundException>(() =>
                Prepare(handler, Intent()));

            Assert.Contains("kandahar", ex.Message);
        }
    }

    // ── the happy path ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ docs/DESIGN_Terrain_World.md §4.1 — a v2 definition naming a WORLD file makes the TerrainWorld
    /// singleton resident at commit (never at prepare), and the definition remembers the name the scenario
    /// resolved it by, which is what a save writes back (CE-3015).
    /// </summary>
    [Fact]
    public void AV2DefinitionWithAWorld_PublishesTheTerrainWorld_AndTheResolvedName()
    {
        WriteScenarioHeader("kandahar");
        string folder = Path.Combine(_terrainDir, "kandahar");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName),
            """{"schemaVersion":2,"name":"self-name","world":"w.geojson"}""", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","height":10},
               "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
            """, new UTF8Encoding(false));

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            Assert.False(world.HasSingletonManaged<TerrainWorld>());

            handler.Commit(intent, world);

            var tw = world.GetSingletonManaged<TerrainWorld>()!;
            Assert.Single(tw.Prisms);
            Assert.Equal(10f, tw.SurfaceZ(5, 5, 0f));
            Assert.Equal("kandahar", TerrainDefinition.ResidentName(world));
        }
    }


    /// <summary>
    /// ⭐⭐⭐ <c>CE-2417</c> — the terrain load REPORTS while it works (🔒 user: "for sure all loaders need to report progress, how else we would find out it
    /// is hung?"): the definition and world files read through the reporting stream, every world feature, the cover build, the road network's nodes
    /// and segments — in the prepare (the worker) — and the commit too. 🔴 Red-proof: the world parser's per-feature report removed ⇒ the prepare
    /// advances by fewer than the features it parsed.
    /// </summary>
    [Fact]
    public void CE2417_TheTerrainLoad_AdvancesTheNodesLoadProgress_InPrepareAndCommit()
    {
        WriteScenarioHeader("kandahar");
        string folder = Path.Combine(_terrainDir, "kandahar");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName),
            """{"schemaVersion":2,"name":"kandahar","world":"w.geojson"}""", new UTF8Encoding(false));
        var features = string.Join(",", Enumerable.Range(0, 40).Select(i =>
            "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"height\":10},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[["
          + $"{i * 20},0],[{i * 20 + 10},0],[{i * 20 + 10},10],[{i * 20},10],[{i * 20},0]]]}}}}"));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), "{\"type\":\"FeatureCollection\",\"features\":[" + features + "]}", new UTF8Encoding(false));

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var progress = new Fdp.Core.LoadProgress();
            var previous = Fdp.Core.LoadProgress.Current;
            Fdp.Core.LoadProgress.Current = progress;
            try
            {
                var intent = Intent();
                Prepare(handler, intent);
                long afterPrepare = progress.Value;
                Assert.True(afterPrepare >= 40 + 40, $"40 features parsed + 40 prisms covered ⇒ at least 80 reports; got {afterPrepare}");

                handler.Commit(intent, world);
                Assert.True(progress.Value > afterPrepare, "the commit reports too");
            }
            finally { Fdp.Core.LoadProgress.Current = previous; }
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2244</c> — a terrain that is not resident yet marks the world PENDING from the step's frame-thread <c>BeforePrepare</c> until
    /// its commit (sight is not judged meanwhile — faster nodes' units replicate in during the prepare, CE-2242); an abort clears it too;
    /// a scenario naming no terrain never marks it. ✅ Red-proof: the mark not set in BeforePrepare ⇒ red.
    /// </summary>
    [Fact]
    public void CE2244_ATerrainComing_MarksTheWorldPending_UntilItsCommitOrAbort()
    {
        string folder = Path.Combine(_terrainDir, "kandahar");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName),
            """{"schemaVersion":2,"name":"kandahar","world":"w.geojson"}""", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","features":[]}""", new UTF8Encoding(false));

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent("kandahar");
            handler.BeforePrepare(intent, world);
            Assert.True(Fdp.Toolkit.Terrain.TerrainWorldSource.IsPending(world), "the terrain is coming");
            Prepare(handler, intent);
            Assert.True(Fdp.Toolkit.Terrain.TerrainWorldSource.IsPending(world), "still coming during the prepare");
            handler.Commit(intent, world);
            Assert.False(Fdp.Toolkit.Terrain.TerrainWorldSource.IsPending(world), "in: sight is judged again");

            var (other, otherHolder) = NewHandler();
            using (otherHolder)
            {
                using var second = NewWorld();
                var again = Intent("kandahar");
                other.BeforePrepare(again, second);
                Assert.True(Fdp.Toolkit.Terrain.TerrainWorldSource.IsPending(second));
                other.Abort(again);
                Assert.False(Fdp.Toolkit.Terrain.TerrainWorldSource.IsPending(second), "an abort clears it");

                using var none = NewWorld();
                other.BeforePrepare(Intent(null), none);   // no staged header either: a scenario naming no terrain
                Assert.False(Fdp.Toolkit.Terrain.TerrainWorldSource.IsPending(none));
            }
        }
    }

    private sealed class CountingNavmeshFactory : Fdp.Toolkit.Navigation.INavmeshFactory
    {
        public int Calls;
        public int CallingThread;
        public Fdp.Toolkit.Navigation.INavmeshProvider? Build(TerrainWorld world)
        {
            Calls++;
            CallingThread = Environment.CurrentManagedThreadId;
            return new Fdp.Toolkit.Navigation.Fake.FakeNavmeshProvider();
        }
    }

    /// <summary>
    /// ⭐ W6 — docs/DESIGN_Terrain_World.md §4.1: on a navigation-solver node the bake runs in PREPARE and the node's
    /// navmesh switches at COMMIT, never before; a node that attached no factory bakes nothing.
    /// </summary>
    [Fact]
    public void AnAttachedNavmesh_IsBakedAtPrepare_AndPublishedAtCommit_W6()
    {
        string folder = Path.Combine(_terrainDir, "blocks");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName),
            """{"schemaVersion":2,"world":"w.geojson"}""", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","height":10},
               "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
            """, new UTF8Encoding(false));

        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var factory = new CountingNavmeshFactory();
        var navmesh = new Fdp.Toolkit.Navigation.SwitchableNavmeshProvider();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        residency.AttachNavmesh(factory, navmesh);

        var staged = residency.Prepare("blocks");
        Assert.Equal(1, factory.Calls);
        Assert.False(navmesh.HasBakedMesh);          // ⛔ not before commit

        residency.Commit(world, staged);
        Assert.True(navmesh.HasBakedMesh);

        residency.Unload(world);
        Assert.False(navmesh.HasBakedMesh);          // unload reverts to straight lines

        // A node that attached nothing never bakes.
        var plain = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        plain.Commit(world, plain.Prepare("blocks"));
        Assert.Equal(1, factory.Calls);
    }

    private string WriteBaseAndOverlay(out string overlay)
    {
        string folder = Path.Combine(_terrainDir, "base-t");
        Directory.CreateDirectory(folder);
        string def = Path.Combine(folder, TerrainCatalog.DefinitionFileName);
        File.WriteAllText(def, """{"schemaVersion":2,"world":"w.geojson"}""", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","label":"B","height":10},
               "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
            """, new UTF8Encoding(false));
        string baseHash = TerrainContentHash.OfTerrain(def);
        overlay = WriteOverlay("pending", baseHash);
        return baseHash;
    }

    /// <summary>Writes an overlay of one building "O" compiled against <paramref name="baseHash"/> into <c>{staging}/Terrain/overlays/&lt;its hash&gt;/</c>; returns that folder.</summary>
    private string WriteOverlay(string unused, string baseHash)
    {
        string tmp = Path.Combine(_stagingRoot, "ovl-build-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(tmp);
        File.WriteAllText(Path.Combine(tmp, "overlay.world.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","label":"O","height":4},
               "geometry":{"type":"Polygon","coordinates":[[[30,0],[40,0],[40,10],[30,10],[30,0]]]}}]}
            """, new UTF8Encoding(false));
        string hash = TerrainContentHash.OfOverlay(tmp, baseHash);
        File.WriteAllText(Path.Combine(tmp, "overlay.json"),
            $"{{\"format\":1,\"contentHash\":\"{hash}\",\"base\":{{\"name\":\"base-t\",\"contentHash\":\"{baseHash}\"}},\"scenario\":\"s\",\"world\":\"overlay.world.geojson\"}}", new UTF8Encoding(false));
        string dest = Path.Combine(_terrainDir, TerrainCatalog.OverlaysDirectoryName, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (Directory.Exists(dest)) Directory.Delete(dest, true);   // the same world under another base hash is the same folder name: the latest header wins
        Directory.Move(tmp, dest);
        return dest;
    }

    /// <summary>
    /// ⭐ O-2/O-4 (CE-4416/4415, DESIGN_Terrain_Authoring §18a) — <c>Prepare(name, overlayFolder)</c> stages the MERGED world (base + overlay, overlay appended); a second Prepare with the same
    /// overlay HASH is the idempotent no-op, no overlay is a real change. 🔴 Red-proof: ignore <c>overlayFolder</c> in <c>Prepare</c> ⇒ the staged world has one prism.
    /// </summary>
    [Fact]
    public void Prepare_WithAnOverlayFolder_StagesTheMergedWorld_O2()
    {
        WriteBaseAndOverlay(out var overlay);
        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);

        var plain = residency.Prepare("base-t");
        Assert.Single(plain.World!.Prisms);
        var staged = residency.Prepare("base-t", overlay);
        Assert.Equal(new[] { "B", "O" }, staged.World!.Prisms.Select(p => p.Label).ToArray());   // the overlay is appended: the base keeps index 0
        residency.Commit(world, staged);
        Assert.False(residency.Prepare("base-t", overlay).HasWork);   // same terrain, same overlay hash: already resident
        Assert.True(residency.Prepare("base-t").HasWork);             // the overlay went away: a real change
    }

    /// <summary>
    /// ⭐ O-4 — the node's terrain STEP reads <c>OverlayHash</c> from the staged header, resolves <c>Terrain/overlays/&lt;hash&gt;/</c> and commits the MERGED world; a hash with no folder throws.
    /// 🔴 Red-proof: don't pass the folder from the step ⇒ one prism; drop the throw ⇒ the missing-folder case loads the bare terrain.
    /// </summary>
    [Fact]
    public void TheStep_LoadsTheOverlayNamedByTheStagedHeader_O4()
    {
        WriteBaseAndOverlay(out var overlay);
        string hash = Path.GetFileName(overlay);
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), $"{{\"TerrainName\":\"base-t\",\"OverlayHash\":\"{hash}\"}}", new UTF8Encoding(false));
        var (handler, holder) = NewHandler();
        using (holder)
        {
            using var world = NewWorld();
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world);
            var w = world.GetSingletonManaged<TerrainWorld>();
            Assert.Equal(new[] { "B", "O" }, w.Prisms.Select(p => p.Label).ToArray());
        }

        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), "{\"TerrainName\":\"base-t\",\"OverlayHash\":\"deadbeef\"}", new UTF8Encoding(false));
        var (handler2, holder2) = NewHandler();
        using (holder2) Assert.Throws<FileNotFoundException>(() => Prepare(handler2, Intent()));
    }

    /// <summary>
    /// ⭐ O-4 (C-5) — an overlay compiled against ANOTHER base is refused, the message naming both hashes, the terrain and the overlay folder; so is a torn overlay (its files do not hash to its header).
    /// 🔴 Red-proof: skip the BaseHash comparison ⇒ no throw.
    /// </summary>
    [Fact]
    public void AnOverlayForAnotherBase_IsRefused_NamingBothHashes_O4()
    {
        string baseHash = WriteBaseAndOverlay(out _);
        string other = new string('a', 64);
        string wrong = WriteOverlay("x", other);
        using var holder = new RoadNetworkHolder();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        var e = Assert.Throws<InvalidOperationException>(() => residency.Prepare("base-t", wrong));
        Assert.Contains(other, e.Message); Assert.Contains(baseHash, e.Message); Assert.Contains("base-t", e.Message); Assert.Contains(wrong, e.Message);

        string torn = WriteOverlay("y", baseHash);
        File.AppendAllText(Path.Combine(torn, "overlay.world.geojson"), " ");
        var t = Assert.Throws<InvalidOperationException>(() => residency.Prepare("base-t", torn));
        Assert.Contains("torn", t.Message);
    }

    /// <summary>
    /// ⭐ O-4 — the same overlay loaded by two FRESH residencies (two nodes, two processes) gives merged worlds whose part lists are identical element for element: labels, counts, door keys, panel indices.
    /// 🔴 Red-proof: have the merge (or the parse) order parts by a per-process value (a hash code / a dictionary order) ⇒ the lists differ between residencies.
    /// </summary>
    [Fact]
    public void TwoFreshResidencies_LoadTheSameOverlayToIdenticalMergedWorlds_O4()
    {
        WriteBaseAndOverlay(out var overlay);
        // a richer overlay: a house with a door, so panel indices and door keys are in play
        string hs = Path.Combine(_stagingRoot, "ovl-rich"); Directory.CreateDirectory(hs);
        File.WriteAllText(Path.Combine(hs, "overlay.world.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","label":"H1","doors":{"front":"open"},"building":{"footprint":[[0,0],[10,0],[10,8],[0,8]],
                "storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
               "geometry":{"type":"Point","coordinates":[60,20]}},
              {"type":"Feature","properties":{"kind":"slab","z":2.0,"structure":"bridge-1"},"geometry":{"type":"Polygon","coordinates":[[[70,10],[80,10],[80,14],[70,14],[70,10]]]}},
              {"type":"Feature","properties":{"kind":"surface","surface":"forest"},"geometry":{"type":"Polygon","coordinates":[[[90,40],[100,40],[100,50],[90,50],[90,40]]]}}]}
            """, new UTF8Encoding(false));
        string baseHash = TerrainContentHash.OfTerrain(Path.Combine(_terrainDir, "base-t", TerrainCatalog.DefinitionFileName));
        string hash = TerrainContentHash.OfOverlay(hs, baseHash);
        File.WriteAllText(Path.Combine(hs, "overlay.json"), $"{{\"format\":1,\"contentHash\":\"{hash}\",\"base\":{{\"name\":\"base-t\",\"contentHash\":\"{baseHash}\"}},\"scenario\":\"s\",\"world\":\"overlay.world.geojson\"}}");

        TerrainWorld Load()
        {
            using var holder = new RoadNetworkHolder();
            return new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder).Prepare("base-t", hs).World!;
        }
        var a = Load(); var b = Load();
        Assert.NotSame(a, b);
        Assert.Equal(a.Prisms.Select(p => (p.Label, p.Panel, p.Building, p.Kind, p.Footprint.Length)), b.Prisms.Select(p => (p.Label, p.Panel, p.Building, p.Kind, p.Footprint.Length)));
        Assert.Equal(a.Panels.Select(p => (p.A, p.B, p.Building, p.Storey)), b.Panels.Select(p => (p.A, p.B, p.Building, p.Storey)));
        Assert.Equal(a.Buildings.Select(x => x.Label), b.Buildings.Select(x => x.Label));
        Assert.Equal(a.Doors.Select(d => (d.Key, d.Panel, d.Opening)), b.Doors.Select(d => (d.Key, d.Panel, d.Opening)));
        Assert.Equal(a.Walkables.Select(w => w.Building), b.Walkables.Select(w => w.Building));
        Assert.Equal(a.Surfaces.Count, b.Surfaces.Count);
        Assert.Contains(a.Doors, d => d.Key.EndsWith("/H1/front"));
        Assert.Contains(a.Buildings, x => x.Label == "bridge-1");
    }

    /// <summary>
    /// ⭐ O-4 part 2 (CE-4415, DESIGN_Terrain_Authoring §18c) — compile, then load: a scenario with a block and a house over a base terrain compiles to <c>terrain-overlay/</c>; a FRESH
    /// residency's <c>Prepare(name, folder)</c> accepts it (the hashes agree), the merged world holds the expanded houses labelled <c>&lt;guid&gt;/&lt;i&gt;</c> with their doors, and the base's own
    /// parts keep their indices; changing a byte of the base afterwards makes the same overlay REFUSED. 🔴 Red-proof: have the compiler hash another base (or none) ⇒ the load refuses.
    /// </summary>
    [Fact]
    public void ACompiledOverlay_LoadsInAFreshResidency_AndIsRefusedOnceTheBaseChanges_O4()
    {
        const string hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";
        string assets = Path.Combine(_stagingRoot, "assets");
        string terrains = Path.Combine(assets, "terrain"), folder = Path.Combine(terrains, "base-t");
        Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(assets, "buildings")); Directory.CreateDirectory(Path.Combine(assets, "terrain-recipes"));
        File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2,"world":"w.geojson"}""");
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[]}""");
        File.WriteAllText(Path.Combine(assets, "buildings", "hut.building.json"), hut);
        File.WriteAllText(Path.Combine(assets, "terrain-recipes", "village.recipe.json"), """{"id":"village","houses":{"templates":["hut"],"count":[6,6],"radius":50,"minSpacing":4},"woods":{"count":[1,1],"size":[8,12]}}""");
        string scenarioDir = Path.Combine(_stagingRoot, "scn"); Directory.CreateDirectory(scenarioDir);
        const string block = "b1000000-0000-4000-8000-000000000001", house = "c1000000-0000-4000-8000-000000000002";
        var dom = System.Text.Json.Nodes.JsonNode.Parse("""
            {"header":{"terrainName":"base-t"},"entities":{
              "BLOCK":{"TerrainBlock":{"Recipe":"village","Seed":5,"HeadingDeg":0,"Fit":15},"SimTransform":{"Position":[200,200,0]}},
              "HOUSE":{"TerrainFeature":{"Kind":1,"Template":"hut","HeadingDeg":90,"Pad":0},"SimTransform":{"Position":[350,350,0]}}}}
            """.Replace("BLOCK", block).Replace("HOUSE", house))!.AsObject();
        var catalog = new TerrainCatalog(new[] { terrains });
        var r = OverlayCompiler.Compile(dom, scenarioDir, catalog.ResolveDefinition, bakeNavtiles: false);
        Assert.True(r.Wrote);
        string overlay = Path.Combine(scenarioDir, OverlayCompiler.FolderName);

        TerrainWorld Load()
        {
            using var holder = new RoadNetworkHolder();
            return new TerrainResidency(catalog, holder).Prepare("base-t", overlay).World!;
        }
        var w = Load();
        var labels = w.Buildings.Select(b => b.Label).ToList();
        Assert.Equal(7, labels.Count);
        Assert.Contains(house + "/0", labels);
        for (int i = 0; i < 6; i++) Assert.Contains($"{block}/{i}", labels);
        Assert.Equal(7, w.Doors.Count);
        Assert.Contains(w.Doors, d => d.Key == $"base-t/{block}/3/front");
        Assert.Single(w.Surfaces);
        Assert.Equal(labels, Load().Buildings.Select(b => b.Label).ToList());   // a second fresh residency: the same world

        File.WriteAllText(Path.Combine(folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[ ]}""");
        Assert.Throws<InvalidOperationException>(() => Load());
    }

    /// <summary>A content loader that counts its prepares.</summary>
    private sealed class CountingContentLoader : ITerrainContentLoader
    {
        public int Prepares;
        public object? Prepare(TerrainDefinition definition, string terrainFolder, string terrainName, TerrainWorld world, string? overlayFolder) { Prepares++; return null; }
        public void Commit(EntityRepository world, object? prepared) { }
        public void Unload(EntityRepository world) { }
    }

    /// <summary>⭐ CE-4013 (§4.1ae T1) — terrain content is the NODE's: a loader added to one node's registry loads on that node's
    /// residency only, even when it was added after the residency was built (a capability composes after the host built it).</summary>
    [Fact]
    public void CE4013_TerrainContent_IsPerNode_AndAttachesWhenAddedLater()
    {
        WriteTerrainDefinition("plain");
        using var holder = new RoadNetworkHolder();
        var mine = new TerrainContentRegistry();
        var other = new TerrainContentRegistry();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder, mine);
        var neighbour = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder, other);
        var loader = new CountingContentLoader();

        mine.Add("test-content", () => loader);   // after both residencies exist
        residency.Prepare("plain");
        neighbour.Prepare("plain");

        Assert.Equal(1, loader.Prepares);
        Assert.Same(mine, residency.Content);
    }

    /// <summary>⭐ CE-4013 (§4.1ae T2) — HROT's universal load parts are written once: the knowledge base, then the terrain, over the
    /// node's terrain content.</summary>
    [Fact]
    public void CE4013_TheUniversalParts_AreTheKnowledgeBaseThenTheTerrain_OverTheNodesContent()
    {
        using var holder = new RoadNetworkHolder();
        var content = new TerrainContentRegistry();

        var (parts, residency) = HrotUniversalLoadParts.Build(null, _stagingRoot, holder, content);

        Assert.Collection(parts, p => Assert.IsType<KnowledgeBaseLoadStep>(p), p => Assert.IsType<TerrainLoadStep>(p));
        Assert.Same(content, residency.Content);
    }

    /// <summary>
    /// ⭐ EQS §19 — a terrain commit publishes the terrain's COVER database (building edges), so a cover query has points on
    /// every node that loaded the terrain; unload replaces it with an empty one (never the previous terrain's cover).
    /// </summary>
    [Fact]
    public void Commit_PublishesTheTerrainsCover_AndUnloadEmptiesIt()
    {
        var folder = Path.Combine(_terrainDir, "blocks");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "terrain.json"),
            """{"schemaVersion":2,"world":"w.geojson"}""", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","height":10},
               "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
            """, new UTF8Encoding(false));

        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        residency.Commit(world, residency.Prepare("blocks"));

        var cover = Assert.IsType<Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider>(
            world.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>());
        Assert.Equal(16, cover.Points.Count);   // four 10 m faces, a point every 2.5 m
        var near = new Fdp.Toolkit.Spatial.Eqs.CoverPoint[32];
        Assert.True(cover.GetCoverPointsInRadius(new System.Numerics.Vector2(5, -1), 3f, near) > 0);

        residency.Unload(world);
        Assert.Empty(Assert.IsType<Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider>(
            world.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>()).Points);
    }

    // ── what a scenario's terrain does to the resident one (CE-3075) ──────────────────────────

    /// <summary>A v2 terrain folder with one 10 m building at the origin; its name is the folder name.</summary>
    private void WriteBlockTerrain(string name)
    {
        var folder = Path.Combine(_terrainDir, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "terrain.json"),
            """{"schemaVersion":2,"world":"w.geojson"}""", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"building","height":10},
               "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
            """, new UTF8Encoding(false));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3075</c> — 🔒 user, <c>2026-10-05</c>: <i>"unload terrain when scenario names none."</i> A scenario that
    /// names no terrain, loaded over one that did, used to INHERIT it (measured live: <c>hill-attack-close</c> after
    /// <c>test-town</c> ran on test-town). Now the commit leaves no terrain: no definition, an empty unnamed world, no
    /// cover — and a later load of the same terrain re-ingests.
    /// </summary>
    [Fact]
    public void AScenarioNamingNoTerrain_UnloadsTheResidentOne_CE3075()
    {
        WriteBlockTerrain("blocks");
        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        residency.Commit(world, residency.Prepare("blocks"));
        Assert.Equal("blocks", TerrainDefinition.ResidentName(world));

        var staged = residency.Prepare(null);
        Assert.True(staged.HasWork);                                    // ⛔ was: nothing to do ⇒ the old terrain stayed
        Assert.Equal("blocks", TerrainDefinition.ResidentName(world));  // ⭐ prepare touches no ECS
        residency.Commit(world, staged);

        Assert.Null(TerrainDefinition.ResidentName(world));
        Assert.Null(world.GetSingletonManaged<TerrainDefinition>());
        var tw = world.GetSingletonManaged<TerrainWorld>()!;
        Assert.True(string.IsNullOrEmpty(tw.Name));
        Assert.Empty(tw.Prisms);
        Assert.Equal(0f, tw.SurfaceZ(5, 5, 0f));                         // flat ground where the building stood
        Assert.Empty(Assert.IsType<Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider>(
            world.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>()).Points);

        // Nothing resident any more ⇒ a second terrain-less load has nothing to do, and the same terrain loads again.
        Assert.False(residency.Prepare(null).HasWork);
        residency.Commit(world, residency.Prepare("blocks"));
        Assert.Equal("blocks", TerrainDefinition.ResidentName(world));
        Assert.Single(world.GetSingletonManaged<TerrainWorld>()!.Prisms);
    }

    /// <summary>
    /// ⭐ <c>CE-3075</c> — the other two halves of the same rule: 🔒 <i>"the terrain only stays untouched if scenario load
    /// requests same terrain as already loaded"</i>; a DIFFERENT terrain replaces it.
    /// </summary>
    [Fact]
    public void TheSameTerrainStaysUntouched_ADifferentOneReplacesIt_CE3075()
    {
        WriteBlockTerrain("blocks");
        WriteBlockTerrain("other");
        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        residency.Commit(world, residency.Prepare("blocks"));
        var first = world.GetSingletonManaged<TerrainWorld>();

        Assert.False(residency.Prepare("blocks").HasWork);              // same ⇒ untouched
        Assert.Same(first, world.GetSingletonManaged<TerrainWorld>());

        residency.Commit(world, residency.Prepare("other"));            // different ⇒ replaced
        Assert.Equal("other", TerrainDefinition.ResidentName(world));
        Assert.NotSame(first, world.GetSingletonManaged<TerrainWorld>());
    }

    [Fact]
    public void ADefinitionListingARoadNetwork_PopulatesZoneEnvironmentData_WithNoZonesSection()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);

            // ⭐ PrepareAsync must NOT mutate ECS — that is the interface's contract.
            Assert.False(world.HasSingleton<ZoneEnvironmentData>());
            Assert.False(world.HasSingletonManaged<TerrainDefinition>());

            handler.Commit(intent, world);

            Assert.True(world.HasSingleton<ZoneEnvironmentData>());
            var blob = world.GetSingleton<ZoneEnvironmentData>().RoadNetwork;
            Assert.True(blob.Nodes.IsCreated);
            Assert.Equal(2, blob.Nodes.Length);

            var def = world.GetSingletonManaged<TerrainDefinition>()!;
            Assert.Equal("kandahar", def.Name);
            Assert.Single(def.RoadNetworks);
        }
    }

    [Fact]
    public void TheCommittedGraph_IsPublishedIntoTheHolder_SoABackgroundSolverCanSeeIt()
    {
        // ⭐ C7's half: the singleton alone is unreachable from a SlowBackground module.
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world);

            using var lease = holder.Borrow();
            Assert.True(lease.Value.Nodes.IsCreated);
            Assert.Equal(2, lease.Value.Nodes.Length);
        }
    }

    // ── the differential cache ────────────────────────────────────────────────────────────────

    [Fact]
    public void ReloadingTheSameUnchangedTerrain_DoesNoReIngestion()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var first = Intent();
            Prepare(handler, first);
            handler.Commit(first, world);

            // ⭐ The cache key is (name, file timestamp) — unchanged ⇒ the second prepare stages nothing,
            //   so the commit has nothing to publish and the holder does not gain a generation.
            var second = Intent();
            Prepare(handler, second);
            handler.Commit(second, world);

            Assert.Equal(1, holder.LiveGenerations);
        }
    }

    [Fact]
    public void AnAbortedRound_DoesNotAdvanceTheCache_SoTheNextAttemptReIngests()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();
        using var world = NewWorld();
        using (holder)
        {
            var aborted = Intent();
            Prepare(handler, aborted);
            handler.Abort(aborted);

            Assert.False(world.HasSingleton<ZoneEnvironmentData>());

            // ⛔ Believing a load that never committed is how a node ends up with no road graph and a
            //    cache that says it has one.
            var retry = Intent();
            Prepare(handler, retry);
            handler.Commit(retry, world);

            Assert.True(world.HasSingleton<ZoneEnvironmentData>());
        }
    }

    // ── ⭐⭐⭐ THE RAIL THAT MATTERS ────────────────────────────────────────────────────────────

    [Fact]
    public void ANodeWithNoAuthoringDeps_StillGetsTheTerrainPart()
    {
        // ⛔⛔ The scenario LOAD step is offered only when a host has the full authoring deps
        //    (extractor / source / id-allocator). A pure MuscleGround node passes NONE of them — and the
        //    muscle is precisely the role that consumes the road network. Hanging terrain off the scenario
        //    step would leave it unloaded on the node that needs it most.
        // ⭐ The rail is now a statement about the CHAIN rather than about a registration order: the role
        //   requires the terrain part, so the chain carries it whatever else this host does or does not
        //   compose. 📄 docs/DESIGN_Cluster_Load_Phase.md §4.1b.
        var bootstrapper = new NodeBootstrapper();
        using var world  = new EntityRepository();
        using var kernel = new Fdp.ModuleHost.ModuleHostKernel(world, new EventAccumulator());

        // ⚠ tkbDb is handed in because PRODUCTION hands it in — every ECS node needs a knowledge base,
        //   and composing without one now throws by design. That throw is asserted in LoadPhaseChainTests.
        using var slave = bootstrapper.BuildOrchestration(
            NodeRole.MuscleGround, kernel, world, nodeId: 1,
            participant: null, eventBus: new FdpEventBus(),
            tkbDb: Hrot.Map.Common.HrotEnvironment.CreateTkb());

        Assert.True(slave.IsHandlerRegistered<Hrot.Map.Common.ClusterLoad.LoadPhaseChain>(),
            "every ECS host registers the load-phase chain — it is the ONE participant in the load step");

        var parts = Hrot.Map.Common.ClusterLoad.RoleLoadRequirements.PartsFor(NodeRole.MuscleGround);
        Assert.Contains(Hrot.Map.Common.ClusterLoad.LoadPart.Terrain, parts);
        Assert.Contains(Hrot.Map.Common.ClusterLoad.LoadPart.KnowledgeBase, parts);
        Assert.DoesNotContain(Hrot.Map.Common.ClusterLoad.LoadPart.ScenarioEntities, parts);
    }

    /// <summary>
    /// 🔴 <b>The defect this rail was written against, and why every other test here was blind to it.</b>
    ///
    /// <para>Every test above calls <c>handler.Commit(intent, world)</c> — handing the repository in.
    /// <b><c>ClusterSlave</c> never does that.</b> It commits with <c>repo: null</c> at BOTH of its
    /// dispatch sites (<c>ClusterSlave.cs:271</c> and <c>:432</c>), so the handler as originally written
    /// took its "no-ECS host" branch on EVERY host: it disposed the staged blob and published nothing —
    /// no <c>TerrainDefinition</c>, no <c>ZoneEnvironmentData</c>, no holder swap. A capability that
    /// reports present and silently no-ops (<c>R-133</c>), invisible because the suite exercised a path
    /// production does not take.</para>
    ///
    /// <para>⭐ The fix is the established pattern — <c>repo ?? _world</c>, as
    /// <c>HrotScenarioLoadHandler.cs:196</c> already does. This rail drives commit the way the slave
    /// does, with a NULL repo, and requires the publish to land anyway.</para>
    /// </summary>
    [Fact]
    public void TheCHAINPublishesThroughItsOwnWorld_BecauseClusterSlaveAlwaysCommitsWithNullRepo()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        using var world  = NewWorld();
        using var holder = new RoadNetworkHolder();

        // ⭐ Driven exactly as production does: the CHAIN holds the world, and the slave commits with a
        //   null repository. ⚠ The step itself no longer holds a world at all — which is the structural
        //   half of the fix: the trap can no longer be re-introduced per host, because there is only one
        //   place left that can fall into it.
        var chain = Hrot.Map.Common.ClusterLoad.LoadPhaseChain.FromRoles(
            NodeRole.MuscleGround,
            new Hrot.Map.Common.ClusterLoad.ILoadPartProvider[]
            {
                new Hrot.Map.Common.ClusterLoad.KnowledgeBaseLoadStep(
                    Hrot.Map.Common.HrotEnvironment.CreateTkb(), _stagingRoot),
                new TerrainLoadStep(new TerrainResidency(_stagingRoot, holder), _stagingRoot),
            },
            world);

        var intent = new ExecuteNodeOpIntent
        {
            Operation     = NodeOpType.PrepareLive,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload(
                ScenarioId: "scn", TargetState: ClusterState.LoadingLive),
        };

        chain.PrepareAsync(intent, CancellationToken.None).GetAwaiter().GetResult();
        chain.Commit(intent, repo: null);

        Assert.True(world.HasSingleton<ZoneEnvironmentData>(),
            "ClusterSlave commits with repo: null, so the chain must publish through the world it holds");
        Assert.Equal("kandahar", world.GetSingletonManaged<TerrainDefinition>()!.Name);
        Assert.True(world.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Nodes.IsCreated);
    }

    /// <summary>
    /// ⚠ The other half of the same contract: a genuinely no-ECS host (ExCon / CGF skeleton) has no
    /// world to inject, and it must still ACK cleanly rather than throw — it simply has nowhere to
    /// publish, and the staged blob is freed rather than leaked.
    /// </summary>
    [Fact]
    public void ANoEcsHost_WithNoWorldAndNoRepo_StillCommitsCleanly()
    {
        WriteScenarioHeader("kandahar");
        WriteRoadNetwork("roads.json");
        WriteTerrainDefinition("kandahar", "roads.json");

        var (handler, holder) = NewHandler();   // no world injected
        using (holder)
        {
            var intent = Intent();
            Prepare(handler, intent);
            handler.Commit(intent, world: null);   // must not throw

            // ⭐ Still only the holder's initial generation — nothing was published, and the staged blob
            //   was freed rather than leaked. (LiveGenerations is `1 + retired-but-leased`, so 1 is the
            //   floor, not zero.)
            Assert.Equal(1, holder.LiveGenerations);
        }
    }

    // ══ S4 — WHAT TERRAIN INHERITS FROM THE ARTIFACT-STAGING BATCH, AND WHAT IT DOES NOT ═══════════
    //
    // ⭐⭐ S4 was dispatched as a CONFIRMATION, not a port: "terrain mirrors the TKB loader field for
    //    field and should inherit both skips with NO new code; if it needs its own path, that is a
    //    FINDING." 📐 Measured, it is BOTH — and the split is exactly on the header/artifact line.

    /// <summary>
    /// ✅ <b><c>S4</c>, the half that DOES inherit with no new code:</b> the terrain NAME rides the same
    /// staged <c>ScenarioHeader.json</c> the TKB loader reads, so the orchestrator's <c>S2c</c> writer
    /// serves both readers from one file.
    ///
    /// <para>⭐ Asserted through <c>StorageGatewayModule.BuildStagedHeaderJson</c> — the ORCHESTRATOR's
    /// own writer — rather than a hand-written fixture. ⛔ A fixture that writes the header itself could
    /// not catch the orchestrator emitting a shape the node cannot parse, which is the actual risk:
    /// the scenario's own header block is nested and camelCase, and this one is flat and PascalCase.</para>
    /// </summary>
    [Fact]
    public void TheOrchestratorsStagedHeaderIsReadableByTheTerrainReader()
    {
        var json = Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(
            new Hrot.Orchestrator.StagedArtifactNames("basic-desert"));
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), json, new UTF8Encoding(false));

        Assert.Equal("basic-desert", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(_stagingRoot));
    }

    /// <summary>
    /// ⭐ A scenario naming NO terrain reads back as "no terrain" — ⛔ not as an empty
    /// string or a throw. ⚠ The no-terrain path stays legal, exactly like the no-TKB one.
    /// </summary>
    [Fact]
    public void AStagedHeaderWithNoTerrainNameReadsBackAsNull()
    {
        var json = Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(
            new Hrot.Orchestrator.StagedArtifactNames(null));
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), json, new UTF8Encoding(false));

        Assert.Null(Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(_stagingRoot));
    }

    /// <summary>
    /// 🔴🔴 <b><c>S4</c>'s FINDING — terrain does NOT inherit the ARTIFACT half, and cannot.</b>
    ///
    /// <para>📐 Measured: the TKB artifact is <c>{{node}}/TKB/{{name}}.zip</c>
    /// (<c>TkbLoadClusterStateHandler.cs:78</c>) and the terrain definition is
    /// <c>{{node}}/Terrain/{{name}}.json</c> (<c>TerrainLoadClusterStateHandler.cs:138</c>) — a
    /// DIFFERENT DIRECTORY and a DIFFERENT EXTENSION. ⇒ <c>S2b</c>, which copies one named zip out of
    /// <c>{{nas}}/tkb</c>, cannot serve it; staging terrain needs a second artifact KIND.</para>
    ///
    /// <para>⛔ <b>Deliberately NOT built here.</b> The dispatch ruled that a terrain-specific path is a
    /// FINDING to report rather than work to do, and a second artifact kind is squarely the wider
    /// asset-management model the dispatch fenced off. ⭐ This rail PINS the gap so it cannot be
    /// mistaken for done: it asserts the two roots differ, which is the whole reason.</para>
    ///
    /// <para>⇒ <c>BP-550</c> is closed for the TKB and stays OPEN for terrain. 📄 reported in
    /// docs/blueprints/batches/REPORT_Artifact_Staging.md and folded into
    /// docs/DESIGN_Artifact_Staging.md §5a.</para>
    /// </summary>
    [Fact]
    public void TerrainArtifactsAreNotStagedByTheTkbPath_AndTheRootsDiffer()
    {
        var tkbRoot = Fdp.Toolkit.Orchestration.OrchestrationConstants.GetTkbStagingRoot(_stagingRoot);

        Assert.Equal(_tkbDir, tkbRoot);
        Assert.NotEqual(_terrainDir, tkbRoot);

        // ⭐ The header (S2c) reaches the terrain reader; the DEFINITION file does not arrive with it.
        var json = Hrot.Orchestrator.StorageGatewayModule.BuildStagedHeaderJson(
            new Hrot.Orchestrator.StagedArtifactNames("basic-desert"));
        File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), json, new UTF8Encoding(false));

        Assert.Equal("basic-desert", Fdp.Toolkit.Terrain.ScenarioTerrainName.Read(_stagingRoot));
        Assert.False(File.Exists(Path.Combine(_terrainDir, "basic-desert.json")),
            "nothing in the TKB staging path publishes a terrain definition — that is S4's finding");
    }

    /// <summary>
    /// ⭐⭐ CE-3126 (R-229) — committing a terrain sets the node's geo transform (the world singleton) to the terrain's own
    /// origin; a terrain that declares none, and unloading, give 0,0,0. 📄 docs/DESIGN_Geo_Origin.md §2 B/D.
    /// </summary>
    [Fact]
    public void CE3126_Commit_SetsTheGeoOriginFromTheTerrain_MissingAndUnloadAreZero()
    {
        foreach (var (name, json) in new[]
        {
            ("placed", """{"schemaVersion":2,"origin":{"lat":48.1,"lon":11.5,"alt":500}}"""),
            ("unplaced", """{"schemaVersion":2}"""),
        })
        {
            var folder = Path.Combine(_terrainDir, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "terrain.json"), json, new UTF8Encoding(false));
        }

        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var geo = Hrot.Map.Common.HrotEnvironment.CreateGeoTransform();
        world.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);

        residency.Commit(world, residency.Prepare("placed"));
        Assert.Equal(48.1, geo.Origin.lat, 9);
        Assert.Equal(11.5, geo.Origin.lon, 9);
        Assert.Equal(500.0, geo.Origin.alt, 9);
        Assert.Equal(32, Assert.IsType<Fdp.Modules.Geographic.Transforms.UtmTransform>(geo.Inner).Zone);   // ⭐ CE-4396: the origin IS the UTM frame

        residency.Commit(world, residency.Prepare("unplaced"));
        Assert.False(geo.HasFrame);   // ⛔ no default place: a terrain without an origin is LOCAL-ONLY, geodetic calls refused
        Assert.Throws<Fdp.Modules.Geographic.Transforms.NoGeoFrameException>(() => geo.ToGeodetic(System.Numerics.Vector3.Zero));

        residency.Commit(world, residency.Prepare("placed"));
        residency.Unload(world);
        Assert.False(geo.HasFrame);
    }

    /// <summary>
    /// ⭐ CE-4394 (UTM §12.2 U-2) — a terrain that declares its exact UTM frame switches the node's ONE transform to it (the object every
    /// holder keeps is the same; its inside changes), and the next origin-only terrain switches it to the UTM frame its origin gives. ✅ Red-proof: Commit not passing
    /// the definition's frame ⇒ the transform stays a tangent plane ⇒ red.
    /// </summary>
    [Fact]
    public void CE4394_Commit_SelectsTheTerrainsUtmFrame_OnTheOneTransform_AndATangentTerrainSwitchesBack()
    {
        foreach (var (name, json) in new[]
        {
            ("utm", """{"schemaVersion":2,"origin":{"lat":54.5,"lon":7.0,"alt":0},"frame":{"utm":{"zone":32,"hemisphere":"N","e0":372000,"n0":6043000},"corner":[100,200]}}"""),
            ("placed", """{"schemaVersion":2,"origin":{"lat":48.1,"lon":11.5,"alt":500}}"""),
        })
        {
            var folder = Path.Combine(_terrainDir, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "terrain.json"), json, new UTF8Encoding(false));
        }

        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var geo = Hrot.Map.Common.HrotEnvironment.CreateGeoTransform();
        world.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
        Fdp.Modules.Geographic.IGeographicTransform kept = geo;   // what every translator / module holds
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);

        residency.Commit(world, residency.Prepare("utm"));
        var utm = Assert.IsType<Fdp.Modules.Geographic.Transforms.UtmTransform>(geo.Inner);
        Assert.Equal(372_100.0, utm.E0);
        Assert.Equal(6_043_200.0, utm.N0);
        var (lat, lon, _) = kept.ToGeodetic(System.Numerics.Vector3.Zero);
        var exact = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(372_100.0, 6_043_200.0, 32, false);
        Assert.Equal(exact.LatDeg, lat, 9);
        Assert.Equal(exact.LonDeg, lon, 9);

        residency.Commit(world, residency.Prepare("placed"));
        var byRule = Assert.IsType<Fdp.Modules.Geographic.Transforms.UtmTransform>(geo.Inner);   // ⭐ CE-4396: an origin-only terrain is UTM by the rule
        Assert.Equal(32, byRule.Zone);
        Assert.Equal(48.1, kept.Origin.lat, 9);
    }

    /// <summary>
    /// ⭐ CE-3128 (Nav v2 §5.2a D7) — a terrain commit publishes the node's road graph CARRIER as a world singleton, so a reader on a
    /// background snapshot (the EQS solver's danger sensor) leases the graph through <c>RoadNetworkSource.Live</c>; unload keeps it
    /// published (empty graph), never a stale one.
    /// </summary>
    [Fact]
    public void CE3128_Commit_PublishesTheRoadHolder_ForBackgroundReaders()
    {
        var folder = Path.Combine(_terrainDir, "plain");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2}""", new UTF8Encoding(false));

        using var holder = new RoadNetworkHolder();
        using var world = NewWorld();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
        var live = CarKinem.Road.RoadNetworkSource.Live(world);
        Assert.Null(live());

        residency.Commit(world, residency.Prepare("plain"));
        Assert.Same(holder, live());

        residency.Unload(world);
        Assert.Same(holder, live());
    }

    /// <summary>A bake that waits until the test lets it finish — the slow navigation node of CE-2242.</summary>
    private sealed class GatedNavmeshFactory : Fdp.Toolkit.Navigation.INavmeshFactory
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Release = new(false);
        public Fdp.Toolkit.Navigation.INavmeshProvider? Build(TerrainWorld world)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
            return new Fdp.Toolkit.Navigation.Fake.FakeNavmeshProvider();
        }
    }

    /// <summary>
    /// ⭐⭐ CE-2242 — 📐 measured live: a node still baking its navmesh received the scenario's entities (other nodes had
    /// committed and CGF spawned) with its geo origin unset, so every geodetic position landed ~900 km out. On a load into an
    /// empty world (the world boundary, a late joiner's catch-up) the origin is in place BEFORE the bake; any other load still
    /// changes it only at commit (docs/DESIGN_Geo_Origin.md §2 D, §4).
    /// </summary>
    [Fact]
    public void CE2242_OnAFreshWorld_TheGeoOriginIsSetBeforeTheBakeFinishes_OtherLoadsWaitForCommit()
    {
        foreach (var (boundary, catchUp) in new[] { (true, false), (false, true), (false, false) })
        {
            string folder = Path.Combine(_terrainDir, "placed");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName),
                """{"schemaVersion":2,"world":"w.geojson","origin":{"lat":48.1,"lon":11.5,"alt":500}}""", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "w.geojson"), """
                {"type":"FeatureCollection","features":[
                  {"type":"Feature","properties":{"kind":"building","height":10},
                   "geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}}]}
                """, new UTF8Encoding(false));

            using var holder = new RoadNetworkHolder();
            using var world  = NewWorld();
            var geo = Hrot.Map.Common.HrotEnvironment.CreateGeoTransform();
            world.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
            var residency = new TerrainResidency(new TerrainCatalog(new[] { _terrainDir }), holder);
            var bake = new GatedNavmeshFactory();
            residency.AttachNavmesh(bake, new Fdp.Toolkit.Navigation.SwitchableNavmeshProvider());

            var chain = Hrot.Map.Common.ClusterLoad.LoadPhaseChain.FromRoles(
                NodeRole.MuscleGround,
                new Hrot.Map.Common.ClusterLoad.ILoadPartProvider[]
                {
                    new Hrot.Map.Common.ClusterLoad.KnowledgeBaseLoadStep(Hrot.Map.Common.HrotEnvironment.CreateTkb(), _stagingRoot),
                    new TerrainLoadStep(residency, _stagingRoot),
                },
                world);

            var intent = new ExecuteNodeOpIntent
            {
                Operation     = NodeOpType.PrepareLive,
                TransactionId = Guid.NewGuid(),
                DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.EditLoadHandlerPayload(
                    ScenarioId: "scn", TargetState: ClusterState.LoadingLive, TerrainName: "placed",
                    IsWorldBoundary: boundary, IsCatchUp: catchUp),
            };

            var prepare = chain.PrepareAsync(intent, CancellationToken.None);
            Assert.True(bake.Entered.Wait(TimeSpan.FromSeconds(10)), "the navmesh bake must start");
            Assert.False(prepare.IsCompleted);

            if (boundary || catchUp)
                Assert.Equal(48.1, geo.Origin.lat, 9);           // ⭐ in place while the bake still runs
            else
                Assert.Equal((0.0, 0.0, 0.0), geo.Origin);       // ⛔ a load over a live world moves it only at commit

            bake.Release.Set();
            prepare.GetAwaiter().GetResult();
            chain.Commit(intent, repo: null);
            Assert.Equal(48.1, geo.Origin.lat, 9);
            Assert.Equal(11.5, geo.Origin.lon, 9);
        }
    }
}
