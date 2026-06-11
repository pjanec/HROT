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
}
