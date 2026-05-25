using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Creates a new Blueprint asset from a recipe template by cloning its structure
/// and assigning a fresh identity.
/// </summary>
public sealed class NewFromRecipeService
{
    /// <summary>
    /// Clones <paramref name="recipe"/> into a new asset with a fresh AssetId and the
    /// given <paramref name="newName"/>. The <c>EditorMetadata.Recipe</c> block is
    /// stripped from the clone so the copy is not itself treated as a recipe.
    /// </summary>
    /// <returns>The new (unregistered) asset, ready for the host to save and register.</returns>
    public BlueprintAsset CreateFromRecipe(BlueprintAsset recipe, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName must not be empty.", nameof(newName));

        var json  = BlueprintJsonServices.Serialize(recipe);
        var clone = BlueprintJsonServices.Deserialize(json)
                    ?? throw new InvalidOperationException("Serialization round-trip returned null.");

        clone.AssetId = Guid.NewGuid();
        clone.Name    = newName;
        clone.EditorMetadata.Recipe = null;  // strip recipe metadata from the copy

        return clone;
    }
}
