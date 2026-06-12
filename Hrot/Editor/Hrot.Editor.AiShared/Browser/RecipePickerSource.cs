using Hrot.Editor.AiShared.Recipes;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Associates an <see cref="AssetKind"/> with the recipe asset chosen from
/// that kind's <see cref="INewAssetService.AvailableRecipes"/> list.
/// Used as the <see cref="PickerEntry.Tag"/> payload in <see cref="RecipePickerSource"/>.
/// </summary>
public sealed record RecipeChoice(AssetKind Kind, IEditableAsset Recipe);

/// <summary>
/// Projects per-kind recipes (from <see cref="INewAssetService.AvailableRecipes"/>,
/// including the "Empty" entry) into Tree-layout <see cref="PickerEntry"/> values —
/// the data seam for T7's new-from-recipe launcher.
/// </summary>
/// <remarks>
/// <para>
/// <b>D-T6-1:</b> This source exposes its recipe→entry projection publicly
/// (<see cref="ToEntry"/>, <see cref="BuildEntries"/>) so that T7 can feed it
/// through the entry-driven <c>IPickerRegistry.OpenPicker(PickerRequest{…})</c>
/// path — just as <see cref="AssetPickerSource"/> does for assets.
/// </para>
/// <para>
/// <b>Deterministic:</b> constructor accepts injectable seams
/// (<paramref name="describe"/>, <paramref name="recipeCategory"/>) so
/// logic exercised by tests never touches the real filesystem.
/// </para>
/// </remarks>
public sealed class RecipePickerSource : IPickerSource<RecipeChoice>
{
    private readonly IReadOnlyDictionary<AssetKind, INewAssetService> _services;
    private readonly Func<IEditableAsset, string?> _describe;
    private readonly Func<IEditableAsset, string?> _recipeCategory;
    private readonly IReadOnlyList<AssetKind> _kinds;

    /// <summary>
    /// Creates a <see cref="RecipePickerSource"/> that projects recipes from
    /// <paramref name="services"/> into <see cref="PickerEntry"/> values.
    /// </summary>
    /// <param name="services">
    /// Per-kind <see cref="INewAssetService"/> instances (never <see langword="null"/>).
    /// Iterated in <see cref="AssetKind"/> enum declaration order.
    /// </param>
    /// <param name="describe">
    /// Optional function that returns a long description for a recipe (e.g. recipe
    /// metadata). When <see langword="null"/>, all descriptions are <see langword="null"/>.
    /// </param>
    /// <param name="recipeCategory">
    /// Optional function that returns a sub-category for a recipe. When non-null
    /// and non-empty, the result is appended to the kind label as
    /// <c>"Kind/SubCategory"</c>. When <see langword="null"/>, the category is
    /// just the kind label.
    /// </param>
    public RecipePickerSource(
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        Func<IEditableAsset, string?>? describe = null,
        Func<IEditableAsset, string?>? recipeCategory = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _describe = describe ?? (_ => null);
        _recipeCategory = recipeCategory ?? (_ => null);
        _kinds = services.Keys.OrderBy(k => k).ToList().AsReadOnly();
    }

    // ── IPickerSource<RecipeChoice> properties ──────────────────────────

    /// <inheritdoc/>
    public string Title => "New Asset";

    /// <inheritdoc/>
    public string EmptyResultText => "No recipes found.";

    /// <inheritdoc/>
    public PickerLayout PreferredLayout => PickerLayout.Tree;

    /// <inheritdoc/>
    public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;

    /// <inheritdoc/>
    public QueryCost Cost => QueryCost.Cheap;

    /// <inheritdoc/>
    public bool IsAsync => false;

    /// <inheritdoc/>
    public bool AllowsDragOut => false;

    /// <inheritdoc/>
    public bool AllowsDragIn => false;

    /// <inheritdoc/>
    public bool AllowArbitraryTextInput => false;

    // ── Query ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<RecipeChoice> Query(
        string text,
        IReadOnlyDictionary<string, object?>? context)
    {
        var results = new List<RecipeChoice>();

        foreach (var kind in _kinds)
        {
            if (!_services.TryGetValue(kind, out var service))
                continue;

            foreach (var recipe in service.AvailableRecipes())
            {
                if (string.IsNullOrEmpty(text)
                    || recipe.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new RecipeChoice(kind, recipe));
                }
            }
        }

        return results.AsReadOnly();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RecipeChoice>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct)
        => Task.FromResult(Query(text, context));

    // ── Projection (public seam for T7) ────────────────────────────────

    /// <summary>
    /// Projects a single <see cref="RecipeChoice"/> into a
    /// <see cref="PickerEntry"/> with kind-grouped <see cref="PickerEntry.Category"/>,
    /// per-kind <see cref="PickerEntry.IconKey"/>, and <see cref="PickerEntry.Tag"/>
    /// set to the <see cref="RecipeChoice"/> itself.
    /// </summary>
    /// <param name="rc">The recipe choice to project (never <see langword="null"/>).</param>
    /// <returns>
    /// A <see cref="PickerEntry"/> whose <see cref="PickerEntry.Tag"/> is
    /// <paramref name="rc"/> — the T7 router consumes this to launch the
    /// new-asset dialog for the selected recipe.
    /// </returns>
    public PickerEntry ToEntry(RecipeChoice rc)
    {
        if (rc == null) throw new ArgumentNullException(nameof(rc));

        var sub = _recipeCategory(rc.Recipe);
        string? category = !string.IsNullOrEmpty(sub)
            ? $"{rc.Kind}/{sub}"
            : rc.Kind.ToString();

        return new PickerEntry(
            Id: GetItemKey(rc),
            Name: rc.Recipe.Name,
            Description: _describe(rc.Recipe),
            Category: category,
            Keywords: null,
            IconTextureId: null,
            Tag: rc,
            IconKey: AssetKindIcons.GetIconKey(rc.Kind));
    }

    /// <summary>
    /// Convenience method that queries the services and projects every matching
    /// recipe through <see cref="ToEntry"/>. Used by T7 as
    /// <c>PickerRequest.ItemsProvider</c>.
    /// </summary>
    /// <param name="text">Optional search filter.</param>
    /// <param name="context">Optional picker context (unused by this source).</param>
    /// <returns>A read-only list of <see cref="PickerEntry"/> values.</returns>
    public IReadOnlyList<PickerEntry> BuildEntries(
        string text,
        IReadOnlyDictionary<string, object?>? context)
        => Query(text, context).Select(ToEntry).ToList().AsReadOnly();

    // ── Identity / search helpers ──────────────────────────────────────

    /// <inheritdoc/>
    public string GetItemKey(RecipeChoice item) => $"{item.Kind}:{item.Recipe.Name}";

    /// <inheritdoc/>
    public string GetSearchableText(RecipeChoice item) => item.Recipe.Name;

    // ── Rendering (minimal — Tree layout uses PickerEntry directly) ────

    /// <inheritdoc/>
    public void RenderItem(RecipeChoice item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
            ImGuiNET.ImGui.TextUnformatted(item.Recipe.Name);
    }

    /// <inheritdoc/>
    public void RenderPreview(RecipeChoice item, IPickerRenderContext ctx)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
        {
            var desc = _describe(item.Recipe);
            if (desc != null)
                ImGuiNET.ImGui.TextUnformatted(desc);
        }
    }

    /// <inheritdoc/>
    public bool IsPreviewExpensive(RecipeChoice item) => false;

    /// <inheritdoc/>
    public bool CanAcceptDrop(object payload) => false;
}
