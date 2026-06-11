namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// Kind-agnostic recipe metadata used by the unified asset creation flow
/// (INewAssetService, dialogs, BTree/HSM/Scenario). Mirrors the fields of the
/// Compiler JSON-model <c>Hrot.Blueprints.Core.Assets.RecipeMetadata</c> but lives
/// in the shared net8.0 editor layer so all asset kinds can consume it.
/// </summary>
public sealed class RecipeMetadata
{
    /// <summary>
    /// Human-readable name shown in recipe pickers.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Grouping label (e.g. "AI", "Movement", "Combat").
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Longer description shown in the recipe details panel.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Difficulty label. Defaults to <c>"Beginner"</c>.
    /// </summary>
    public string Difficulty { get; set; } = "Beginner";

    /// <summary>
    /// List of concepts the recipe teaches. Never null.
    /// </summary>
    public List<string> ConceptsTaught { get; set; } = new();
}
