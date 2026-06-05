using Hrot.Blueprints.Core.Assets;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

public sealed class RecipeCreateModal
{
    private const string PopupId = "New from Recipe##bp_recipe_modal";

    private readonly Action<BlueprintAsset, string> _onConfirm;
    private readonly List<BlueprintAsset> _recipes;

    private bool _openRequested;
    private string _name = "NewBlueprint";
    private int _recipeIndex;

    public RecipeCreateModal(Action<BlueprintAsset, string> onConfirm)
    {
        _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _recipes = BlueprintEditorBootstrap.DiscoverRecipes();
    }

    public void Open()
    {
        _name = "NewBlueprint";
        _recipeIndex = 0;
        _openRequested = true;
    }

    public void Draw()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_recipes.Count == 0)
        {
            ImGui.TextDisabled("No recipes found in the project.");
            if (ImGui.Button("Close"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted("New Asset Name");
        ImGui.SetNextItemWidth(300f);
        ImGui.InputText("##bp_recipe_name", ref _name, 128);

        ImGui.TextUnformatted("Select Recipe");
        ImGui.SetNextItemWidth(300f);

        _recipeIndex = Math.Clamp(_recipeIndex, 0, _recipes.Count - 1);
        var selectedRecipe = _recipes[_recipeIndex];
        string preview = selectedRecipe.EditorMetadata.Recipe?.DisplayName ?? selectedRecipe.Name;

        if (ImGui.BeginCombo("##bp_recipe_combo", preview))
        {
            for (int i = 0; i < _recipes.Count; i++)
            {
                var recipe = _recipes[i];
                var recipeMeta = recipe.EditorMetadata.Recipe;
                string label = recipeMeta != null
                    ? $"{recipeMeta.DisplayName} ({FormatBadge(recipe, recipeMeta)})"
                    : recipe.Name;

                bool isSelected = _recipeIndex == i;
                if (ImGui.Selectable(label, isSelected))
                    _recipeIndex = i;

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.Separator();
        var meta = selectedRecipe.EditorMetadata.Recipe;
        if (meta != null)
        {
            ImGui.TextDisabled($"Difficulty: {meta.Difficulty} | Category: {meta.Category}");
            if (!string.IsNullOrWhiteSpace(meta.Description))
                ImGui.TextWrapped(meta.Description);

            if (meta.ConceptsTaught.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Concepts Taught:");
                foreach (var concept in meta.ConceptsTaught)
                    ImGui.BulletText(concept);
            }
        }
        ImGui.Separator();

        bool isValid = !string.IsNullOrWhiteSpace(_name);
        ImGui.BeginDisabled(!isValid);
        if (ImGui.Button("Create", new System.Numerics.Vector2(120, 0)))
        {
            _onConfirm(selectedRecipe, _name.Trim());
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(120, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static string FormatBadge(BlueprintAsset recipe, RecipeMetadata recipeMeta)
    {
        if (string.Equals(recipe.Name, "CoverAwarePatrol", StringComparison.Ordinal))
            return "* recommended for learning";

        return string.IsNullOrWhiteSpace(recipeMeta.Difficulty)
            ? "Recipe"
            : recipeMeta.Difficulty;
    }
}
