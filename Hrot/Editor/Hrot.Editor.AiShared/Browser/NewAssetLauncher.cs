using System;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Encapsulates "build a <see cref="RecipePickerSource"/> from the per-kind
/// <see cref="INewAssetService"/> registry → build a Tree <see cref="PickerRequest"/>
/// from <see cref="RecipePickerSource.BuildEntries"/> → open it → route the picked
/// (kind, recipe) through <c>showNewAssetDialog</c>". The <c>openPicker</c> and
/// <c>showNewAssetDialog</c> are injected delegate seams so the launcher is
/// unit-testable without ImGui or a live registry.
/// </summary>
/// <remarks>
/// <para>
/// In production, wire <c>openPicker</c> to <see cref="PickerRegistry.OpenPicker"/>.
/// The pick result invokes <c>showNewAssetDialog(kind, recipe)</c>, which seeds and
/// confirms a <see cref="NewAssetDialog"/> to create+open the new asset.
/// </para>
/// <para>
/// <b>D-T7-1:</b> The interactive name/folder UI is deferred as DBT-A3.
/// Production seeds the dialog with a default name and confirms immediately.
/// </para>
/// </remarks>
public sealed class NewAssetLauncher
{
    private readonly Action<PickerRequest, Action<PickerResult>> _openPicker;
    private readonly IReadOnlyDictionary<AssetKind, INewAssetService> _services;
    private readonly Action<AssetKind, IEditableAsset> _showNewAssetDialog;
    private readonly Func<IEditableAsset, string?> _describe;
    private readonly Func<IEditableAsset, string?> _recipeCategory;
    private readonly Action<AssetKind, IEditableAsset>? _showNewMacroDialog;

    // ⭐ New scenario N1 (R-4135) — the terrains a BLANK scenario can start on (TerrainCatalog.List, live per open), the host's Save-As naming for one,
    //   and its pane text. All three null ⇒ the New Scenario picker offers recipes only (a host with no terrain catalog).
    private readonly Func<IReadOnlyList<string>>? _terrains;
    private readonly Action<string>? _showNewBlankDialog;
    private readonly Func<string, string?>? _describeTerrain;

    /// <summary>The group names of the New Scenario picker (§2.5's mockup).</summary>
    public const string BlankGroup = "Blank, on a terrain", RecipeGroup = "From a recipe";

    /// <summary>The default pane text of a terrain row.</summary>
    public const string BlankTerrainText = "A new, empty scenario on this terrain. The terrain is fixed from now on — a different terrain means a new scenario.";

    /// <summary>
    /// Initializes the launcher.
    /// </summary>
    /// <param name="openPicker">
    /// Seam that opens a picker window from a <see cref="PickerRequest"/>.
    /// Production wires to <see cref="PickerRegistry.OpenPicker"/>; tests inject a fake.
    /// </param>
    /// <param name="services">
    /// Registry of per-kind <see cref="INewAssetService"/> implementations (never <see langword="null"/>).
    /// </param>
    /// <param name="showNewAssetDialog">
    /// Called when a recipe is picked — receives the <see cref="AssetKind"/> and the
    /// chosen recipe <see cref="IEditableAsset"/>. Production seeds and confirms a
    /// <see cref="NewAssetDialog"/>.
    /// </param>
    /// <param name="describe">
    /// Optional function returning a description for a recipe. Passed through to
    /// <see cref="RecipePickerSource"/>. When <see langword="null"/>, descriptions are omitted.
    /// </param>
    /// <param name="recipeCategory">
    /// Optional function returning a sub-category for a recipe. Passed through to
    /// <see cref="RecipePickerSource"/>. When <see langword="null"/>, the category is just the kind label.
    /// </param>
    public NewAssetLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        Action<AssetKind, IEditableAsset> showNewAssetDialog,
        Func<IEditableAsset, string?>? describe = null,
        Func<IEditableAsset, string?>? recipeCategory = null,
        Action<AssetKind, IEditableAsset>? showNewMacroDialog = null,
        Func<IReadOnlyList<string>>? terrains = null,
        Action<string>? showNewBlankDialog = null,
        Func<string, string?>? describeTerrain = null)
    {
        _showNewMacroDialog = showNewMacroDialog;
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _showNewAssetDialog = showNewAssetDialog ?? throw new ArgumentNullException(nameof(showNewAssetDialog));
        _terrains = terrains;
        _showNewBlankDialog = showNewBlankDialog;
        _describeTerrain = describeTerrain;
        _describe = describe ?? (_ => null);
        _recipeCategory = recipeCategory ?? (_ => null);
    }

    /// <summary>
    /// Build a Tree-layout <see cref="PickerRequest"/> from
    /// <see cref="RecipePickerSource.BuildEntries"/> and open it.
    /// On pick → <c>showNewAssetDialog(kind, recipe)</c>.
    /// Cancel → nothing.
    /// </summary>
    public void Open() => OpenCore(product: null);

    /// <summary>
    /// ⭐ <c>CE-460</c> (E4) — File / New Behavior… · New Action… · New Condition…: the SAME tree, rooted at
    /// <paramref name="product"/> (technology is the first level), routed through the SAME
    /// <c>showNewAssetDialog</c> ⇒ ⛔ no second creation path. Picking the not-creatable C# row does nothing.
    /// </summary>
    public void Open(AuthoringProduct product) => OpenCore(product);

    /// <summary>
    /// ⭐ <c>CE-4140</c> (File › New Scenario…) — the SAME picker and the SAME <c>showNewAssetDialog</c> (Save-As naming, then the ONE create-core), over the recipes of ONE
    /// <paramref name="kind"/> only. ⛔ No second picker or dialog. A kind this launcher has no service for lists nothing.
    /// </summary>
    public void Open(AssetKind kind) => OpenCore(product: null, only: kind);

    /// <summary>⭐ CE-4210 MA-6 — <i>New macro…</i>: the SAME scenario-recipe picker (a macro starts from a seed's terrain), then the host's macro naming dialog (a name under <c>macros/</c>) and the same <c>CreateNew</c>.
    /// ⛔ A host with no such dialog (<c>showNewMacroDialog</c> null) opens nothing — the caller greys the item.</summary>
    public bool CanOpenMacro => _showNewMacroDialog is not null && _services.ContainsKey(AssetKind.Scenario);

    public void OpenMacro() => OpenCore(product: null, only: AssetKind.Scenario, macro: true);

    private void OpenCore(AuthoringProduct? product, AssetKind? only = null, bool macro = false)
    {
        var services = only is { } k
            ? _services.Where(s => s.Key == k).ToDictionary(s => s.Key, s => s.Value)
            : _services;
        var source = new RecipePickerSource(services, _describe, _recipeCategory, product);
        bool blanks = only == AssetKind.Scenario && !macro && _terrains != null && _showNewBlankDialog != null;   // ⭐ N1 — the two groups

        var request = new PickerRequest
        {
            ContextKey = macro ? "assets.new.macro" : only is { } ok ? $"assets.new.{ok.ToString().ToLowerInvariant()}"
                       : product is { } p ? $"assets.new.{p.ToString().ToLowerInvariant()}" : "assets.new",
            Title = macro ? "New Macro" : only is { } tk ? $"New {tk}" : source.Title,
            // ⭐ CE-460 (D2) — the product's default technology is pre-selected: Enter takes it.
            InitialSelectionId = source.DefaultEntryId(),
            Layout = PickerLayout.Tree,
            SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = blanks ? () => ScenarioEntries(source) : () => source.BuildEntries("", null),
        };

        _openPicker(request, result =>
        {
            if (result.Cancelled)
                return;

            if (result.First?.Tag is BlankTerrainChoice blank) { _showNewBlankDialog?.Invoke(blank.Terrain); return; }   // ⭐ N1

            if (result.First?.Tag is RecipeChoice rc)
            {
                if (macro) _showNewMacroDialog?.Invoke(rc.Kind, rc.Recipe);
                else _showNewAssetDialog(rc.Kind, rc.Recipe);
            }
        });
    }

    /// <summary>⭐ N1 — the New Scenario picker's TWO groups in ONE tree (Q2): every terrain (live), then the recipes.</summary>
    public IReadOnlyList<PickerEntry> ScenarioEntries(RecipePickerSource recipes)
    {
        var list = new List<PickerEntry>();
        foreach (var t in _terrains?.Invoke() ?? Array.Empty<string>())
            list.Add(new PickerEntry(Id: "terrain:" + t, Name: t, Description: _describeTerrain?.Invoke(t) ?? BlankTerrainText, Category: BlankGroup,
                Keywords: null, IconTextureId: null, Tag: new BlankTerrainChoice(t), IconKey: null));
        foreach (var e in recipes.BuildEntries("", null))
            list.Add(e with { Category = RecipeGroup });
        return list;
    }
}
