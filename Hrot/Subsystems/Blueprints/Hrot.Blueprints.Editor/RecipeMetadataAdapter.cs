using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Maps the Compiler JSON-model <see cref="Core.Assets.RecipeMetadata"/> to the
/// kind-agnostic shared <see cref="Hrot.Editor.AiShared.Recipes.RecipeMetadata"/>
/// so blueprint recipe code can surface shared metadata to the unified creation flow.
/// </summary>
public static class RecipeMetadataAdapter
{
    /// <summary>
    /// Converts a Compiler <see cref="Core.Assets.RecipeMetadata"/> instance to the
    /// shared <see cref="Hrot.Editor.AiShared.Recipes.RecipeMetadata"/> type.
    /// </summary>
    /// <param name="compilerMeta">The source Compiler metadata. May be null.</param>
    /// <returns>
    /// A new shared <see cref="Hrot.Editor.AiShared.Recipes.RecipeMetadata"/> with the same values,
    /// or null when <paramref name="compilerMeta"/> is null.
    /// </returns>
    public static Hrot.Editor.AiShared.Recipes.RecipeMetadata? ToShared(
        this Core.Assets.RecipeMetadata? compilerMeta)
    {
        if (compilerMeta == null)
            return null;

        return new Hrot.Editor.AiShared.Recipes.RecipeMetadata
        {
            DisplayName    = compilerMeta.DisplayName,
            Category       = compilerMeta.Category,
            Description    = compilerMeta.Description,
            Difficulty     = compilerMeta.Difficulty,
            ConceptsTaught = new List<string>(compilerMeta.ConceptsTaught),
        };
    }

    /// <summary>
    /// ⭐⭐ <b><c>MA-020</c> — the shared <see cref="Hrot.Editor.AiShared.Recipes.RecipeMetadata"/> behind a
    /// recipe entry, or <see langword="null"/> when it carries none.</b>
    ///
    /// <para>⭐ <b>This assembly is the right home, and the reason is a reference wall:</b> the metadata
    /// lives on <c>BlueprintAsset.EditorMetadata.Recipe</c>, so the resolver must see
    /// <see cref="Variables.BlueprintEditableAssetAdapter"/> — which <c>Hrot.Editor.AiShared</c> cannot
    /// *(it is the layer BELOW)*. ⛔ Both <c>EditorSubsystem</c> and <c>CgfSubsystem</c> already reference
    /// THIS assembly, so one implementation serves both hosts *(ruling 9)*.</para>
    ///
    /// <para>⚠ A recipe of another kind returns <see langword="null"/> rather than throwing: the BTree and
    /// HSM services offer SYNTHETIC "Empty"/"Starter" entries that genuinely carry no metadata, and
    /// <b>"no description" and "not a blueprint" are the same honest answer to the caller.</b></para>
    /// </summary>
    public static Hrot.Editor.AiShared.Recipes.RecipeMetadata? SharedMetadataOf(
        Hrot.Editor.AiShared.IEditableAsset? recipe)
        => recipe is Variables.BlueprintEditableAssetAdapter adapter
            ? adapter.Asset?.EditorMetadata?.Recipe.ToShared()
            : null;

    /// <summary>
    /// ⭐ The <c>describe</c> seam of <c>RecipePickerSource</c> / <c>NewAssetLauncher</c>.
    /// ⚠ Both seams were optional and NO production caller passed them, so every recipe rendered with a
    /// null description while the metadata was sitting on the asset — 📌 the silent-default shape
    /// *(the caller HAD the value and did not pass it)*. ⭐ This is the value they now pass.
    /// </summary>
    public static string? DescribeRecipe(Hrot.Editor.AiShared.IEditableAsset recipe)
    {
        var description = SharedMetadataOf(recipe)?.Description;
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    /// <summary>
    /// ⭐ The <c>recipeCategory</c> seam — the sub-category appended to the kind label as
    /// <c>"Kind/SubCategory"</c>. ⛔ Null when the recipe declares none, which leaves the plain kind label.
    /// </summary>
    public static string? RecipeCategory(Hrot.Editor.AiShared.IEditableAsset recipe)
    {
        var category = SharedMetadataOf(recipe)?.Category;
        return string.IsNullOrWhiteSpace(category) ? null : category;
    }
}
