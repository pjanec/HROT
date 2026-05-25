using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class NewFromRecipeServiceTests
{
    private static BlueprintAsset MakeRecipe(string name = "MyRecipe") =>
        new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = name,
            Dispatch = BlueprintDispatchKind.Instance,
            EditorMetadata = new AssetMetadata
            {
                Recipe = new RecipeMetadata
                {
                    DisplayName    = "My Recipe",
                    Description    = "Test recipe",
                    ConceptsTaught = new List<string> { "ConceptA", "ConceptB" }
                }
            }
        };

    [Fact]
    public void CreateFromRecipe_AssignsFreshAssetId()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        var clone  = svc.CreateFromRecipe(recipe, "MyCopy");
        Assert.NotEqual(recipe.AssetId, clone.AssetId);
        Assert.NotEqual(Guid.Empty, clone.AssetId);
    }

    [Fact]
    public void CreateFromRecipe_SetsNewName()
    {
        var svc   = new NewFromRecipeService();
        var clone = svc.CreateFromRecipe(MakeRecipe(), "FancyName");
        Assert.Equal("FancyName", clone.Name);
    }

    [Fact]
    public void CreateFromRecipe_StripsRecipeMetadata()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        Assert.NotNull(recipe.EditorMetadata.Recipe);  // sanity-check recipe has metadata

        var clone = svc.CreateFromRecipe(recipe, "Copy");
        Assert.Null(clone.EditorMetadata.Recipe);
    }

    [Fact]
    public void CreateFromRecipe_PreservesDispatch()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        var clone  = svc.CreateFromRecipe(recipe, "Copy");
        Assert.Equal(recipe.Dispatch, clone.Dispatch);
    }

    [Fact]
    public void CreateFromRecipe_DoesNotMutateOriginal()
    {
        var svc      = new NewFromRecipeService();
        var recipe   = MakeRecipe();
        var origId   = recipe.AssetId;
        var origName = recipe.Name;
        _ = svc.CreateFromRecipe(recipe, "Copy");
        Assert.Equal(origId,   recipe.AssetId);
        Assert.Equal(origName, recipe.Name);
        Assert.NotNull(recipe.EditorMetadata.Recipe);  // original still has recipe metadata
    }

    [Fact]
    public void CreateFromRecipe_EmptyName_Throws()
    {
        var svc = new NewFromRecipeService();
        Assert.Throws<ArgumentException>(() => svc.CreateFromRecipe(MakeRecipe(), ""));
    }

    [Fact]
    public void CreateFromRecipe_TwoCalls_DifferentAssetIds()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        var clone1 = svc.CreateFromRecipe(recipe, "Copy1");
        var clone2 = svc.CreateFromRecipe(recipe, "Copy2");
        Assert.NotEqual(clone1.AssetId, clone2.AssetId);
    }
}
