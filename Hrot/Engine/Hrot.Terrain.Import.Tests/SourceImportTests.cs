using System.Text.Json;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Import.Tests;

[CollectionDefinition("TerrainSources", DisableParallelization = true)]
public sealed class TerrainSourcesCollection { }

/// <summary>
/// ⭐ CE-11037 (docs/DESIGN_Terrain_Import_UTM.md §13) — a terrain imports on first use from its committed input, through the catalog's hook.
/// Small synthetic input sets (MiniSet / hrot-terrain-synth), never Stechovice. The hook is process-wide, so the class runs alone.
/// </summary>
[Collection("TerrainSources")]
public sealed class SourceImportTests : IDisposable
{
    private readonly Env _env = new();
    private string Content => Path.Combine(_env.Root, "content");
    private string TerrainRoot => Path.Combine(Content, "assets", "terrain");
    private string InputRoot => Path.Combine(Content, "assets", "terrain-input");
    private int _imports;
    private readonly List<string> _log = new();

    public SourceImportTests() { }
    public void Dispose() { TerrainSources.Import = null; TerrainSources.SharedImportRoot = null; _env.Dispose(); }

    private SourceImport Hook(Action<string>? onLog = null)
        => new(root => { Interlocked.Increment(ref _imports); return SourceImport.DefaultImporter(root); },
               m => { lock (_log) _log.Add(m); onLog?.Invoke(m); }, repoRoot: Path.Combine(_env.Root, "no-repo"));

    private TerrainCatalog Install(SourceImport? hook = null)
    {
        TerrainSources.Import = hook ?? Hook();
        return TerrainCatalog.ForNode(null, Content);
    }

    private static void ImportNow(TerrainImporter imp, string set, string name, bool replace, IProgress<ImportProgress>? p, CancellationToken ct) => imp.ImportAsync(set, name, replace, p, ct).GetAwaiter().GetResult();
    private static bool Done(Task t, int ms) => t.Wait(ms);
    private static string? Resultof(Task<string?> t) => t.Result;

    private string Input(string name, MiniSet? mini = null) => (mini ?? new MiniSet()).Write(Path.Combine(InputRoot, name));
    private static string Sha(string set) => Manifest.Sha256(Path.Combine(set, "manifest.json"));
    private static string Recorded(string def) => SourceImport.RecordedSha(def)!;

    [Fact]
    public void AMissingTerrain_WithAnInputSet_ImportsOnce_ThenResolvesWithNoImport()
    {
        string set = Input("mini");
        var catalog = Install();
        var def = catalog.ResolveDefinition("mini");
        Assert.Equal(Path.Combine(TerrainRoot, "mini", "terrain.json"), def);
        Assert.Equal(1, _imports);
        for (int i = 0; i < 5; i++) Assert.Equal(def, catalog.ResolveDefinition("mini"));
        Assert.Equal(1, _imports);
        Assert.Equal(Sha(set), Recorded(def!));                       // terrain.json records the input's manifest sha256
        Assert.NotNull(Env.Load(Path.GetDirectoryName(def)!, "mini").Height);   // and it LOADS through the real parsers
        Assert.Empty(Directory.GetDirectories(TerrainRoot, "*.import-*"));
    }

    [Fact]
    public void AFirstUseImport_PrefillsTheNavmesh_AndTheFollowingLoadBakesNothing()
    {
        Input("mini", new MiniSet { Buildings = { MiniSet.Rect("a", -40, -30, -20, -14, storeys: 2) } });
        var def = Install().ResolveDefinition("mini")!;
        string folder = Path.GetDirectoryName(def)!, navtiles = Path.Combine(folder, Fdp.Toolkit.Navigation.Recast.NavTileCache.TerrainFolderName);
        Assert.True(Directory.Exists(navtiles));
        Assert.NotEmpty(Directory.EnumerateFiles(navtiles, "*" + Fdp.Toolkit.Navigation.Recast.NavTilePack.Extension, SearchOption.AllDirectories));   // packs
        var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "mini")!;
        var factory = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory { TileCache = new Fdp.Toolkit.Navigation.Recast.NavTileCache(Path.Combine(_env.Root, "own-cache")) };
        Assert.NotNull(factory.Build(world));
        Assert.Equal(0, factory.LastStats.Baked);                    // the first load bakes nothing
        Assert.True(factory.LastStats.FromCache > 0);
    }

    /// <summary>
    /// ⭐ CE-11040 (handoff ui → terrain, Stechovice showcase §2 row 4) — a first use imports INSIDE the node's load (6 min for Stechovice on 4 cores), and the
    /// import's stages reach the load's one progress report (<see cref="Fdp.Core.LoadProgress.Detail"/>), so the 30 s stall check sees it advancing.
    /// 🔴 Mutation: <c>null</c> progress at <c>SourceImport.TryImport</c> again ⇒ the detail never says "import …" ⇒ red.
    /// </summary>
    [Fact]
    public void CE11040_AFirstUseImport_ReportsItsStages_IntoTheLoadsProgress()
    {
        Input("mini");
        var progress = new Fdp.Core.LoadProgress();
        Fdp.Core.LoadProgress.Current = progress;
        string? def;
        try { def = Install().ResolveDefinition("mini"); }
        finally { Fdp.Core.LoadProgress.Current = null; }
        Assert.NotNull(def);
        Assert.Equal(1, _imports);
        Assert.Matches(@"^import \w+ 1000/1000$", progress.Detail!);   // the last stage's last call (done == total) always lands
    }

    /// <summary>
    /// ⭐⭐ <c>CE-5060</c> (DESIGN_Load_GeoReference_From_Nodes §2) — on a FIRST use the frame-thread readers (the master's GeoReference, the node's origin-ahead) get the
    /// origin WITHOUT importing: <see cref="TerrainCatalog.PeekDefinition"/> answers from the input set's manifest — the very origin and UTM frame the import then writes.
    /// 🔴 Mutation: PeekDefinition through ResolveDefinition ⇒ <c>_imports</c> 1 at the peek.
    /// </summary>
    [Fact]
    public void CE5060_PeekDefinition_OnAFirstUse_IsTheImportsOrigin_WithoutImporting()
    {
        Input("mini");
        var catalog = Install();
        var peek = catalog.PeekDefinition("mini");
        Assert.Equal(0, _imports);                                                                          // ⭐ nothing imported, no lock taken
        Assert.False(Directory.Exists(Path.Combine(TerrainRoot, "mini")));
        Assert.NotNull(peek?.Origin);
        var written = catalog.ReadDefinition("mini")!;                                                      // now the import
        Assert.Equal(1, _imports);
        Assert.Equal(written.Origin, peek!.Origin);
        Assert.Equal(written.Utm, peek.Utm);
        Assert.Equal(written.Origin, catalog.PeekDefinition("mini")!.Origin);                               // and after it, the same
        Assert.Equal(1, _imports);
    }

    /// <summary>⭐ <c>CE-5060</c> — a STALE copy is peeked as its input set says (the import about to replace it), with no import; a HAND-MADE terrain (no recorded input) keeps its own origin.</summary>
    [Fact]
    public void CE5060_PeekDefinition_AStaleCopy_IsTheInputs_AHandMadeOne_ItsOwn()
    {
        Input("mini");
        var catalog = Install();
        var def = catalog.ResolveDefinition("mini")!;
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(def))!.AsObject();
        json.Remove(SourceImport.RecordedRevisionKey);
        json["origin"] = new System.Text.Json.Nodes.JsonObject { ["lat"] = 1.0, ["lon"] = 2.0, ["alt"] = 0.0 };   // stale, and (to tell them apart) a different origin
        File.WriteAllText(def, json.ToJsonString());
        var input = Install().PeekDefinition("mini")!;
        Assert.Equal(1, _imports);                                                                          // no re-import at the peek
        Assert.NotEqual(1.0, input.Origin!.Value.Lat);                                                      // the input's origin, not the stale file's
        json.Remove(SourceImport.RecordedShaKey);                                                           // now a hand-made terrain: never touched
        File.WriteAllText(def, json.ToJsonString());
        Assert.Equal(1.0, Install().PeekDefinition("mini")!.Origin!.Value.Lat);
        Assert.Equal(1, _imports);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-5060</c> P1 — every import stage advances the load's COUNT (both heartbeat sources beat only while it moves; a detail alone does not), not
    /// just its text. 🔴 Mutation: drop the <c>LoadProgress.Report()</c> in <c>LoadDetailProgress</c> ⇒ the count does not move (no navmesh prefill here, so only the import's own stage reports can move it).
    /// </summary>
    [Fact]
    public void CE5060_AFirstUseImport_AdvancesTheLoadsCount_NotOnlyItsText()
    {
        Input("mini");
        var progress = new Fdp.Core.LoadProgress();
        Fdp.Core.LoadProgress.Current = progress;
        long before = progress.Value;
        // no navmesh prefill here: the baker reports per tile itself, which would hide a stage that does not
        TerrainSources.Import = new SourceImport(root => { Interlocked.Increment(ref _imports); return new TerrainImporter(root); }, null, Path.Combine(_env.Root, "no-repo"));
        try { Assert.NotNull(TerrainCatalog.ForNode(null, Content).ResolveDefinition("mini")); }
        finally { Fdp.Core.LoadProgress.Current = null; }
        Assert.Equal(1, _imports);
        Assert.True(progress.Value - before >= 5, $"the import advanced the count {progress.Value - before} times");   // one per stage report, at least
    }

    /// <summary>⭐ <c>CE-5060</c> P2 — a load waiting for ANOTHER import of the same terrain (a second node on this machine) keeps its count moving while it waits.
    /// 🔴 Mutation: drop the report in the lock's wait loop ⇒ the count does not move while the lock is held.</summary>
    [Fact]
    public void CE5060_ALoadWaitingForAnotherImport_KeepsItsCountMoving()
    {
        string set = Input("mini");
        Directory.CreateDirectory(TerrainRoot);
        var held = new FileStream(Path.Combine(TerrainRoot, ".mini.import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var catalog = Install();
        var progress = new Fdp.Core.LoadProgress();
        var task = Task.Run(() => { Fdp.Core.LoadProgress.Current = progress; return catalog.ResolveDefinition("mini"); });
        Thread.Sleep(300);
        long a = progress.Value;
        Thread.Sleep(300);
        Assert.True(progress.Value > a, "the count moved while waiting for the lock");
        Assert.Equal("import: waiting for another import 0/1", progress.Detail);
        ImportNow(new TerrainImporter(TerrainRoot, _env.Scenarios), set, "mini", false, null, default);
        held.Dispose();
        Assert.True(Done(task, 20000));
        Assert.Equal(0, _imports);
    }

    [Fact]
    public void NoInputSet_IsNullAsToday_AndTheImporterIsNeverBuilt()
    {
        var catalog = Install();
        Assert.Null(catalog.ResolveDefinition("ghost"));
        Assert.Null(catalog.ResolveDefinition("europe/ghost"));        // a sub-folder name has no input set by construction
        Assert.Equal(0, _imports);
        Assert.False(Directory.Exists(TerrainRoot) && Directory.GetFileSystemEntries(TerrainRoot).Length > 0);
    }

    [Fact]
    public void AnExistingTerrain_WithNoRecordedSha_IsNeverTouched_EvenWhenAnInputSetHasItsName()
    {
        Input("hand");
        Directory.CreateDirectory(Path.Combine(TerrainRoot, "hand"));
        string def = Path.Combine(TerrainRoot, "hand", "terrain.json");
        File.WriteAllText(def, "{ \"schemaVersion\": 2, \"name\": \"hand\" }");
        Assert.Equal(def, Install().ResolveDefinition("hand"));
        Assert.Equal(0, _imports);
        Assert.Equal("{ \"schemaVersion\": 2, \"name\": \"hand\" }", File.ReadAllText(def));
    }

    [Fact]
    public void AnExistingCurrentTerrain_ResolvesWithoutTouchingTheImporter()
    {
        string set = Input("mini");
        var seed = new TerrainImporter(TerrainRoot, _env.Scenarios);
        ImportNow(seed, set, "mini", false, null, default);   // already there (the importer is NOT the hook's)
        var catalog = Install();
        for (int i = 0; i < 3; i++) Assert.NotNull(catalog.ResolveDefinition("mini"));
        Assert.Equal(0, _imports);
    }

    /// <summary>
    /// ⭐ <c>CE-5056</c> — a terrain imported by an OLDER importer revision (here: none recorded, as every import before revisions were) is re-imported on its
    /// next use, even though its input did not change — so a change to what the generator makes (1.2 m doors, stairs clear of doors) reaches every machine.
    /// Current afterwards: no second re-import. 🔒 Red-proof: compare the sha alone in <c>SourceImport.Current</c> ⇒ 1 import, the old doors stay.
    /// </summary>
    [Fact]
    public void CE5056_AnImportFromAnOlderImporterRevision_ReImportsOnce()
    {
        string set = Input("mini");
        var catalog = Install();
        var def = catalog.ResolveDefinition("mini")!;
        Assert.Equal(TerrainImporter.Revision, SourceImport.RecordedRevision(def));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(def))!.AsObject();
        json.Remove(SourceImport.RecordedRevisionKey);                                                      // as imported before revisions were recorded
        File.WriteAllText(def, json.ToJsonString());
        Assert.Equal(1, SourceImport.RecordedRevision(def));
        Assert.Equal(Sha(set), Recorded(def));                                                              // the input is unchanged
        Assert.Equal(def, Install().ResolveDefinition("mini"));
        Assert.Equal(2, _imports);
        Assert.Equal(TerrainImporter.Revision, SourceImport.RecordedRevision(def));
        Assert.Equal(def, Install().ResolveDefinition("mini"));
        Assert.Equal(2, _imports);                                                                          // current again
    }

    [Fact]
    public void AChangedManifest_ReImports_AndKeepsTheOldVersion()
    {
        string set = Input("mini");
        var catalog = Install();
        var def = catalog.ResolveDefinition("mini")!;
        string oldSha = Recorded(def), oldHash = TerrainContentHash.OfTerrain(def);
        var changed = new MiniSet { Ground = (x, y) => (float)(20 + 0.02 * y) };
        changed.Write(set);
        Assert.NotEqual(oldSha, Sha(set));
        var def2 = catalog.ResolveDefinition("mini");
        Assert.Equal(def, def2);
        Assert.Equal(2, _imports);
        Assert.Equal(Sha(set), Recorded(def2!));
        Assert.True(File.Exists(Path.Combine(TerrainRoot, "mini", "versions", oldHash, "terrain.json")));   // AQ112: the old one is kept by its hash
        Assert.Equal(def, catalog.ResolveDefinition("mini"));
        Assert.Equal(2, _imports);                                                                          // current again: no third import
    }

    [Fact]
    public void ACancelledImport_LeavesNothingResolvable_AndTheNextCallImports()
    {
        Input("mini");
        using var cts = new CancellationTokenSource();
        var hook = Hook(m => { if (m.Contains("importing")) cts.Cancel(); });
        var roots = new[] { TerrainRoot };
        Assert.ThrowsAny<OperationCanceledException>(() => hook.TryImport("mini", null, () => TerrainRoot, roots, cts.Token));
        Assert.Equal(1, _imports);
        Assert.False(Directory.Exists(Path.Combine(TerrainRoot, "mini")));
        Assert.Empty(Directory.GetDirectories(TerrainRoot));           // no temp folder either
        Assert.Null(new TerrainCatalog(roots).ResolveDefinition("mini"));   // nothing resolvable (no hook on this catalog)
        TerrainSources.Import = Hook();
        Assert.NotNull(new TerrainCatalog(roots).ResolveDefinition("mini"));
        Assert.Equal(2, _imports);
    }

    [Fact]
    public void AFailedImport_IsLoud_LeavesNothing_AndTheNextCallImports()
    {
        string set = Input("mini");
        string trees = Path.Combine(set, InputSetLayout.Trees);
        string kept = File.ReadAllText(trees);
        File.Delete(trees);                                             // the set is now invalid: the importer throws
        var catalog = Install();
        Assert.ThrowsAny<Exception>(() => catalog.ResolveDefinition("mini"));
        Assert.Equal(1, _imports);
        Assert.False(File.Exists(Path.Combine(TerrainRoot, "mini", "terrain.json")));
        Assert.Empty(Directory.GetDirectories(TerrainRoot));
        File.WriteAllText(trees, kept);
        Assert.NotNull(catalog.ResolveDefinition("mini"));
        Assert.Equal(2, _imports);
    }

    [Fact]
    public void ConcurrentResolves_InOneProcess_ImportOnce()
    {
        Input("mini");
        var catalog = Install(Hook(m => { if (m.Contains("importing")) Thread.Sleep(300); }));   // widens the window a missing lock would lose
        const int n = 6;
        using var gate = new Barrier(n);
        var results = new string?[n];
        var threads = Enumerable.Range(0, n).Select(i => new Thread(() => { gate.SignalAndWait(); results[i] = catalog.ResolveDefinition("mini"); })).ToList();
        threads.ForEach(t => t.Start()); threads.ForEach(t => t.Join());
        Assert.Equal(1, _imports);
        Assert.All(results, r => Assert.Equal(Path.Combine(TerrainRoot, "mini", "terrain.json"), r));
    }

    [Fact]
    public void AnotherProcessHoldingTheLock_IsWaitedFor_ThenItsFolderIsResolved_WithNoImportHere()
    {
        string set = Input("mini");
        Directory.CreateDirectory(TerrainRoot);
        string lockFile = Path.Combine(TerrainRoot, ".mini.import.lock");
        var held = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);   // what a second PROCESS's hook holds while it imports
        var catalog = Install();
        var task = Task.Run(() => catalog.ResolveDefinition("mini"));
        Assert.False(Done(task, 500));                                  // waits: it neither imported nor gave up
        Assert.Equal(0, _imports);
        ImportNow(new TerrainImporter(TerrainRoot, _env.Scenarios), set, "mini", false, null, default);   // the other process finishes
        held.Dispose();
        Assert.True(Done(task, 20000));
        Assert.Equal(Path.Combine(TerrainRoot, "mini", "terrain.json"), Resultof(task));
        Assert.Equal(0, _imports);
    }

    [Fact]
    public void TwoImportsOfTheSameInput_AreByteIdentical()
    {
        string set = _env.Synth();
        string a = Path.Combine(_env.Root, "A"), b = Path.Combine(_env.Root, "B");
        ImportNow(new TerrainImporter(a, _env.Scenarios), set, "synth", false, null, default);
        ImportNow(new TerrainImporter(b, _env.Scenarios), set, "synth", false, null, default);
        static List<string> Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Where(f => f != TerrainContentHash.CacheFileName && !f.EndsWith("/" + TerrainContentHash.CacheFileName)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var fa = Files(Path.Combine(a, "synth")); var fb = Files(Path.Combine(b, "synth"));
        Assert.Equal(fa, fb);
        Assert.True(fa.Count > 3);
        foreach (var rel in fa) Assert.True(File.ReadAllBytes(Path.Combine(a, "synth", rel)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, "synth", rel))), rel);
        Assert.Equal(TerrainContentHash.OfTerrain(Path.Combine(a, "synth", "terrain.json")), TerrainContentHash.OfTerrain(Path.Combine(b, "synth", "terrain.json")));
    }

    // ---- every host kind builds its catalog its own way; the hook is inside, so each imports
    [Fact]
    public void ANodeCatalog_WithAStagingRoot_ImportsIntoTheSharedRoot_AndFindsTheInputBesideTheNas()
    {
        Input("mini");                                                  // beside the NAS's assets/terrain, not beside the staging root
        string staging = Path.Combine(_env.Root, "node-staging");
        TerrainSources.Import = Hook();
        var def = TerrainCatalog.ForNode(staging, Content).ResolveDefinition("mini");
        Assert.Equal(Path.Combine(TerrainRoot, "mini", "terrain.json"), def);   // ⭐ CE-11039 F-2 (revised): the SHARED root, not the node's private staging
        Assert.False(Directory.Exists(Path.Combine(staging, "Terrain", "mini")));
        Assert.Equal(1, _imports);
    }

    /// <summary>
    /// ⭐⭐ CE-11039 — a terrain imports ONCE PER MACHINE: two catalogs with DIFFERENT staging roots (two nodes; two verify children) and one NAS import once, the second resolving what the
    /// first left in the NAS. 🔴 Mutation: the import target back to the first writable root (the staging root) ⇒ two imports, and the first's folder is in its staging root.
    /// </summary>
    [Fact]
    public void TwoStagingRoots_AndOneNas_ImportOnce()
    {
        Input("mini");
        TerrainSources.Import = Hook();
        var first = TerrainCatalog.ForNode(Path.Combine(_env.Root, "stage-1"), Content).ResolveDefinition("mini");
        var second = TerrainCatalog.ForNode(Path.Combine(_env.Root, "stage-2"), Content).ResolveDefinition("mini");
        Assert.Equal(1, _imports);
        Assert.Equal(Path.Combine(TerrainRoot, "mini", "terrain.json"), first);
        Assert.Equal(first, second);
        Assert.False(Directory.Exists(Path.Combine(_env.Root, "stage-1", "Terrain", "mini")));
        Assert.False(Directory.Exists(Path.Combine(_env.Root, "stage-2", "Terrain", "mini")));
    }

    /// <summary>
    /// ⭐⭐ CE-11039 — the scenario-regression case: every verify child has its OWN staging root AND its own private NAS, so only a machine-wide root can make the import happen once. Two catalogs, two
    /// staging roots, two NASes, one <see cref="TerrainSources.SharedImportRoot"/> (a dev checkout's <c>assets/terrain</c>) import once; the second finds the first's folder with no import.
    /// 🔴 Mutation: the target ignoring the machine root ⇒ two imports, each in its own NAS.
    /// </summary>
    [Fact]
    public void TwoPrivateNases_AndOneMachineRoot_ImportOnce()
    {
        string machine = Path.Combine(_env.Root, "repo-assets-terrain");
        string repo = Path.Combine(_env.Root, "repo");
        new MiniSet().Write(Path.Combine(repo, "assets", "terrain-input", "mini"));
        TerrainSources.SharedImportRoot = machine;
        TerrainSources.Import = new SourceImport(root => { Interlocked.Increment(ref _imports); return SourceImport.DefaultImporter(root); }, null, repo);
        var first = TerrainCatalog.ForNode(Path.Combine(_env.Root, "run1", "node"), Path.Combine(_env.Root, "run1", "shared")).ResolveDefinition("mini");
        var second = TerrainCatalog.ForNode(Path.Combine(_env.Root, "run2", "node"), Path.Combine(_env.Root, "run2", "shared")).ResolveDefinition("mini");
        Assert.Equal(1, _imports);
        Assert.Equal(Path.Combine(machine, "mini", "terrain.json"), first);
        Assert.Equal(first, second);
        Assert.False(Directory.Exists(Path.Combine(_env.Root, "run1", "shared", "assets", "terrain", "mini")));
    }

    /// <summary>⭐ CE-11039 — a NAS that cannot be written (here: its <c>assets/terrain</c> is a FILE, so no folder can be created; root ignores chmod) falls back to the node's staging root: the terrain still imports.</summary>
    [Fact]
    public void AnUnwritableNas_FallsBackToTheStagingRoot()
    {
        Input("mini");
        Directory.CreateDirectory(Path.GetDirectoryName(TerrainRoot)!);
        File.WriteAllText(TerrainRoot, "not a folder");
        string staging = Path.Combine(_env.Root, "node-staging");
        TerrainSources.Import = Hook();
        var def = TerrainCatalog.ForNode(staging, Content).ResolveDefinition("mini");
        Assert.Equal(Path.Combine(staging, "Terrain", "mini", "terrain.json"), def);
        Assert.Equal(1, _imports);
    }

    [Fact]
    public void ADirectCatalog_AndTheTwoPublicResolvers_AllImport()
    {
        Input("mini");
        TerrainSources.Import = Hook();
        var catalog = new TerrainCatalog(new[] { TerrainRoot });
        Assert.NotNull(catalog.ReadDefinition("mini"));
        Assert.NotNull(catalog.ResolveFolder("mini"));
        Assert.Equal(1, _imports);
        Assert.Contains("mini", catalog.List());
    }

    /// <summary>
    /// ⭐⭐ <c>CE-11061</c> (unreal-ig) / <c>CE-5060</c> — a STALE import (an older importer revision) that every run finds FIRST in its own private NAS (seeded from the machine's
    /// stale copy) re-imports ONCE, into the machine-wide root, and every later run resolves that current copy with no import. 📌 Measured: each verify child re-imported
    /// Stechovice into its private NAS (207 s, thrown away with the run) and the stale machine copy stayed stale, so every run imported again.
    /// 🔴 Mutation: a stale hit re-imports into the root it was FOUND in ⇒ one import per run (3), each in its private NAS, and the machine copy stays at revision 1.
    /// </summary>
    [Fact]
    public void CE11061_AStaleImport_FoundFirstInEveryRunsPrivateNas_ReImportsOnce_IntoTheMachineRoot()
    {
        string machine = Path.Combine(_env.Root, "repo-assets-terrain");
        string repo = Path.Combine(_env.Root, "repo");
        new MiniSet().Write(Path.Combine(repo, "assets", "terrain-input", "mini"));
        TerrainSources.SharedImportRoot = machine;
        TerrainSources.Import = new SourceImport(root => { Interlocked.Increment(ref _imports); return SourceImport.DefaultImporter(root); }, m => { lock (_log) _log.Add(m); }, repo);
        var def = TerrainCatalog.ForNode(Path.Combine(_env.Root, "run0", "node"), Path.Combine(_env.Root, "run0", "shared")).ResolveDefinition("mini")!;
        Assert.Equal(Path.Combine(machine, "mini", "terrain.json"), def);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(def))!.AsObject();
        json.Remove(SourceImport.RecordedRevisionKey);                                                      // imported before revisions were recorded
        File.WriteAllText(def, json.ToJsonString());
        Assert.Equal(1, SourceImport.RecordedRevision(def));
        string Nas(int run) => Path.Combine(_env.Root, "run" + run, "shared");
        for (int run = 1; run <= 3; run++) CopyTree(Path.Combine(machine, "mini"), Path.Combine(Nas(run), "assets", "terrain", "mini"));   // each run's private NAS seeded with the stale copy

        for (int run = 1; run <= 3; run++)
            Assert.Equal(def, TerrainCatalog.ForNode(Path.Combine(_env.Root, "run" + run, "node"), Nas(run)).ResolveDefinition("mini"));
        Assert.Equal(2, _imports);                                                                          // the first import + ONE re-import, not one per run
        Assert.Equal(TerrainImporter.Revision, SourceImport.RecordedRevision(def));                         // the machine copy is current now
        Assert.Contains(_log, l => l.Contains("stale") && l.Contains(Path.Combine(Nas(1), "assets", "terrain", "mini")));   // the stale copy is named once it is passed over
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    [Fact]
    public void TheRepositorysInputSet_IsFoundWhenTheContentRootHasNone()
    {
        string repo = Path.Combine(_env.Root, "repo");
        new MiniSet().Write(Path.Combine(repo, "assets", "terrain-input", "mini"));
        TerrainSources.Import = new SourceImport(root => { _imports++; return SourceImport.DefaultImporter(root); }, null, repo);
        Assert.NotNull(TerrainCatalog.ForNode(null, Content).ResolveDefinition("mini"));
        Assert.Equal(1, _imports);
    }
}
