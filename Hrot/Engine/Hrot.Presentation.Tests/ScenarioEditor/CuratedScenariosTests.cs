using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Rails for <see cref="CuratedScenarios"/> — the ONE content seed (⭐ CE-4089): the repository's <c>scenarios/</c>, <c>assets/</c> and <c>recipes/</c> copied into the
/// NAS on start, and saved back on demand. The pure (source, working) seams are driven with temp dirs; the walk-up probe is exercised separately.
/// </summary>
public class CuratedScenariosTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "curated-scn-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteScenario(string root, string relName, string body)
    {
        var dir = Path.Combine(root, relName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ContentSeed.MarkerFileName), body);
    }

    private static string ReadScenario(string root, string relName)
        => File.ReadAllText(Path.Combine(root, relName.Replace('/', Path.DirectorySeparatorChar),
                                         ContentSeed.MarkerFileName));

    [Fact]
    public void CuratedRelPaths_ListsEveryFolderWithAMarker_IncludingNested()
    {
        var src = NewTempDir();
        WriteScenario(src, "test-fire", "{}");
        WriteScenario(src, "test-move", "{}");
        WriteScenario(src, "Combat/Ambush", "{}");   // nesting, like the runtime enumerator
        Directory.CreateDirectory(Path.Combine(src, "not-a-scenario"));   // no marker → not listed

        var names = ContentSeed.CuratedRelPaths(src);

        Assert.Equal(new[] { "Combat/Ambush", "test-fire", "test-move" }, names.ToArray());
    }

    [Fact]
    public void SeedFrom_CopiesEveryCuratedName_ForceOverwriting_AndLeavesNonCuratedUntouched()
    {
        var src = NewTempDir();
        var work = NewTempDir();
        WriteScenario(src, "test-fire", "GIT-v2");
        WriteScenario(work, "test-fire", "USER-EDIT");     // will be overwritten by the curated copy
        WriteScenario(work, "my-own", "MINE");             // NOT curated → must survive untouched

        var copied = ContentSeed.SeedFrom(src, work);

        Assert.Equal(new[] { "test-fire" }, copied.ToArray());
        Assert.Equal("GIT-v2", ReadScenario(work, "test-fire"));   // force-overwrote the user's edit
        Assert.Equal("MINE", ReadScenario(work, "my-own"));        // non-curated untouched
        Assert.True(Directory.Exists(Path.Combine(work, "my-own"))); // nothing deleted
    }

    [Fact]
    public void SeedFrom_CopiesSidecarFilesInTheScenarioFolder()
    {
        var src = NewTempDir();
        var work = NewTempDir();
        WriteScenario(src, "test-fire", "{}");
        File.WriteAllText(Path.Combine(src, "test-fire", "notes.txt"), "sidecar");

        ContentSeed.SeedFrom(src, work);

        Assert.Equal("sidecar", File.ReadAllText(Path.Combine(work, "test-fire", "notes.txt")));
    }

    [Fact]
    public void SaveTo_CopiesWorkingCopiesBackToGit_ForOnlyTheCuratedSet()
    {
        var src = NewTempDir();
        var work = NewTempDir();
        WriteScenario(src, "test-fire", "OLD");         // curated member (defines the set)
        WriteScenario(work, "test-fire", "NEW-EDIT");   // the working copy the user tuned
        WriteScenario(work, "my-own", "MINE");          // not in the set → must NOT be promoted

        var written = CuratedScenarios.SaveTo(src, work);

        Assert.Equal(new[] { "test-fire" }, written.ToArray());
        Assert.Equal("NEW-EDIT", ReadScenario(src, "test-fire"));       // refreshed from working
        Assert.False(Directory.Exists(Path.Combine(src, "my-own")));    // never added to git
    }

    [Fact]
    public void SaveTo_SkipsACuratedNameWithNoWorkingCopy_WithoutError()
    {
        var src = NewTempDir();
        var work = NewTempDir();
        WriteScenario(src, "test-fire", "{}");
        WriteScenario(src, "test-move", "KEEP");   // curated, but the user never seeded/deleted its working copy
        WriteScenario(work, "test-fire", "EDIT");

        var written = CuratedScenarios.SaveTo(src, work);

        Assert.Equal(new[] { "test-fire" }, written.ToArray());  // only the one with a working copy
        Assert.Equal("KEEP", ReadScenario(src, "test-move"));    // untouched, not blanked
    }

    [Fact]
    public void SeedFrom_EmptySource_IsANoOp_NotAnError()
    {
        var src = NewTempDir();   // no scenarios at all
        var work = NewTempDir();
        WriteScenario(work, "my-own", "MINE");

        var copied = ContentSeed.SeedFrom(src, work);

        Assert.Empty(copied);
        Assert.Equal("MINE", ReadScenario(work, "my-own"));
    }

    [Fact]
    public void TryFindSourceScenariosDirectory_IsTheSourceTreesScenarios_OrNull()
    {
        // ⭐ CE-4089 — the probe is the ONE repository walk-up (ContentLayout): in a checkout it answers <repo>/scenarios, in a deployed build null
        // (nothing to seed, menu disabled). Asserted as agreement with the walk-up, not as a fixed verdict.
        var found = CuratedScenarios.TryFindSourceScenariosDirectory();
        var tree = ContentLayout.TryFindSourceTree();
        Assert.Equal(tree is null ? null : ContentLayout.Scenarios(tree), found);
    }

    /// <summary>
    /// ⭐⭐ CE-1365 — the working root is shared by every editor and test host on the machine: seeds racing into it (here two sources, four
    /// seeders each) never fail and never let a reader see a half-written scenario — every read is one whole version or the other.
    /// 🔴 With the in-place <c>File.Copy(overwrite)</c> readers saw truncated files and seeders threw on each other's handles.
    /// </summary>
    [Fact]
    public void CE1365_Seeds_racing_into_one_shared_root_never_expose_a_half_written_scenario()
    {
        var srcA = NewTempDir(); var srcB = NewTempDir(); var work = NewTempDir();
        string a = "{\"v\":\"" + new string('a', 2_000_000) + "\"}", b = "{\"v\":\"" + new string('b', 1_500_000) + "\"}";
        WriteScenario(srcA, "golden", a);
        WriteScenario(srcB, "golden", b);
        ContentSeed.SeedFrom(srcA, work);

        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var stop = new System.Threading.CancellationTokenSource();
        var seeders = Enumerable.Range(0, 8).Select(i => System.Threading.Tasks.Task.Run(() =>
        {
            for (int n = 0; n < 25; n++)
                try { ContentSeed.SeedFrom(i % 2 == 0 ? srcA : srcB, work); }
                catch (System.Exception e) { failures.Enqueue("seed: " + e.GetType().Name + " " + e.Message); }
        })).ToArray();
        var reader = System.Threading.Tasks.Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                try
                {
                    var text = ReadScenario(work, "golden");
                    if (text != a && text != b) failures.Enqueue($"read a partial file ({text.Length} chars)");
                }
                catch (IOException) { /* the rename's instant on a platform that locks — not a torn read */ }
        });
        System.Threading.Tasks.Task.WaitAll(seeders);
        stop.Cancel();
        reader.Wait();

        Assert.True(failures.IsEmpty, string.Join("\n", failures.Take(5)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(work, "golden"), "*.tmp"));   // no temp file left behind
    }

    // ══ CE-4089 — the content seed: scenarios/ + assets/ + recipes/ ══════════════════════════════════════════════

    /// <summary>⭐ A source tree in miniature: the marker, one scenario, assets of three kinds, a recipe.</summary>
    private static string NewSourceTree()
    {
        var tree = NewTempDir();
        File.WriteAllText(Path.Combine(tree, ContentLayout.RepositoryMarkerFileName), "");
        WriteScenario(ContentLayout.Scenarios(tree), "demo", "SCN-v1");
        Write(ContentLayout.TerrainAssets(tree), "demo-city/terrain.json", "TERRAIN-v1");
        Write(ContentLayout.BehaviorAssets(tree, ContentLayout.BTreesFolder), "Samples/A.btree.json", "BT-v1");
        Write(ContentLayout.IconAssets(tree), "t72.png", "PNG");
        Write(ContentLayout.ScenarioRecipes(tree), "seed/scenario.json", "SEED-v1");
        Write(ContentLayout.BehaviorRecipes(tree, ContentLayout.BlueprintsFolder), "R.bp.json", "RECIPE-v1");
        return tree;
    }

    private static void Write(string root, string rel, string body)
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body);
    }

    private static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// ⭐⭐⭐ <b>Success condition 3 — the seed.</b> <c>scenarios/</c>, <c>assets/</c> and <c>recipes/</c> are copied from a source tree into the NAS root, a
    /// SECOND seed with identical bytes writes NOTHING (CE-1365's replace-if-different), and a changed file is rewritten alone.
    /// </summary>
    [Fact]
    public void CE4089_TheSeed_CopiesTheThreeFolders_AndASecondSeedWritesNothing()
    {
        var tree = NewSourceTree();
        var nas = NewTempDir();

        var first = ContentSeed.SeedTreeFrom(tree, nas);

        Assert.Equal("SCN-v1", Read(ContentLayout.Scenarios(nas), "demo/scenario.json"));
        Assert.Equal("TERRAIN-v1", Read(ContentLayout.TerrainAssets(nas), "demo-city/terrain.json"));
        Assert.Equal("BT-v1", Read(ContentLayout.BehaviorAssets(nas, ContentLayout.BTreesFolder), "Samples/A.btree.json"));
        Assert.Equal("PNG", Read(ContentLayout.IconAssets(nas), "t72.png"));
        Assert.Equal("SEED-v1", Read(ContentLayout.ScenarioRecipes(nas), "seed/scenario.json"));
        Assert.Equal("RECIPE-v1", Read(ContentLayout.BehaviorRecipes(nas, ContentLayout.BlueprintsFolder), "R.bp.json"));
        Assert.Equal(6, first.Count);

        Assert.Empty(ContentSeed.SeedTreeFrom(tree, nas));        // identical bytes ⇒ nothing written

        Write(ContentLayout.BehaviorAssets(tree, ContentLayout.BTreesFolder), "Samples/A.btree.json", "BT-v2");
        var again = ContentSeed.SeedTreeFrom(tree, nas);
        Assert.Single(again);                                          // only the changed file
        Assert.EndsWith("A.btree.json", again[0]);
        Assert.Equal("BT-v2", Read(ContentLayout.BehaviorAssets(nas, ContentLayout.BTreesFolder), "Samples/A.btree.json"));
    }

    /// <summary>
    /// ⭐⭐ <b>Success condition 3 — no source tree ⇒ no-op.</b> Started from outside any checkout (a production install), <c>SeedIntoNas</c> copies
    /// nothing, and <c>CanSaveToGit</c> is false. ⭐ And it never deletes: a NAS file the repository never had is untouched by a real seed.
    /// </summary>
    [Fact]
    public void CE4089_TheSeed_WithNoSourceTree_IsANoOp_AndNeverDeletes()
    {
        var outside = NewTempDir();                       // nothing above it holds HROT.sln beside assets/
        var nas = NewTempDir();
        Write(ContentLayout.IconAssets(nas), "published-only.png", "MINE");

        Assert.Null(ContentLayout.TryFindSourceTree(outside));
        Assert.Empty(ContentSeed.SeedIntoNas(nas, outside));
        Assert.False(CuratedScenarios.CanSaveToGit(outside));
        Assert.Empty(CuratedScenarios.SaveToGit(nas, outside));
        Assert.Equal("MINE", Read(ContentLayout.IconAssets(nas), "published-only.png"));
        Assert.False(Directory.Exists(ContentLayout.Scenarios(nas)));    // nothing was created either

        // …while WITH a source tree (found by its marker, from a folder below it) the same call seeds, and still deletes nothing.
        var tree = NewSourceTree();
        var below = Directory.CreateDirectory(Path.Combine(tree, "Hrot", "Somewhere")).FullName;
        Assert.Equal(tree, ContentLayout.TryFindSourceTree(below));
        Assert.NotEmpty(ContentSeed.SeedIntoNas(nas, below));
        Assert.Equal("MINE", Read(ContentLayout.IconAssets(nas), "published-only.png"));
        Assert.Equal("PNG", Read(ContentLayout.IconAssets(nas), "t72.png"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Success condition 4 — Save to Git, per kind.</b> A NEW and a CHANGED asset in the NAS are copied back; NOTHING is deleted from the repository
    /// (an asset the NAS no longer has stays); <b>scenarios keep the curated-names rule</b> — a changed curated scenario is refreshed, a NAS-only scenario is
    /// never promoted.
    /// </summary>
    [Fact]
    public void CE4089_SaveToGit_CopiesNewAndChangedAssets_NeverDeletes_AndScenariosStayCurated()
    {
        var tree = NewSourceTree();
        var nas = NewTempDir();
        ContentSeed.SeedTreeFrom(tree, nas);

        Write(ContentLayout.BehaviorAssets(nas, ContentLayout.BTreesFolder), "Samples/A.btree.json", "BT-EDITED");      // changed
        Write(ContentLayout.BehaviorAssets(nas, ContentLayout.BTreesFolder), "Samples/NEW.btree.json", "BT-NEW");        // new
        Write(ContentLayout.ScenarioRecipes(nas), "seed2/scenario.json", "SEED2");                                        // a new recipe
        WriteScenario(ContentLayout.Scenarios(nas), "demo", "SCN-EDITED");                                                // curated scenario, changed
        WriteScenario(ContentLayout.Scenarios(nas), "nas-only", "MINE");                                                  // not curated
        File.Delete(Path.Combine(ContentLayout.IconAssets(nas), "t72.png"));                                              // gone from the NAS

        var written = CuratedScenarios.SaveTreeTo(tree, nas);

        Assert.Equal("BT-EDITED", Read(ContentLayout.BehaviorAssets(tree, ContentLayout.BTreesFolder), "Samples/A.btree.json"));
        Assert.Equal("BT-NEW", Read(ContentLayout.BehaviorAssets(tree, ContentLayout.BTreesFolder), "Samples/NEW.btree.json"));
        Assert.Equal("SEED2", Read(ContentLayout.ScenarioRecipes(tree), "seed2/scenario.json"));
        Assert.Equal("SCN-EDITED", Read(ContentLayout.Scenarios(tree), "demo/scenario.json"));
        Assert.False(Directory.Exists(Path.Combine(ContentLayout.Scenarios(tree), "nas-only")));          // scenarios: curated names only
        Assert.Equal("PNG", Read(ContentLayout.IconAssets(tree), "t72.png"));                              // nothing deleted
        Assert.Equal("TERRAIN-v1", Read(ContentLayout.TerrainAssets(tree), "demo-city/terrain.json"));     // unchanged files left alone
        Assert.DoesNotContain(written, w => w.Contains("terrain.json", System.StringComparison.Ordinal));
        Assert.Equal(4, written.Count);
    }
}
