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
/// ⭐ <c>CE-460</c> (E4, handoff D5) — a technology the product-first tree SHOWS but cannot create, so the
/// menu tells the truth about what exists. Picking it creates nothing; <paramref name="Reason"/> says why.
/// 📌 Today: hand-written C# — authored in VS Code, and editor creation is its own slice (<c>CE-459</c>).
/// </summary>
public sealed record NotCreatableChoice(string Technology, string Reason);

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
    private readonly AuthoringProduct? _product;

    /// <summary>The label of the not-creatable C# row under a product root (<c>CE-459</c> enables it).</summary>
    public const string CSharpTechnology = "C#";

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
    /// <param name="product">
    /// ⭐ <c>CE-460</c> (E4) — <see langword="null"/> is New Asset's technology-first tree, unchanged.
    /// A product ROOTS the tree there: only recipes whose <see cref="INewAssetService.ProductOf"/> matches
    /// are listed, the path starts at the technology (<c>"Kind[/Sub]"</c>), a technology holding ONE recipe
    /// collapses to a leaf named after it, and a not-creatable C# row is appended.
    /// </param>
    public RecipePickerSource(
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        Func<IEditableAsset, string?>? describe = null,
        Func<IEditableAsset, string?>? recipeCategory = null,
        AuthoringProduct? product = null)
    {
        _product = product;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _describe = describe ?? (_ => null);
        _recipeCategory = recipeCategory ?? (_ => null);
        _kinds = services.Keys.OrderBy(k => k).ToList().AsReadOnly();
    }

    // ── IPickerSource<RecipeChoice> properties ──────────────────────────

    /// <inheritdoc/>
    public string Title => _product is { } p ? $"New {p}" : "New Asset";

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
                if (_product is { } product && service.ProductOf(recipe) != product)
                    continue;

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
    {
        var entries = Query(text, context).Select(ToEntry).ToList();
        if (_product is not { } product)
            return entries.AsReadOnly();

        // ⭐ D1 — a technology folder holding exactly ONE recipe collapses to a leaf named after the
        //   technology: under New Condition the author picks "Blueprint", not "Blueprint ▸ Condition".
        //   ⚠ Only a PLAIN technology folder collapses; a recipe with its own sub-category keeps its path.
        var perKind = entries.GroupBy(e => ((RecipeChoice)e.Tag!).Kind).ToDictionary(g => g.Key, g => g.Count());
        for (int i = 0; i < entries.Count; i++)
        {
            var rc = (RecipeChoice)entries[i].Tag!;
            if (perKind[rc.Kind] != 1 || entries[i].Category != rc.Kind.ToString())
                continue;

            bool blank = _services.TryGetValue(rc.Kind, out var svc) && svc.IsBlankTemplate(rc.Recipe);
            entries[i] = entries[i] with
            {
                Name     = blank ? rc.Kind.ToString() : $"{rc.Kind}: {rc.Recipe.Name}",
                Category = null,
            };
        }

        // ⭐ D5 — the C# row, shown and not creatable. ⛔ Not filtered by the search text: it is the
        //   honest answer to "is there another way to make one of these?", whatever was typed.
        entries.Add(new PickerEntry(
            Id:            $"notcreatable:{CSharpTechnology}:{product}",
            Name:          $"{CSharpTechnology} (hand-written, not created here)",
            Description:   $"A hand-written C# {product.ToString().ToLowerInvariant()} is authored in VS Code with the AI "
                           + "behaviour folder open. Creating one from the editor is a separate slice (CE-459).",
            Category:      null,
            Keywords:      null,
            IconTextureId: null,
            Tag:           new NotCreatableChoice(CSharpTechnology,
                               "Hand-written C# is authored in VS Code; editor creation is CE-459."),
            IconKey:       null));

        return entries.AsReadOnly();
    }

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
