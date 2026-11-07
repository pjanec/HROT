using System;
using System.Collections.Generic;
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
    private readonly Func<string, string?>? _terrainDefinition;   // ⭐ B-9 N1 pane — terrain name → its terrain.json (the catalog that lists it): the pane's facts and still come from there

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
        Func<string, string?>? describeTerrain = null,
        Func<string, string?>? terrainDefinition = null)
    {
        _terrainDefinition = terrainDefinition;
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
            // ⭐ B-9 N1 pane — the same wide side pane as the Open Scenario picker (D25); a terrain row draws its facts under the default pane (still, name)
            ShowSidePane = blanks,
            DrawPaneAfterDefault = blanks,
            DrawPane = blanks ? DrawTerrainFacts : null,
            DefaultSize = blanks ? new System.Numerics.Vector2(AssetPickerLauncher.ScenarioPickerWidth, AssetPickerLauncher.ScenarioPickerHeight) : null,
            SidePaneWidth = blanks ? AssetPickerLauncher.ScenarioPaneWidth : 280f,
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

    /// <summary>Where the pane's value column starts (px from the pane's left) — the mockup's label / value alignment.</summary>
    public const float PaneValueX = 124f;

    private sealed record PaneText(object Source, string? Origin, string? Roads, string Recipes);
    private readonly Dictionary<string, PaneText> _paneTexts = new(StringComparer.Ordinal);

    /// <summary>The pane lines of terrain <paramref name="terrain"/>, built once per change of its facts (never per frame).</summary>
    private PaneText? TextOf(string terrain)
    {
        if (_terrainDefinition?.Invoke(terrain) is not { } def || ScenarioPaneFacts.GetTerrain(def) is not { } f) return null;
        if (_paneTexts.TryGetValue(terrain, out var hit) && ReferenceEquals(hit.Source, f)) return hit;
        string? origin = f.Lat is { } lat && f.Lon is { } lon
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Abs(lat):0.000}° {(lat < 0 ? 'S' : 'N')}, {Math.Abs(lon):0.000}° {(lon < 0 ? 'W' : 'E')}") : null;
        string? roads = f.RoadNetworks.Count > 0 ? string.Join(", ", f.RoadNetworks) + " · " + f.RoadNetworks.Count + (f.RoadNetworks.Count == 1 ? " network" : " networks") : null;
        var on = ScenarioPaneFacts.RecipesOn(terrain);
        var text = new PaneText(f, origin, roads, on.Count == 0 ? "none" : string.Join(", ", on));
        _paneTexts[terrain] = text;
        return text;
    }

    private readonly List<float> _lastValueOffsets = new();

    /// <summary>A probe for the frame rail (R-4138): for each label / value row the last terrain pane drew, how far the value's left edge is from its label's (px). The mockup's is <see cref="PaneValueX"/> for every row.</summary>
    public IReadOnlyList<float> LastPaneValueOffsets => _lastValueOffsets;

    /// <summary>⭐ B-9 N1 pane (docs/UX/new-scenario-visual.svg) — a terrain row's facts under the default pane: Origin · Roads · Recipes on it as label / value rows, then the default text. A recipe row draws nothing extra.</summary>
    private void DrawTerrainFacts(PickerEntry entry, PickerPaneContext pane)
    {
        if (entry.Tag is not BlankTerrainChoice choice) return;
        _lastValueOffsets.Clear();
        void Row(string label, string value)
        {
            ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Text, pane.Theme.TextMuted); ImGuiNET.ImGui.TextUnformatted(label); ImGuiNET.ImGui.PopStyleColor();
            float labelX = ImGuiNET.ImGui.GetItemRectMin().X;
            ImGuiNET.ImGui.SameLine(ImGuiNET.ImGui.GetCursorStartPos().X + PaneValueX);
            ImGuiNET.ImGui.PushTextWrapPos(0f); ImGuiNET.ImGui.TextUnformatted(PickerText.Drawable(value)); ImGuiNET.ImGui.PopTextWrapPos();
            _lastValueOffsets.Add(ImGuiNET.ImGui.GetItemRectMin().X - labelX);
        }
        var t = TextOf(choice.Terrain);
        if (t != null)
        {
            if (t.Origin != null) Row("Origin", t.Origin);
            if (t.Roads != null) Row("Roads", t.Roads);
            Row("Recipes on it", t.Recipes);
        }
        if (string.IsNullOrEmpty(entry.Description))
        {
            ImGuiNET.ImGui.Spacing();
            ImGuiNET.ImGui.PushTextWrapPos(0f); ImGuiNET.ImGui.TextUnformatted(PickerText.Drawable(BlankTerrainText)); ImGuiNET.ImGui.PopTextWrapPos();
        }
    }

    /// <summary>⭐ N1 — the New Scenario picker's TWO groups in ONE tree (Q2): every terrain (live), then the recipes.</summary>
    public IReadOnlyList<PickerEntry> ScenarioEntries(RecipePickerSource recipes)
    {
        var list = new List<PickerEntry>();
        foreach (var t in _terrains?.Invoke() ?? Array.Empty<string>())
            // ⭐ B-9 N1 pane — the terrain's still (preview.jpg beside its terrain.json) is the row's ImageKey, as a scenario row's is; the default text is drawn UNDER the facts (DrawTerrainFacts)
            list.Add(new PickerEntry(Id: "terrain:" + t, Name: t, Description: _describeTerrain?.Invoke(t), Category: BlankGroup,
                Keywords: null, IconTextureId: null, Tag: new BlankTerrainChoice(t), IconKey: null,
                ImageKey: _terrainDefinition?.Invoke(t) is { } def ? ScenarioPaneFacts.TerrainPreviewOf(def) : null));
        foreach (var e in recipes.BuildEntries("", null))
            list.Add(e with { Category = RecipeGroup });
        return list;
    }
}
