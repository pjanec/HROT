using System;
using System.Linq;

namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// ⭐⭐⭐ <b><c>MA-021</c> — resolve a recipe by NAME, or fall back to the kind's blank template.</b>
///
/// <para>⭐⭐ <b>Shared because BOTH hosts create.</b> 📐 <c>EditorSubsystem</c> and <c>CgfSubsystem</c>
/// each compose their own per-kind <see cref="INewAssetService"/> registry and each answer
/// <c>POST /assets</c>; the NAME→recipe lookup between them is one rule, so it is one implementation
/// *(ruling 9)*. ⛔ Two copies would drift on the only part that matters — what an unmatched name does.</para>
///
/// <para>⛔⛔ <b>An unmatched name is an ERROR, never a silent fallback to the blank template.</b>
/// 📌 Creating a blank asset when a recipe was asked for is the silent-wrong-answer shape this MCP
/// surface has now caught twice *(<c>MA-004</c>: an id that resolves to nothing; <c>MA-017</c>: a
/// command accepted that built nothing)*. ⭐ The refusal carries the names that WOULD have worked, so
/// the caller can correct it in one step.</para>
/// </summary>
public static class RecipeByName
{
    /// <summary>
    /// Picks the recipe called <paramref name="recipeName"/> from
    /// <see cref="INewAssetService.AvailableRecipes"/>, case-insensitively.
    /// </summary>
    /// <param name="service">The kind's new-asset service.</param>
    /// <param name="recipeName">
    /// The recipe name, or <see langword="null"/>/blank for the kind's blank template.
    /// </param>
    /// <returns>
    /// The recipe and a <see langword="null"/> error on success. ⚠ A <see langword="null"/> recipe with a
    /// <see langword="null"/> error is a valid answer: it means "use the in-code empty", which is what
    /// <see cref="INewAssetService.CreateNew"/> does with a null recipe.
    /// </returns>
    public static (IEditableAsset? Recipe, string? Error) Resolve(
        INewAssetService service, string? recipeName)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));

        var recipes = service.AvailableRecipes();

        if (string.IsNullOrWhiteSpace(recipeName))
        {
            foreach (var candidate in recipes)
                if (service.IsBlankTemplate(candidate))
                    return (candidate, null);

            // ⚠ A kind that offers no blank template is legitimate — null means "the in-code empty",
            //   which is exactly what CreateNew(null, …) already does. ⛔ Not an error.
            return (null, null);
        }

        foreach (var candidate in recipes)
            if (string.Equals(candidate.Name, recipeName, StringComparison.OrdinalIgnoreCase))
                return (candidate, null);

        return (null,
                $"[ERROR] '{recipeName}' is not a recipe {service.Kind} offers. Available: "
              + string.Join(", ", recipes.Select(r => $"'{r.Name}'"))
              + ". List them with GET /assets/recipes.");
    }
}
