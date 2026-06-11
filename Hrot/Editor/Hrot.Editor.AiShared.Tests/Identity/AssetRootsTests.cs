namespace Hrot.Editor.AiShared.Tests.Identity;

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
        Assert.Equal(Path.Combine("Assets", "Blueprints"), AssetRoots.AssetsRelative(AssetKind.Blueprint));
        Assert.Equal(Path.Combine("Assets", "BTrees"),     AssetRoots.AssetsRelative(AssetKind.BTree));
        Assert.Equal(Path.Combine("Assets", "HSMs"),       AssetRoots.AssetsRelative(AssetKind.Hsm));
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
        Assert.Equal(Path.Combine("Recipes", "Blueprints"), AssetRoots.RecipesRelative(AssetKind.Blueprint));
        Assert.Equal(Path.Combine("Recipes", "BTrees"),     AssetRoots.RecipesRelative(AssetKind.BTree));
        Assert.Equal(Path.Combine("Recipes", "HSMs"),       AssetRoots.RecipesRelative(AssetKind.Hsm));
    }

    [Fact]
    public void ScenariosRecipesRelative_ReturnsLiteralSegment()
    {
        Assert.Equal(Path.Combine("Recipes", "Scenarios"), AssetRoots.ScenariosRecipesRelative);
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
        Assert.Equal(Path.Combine("Recipes", "Scenarios"),
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
            Path.Combine("Assets", "Blueprints"));
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.BTree),
            Path.Combine("Assets", "BTrees"));
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.Hsm),
            Path.Combine("Assets", "HSMs"));
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
            Path.Combine("Recipes", "Blueprints"));
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.BTree),
            Path.Combine("Recipes", "BTrees"));
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Hsm),
            Path.Combine("Recipes", "HSMs"));

        // Scenario now uses the RecipesFor(AssetKind.Scenario) arm.
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Scenario),
            Path.Combine("Recipes", "Scenarios"));

        // Backward-compat: dedicated member still points to the same place.
        AssertEndsWithRelative(AssetRoots.ScenariosRecipesRoot,
            Path.Combine("Recipes", "Scenarios"));
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
        // The absolute path must be <BaseDirectory> + relative segment.
        foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm })
        {
            var absolute = AssetRoots.AssetsFor(kind);
            var expected = Path.Combine(AppContext.BaseDirectory, AssetRoots.AssetsRelative(kind));
            Assert.Equal(expected, absolute);
        }
    }

    [Fact]
    public void RecipesFor_DelegatesTo_RecipesRelative()
    {
        foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm, AssetKind.Scenario })
        {
            var absolute = AssetRoots.RecipesFor(kind);
            var expected = Path.Combine(AppContext.BaseDirectory, AssetRoots.RecipesRelative(kind));
            Assert.Equal(expected, absolute);
        }
    }

    [Fact]
    public void ScenariosRecipesRoot_DelegatesTo_ScenariosRecipesRelative()
    {
        var absolute = AssetRoots.ScenariosRecipesRoot;
        var expected = Path.Combine(AppContext.BaseDirectory, AssetRoots.ScenariosRecipesRelative);
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

        // But ScenariosRecipesRoot still exists and points to Recipes/Scenarios.
        string scenariosRoot = AssetRoots.ScenariosRecipesRoot;
        AssertEndsWithRelative(scenariosRoot, "Recipes/Scenarios");

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
}
