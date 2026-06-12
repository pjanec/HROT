using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Projects the <see cref="IAssetCatalog"/> into <see cref="PickerEntry"/> values
/// consumable by NodeEdit's Tree-layout picker (§5.2 Asset Picker — Editor Integration).
/// </summary>
/// <remarks>
/// <para>
/// <b>DEC-15:</b> This source exposes its asset→entry projection publicly
/// (<see cref="ToEntry"/>, <see cref="BuildEntries"/>) so that T3 can feed it
/// through the entry-driven <c>IPickerRegistry.OpenPicker(PickerRequest{…})</c>
/// path — the source-driven <c>registry.Open</c> path would discard
/// <see cref="PickerEntry.Category"/> and <see cref="PickerEntry.IconKey"/>.
/// </para>
/// <para>
/// <b>Headless-deterministic:</b> constructor accepts injectable seams
/// (<paramref name="baseFolderResolver"/>, <paramref name="describe"/>) so
/// logic exercised by tests never touches the real filesystem.
/// </para>
/// </remarks>
public sealed class AssetPickerSource : IPickerSource<IEditableAsset>
{
    private readonly IAssetCatalog _catalog;
    private readonly AssetKindFilter _kinds;
    private readonly Func<AssetKind, string?> _baseFolderResolver;
    private readonly Func<IEditableAsset, string?> _describe;
    private readonly IReadOnlyList<AssetKind> _permittedKinds;
    private readonly bool _isSingleKind;

    /// <summary>
    /// Creates an <see cref="AssetPickerSource"/> that projects assets from
    /// <paramref name="catalog"/> into <see cref="PickerEntry"/> values.
    /// </summary>
    /// <param name="catalog">The asset catalog (never <see langword="null"/>).</param>
    /// <param name="kinds">Bitmask filter controlling which <see cref="AssetKind"/>
    /// values appear. Defaults to <see cref="AssetKindFilter.All"/>.</param>
    /// <param name="baseFolderResolver">
    /// Returns the base folder for a given <see cref="AssetKind"/> (used for
    /// relative-path computation). Defaults to
    /// <see cref="AssetBrowserPanel.BaseFolderFor"/>. Inject a deterministic
    /// function for headless tests.
    /// </param>
    /// <param name="describe">
    /// Optional function that returns a long description for an asset (e.g. recipe
    /// metadata). When <see langword="null"/>, all descriptions are <see langword="null"/>.
    /// </param>
    public AssetPickerSource(
        IAssetCatalog catalog,
        AssetKindFilter kinds = AssetKindFilter.All,
        Func<AssetKind, string?>? baseFolderResolver = null,
        Func<IEditableAsset, string?>? describe = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _kinds = kinds;
        _baseFolderResolver = baseFolderResolver ?? AssetBrowserPanel.BaseFolderFor;
        _describe = describe ?? (_ => null);
        _permittedKinds = AssetKindFilterMapping.PermittedKinds(kinds);
        _isSingleKind = _permittedKinds.Count == 1;
    }

    // ── IPickerSource<IEditableAsset> properties ───────────────────────

    /// <inheritdoc/>
    public string Title => "Open Asset";

    /// <inheritdoc/>
    public string EmptyResultText => "No assets found.";

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
    public IReadOnlyList<IEditableAsset> Query(
        string text,
        IReadOnlyDictionary<string, object?>? context)
    {
        var assets = _catalog.All.Where(a => _permittedKinds.Contains(a.Kind));

        if (!string.IsNullOrEmpty(text))
            assets = assets.Where(a =>
                a.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

        return assets.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IEditableAsset>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct)
        => Task.FromResult(Query(text, context));

    // ── Projection (public seam for T3) ────────────────────────────────

    /// <summary>
    /// Projects a single <see cref="IEditableAsset"/> into a
    /// <see cref="PickerEntry"/> with kind-grouped <see cref="PickerEntry.Category"/>,
    /// per-kind <see cref="PickerEntry.IconKey"/>, and <see cref="PickerEntry.Tag"/>
    /// set to the asset itself.
    /// </summary>
    /// <param name="asset">The asset to project (never <see langword="null"/>).</param>
    /// <returns>
    /// A <see cref="PickerEntry"/> whose <see cref="PickerEntry.Tag"/> is
    /// <paramref name="asset"/> — the T3 router consumes this to open the
    /// correct editor.
    /// </returns>
    public PickerEntry ToEntry(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        var baseFolder = _baseFolderResolver(asset.Kind);
        var relPath = AssetRelPath.RelPath(asset, baseFolder);

        // Extract the subfolder (directory part) from the relative path.
        string? subfolder = null;
        int lastSlash = relPath.LastIndexOf('/');
        if (lastSlash >= 0)
            subfolder = relPath.Substring(0, lastSlash);

        string? category;
        if (_isSingleKind)
        {
            // Single-kind: no kind prefix; null when at root level.
            category = subfolder is { Length: > 0 } ? subfolder : null;
        }
        else
        {
            // All / multi-kind: always include the kind label as the top-level category.
            var kindLabel = asset.Kind.ToString(); // e.g. "Blueprint", "Hsm"
            category = subfolder is { Length: > 0 }
                ? $"{kindLabel}/{subfolder}"
                : kindLabel;
        }

        return new PickerEntry(
            Id: GetItemKey(asset),
            Name: asset.Name,
            Description: _describe(asset),
            Category: category,
            Keywords: null,
            IconTextureId: null,
            Tag: asset,
            IconKey: AssetKindIcons.GetIconKey(asset.Kind));
    }

    /// <summary>
    /// Convenience method that queries the catalog and projects every matching
    /// asset through <see cref="ToEntry"/>. Used by T3 as
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
    public string GetItemKey(IEditableAsset item) => item.AssetId.ToString();

    /// <inheritdoc/>
    public string GetSearchableText(IEditableAsset item) => item.Name;

    // ── Rendering (minimal — Tree layout uses PickerEntry directly) ────

    /// <inheritdoc/>
    public void RenderItem(IEditableAsset item, bool selected, bool keyboardFocused, IPickerRenderContext ctx)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
            ImGuiNET.ImGui.TextUnformatted(item.Name);
    }

    /// <inheritdoc/>
    public void RenderPreview(IEditableAsset item, IPickerRenderContext ctx)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
        {
            var desc = _describe(item);
            if (desc != null)
                ImGuiNET.ImGui.TextUnformatted(desc);
        }
    }

    /// <inheritdoc/>
    public bool IsPreviewExpensive(IEditableAsset item) => false;

    /// <inheritdoc/>
    public bool CanAcceptDrop(object payload) => false;
}
