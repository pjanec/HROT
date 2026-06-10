using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// MTB-P0-T3 SC1: Verify <see cref="BlueprintEditorBootstrap.DiscoverRecipes"/>
/// reads from the new Recipes/Blueprints root.
/// </summary>
public sealed class DiscoverRecipesTests
{
    /// <summary>
    /// The production recipes (CountingDemo, EditorTypesDemo, etc.) must be
    /// discoverable from the Recipes/Blueprints output directory.
    /// </summary>
    [Fact]
    public void Discovers_FromRecipesBlueprintsRoot()
    {
        // Ensure the Hrot.AI.Behaviors assembly is loaded so DiscoverRecipes
        // can resolve its output directory.  Many tests (e.g. RecipeIntegrityTests)
        // already load this assembly implicitly, but isolated test-filter runs may not.
        // Force-load Hrot.AI.Behaviors so DiscoverRecipes can resolve its output directory.
        _ = Assembly.Load("Hrot.AI.Behaviors");

        var recipes = BlueprintEditorBootstrap.DiscoverRecipes();

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

        var recipes = BlueprintEditorBootstrap.DiscoverRecipes();

        Assert.All(recipes, r =>
        {
            Assert.NotEqual(Guid.Empty, r.AssetId);
        });
    }
}
