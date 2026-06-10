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

    // ── AssetsFor ────────────────────────────────────────────────

    [Fact]
    public void AssetsFor_EachFileKind_ReturnsExpectedRelativeSegment()
    {
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.Blueprint), "Assets/Blueprints");
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.BTree),     "Assets/BTrees");
        AssertEndsWithRelative(AssetRoots.AssetsFor(AssetKind.Hsm),       "Assets/HSMs");
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

    // ── RecipesFor ────────────────────────────────────────────────

    [Fact]
    public void RecipesFor_AllKinds_IncludingScenario()
    {
        // File kinds via RecipesFor.
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Blueprint), "Recipes/Blueprints");
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.BTree),     "Recipes/BTrees");
        AssertEndsWithRelative(AssetRoots.RecipesFor(AssetKind.Hsm),       "Recipes/HSMs");

        // Scenario via dedicated member (no AssetKind.Scenario yet).
        AssertEndsWithRelative(AssetRoots.ScenariosRecipesRoot, "Recipes/Scenarios");
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

    // ── Scenario has no Assets root ───────────────────────────────

    [Fact]
    public void AssetsFor_Scenario_HasNoAssetsRoot()
    {
        // There is no AssetKind.Scenario yet; the DESIGN calls for a dedicated
        // ScenariosRecipesRoot as the *only* scenario root.  Verifying that:
        //   1. ScenariosRecipesRoot points to Recipes/Scenarios (not Assets),
        //   2. No scenario Assets root exists (there's no enum value to pass),
        //   3. Unsupported kinds (Blackboard, Utility) throw, documenting the contract.
        string scenariosRoot = AssetRoots.ScenariosRecipesRoot;
        AssertEndsWithRelative(scenariosRoot, "Recipes/Scenarios");

        // Confirm the scenario root is under RecipesRoot, NOT AssetsRoot.
        string recipesRoot = AssetRoots.RecipesRoot;
        string assetsRoot  = AssetRoots.AssetsRoot;

        Assert.StartsWith(recipesRoot, scenariosRoot);
        Assert.False(scenariosRoot.StartsWith(assetsRoot),
            "Scenario root must NOT be under AssetsRoot — scenarios have no Assets root.");

        // Blackboard and Utility throw from AssetsFor (already covered above);
        // they also throw from RecipesFor because they have neither root.
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
