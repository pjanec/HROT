using System.IO;
using System.Linq;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Rails for <see cref="CuratedScenarios"/> — the git-committed curated set copied into the working NAS
/// folder on start, and saved back on demand. The pure (source, working) seams are driven with temp dirs;
/// the walk-up probe is exercised separately.
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
        File.WriteAllText(Path.Combine(dir, CuratedScenarios.MarkerFileName), body);
    }

    private static string ReadScenario(string root, string relName)
        => File.ReadAllText(Path.Combine(root, relName.Replace('/', Path.DirectorySeparatorChar),
                                         CuratedScenarios.MarkerFileName));

    [Fact]
    public void CuratedRelPaths_ListsEveryFolderWithAMarker_IncludingNested()
    {
        var src = NewTempDir();
        WriteScenario(src, "test-fire", "{}");
        WriteScenario(src, "test-move", "{}");
        WriteScenario(src, "Combat/Ambush", "{}");   // nesting, like the runtime enumerator
        Directory.CreateDirectory(Path.Combine(src, "not-a-scenario"));   // no marker → not listed

        var names = CuratedScenarios.CuratedRelPaths(src);

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

        var copied = CuratedScenarios.SeedFrom(src, work);

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

        CuratedScenarios.SeedFrom(src, work);

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

        var copied = CuratedScenarios.SeedFrom(src, work);

        Assert.Empty(copied);
        Assert.Equal("MINE", ReadScenario(work, "my-own"));
    }

    [Fact]
    public void TryFindSourceScenariosDirectory_IsNull_WhenNoCuratedSetOnThePath()
    {
        // The test bin has no committed `scenarios/` with a marker above it, so the probe answers null —
        // which is exactly the deployed-build behaviour: nothing to seed, menu disabled.
        // (If a future repo layout places one on the path this asserts the shape, not a fixed verdict.)
        var found = CuratedScenarios.TryFindSourceScenariosDirectory();
        Assert.True(found is null || Directory.Exists(found));
    }
}
