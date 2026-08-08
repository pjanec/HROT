using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Blueprint implementation of <see cref="INewAssetService"/>.
/// Creates new in-memory Blueprint assets from recipes (via
/// <see cref="NewFromRecipeService"/>) or one of the built-in blank templates
/// (see <see cref="BlankTemplates"/>).
/// </summary>
public sealed class BlueprintNewAssetService : INewAssetService
{
    /// <summary>
    /// One row per built-in blank-template recipe the "New Blueprint" picker offers.
    /// This is the dispatch choice (BP-92): rather than a two-way toggle, each offered
    /// <see cref="BlueprintDispatchKind"/> gets its own blank-template recipe entry —
    /// exactly how Unreal offers "Blueprint Class / Function Library / Macro Library"
    /// as separate create-asset entries. A fourth row (MacroLibrary) slots in here
    /// without a data migration, per docs/blueprints/Architect_Question_25_Macros.md —
    /// that is why this is a table and not a bool.
    ///
    /// AiPrimitive is deliberately NOT offered here: an AiPrimitive asset needs a
    /// Primitive declaration and hostings that this flow does not populate.
    /// </summary>
    private readonly record struct BlankTemplateRow(string Name, BlueprintDispatchKind Dispatch, string Description);

    private static readonly BlankTemplateRow[] BlankTemplates =
    {
        new("Empty",
            BlueprintDispatchKind.Instance,
            "Start from scratch with an empty blueprint. Runs on an entity instance; graphs may contain latent nodes such as Delay."),
        new("Function Library",
            BlueprintDispatchKind.Library,
            "A shared library of pure Functions, callable from any other blueprint. Compiles to static methods, so its graphs cannot contain latent nodes such as Delay."),
    };

    private readonly NewFromRecipeService _newFromRecipeService = new();
    private readonly BlueprintEditableAssetAdapter[] _blankTemplateRecipes;

    public BlueprintNewAssetService()
    {
        _blankTemplateRecipes = new BlueprintEditableAssetAdapter[BlankTemplates.Length];
        for (int i = 0; i < BlankTemplates.Length; i++)
        {
            var row   = BlankTemplates[i];
            var asset = MakeEmptyBlueprint(row.Dispatch, row.Name);
            // The recipe entry in AvailableRecipes carries recipe metadata.
            asset.EditorMetadata.Recipe = new Core.Assets.RecipeMetadata
            {
                DisplayName = row.Name,
                Description = row.Description,
            };
            _blankTemplateRecipes[i] = new BlueprintEditableAssetAdapter(asset);
        }
    }

    public AssetKind Kind => AssetKind.Blueprint;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        BlueprintAsset newAsset;

        if (recipe == null)
        {
            // Null recipe preserves today's default behaviour: the first table row (Instance).
            newAsset = MakeEmptyBlueprint(BlankTemplates[0].Dispatch, name);
            newAsset.AssetId = Guid.NewGuid();
        }
        else if (TryGetBlankTemplateRow(recipe, out var row))
        {
            newAsset = MakeEmptyBlueprint(row.Dispatch, name);
            newAsset.AssetId = Guid.NewGuid();
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
        var recipes = new List<IEditableAsset>(_blankTemplateRecipes);

        foreach (var bpRecipe in BlueprintEditorBootstrap.DiscoverRecipes())
        {
            recipes.Add(new BlueprintEditableAssetAdapter(bpRecipe));
        }

        return recipes;
    }

    /// <inheritdoc />
    public bool IsBlankTemplate(IEditableAsset recipe)
        => TryGetBlankTemplateRow(recipe, out _);

    /// <summary>
    /// Returns true (and the matching <see cref="BlankTemplateRow"/>) when <paramref name="recipe"/>
    /// is exactly one of the cached built-in blank-template instances that <see cref="AvailableRecipes"/>
    /// returned — matched by <see cref="IEditableAsset.AssetId"/>, not by name, since the picker
    /// hands back the very instances this service created.
    /// </summary>
    private bool TryGetBlankTemplateRow(IEditableAsset recipe, out BlankTemplateRow row)
    {
        for (int i = 0; i < _blankTemplateRecipes.Length; i++)
        {
            if (_blankTemplateRecipes[i].AssetId == recipe.AssetId)
            {
                row = BlankTemplates[i];
                return true;
            }
        }

        row = default;
        return false;
    }

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
    /// The returned asset has no recipe metadata; callers that use this as a blank-template
    /// recipe entry (the constructor) add it afterwards.
    /// </summary>
    private static BlueprintAsset MakeEmptyBlueprint(BlueprintDispatchKind dispatch, string name)
    {
        return new BlueprintAsset
        {
            Header         = new Header(),
            AssetId        = Guid.NewGuid(),
            Name           = name,
            Dispatch       = dispatch,
            EditorMetadata = new AssetMetadata(),
        };
    }
}
