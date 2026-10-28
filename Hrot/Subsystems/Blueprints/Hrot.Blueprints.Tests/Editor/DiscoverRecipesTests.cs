using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// MTB-P0-T3 SC1: Verify <see cref="BlueprintEditorBootstrap.DiscoverRecipes"/>
/// reads from the recipes/behaviors/Blueprints root of the content root.
/// ⭐ CE-4089: the content root is the NAS (or the configured root); these rails point the configured root at the repository tree —
/// where the recipes live — and restore it. ⚠ <see cref="AssetRoots.Configure"/> is process-global, hence the serial collection.
/// </summary>
[Collection(AssetRootsConfiguredCollection.Name)]
public sealed class DiscoverRecipesTests
{
    private static T WithTheRepositoryAsContentRoot<T>(Func<T> body)
    {
        var saved = AssetRoots.ConfiguredRoot;
        try
        {
            AssetRoots.Configure(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree());
            return body();
        }
        finally { AssetRoots.Configure(saved); }
    }

    /// <summary>
    /// The production recipes (CountingDemo, EditorTypesDemo, etc.) must be
    /// discoverable from the recipes/behaviors/Blueprints root.
    /// </summary>
    [Fact]
    public void Discovers_FromRecipesBlueprintsRoot()
    {
        // Ensure the Hrot.AI.Behaviors assembly is loaded so DiscoverRecipes
        // can resolve its output directory.  Many tests (e.g. RecipeIntegrityTests)
        // already load this assembly implicitly, but isolated test-filter runs may not.
        // Force-load Hrot.AI.Behaviors so DiscoverRecipes can resolve its output directory.
        _ = Assembly.Load("Hrot.AI.Behaviors");

        var recipes = WithTheRepositoryAsContentRoot(BlueprintEditorBootstrap.DiscoverRecipes);

        // At minimum the CountingDemo recipe must be present (it's the simplest
        // recipe and has been committed since WHEN-M11-T4).
        Assert.NotEmpty(recipes);

        var recipeNames = recipes.Select(r => r.Name).ToHashSet();
        Assert.Contains("CountingDemo", recipeNames);

        // Every returned recipe must carry EditorMetadata.Recipe != null
        // (DiscoverRecipes filters on this).
        Assert.All(recipes, r =>
        {
            Assert.NotNull(r.EditorMetadata);
            Assert.NotNull(r.EditorMetadata.Recipe);
        });
    }

    /// <summary>
    /// Recipes must NOT carry an AssetId of all-zeros — they are valid
    /// blueprint assets with real identity.
    /// </summary>
    [Fact]
    public void DiscoveredRecipes_HaveNonEmptyAssetIds()
    {
        // Force-load Hrot.AI.Behaviors so DiscoverRecipes can resolve its output directory.
        _ = Assembly.Load("Hrot.AI.Behaviors");

        var recipes = WithTheRepositoryAsContentRoot(BlueprintEditorBootstrap.DiscoverRecipes);

        Assert.All(recipes, r =>
        {
            Assert.NotEqual(Guid.Empty, r.AssetId);
        });
    }
}

/// <summary>⭐ CE-4089 — classes that point the process-global <see cref="AssetRoots"/> at another root run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AssetRootsConfiguredCollection
{
    public const string Name = "AssetRootsConfigured";
}
