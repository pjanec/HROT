using System;
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
    public AssetPickerLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        IAssetCatalog catalog,
        AssetPickActionRouter router,
        Func<AssetKind, string?>? baseFolderResolver = null,
        Func<IEditableAsset, string?>? describe = null)
    {
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
    public void Open(AssetKindFilter kinds, Action<IEditableAsset?>? onPicked = null)
    {
        var source = new AssetPickerSource(_catalog, kinds, _baseFolderResolver, _describe);

        var request = new PickerRequest
        {
            ContextKey = $"assets.open.{kinds}",
            Title = "Open Asset",
            Layout = PickerLayout.Tree,
            SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = () => source.BuildEntries("", null),
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
