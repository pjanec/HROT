using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class NewAssetServiceTests
{
    private static BlueprintEditableAssetAdapter Wrap(BlueprintAsset asset)
        => new(asset);

    [Fact]
    public void CreateNew_MintsFreshAssetId()
    {
        var svc = new BlueprintNewAssetService();
        var result1 = svc.CreateNew(null, "Test1", "");
        var result2 = svc.CreateNew(null, "Test2", "");

        Assert.NotEqual(Guid.Empty, result1.AssetId);
        Assert.NotEqual(Guid.Empty, result2.AssetId);
        Assert.NotEqual(result1.AssetId, result2.AssetId);
    }

    [Fact]
    public void CreateNew_MintsDifferentIdThanRecipe()
    {
        var svc = new BlueprintNewAssetService();
        var recipe = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "MyRecipe",
            EditorMetadata = new AssetMetadata
            {
                Recipe = new Core.Assets.RecipeMetadata
                {
                    DisplayName = "My Recipe",
                },
            },
        };

        var result = svc.CreateNew(Wrap(recipe), "Clone", "");
        Assert.NotEqual(recipe.AssetId, result.AssetId);
        Assert.NotEqual(Guid.Empty, result.AssetId);
    }

    [Fact]
    public void Empty_ProducesMinimalValidBlueprint_InCode()
    {
        var svc = new BlueprintNewAssetService();
        var result = svc.CreateNew(null, "MyEmptyBlueprint", "some/subfolder");

        Assert.Equal("MyEmptyBlueprint", result.Name);
        Assert.Equal(AssetKind.Blueprint, result.Kind);
        Assert.NotEqual(Guid.Empty, result.AssetId);

        // Verify the underlying BlueprintAsset is minimal but valid
        var adapter = Assert.IsType<BlueprintEditableAssetAdapter>(result);
        var bp = adapter.Asset;
        Assert.NotNull(bp);
        Assert.Equal("MyEmptyBlueprint", bp.Name);
        Assert.NotEqual(Guid.Empty, bp.AssetId);
        // Dispatch is set (required field)
        Assert.True(bp.Dispatch == BlueprintDispatchKind.Instance
                    || bp.Dispatch == BlueprintDispatchKind.Library
                    || bp.Dispatch == BlueprintDispatchKind.AiPrimitive);
        // Editor metadata exists but has no Recipe (clean asset)
        Assert.NotNull(bp.EditorMetadata);
        Assert.Null(bp.EditorMetadata.Recipe);
        // Graphs list is initialized (non-null)
        Assert.NotNull(bp.Graphs);
    }

    [Fact]
    public void Empty_ProducesMinimalValidBlueprint_WithEmptySentinel()
    {
        var svc = new BlueprintNewAssetService();
        // Passing the "Empty" synthetic recipe also triggers in-code synthesis
        var emptyRecipe = svc.AvailableRecipes().First(r => r.Name == "Empty");
        var result = svc.CreateNew(emptyRecipe, "FromEmpty", "");

        var adapter = Assert.IsType<BlueprintEditableAssetAdapter>(result);
        var bp = adapter.Asset;
        Assert.Equal("FromEmpty", bp.Name);
        Assert.NotEqual(Guid.Empty, bp.AssetId);
        Assert.Null(bp.EditorMetadata.Recipe);
    }

    [Fact]
    public void CreateNew_FromRecipe_ClonesContent_NewIdentity()
    {
        var svc = new BlueprintNewAssetService();
        var recipe = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "MyRecipe",
            Dispatch = BlueprintDispatchKind.Library,
            EditorMetadata = new AssetMetadata
            {
                Description = "A test recipe",
                Recipe = new Core.Assets.RecipeMetadata
                {
                    DisplayName    = "My Recipe",
                    Category       = "Tests",
                    ConceptsTaught = new List<string> { "A", "B" },
                },
            },
            Graphs = new List<Graph>
            {
                new()
                {
                    Name = "Main",
                    Kind = GraphKind.Event,
                    Nodes = new List<Node>
                    {
                        new ReturnNode { Id = Guid.NewGuid() },
                    },
                },
            },
        };

        var result = svc.CreateNew(Wrap(recipe), "CloneName", "");
        var adapter = Assert.IsType<BlueprintEditableAssetAdapter>(result);
        var clone = adapter.Asset;

        // New identity
        Assert.NotEqual(recipe.AssetId, clone.AssetId);
        Assert.NotEqual(Guid.Empty, clone.AssetId);
        Assert.Equal("CloneName", clone.Name);

        // Content cloned
        Assert.Equal(recipe.Dispatch, clone.Dispatch);
        Assert.Equal(recipe.EditorMetadata.Description, clone.EditorMetadata.Description);
        Assert.Single(clone.Graphs);
        Assert.Equal("Main", clone.Graphs[0].Name);
        Assert.Single(clone.Graphs[0].Nodes);

        // Recipe metadata stripped from clone
        Assert.NotNull(clone.EditorMetadata);
        Assert.Null(clone.EditorMetadata.Recipe);

        // Original unchanged
        Assert.NotNull(recipe.EditorMetadata.Recipe);
        Assert.Equal("My Recipe", recipe.EditorMetadata.Recipe.DisplayName);
    }

    [Fact]
    public void AvailableRecipes_IncludesEmptyEntry()
    {
        var svc = new BlueprintNewAssetService();
        var recipes = svc.AvailableRecipes();

        Assert.NotEmpty(recipes);
        var empty = recipes.FirstOrDefault(r => r.Name == "Empty");
        Assert.NotNull(empty);
        Assert.Equal(AssetKind.Blueprint, empty.Kind);
    }

    [Fact]
    public void CreateNew_NullName_Throws()
    {
        var svc = new BlueprintNewAssetService();
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "", ""));
    }

    [Fact]
    public void Kind_IsBlueprint()
    {
        var svc = new BlueprintNewAssetService();
        Assert.Equal(AssetKind.Blueprint, svc.Kind);
    }

    // ── BP-92: blank-template table (Empty / Function Library) ────────────

    [Fact]
    public void AvailableRecipes_ExposesBothBuiltInTemplates_WithExpectedDispatch()
    {
        var svc = new BlueprintNewAssetService();
        var recipes = svc.AvailableRecipes();

        var empty = recipes.First(r => r.Name == "Empty");
        var library = recipes.First(r => r.Name == "Function Library");

        var emptyBp = Assert.IsType<BlueprintEditableAssetAdapter>(empty).Asset;
        var libraryBp = Assert.IsType<BlueprintEditableAssetAdapter>(library).Asset;

        Assert.Equal(BlueprintDispatchKind.Instance, emptyBp.Dispatch);
        Assert.Equal(BlueprintDispatchKind.Library, libraryBp.Dispatch);
    }

    [Fact]
    public void CreateNew_WithLibraryTemplate_ProducesLibraryDispatch_FreshId_RequestedName()
    {
        var svc = new BlueprintNewAssetService();
        var libraryTemplate = svc.AvailableRecipes().First(r => r.Name == "Function Library");

        var result = svc.CreateNew(libraryTemplate, "MyLibrary", "");
        var bp = Assert.IsType<BlueprintEditableAssetAdapter>(result).Asset;

        Assert.Equal(BlueprintDispatchKind.Library, bp.Dispatch);
        Assert.NotEqual(Guid.Empty, bp.AssetId);
        Assert.NotEqual(libraryTemplate.AssetId, bp.AssetId);
        Assert.Equal("MyLibrary", bp.Name);
    }

    [Fact]
    public void CreateNew_WithInstanceTemplate_ProducesInstanceDispatch()
    {
        var svc = new BlueprintNewAssetService();
        var emptyTemplate = svc.AvailableRecipes().First(r => r.Name == "Empty");

        var result = svc.CreateNew(emptyTemplate, "MyInstance", "");
        var bp = Assert.IsType<BlueprintEditableAssetAdapter>(result).Asset;

        Assert.Equal(BlueprintDispatchKind.Instance, bp.Dispatch);
    }

    [Fact]
    public void CreateNew_WithNullRecipe_StillDefaultsToInstanceDispatch()
    {
        // Pre-existing default behaviour must be unchanged: null recipe => first table row (Instance).
        var svc = new BlueprintNewAssetService();
        var result = svc.CreateNew(null, "MyDefault", "");
        var bp = Assert.IsType<BlueprintEditableAssetAdapter>(result).Asset;

        Assert.Equal(BlueprintDispatchKind.Instance, bp.Dispatch);
    }

    [Fact]
    public void CreateNew_FromBuiltInTemplate_StripsRecipeMetadata()
    {
        var svc = new BlueprintNewAssetService();
        var libraryTemplate = svc.AvailableRecipes().First(r => r.Name == "Function Library");

        var result = svc.CreateNew(libraryTemplate, "MyLibrary", "");
        var bp = Assert.IsType<BlueprintEditableAssetAdapter>(result).Asset;

        Assert.NotNull(bp.EditorMetadata);
        Assert.Null(bp.EditorMetadata.Recipe);

        // Original template's recipe metadata is untouched.
        var templateBp = Assert.IsType<BlueprintEditableAssetAdapter>(libraryTemplate).Asset;
        Assert.NotNull(templateBp.EditorMetadata.Recipe);
        Assert.Equal("Function Library", templateBp.EditorMetadata.Recipe!.DisplayName);
    }

    [Fact]
    public void CreateNew_FromLibraryTemplate_RoundTripsDispatchThroughJson()
    {
        var svc = new BlueprintNewAssetService();
        var libraryTemplate = svc.AvailableRecipes().First(r => r.Name == "Function Library");

        var result = svc.CreateNew(libraryTemplate, "MyLibrary", "");
        var bp = Assert.IsType<BlueprintEditableAssetAdapter>(result).Asset;

        var json = BlueprintJsonServices.Serialize(bp);
        var reloaded = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(reloaded);
        Assert.Equal(BlueprintDispatchKind.Library, reloaded!.Dispatch);
    }

    [Fact]
    public void IsBlankTemplate_TrueForBuiltInTemplates_FalseForDiskRecipe()
    {
        var svc = new BlueprintNewAssetService();
        var recipes = svc.AvailableRecipes();

        var empty = recipes.First(r => r.Name == "Empty");
        var library = recipes.First(r => r.Name == "Function Library");

        Assert.True(svc.IsBlankTemplate(empty));
        Assert.True(svc.IsBlankTemplate(library));

        var diskRecipe = Wrap(new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "SomeDiskRecipe",
            EditorMetadata = new AssetMetadata
            {
                Recipe = new Core.Assets.RecipeMetadata { DisplayName = "Some Disk Recipe" },
            },
        });

        Assert.False(svc.IsBlankTemplate(diskRecipe));
    }
}
