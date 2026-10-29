using System;
using System.Collections.Generic;
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
    public AssetPickerLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        IAssetCatalog catalog,
        AssetPickActionRouter router,
        Func<AssetKind, string?>? baseFolderResolver = null,
        Func<IEditableAsset, string?>? describe = null,
        Func<bool>? hasInventory = null)
    {
        _hasInventory = hasInventory;
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

        _openPicker(request, result =>
        {
            if (result.Cancelled)
                return;

            if (result.First?.Tag is IEditableAsset asset)
            {
                if (onPicked != null)
                    onPicked(asset);
                else
                    _router.Route(asset);
            }
        });
    }
}
