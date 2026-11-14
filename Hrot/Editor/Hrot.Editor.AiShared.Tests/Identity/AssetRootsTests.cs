using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// The segment names and the absolute roots of <see cref="AssetRoots"/> — ⭐ <c>CE-4089</c>: <c>assets/behaviors/&lt;Folder&gt;</c> and
/// <c>recipes/behaviors/&lt;Folder&gt;</c> + <c>recipes/scenarios</c> under the NAS root (config → NAS; never the exe folder).
/// ⚠ Serial with every class that mutates the process-global <see cref="AssetRoots.ConfiguredRoot"/>.
/// </summary>
[Collection(AssetRootsTestCollection.Name)]
public sealed class AssetRootsTests
{
    // Helper: normalises directory separators to Path.DirectorySeparatorChar
    // and asserts the path ends with the expected relative segment.
    private static void AssertEndsWithRelative(string actualAbsolute, string expectedRelative)
    {
        // Normalise separators so comparison is OS-consistent.
        string normalised = actualAbsolute.Replace('/', Path.DirectorySeparatorChar)
                                          .Replace('\\', Path.DirectorySeparatorChar);
        string expectedEnd = expectedRelative.Replace('/', Path.DirectorySeparatorChar)
                                             .Replace('\\', Path.DirectorySeparatorChar);
        // The expected segment should be a suffix of the absolute path,
        // possibly preceded by a directory separator.
        Assert.True(
            normalised.EndsWith(Path.DirectorySeparatorChar + expectedEnd) || normalised.EndsWith(expectedEnd),
            $"Expected '{actualAbsolute}' to end with '{expectedEnd}'.");
    }

    // ── AssetsRelative ────────────────────────────────────────────

    [Fact]
    public void AssetsRelative_EachFileKind_ReturnsLiteralSegments()
    {
        // These are pure relative segments — no base path prepended.
        Assert.Equal(Path.Combine("assets", "behaviors", "Blueprints"), AssetRoots.AssetsRelative(AssetKind.Blueprint));
        Assert.Equal(Path.Combine("assets", "behaviors", "BTrees"),     AssetRoots.AssetsRelative(AssetKind.BTree));
        Assert.Equal(Path.Combine("assets", "behaviors", "HSMs"),       AssetRoots.AssetsRelative(AssetKind.Hsm));
    }

    [Fact]
    public void AssetsRelative_Blackboard_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsRelative(AssetKind.Blackboard));
        Assert.Equal("kind", ex.ParamName);
    }

    [Fact]
    public void AssetsRelative_Utility_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsRelative(AssetKind.Utility));
        Assert.Equal("kind", ex.ParamName);
    }

    // ── RecipesRelative ───────────────────────────────────────────

    [Fact]
    public void RecipesRelative_AllFileKinds_ReturnsLiteralSegments()
    {
        Assert.Equal(Path.Combine("recipes", "behaviors", "Blueprints"), AssetRoots.RecipesRelative(AssetKind.Blueprint));
        Assert.Equal(Path.Combine("recipes", "behaviors", "BTrees"),     AssetRoots.RecipesRelative(AssetKind.BTree));
        Assert.Equal(Path.Combine("recipes", "behaviors", "HSMs"),       AssetRoots.RecipesRelative(AssetKind.Hsm));
    }

    [Fact]
    public void ScenariosRecipesRelative_ReturnsLiteralSegment()
    {
        Assert.Equal(Path.Combine("recipes", "scenarios"), AssetRoots.ScenariosRecipesRelative);
    }

    [Fact]
    public void RecipesRelative_Blackboard_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.RecipesRelative(AssetKind.Blackboard));
        Assert.Equal("kind", ex.ParamName);
    }

    [Fact]
    public void RecipesRelative_Utility_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.RecipesRelative(AssetKind.Utility));
        Assert.Equal("kind", ex.ParamName);
    }

    [Fact]
    public void RecipesRelative_Scenario_ReturnsExpectedSegment()
    {
        Assert.Equal(Path.Combine("recipes", "scenarios"),
            AssetRoots.RecipesRelative(AssetKind.Scenario));
    }

    [Fact]
    public void AssetsRelative_Scenario_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsRelative(AssetKind.Scenario));
        Assert.Equal("kind", ex.ParamName);
    }

    // ── AssetsFor (absolute, delegates to AssetsRelative) ─────────

    [Fact]
    public void AssetsFor_EachFileKind_ReturnsExpectedRelativeSegment()
    {
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.Blueprint),
            Path.Combine("assets", "behaviors", "Blueprints"));
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.BTree),
            Path.Combine("assets", "behaviors", "BTrees"));
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.Hsm),
            Path.Combine("assets", "behaviors", "HSMs"));
    }

    [Fact]
    public void AssetsFor_Blackboard_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsFor(AssetKind.Blackboard));
        Assert.Equal("kind", ex.ParamName);
    }

    [Fact]
    public void AssetsFor_Utility_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsFor(AssetKind.Utility));
        Assert.Equal("kind", ex.ParamName);
    }

    [Fact]
    public void AssetsFor_Scenario_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsFor(AssetKind.Scenario));
        Assert.Equal("kind", ex.ParamName);
    }

    // ── RecipesFor (absolute, delegates to RecipesRelative) ────────

    [Fact]
    public void RecipesFor_AllKinds_IncludingScenario()
    {
        // File kinds via RecipesFor.
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Blueprint),
            Path.Combine("recipes", "behaviors", "Blueprints"));
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.BTree),
            Path.Combine("recipes", "behaviors", "BTrees"));
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Hsm),
            Path.Combine("recipes", "behaviors", "HSMs"));

        // Scenario now uses the RecipesFor(AssetKind.Scenario) arm.
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Scenario),
            Path.Combine("recipes", "scenarios"));

        // Backward-compat: dedicated member still points to the same place.
        AssertEndsWithRelative(AssetRoots.ScenariosRecipesRoot,
            Path.Combine("recipes", "scenarios"));
    }

    [Fact]
    public void RecipesFor_Blackboard_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.RecipesFor(AssetKind.Blackboard));
        Assert.Equal("kind", ex.ParamName);
    }

    [Fact]
    public void RecipesFor_Utility_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.RecipesFor(AssetKind.Utility));
        Assert.Equal("kind", ex.ParamName);
    }

    // ── Absolute delegates to Relative helpers ────────────────────

    [Fact]
    public void AssetsFor_DelegatesTo_AssetsRelative()
    {
        // ⭐ CE-4089: the absolute path must be <NAS root> + relative segment (never AppContext.BaseDirectory).
        foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm })
        {
            var absolute = AssetRoots.AssetsFor(kind);
            var expected = Path.Combine(ContentLayout.NasRoot, AssetRoots.AssetsRelative(kind));
            Assert.Equal(expected, absolute);
        }
    }

    [Fact]
    public void RecipesFor_DelegatesTo_RecipesRelative()
    {
        foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm, AssetKind.Scenario })
        {
            var absolute = AssetRoots.RecipesFor(kind);
            var expected = Path.Combine(ContentLayout.NasRoot, AssetRoots.RecipesRelative(kind));
            Assert.Equal(expected, absolute);
        }
    }

    [Fact]
    public void ScenariosRecipesRoot_DelegatesTo_ScenariosRecipesRelative()
    {
        var absolute = AssetRoots.ScenariosRecipesRoot;
        var expected = Path.Combine(ContentLayout.NasRoot, AssetRoots.ScenariosRecipesRelative);
        Assert.Equal(expected, absolute);
    }

    // ── Scenario has no Assets root ───────────────────────────────

    [Fact]
    public void AssetsFor_Scenario_HasNoAssetsRoot()
    {
        // Scenario has AssetsFor(AssetKind.Scenario) → throw
        // (Scenarios are orchestrator/NAS-backed; no Assets root).
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetRoots.AssetsFor(AssetKind.Scenario));
        Assert.Equal("kind", ex.ParamName);

        // But ScenariosRecipesRoot still exists and points to recipes/scenarios.
        string scenariosRoot = AssetRoots.ScenariosRecipesRoot;
        AssertEndsWithRelative(scenariosRoot, "recipes/scenarios");

        // Confirm the scenario root is under RecipesRoot, NOT AssetsRoot.
        string recipesRoot = AssetRoots.RecipesRoot;
        string assetsRoot  = AssetRoots.AssetsRoot;

        Assert.StartsWith(recipesRoot, scenariosRoot);
        Assert.False(scenariosRoot.StartsWith(assetsRoot),
            "Scenario root must NOT be under AssetsRoot — scenarios have no Assets root.");
    }

    // ── Disjoint roots ─────────────────────────────────────────────

    [Fact]
    public void AssetsRoot_And_RecipesRoot_AreDisjoint()
    {
        string assets  = AssetRoots.AssetsRoot;
        string recipes = AssetRoots.RecipesRoot;

        // 1. They are different directories.
        Assert.NotEqual(assets, recipes);

        // 2. Neither is a subpath of the other (the §16 disjoint-roots invariant).
        string assetsNormalised  = assets.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        string recipesNormalised = recipes.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

        Assert.False(recipesNormalised.StartsWith(assetsNormalised + Path.DirectorySeparatorChar),
            $"RecipesRoot '{recipes}' must not be a subpath of AssetsRoot '{assets}'.");
        Assert.False(assetsNormalised.StartsWith(recipesNormalised + Path.DirectorySeparatorChar),
            $"AssetsRoot '{assets}' must not be a subpath of RecipesRoot '{recipes}'.");
    }

    // ── Root properties are non-empty and distinct ─────────────────

    [Fact]
    public void AssetsRoot_And_RecipesRoot_AreNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AssetRoots.AssetsRoot));
        Assert.False(string.IsNullOrWhiteSpace(AssetRoots.RecipesRoot));
    }

    [Fact]
    public void AssetsRoot_And_RecipesRoot_AreAbsolutePaths()
    {
        Assert.True(Path.IsPathRooted(AssetRoots.AssetsRoot));
        Assert.True(Path.IsPathRooted(AssetRoots.RecipesRoot));
    }

    // ══ CE-4089 — the PRODUCTION layout: no source tree, no config ⇒ every content root is under the NAS root and nowhere else ══

    /// <summary>
    /// ⭐⭐⭐ <b>Success condition 2.</b> With no source tree (the walk-up, started outside any checkout, answers null) and no <c>--asset-root</c>, EVERY content
    /// reader resolves under the NAS root — <c>AssetRoots</c> (all members), <c>TerrainCatalog</c>, <c>TerrainAssets</c>' shared buildings root, the icon library
    /// and the audio layer — and NOTHING under <see cref="AppContext.BaseDirectory"/>. The icon library is READ from the NAS folder, not just asked for a path.
    /// 🔴 Red-proofs (each compiles): point any of these at the exe folder or at a source walk-up and this fails naming the root.
    /// </summary>
    [Fact]
    public void CE4089_AProductionLayout_ReadsEveryContentRootFromTheNasOnly()
    {
        const string StagingVar = "FDP_STAGING_ROOT";
        var staging = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ce4089-prod-" + Guid.NewGuid().ToString("N"))).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ce4089-out-" + Guid.NewGuid().ToString("N"))).FullName;
        var previousStaging = Environment.GetEnvironmentVariable(StagingVar);
        var previousConfig = AssetRoots.ConfiguredRoot;
        try
        {
            Environment.SetEnvironmentVariable(StagingVar, staging);
            AssetRoots.Configure(null);

            var nas = ContentLayout.NasRoot;
            Assert.Equal(Path.Combine(staging, "shared"), nas);
            Assert.Null(ContentLayout.TryFindSourceTree(outside));            // the production premise: there is no source tree

            var roots = new List<(string What, string Path)>
            {
                ("AssetRoots.ResolveBase", AssetRoots.ResolveBase()),
                ("AssetRoots.AssetsRoot", AssetRoots.AssetsRoot),
                ("AssetRoots.BehaviorAssetsRoot", AssetRoots.BehaviorAssetsRoot),
                ("AssetRoots.RecipesRoot", AssetRoots.RecipesRoot),
                ("AssetRoots.ScenariosRecipesRoot", AssetRoots.ScenariosRecipesRoot),
                ("TerrainCatalog (NAS arm)", Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, nas).Roots.Single()),
                ("TerrainAssets.DefaultSharedBuildingsRoot", Fdp.Toolkit.Terrain.TerrainAssets.DefaultSharedBuildingsRoot),
                ("AudioLayer.AssetFolder", Hrot.UI.Common.Audio.AudioLayer.AssetFolder),
            };
            foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm })
            {
                roots.Add(($"AssetRoots.AssetsFor({kind})", AssetRoots.AssetsFor(kind)));
                roots.Add(($"AssetRoots.RecipesFor({kind})", AssetRoots.RecipesFor(kind)));
                roots.Add(($"AssetRoots.ResolveAssetsRoot({kind})", AssetRoots.ResolveAssetsRoot(kind, "no", "such", "x.csproj")));
            }

            foreach (var (what, path) in roots)
            {
                Assert.True(path.StartsWith(nas, StringComparison.Ordinal), $"{what} = '{path}' is not under the NAS root '{nas}'.");
                Assert.False(path.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal), $"{what} = '{path}' is under the exe folder.");
            }

            // ⭐ the icon library READS the NAS's assets/icons (and finds nothing in the exe folder)
            var icons = ContentLayout.IconAssets(nas);
            Directory.CreateDirectory(icons);
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 7 };
            File.WriteAllBytes(Path.Combine(icons, "t72.png"), png);
            byte[]? uploaded = null;
            var lib = new Hrot.UI.Common.AddEntity.EntityIconLibrary(upload: bytes => { uploaded = bytes; return (1, 8, 8); });
            Assert.True(lib.TryGet("entity/t72", out _));
            Assert.Equal(png, uploaded);
        }
        finally
        {
            Environment.SetEnvironmentVariable(StagingVar, previousStaging);
            AssetRoots.Configure(previousConfig);
            Directory.Delete(staging, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }
}
