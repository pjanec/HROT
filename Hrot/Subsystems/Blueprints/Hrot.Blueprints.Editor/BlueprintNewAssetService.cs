using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Blueprint implementation of <see cref="INewAssetService"/>.
/// Creates new in-memory Blueprint assets from recipes (via
/// <see cref="NewFromRecipeService"/>) or the hardcoded "Empty" recipe.
/// </summary>
public sealed class BlueprintNewAssetService : INewAssetService
{
    private readonly NewFromRecipeService _newFromRecipeService = new();
    private readonly IEditableAsset _emptyRecipe;
    private readonly BlueprintAsset _emptyBlueprint;

    public BlueprintNewAssetService()
    {
        _emptyBlueprint = MakeEmptyBlueprint();
        // The "Empty" recipe entry in AvailableRecipes carries recipe metadata.
        _emptyBlueprint.EditorMetadata.Recipe = new Core.Assets.RecipeMetadata
        {
            DisplayName = "Empty",
            Description = "Start from scratch with an empty blueprint.",
        };
        _emptyRecipe = new BlueprintEditableAssetAdapter(_emptyBlueprint);
    }

    public AssetKind Kind => AssetKind.Blueprint;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        BlueprintAsset newAsset;

        if (recipe == null || IsEmptyRecipe(recipe))
        {
            newAsset = MakeEmptyBlueprint();
            newAsset.AssetId = Guid.NewGuid();
            newAsset.Name = name;
        }
        else
        {
            var bpRecipe = ExtractBlueprintAsset(recipe);
            newAsset = _newFromRecipeService.CreateFromRecipe(bpRecipe, name);
        }

        // SourceFilePath may be set to a non-empty value by file-writing phases.
        return new BlueprintEditableAssetAdapter(newAsset);
    }

    /// <inheritdoc />
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        var recipes = new List<IEditableAsset> { _emptyRecipe };

        foreach (var bpRecipe in BlueprintEditorBootstrap.DiscoverRecipes())
        {
            recipes.Add(new BlueprintEditableAssetAdapter(bpRecipe));
        }

        return recipes;
    }

    private static bool IsEmptyRecipe(IEditableAsset recipe)
        => string.Equals(recipe.Name, "Empty", StringComparison.OrdinalIgnoreCase);

    private static BlueprintAsset ExtractBlueprintAsset(IEditableAsset recipe)
    {
        if (recipe is BlueprintEditableAssetAdapter adapter)
            return adapter.Asset;

        throw new ArgumentException(
            $"Recipe must be a {nameof(BlueprintEditableAssetAdapter)} wrapping a BlueprintAsset.",
            nameof(recipe));
    }

    /// <summary>
    /// Synthesizes a minimal valid BlueprintAsset in code — no disk read, no file I/O.
    /// The returned asset has no recipe metadata; the caller (constructor) adds it for
    /// the "Empty" recipe entry in <see cref="AvailableRecipes"/>.
    /// </summary>
    private static BlueprintAsset MakeEmptyBlueprint()
    {
        return new BlueprintAsset
        {
            Header         = new Header(),
            AssetId        = Guid.NewGuid(),
            Name           = "Empty",
            Dispatch       = BlueprintDispatchKind.Instance,
            EditorMetadata = new AssetMetadata(),
        };
    }
}
