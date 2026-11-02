using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Encapsulates "build an <see cref="AssetPickerSource"/> for the given kinds →
/// build a Tree <see cref="PickerRequest"/> from <see cref="AssetPickerSource.BuildEntries"/>
/// → open it → route the picked asset". The <c>openPicker</c> is an injected delegate
/// seam so the launcher is unit-testable without ImGui or a live registry.
/// </summary>
/// <remarks>
/// <para>
/// In production, wire <c>openPicker</c> to <see cref="PickerRegistry.OpenPicker"/>.
/// The pick result routes through <see cref="AssetPickActionRouter.Route"/> by default,
/// or through the optional <c>onPicked</c> callback supplied to <see cref="Open"/>.
/// </para>
/// </remarks>
public sealed class AssetPickerLauncher
{
    private readonly Action<PickerRequest, Action<PickerResult>> _openPicker;
    private readonly IAssetCatalog _catalog;
    private readonly AssetPickActionRouter _router;
    private readonly Func<AssetKind, string?>? _baseFolderResolver;
    private readonly Func<IEditableAsset, string?> _describe;
    private readonly Func<bool>? _hasInventory;
    private readonly Func<Hrot.UI.Common.RunMode.RunModeChoice?>? _runChoice;
    private readonly Func<IReadOnlyList<string>>? _macroPaths;
    private readonly Func<string?>? _macrosRoot;

    /// <summary>⭐ CE-4141 (AQ108 D25) — the id of the scenario picker's default footer action (<i>Open</i>).</summary>
    public const string OpenActionId = "open";

    /// <summary>⭐ CE-4141 (AQ108 D25) — the id of <i>Open and go live</i>.</summary>
    public const string GoLiveActionId = "go-live";

    /// <summary>⭐ CE-4141 — the scenario picker's window and pane sizes (px): wide, so the preview picture can be read. A pane width the user dragged is remembered and wins.</summary>
    public const float ScenarioPickerWidth = 1000f, ScenarioPickerHeight = 640f, ScenarioPaneWidth = 560f;

    /// <summary>The muted line of a host with no local scenarios folder (a remote host): the pane has no file to read.</summary>
    public const string NoPreviewNotice = "preview and description not available on this host";

    /// <summary>The id of the waiting row (<see cref="InventoryWaiting"/>) shown while the cluster's scenario list has not arrived.</summary>
    public const string WaitingEntryId = "scenario.waiting-for-inventory";

    /// <summary>
    /// Initializes the launcher.
    /// </summary>
    /// <param name="openPicker">
    /// Seam that opens a picker window from a <see cref="PickerRequest"/>.
    /// Production wires to <see cref="PickerRegistry.OpenPicker"/>; tests inject a fake.
    /// </param>
    /// <param name="catalog">The asset catalog (never <see langword="null"/>).</param>
    /// <param name="router">The pick-action router (never <see langword="null"/>).</param>
    /// <param name="baseFolderResolver">
    /// Optional resolver for base folders. Defaults to <see cref="AssetBrowserPanel.BaseFolderFor"/>.
    /// </param>
    /// <param name="describe">
    /// Optional function returning a description for an asset. When <see langword="null"/>,
    /// descriptions are omitted.
    /// </param>
    /// <param name="hasInventory">⭐ CE-1442 (§11.7 ⑤) — the cluster's lists have arrived (<c>RunGate.HasInventory</c>, through the host's run choice). Until then a picker that lists
    /// SCENARIOS (the cluster's list) leads with the waiting row instead of looking empty; the other kinds (files on disk) list as before. Null ⇒ always arrived (a rail). ⭐ A host with a run choice must pass it.</param>
    /// <param name="runChoice">⭐ CE-4141 (AQ108 D25) — the host's run choice (read at use: it is built after the picker). It makes <i>Open and go live</i> run (<see cref="Hrot.UI.Common.RunMode.RunModeChoice.OpenAndGoLive"/>: the door asks D4's
    /// questions, then loads for live); without it the button is disabled with its reason. ⭐ A host that HAS a run choice must pass it (the forwarding rail checks the Editor, CGF and the Orchestrator).</param>
    public AssetPickerLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        IAssetCatalog catalog,
        AssetPickActionRouter router,
        Func<AssetKind, string?>? baseFolderResolver = null,
        Func<IEditableAsset, string?>? describe = null,
        Func<bool>? hasInventory = null,
        Func<Hrot.UI.Common.RunMode.RunModeChoice?>? runChoice = null,
        Func<IReadOnlyList<string>>? macroPaths = null,
        Func<string?>? macrosRoot = null)
    {
        // ⭐ CE-4210 MA-6 — a host that EDITS macros passes the listing of the NAS `macros/` root (the root the load resolves against); null ⇒ no Macros group (macro authoring is the editor's).
        _macroPaths = macroPaths;
        _macrosRoot = macrosRoot;
        _hasInventory = hasInventory;
        _runChoice = runChoice;
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _baseFolderResolver = baseFolderResolver; // null → AssetPickerSource defaults to AssetBrowserPanel.BaseFolderFor
        _describe = describe ?? (_ => null);
    }

    /// <summary>
    /// Open the Tree picker for the given <paramref name="kinds"/>.
    /// When the user confirms, the picked asset is routed:
    /// <paramref name="onPicked"/> if supplied, else <see cref="AssetPickActionRouter.Route"/>.
    /// Cancel → nothing.
    /// </summary>
    /// <param name="kinds">Bitmask filter controlling which asset kinds appear.</param>
    /// <param name="onPicked">
    /// Optional callback for the selected asset. When <see langword="null"/>,
    /// <see cref="AssetPickActionRouter.Route"/> is used.
    /// </param>
    /// <summary>The rows a picker of <paramref name="kinds"/> shows: the catalog's, led by the waiting row while the cluster's scenario list has not arrived.</summary>
    public IReadOnlyList<PickerEntry> Entries(AssetKindFilter kinds)
        => Entries(kinds, new AssetPickerSource(_catalog, kinds, _baseFolderResolver, _describe));

    private IReadOnlyList<PickerEntry> Entries(AssetKindFilter kinds, AssetPickerSource source)
    {
        var entries = source.BuildEntries("", null);
        if ((kinds & AssetKindFilter.Scenario) != 0)
            entries = entries.Select(e => e.Tag is IEditableAsset { Kind: AssetKind.Scenario } a && ScenarioPaneFacts.PreviewOf(a.SourceFilePath) is { } png
                ? e with { ImageKey = png } : e).ToList();   // ⭐ CE-4141 — the pane's picture: preview.png in the scenario's folder, when there is one
        if ((kinds & AssetKindFilter.Scenario) != 0 && _macroPaths is not null)
        {
            var withMacros = entries.ToList();
            withMacros.AddRange(MacroPickerEntries.Entries(_macroPaths(), _macrosRoot?.Invoke()));
            entries = withMacros;
        }
        if ((kinds & AssetKindFilter.Scenario) == 0 || _hasInventory is null || _hasInventory()) return entries;
        var withWaiting = new List<PickerEntry>(entries.Count + 1) { InventoryWaiting.Entry(WaitingEntryId) };
        withWaiting.AddRange(entries);
        return withWaiting;
    }

    public void Open(AssetKindFilter kinds, Action<IEditableAsset?>? onPicked = null)
    {
        var source = new AssetPickerSource(_catalog, kinds, _baseFolderResolver, _describe);

        var request = new PickerRequest
        {
            ContextKey = $"assets.open.{kinds}",
            Title = "Open Asset",
            Layout = PickerLayout.Tree,
            SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = () => Entries(kinds, source),
        };
        if ((kinds & AssetKindFilter.Scenario) != 0) request = ScenarioPane(request, kinds, source);

        _openPicker(request, result =>
        {
            if (result.Cancelled)
                return;

            if (result.First?.Tag is IEditableAsset asset)
            {
                // ⭐ CE-4141 — Open and go live: the run choice's door asks D4's questions, then loads for live
                if (result.ActionId == GoLiveActionId)
                {
                    if (asset.Kind == AssetKind.Scenario) _runChoice?.Invoke()?.OpenAndGoLive(asset.Name);
                    return;
                }
                if (onPicked != null)
                    onPicked(asset);
                else
                    _router.Route(asset);
            }
        });
    }

    /// <summary>
    /// ⭐ CE-4141 (AQ108 D25) — the scenario picker is the first SOURCE of the generic picker's side pane (CE-4168): the pane shows the entry's <c>preview.png</c>, name and description, then facts read from
    /// its <c>scenario.json</c> (terrain, subsystem, entity count — only what the file has); a host with no local scenarios folder says so. The footer offers <i>Open</i> (default) and <i>Open and go live</i>.
    /// ⏳ The run-setup expanders of D24 (variant, scenario params) are NOT here: scenario variants and params are macros S3 (backend's) and do not exist yet.
    /// </summary>
    /// <summary>
    /// ⭐ CE-4210 MA-8 — the SAME picker's Macros group on its own (the instance's <i>Change…</i>): lists the macro files and calls back with the chosen macro's PATH under <c>macros/</c>.
    /// ⛔ Nothing when this host passed no macro listing.
    /// </summary>
    public bool OpenMacros(Action<string> onPicked)
    {
        if (_macroPaths is null || onPicked is null) return false;
        _openPicker(new PickerRequest
        {
            ContextKey = "assets.open.macros",
            Title = "Pick a macro",
            Layout = PickerLayout.Tree,
            SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = () => MacroPickerEntries.Entries(_macroPaths(), _macrosRoot?.Invoke()),
        }, result =>
        {
            if (!result.Cancelled && result.First?.Tag is MacroPickerEntries.MacroAsset a) onPicked(a.Path_);
        });
        return true;
    }

    private PickerRequest ScenarioPane(PickerRequest basis, AssetKindFilter kinds, AssetPickerSource source) => new()
    {
        ContextKey = basis.ContextKey,
        Title = basis.Title,
        Layout = basis.Layout,
        SelectionMode = basis.SelectionMode,
        ItemsProvider = basis.ItemsProvider,
        ShowSidePane = true,
        DefaultSize = new System.Numerics.Vector2(ScenarioPickerWidth, ScenarioPickerHeight),   // ⭐ D25 "the wide picker": big enough to read the preview
        SidePaneWidth = ScenarioPaneWidth,
        DrawPaneAfterDefault = true,
        // ⏳ AQ108 D24 — the run setup (variant, scenario params; collapsed expanders) goes HERE once scenario variants / params exist (macros S3, backend's).
        DrawPane = (entry, pane) => DrawScenarioFacts(entry, pane),
        Actions = new[]
        {
            new PickerAction(OpenActionId, "Open", IsDefault: true),
            new PickerAction(GoLiveActionId, "Open and go live", DisabledReason: GoLiveDisabledReason),
        },
    };

    /// <summary>Why <i>Open and go live</i> cannot run for <paramref name="entry"/> now, or <see langword="null"/>.</summary>
    public string? GoLiveDisabledReason(PickerEntry? entry)
    {
        if (entry?.Tag is not IEditableAsset { Kind: AssetKind.Scenario }) return "Pick a scenario first";
        var choice = _runChoice?.Invoke();
        return choice is null ? "This host has no run controls — Live cannot be started from here" : choice.OpenAndGoLiveDisabledReason;
    }

    private static void DrawScenarioFacts(PickerEntry entry, NodeEditor.UI.Picker.PickerPaneContext pane)
    {
        if (entry.Tag is not IEditableAsset { Kind: AssetKind.Scenario } asset) return;
        void Muted(string text) { ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Text, pane.Theme.TextMuted); ImGuiNET.ImGui.TextUnformatted(text); ImGuiNET.ImGui.PopStyleColor(); }
        if (string.IsNullOrEmpty(asset.SourceFilePath)) { Muted(NoPreviewNotice); return; }   // a remote host: the name alone
        if (ScenarioPaneFacts.Get(asset.SourceFilePath) is not { } f) return;                   // absent ⇒ nothing, no placeholder
        ImGuiNET.ImGui.Separator();
        if (string.IsNullOrEmpty(entry.Description) && f.Description is { } d)
        {
            ImGuiNET.ImGui.PushTextWrapPos(0f); ImGuiNET.ImGui.TextUnformatted(NodeEditor.UI.Picker.PickerText.Drawable(d)); ImGuiNET.ImGui.PopTextWrapPos();
        }
        if (f.TerrainName is { } terrain) Muted("Terrain: " + NodeEditor.UI.Picker.PickerText.Drawable(terrain));
        if (f.SubsystemType is { } sub) Muted("Subsystem: " + sub);
        if (f.EntityCount > 0) Muted("Entities: " + f.EntityCount);
    }
}
