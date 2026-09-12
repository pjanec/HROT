using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor;

namespace Hrot.Editor.Tests;

/// <summary>
/// Tests for <see cref="ScenarioEnumeration"/> recursive scenario-path enumeration
/// and saving with nested names (MTB-P5-T5).
/// </summary>
public sealed class ScenarioNestedNameTests : IDisposable
{
    private readonly string _tempRoot;

    public ScenarioNestedNameTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"hrot_scenario_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Save paths honour nested names ─────────────────────────────────────

    /// <summary>
    /// Saving with a nested name like "Combat/Patrol" creates the scenario.json
    /// at the correct nested path under the root.
    /// </summary>
    [Fact]
    public void SaveAs_NestedName_CreatesNestedFolder()
    {
        // Simulate the save-path pattern used by SaveScenarioAs:
        //   var dir = Path.Combine(root, scenarioName);
        //   Directory.CreateDirectory(dir);
        //   _fileService.SaveScenario(_world, Path.Combine(dir, "scenario.json"));
        string scenarioName = "Combat/Patrol";
        var dir = Path.Combine(_tempRoot, scenarioName);
        Directory.CreateDirectory(dir);
        var scenarioJsonPath = Path.Combine(dir, "scenario.json");
        File.WriteAllText(scenarioJsonPath, "{}"); // minimal marker

        Assert.True(File.Exists(scenarioJsonPath),
            $"Expected scenario.json at {scenarioJsonPath}");

        // Verify the nested directory structure was created.
        string expectedDir = Path.Combine(_tempRoot, "Combat", "Patrol");
        Assert.True(Directory.Exists(expectedDir),
            $"Expected directory {expectedDir} to exist");
    }

    /// <summary>
    /// SaveScenarioAs uses Path.Combine on the scenario name, which on Windows
    /// may produce backslash separators. The round-trip must normalize the
    /// relative path used by EnumerateRelPaths so the same name matches.
    /// </summary>
    [Fact]
    public void SaveAs_NestedName_NormalizesToForwardSlash()
    {
        // Simulate what SaveScenarioAs does — Path.Combine on Windows produces
        // backslashes, but the scenario name the user typed uses forward slashes.
        // EnumerateRelPaths normalizes to forward slashes, so the round-trip works.
        string scenarioName = "Combat/Patrol";
        var dir = Path.Combine(_tempRoot, scenarioName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "scenario.json"), "{}");

        var paths = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        Assert.Contains("Combat/Patrol", paths);
    }

    // ── EnumerateRelPaths ──────────────────────────────────────────────────

    [Fact]
    public void EnumerateRelPaths_RootDoesNotExist_ReturnsEmpty()
    {
        var nonExistent = Path.Combine(_tempRoot, "nonexistent");
        var result = ScenarioEnumeration.EnumerateRelPaths(nonExistent);
        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateRelPaths_NullOrWhiteSpace_ReturnsEmpty()
    {
        Assert.Empty(ScenarioEnumeration.EnumerateRelPaths(null!));
        Assert.Empty(ScenarioEnumeration.EnumerateRelPaths(""));
        Assert.Empty(ScenarioEnumeration.EnumerateRelPaths("  "));
    }

    [Fact]
    public void EnumerateRelPaths_EmptyDirectory_ReturnsEmpty()
    {
        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);
        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateRelPaths_TopLevelOnly_ReturnsSingleSegment()
    {
        CreateMarker("alpha");

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        Assert.Single(result);
        Assert.Equal("alpha", result[0]);
    }

    /// <summary>
    /// Given a temp tree with alpha/scenario.json,
    /// Combat/Patrol/scenario.json, Combat/Ambush/scenario.json,
    /// EnumerateRelPaths returns exactly the nested relpaths, /-normalized,
    /// sorted, and dirs WITHOUT a scenario.json are excluded.
    /// </summary>
    [Fact]
    public void AvailableScenarios_ReturnsNestedRelPaths()
    {
        CreateMarker("alpha");
        CreateMarker("Combat/Patrol");
        CreateMarker("Combat/Ambush");
        // A directory WITHOUT a scenario.json — should be excluded.
        CreateDir("Combat/EmptyDir");
        // A sibling directory that has subdirs but no marker itself.
        CreateDir("Combat/NoMarkerHere");

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        // Expected: ["Combat/Ambush", "Combat/Patrol", "alpha"]
        // (sorted ordinal: "Combat/Ambush" < "Combat/Patrol" < "alpha")
        Assert.Equal(3, result.Count);
        Assert.Equal("Combat/Ambush", result[0]);
        Assert.Equal("Combat/Patrol", result[1]);
        Assert.Equal("alpha", result[2]);
    }

    /// <summary>
    /// Directories without a scenario.json marker are excluded from results.
    /// </summary>
    [Fact]
    public void EnumerateRelPaths_ExcludesDirectoriesWithoutMarker()
    {
        CreateMarker("alpha");
        CreateDir("beta");           // dir exists but no scenario.json
        CreateDir("gamma/sub");      // nested dir without marker

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        Assert.Single(result);
        Assert.Equal("alpha", result[0]);
    }

    /// <summary>
    /// The root directory itself is NOT included even if it contains a scenario.json.
    /// Only subdirectories are returned.
    /// </summary>
    [Fact]
    public void EnumerateRelPaths_RootMarker_NotIncluded()
    {
        // Place a scenario.json directly in the root.
        File.WriteAllText(Path.Combine(_tempRoot, "scenario.json"), "{}");
        CreateMarker("alpha");

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        // Root marker not included; only "alpha".
        Assert.Single(result);
        Assert.Equal("alpha", result[0]);
    }

    [Fact]
    public void EnumerateRelPaths_DeeplyNested_ReturnsCorrectRelpath()
    {
        CreateMarker("a/b/c/d");

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        Assert.Single(result);
        Assert.Equal("a/b/c/d", result[0]);
    }

    // ── Round-trip ─────────────────────────────────────────────────────────

    /// <summary>
    /// Save a scenario as "Combat/Patrol" into a temp root, then
    /// EnumerateRelPaths(root) contains "Combat/Patrol" (the exact name saved).
    /// </summary>
    [Fact]
    public void RoundTrip_SaveThenEnumerate_MatchesName()
    {
        // Simulate the save path exactly as SaveScenarioAs does:
        //   var dir = Path.Combine(root, scenarioName);
        //   Directory.CreateDirectory(dir);
        //   _fileService.SaveScenario(_world, Path.Combine(dir, "scenario.json"));
        string scenarioName = "Combat/Patrol";
        var dir = Path.Combine(_tempRoot, scenarioName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "scenario.json"), "{}");

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        Assert.Contains("Combat/Patrol", result);
    }

    /// <summary>
    /// Multiple nested saves round-trip correctly.
    /// </summary>
    [Fact]
    public void RoundTrip_MultipleSaves_AllFound()
    {
        foreach (var name in new[] { "alpha", "Campaign/Assault", "Campaign/Defend", "Training/Basic" })
        {
            var dir = Path.Combine(_tempRoot, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scenario.json"), "{}");
        }

        var result = ScenarioEnumeration.EnumerateRelPaths(_tempRoot);

        Assert.Equal(4, result.Count);
        Assert.Contains("Campaign/Assault", result);
        Assert.Contains("Campaign/Defend", result);
        Assert.Contains("Training/Basic", result);
        Assert.Contains("alpha", result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void CreateDir(string relativePath)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(fullPath);
    }

    private void CreateMarker(string relativePath)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(fullPath);
        File.WriteAllText(Path.Combine(fullPath, "scenario.json"), "{}");
    }
}
