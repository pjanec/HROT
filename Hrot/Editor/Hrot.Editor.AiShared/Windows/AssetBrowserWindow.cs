using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

// ── Open-docs view-model (testable, no ImGui) ─────────────────────────────────

/// <summary>
/// Represents one row in the "Open" section of the Asset Browser.
/// </summary>
/// <param name="Document">The backing document.</param>
/// <param name="DisplayName">Asset display name.</param>
/// <param name="KindTag">Short kind label (e.g. "BTree", "Hsm", "Blueprint").</param>
/// <param name="IsDirty">Whether the document has unsaved changes.</param>
/// <param name="IsActive">Whether this document is the currently active one.</param>
public sealed record OpenDocRow(
    AiDocument Document,
    string DisplayName,
    string KindTag,
    bool IsDirty,
    bool IsActive);

/// <summary>
/// View-model for the "Open" section of the Asset Browser.
/// Built by <see cref="AssetBrowserWindow.BuildOpenDocsViewModel"/> — pure, no ImGui.
/// </summary>
/// <param name="Rows">The open document rows (in open order).</param>
public sealed record OpenDocsViewModel(IReadOnlyList<OpenDocRow> Rows);

// ── Window ────────────────────────────────────────────────────────────────────

/// <summary>
/// Global asset browser window.
/// <para>
/// The top section lists all currently open documents across all asset kinds,
/// with markers for the active document (●) and unsaved changes (*).
/// Clicking a row activates the document (switches to its perspective).
/// Clicking [×] closes it.
/// </para>
/// <para>
/// The lower section lists the full asset catalog. Double-clicking an entry
/// calls <see cref="AiDocumentManager.Open"/> (or focuses it if already open).
/// </para>
/// <para>
/// Interaction logic is exposed through <see cref="BuildOpenDocsViewModel"/>,
/// <see cref="HandleActivateRow"/>, and <see cref="HandleCloseRow"/> so that
/// unit tests can exercise the logic without an ImGui context.
/// </para>
/// </summary>
public sealed class AssetBrowserWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IAssetCatalog _catalog;
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;
    private readonly ILiveSessionProvider _liveProvider;
    private readonly AiDocumentManager? _documentManager;

    private IEditableAsset? _pendingRenameAsset;
    private readonly byte[] _browserRenameBuf = new byte[512];
    private bool _openBrowserRenameModal;

    public Action? CustomToolbarDraw { get; set; }

    /// <summary>
    /// Creates the global Asset Browser.
    /// </summary>
    /// <param name="store">Editor selection store.</param>
    /// <param name="catalog">The shared asset catalog.</param>
    /// <param name="refactorService">Refactoring service for rename/delete previews.</param>
    /// <param name="findResults">Window to display find/refactor results.</param>
    /// <param name="liveProvider">Live-session provider (entity count per asset).</param>
    /// <param name="documentManager">
    ///   Optional document manager. When provided, renders the "Open" section
    ///   at the top of the browser and delegates Open/Activate/Close calls to it.
    /// </param>
    public AssetBrowserWindow(
        EditorSelectionStore store,
        IAssetCatalog catalog,
        IRefactorService refactorService,
        FindResultsWindow findResults,
        ILiveSessionProvider liveProvider,
        AiDocumentManager? documentManager = null)
        : base("ai_asset_browser", "Asset Browser", "Authoring", WindowScope.Global)
    {
        _store = store;
        _catalog = catalog;
        _refactorService = refactorService;
        _findResults = findResults;
        _liveProvider = liveProvider;
        _documentManager = documentManager;
    }

    // ── Testable interaction helpers ──────────────────────────────────────────

    /// <summary>
    /// Builds the view-model for the "Open" section.
    /// Pure — no ImGui calls; safe to invoke from unit tests.
    /// Returns an empty model when <paramref name="mgr"/> is <c>null</c>.
    /// </summary>
    public static OpenDocsViewModel BuildOpenDocsViewModel(AiDocumentManager? mgr)
    {
        if (mgr is null)
            return new OpenDocsViewModel(Array.Empty<OpenDocRow>());

        var rows = mgr.OpenDocuments
            .Select(doc => new OpenDocRow(
                Document:    doc,
                DisplayName: doc.Asset.Name,
                KindTag:     doc.Kind.ToString(),
                IsDirty:     doc.IsDirty,
                IsActive:    ReferenceEquals(doc, mgr.Active)))
            .ToList();

        return new OpenDocsViewModel(rows);
    }

    /// <summary>
    /// Activates a row in the "Open" section (click behaviour).
    /// Delegates to <see cref="AiDocumentManager.Activate"/>.
    /// No-op if <paramref name="mgr"/> is null.
    /// </summary>
    public static void HandleActivateRow(AiDocumentManager? mgr, AiDocument doc)
        => mgr?.Activate(doc);

    /// <summary>
    /// Closes a row in the "Open" section ([×] button behaviour).
    /// Delegates to <see cref="AiDocumentManager.Close"/>.
    /// No-op if <paramref name="mgr"/> is null.
    /// </summary>
    public static void HandleCloseRow(AiDocumentManager? mgr, AiDocument doc)
        => mgr?.Close(doc);

    /// <summary>
    /// Opens an asset from the catalog (double-click behaviour).
    /// Delegates to <see cref="AiDocumentManager.Open"/>.
    /// No-op if <paramref name="mgr"/> is null.
    /// </summary>
    public static void HandleCatalogOpen(AiDocumentManager? mgr, IEditableAsset asset)
        => mgr?.Open(asset);

    // ── Rendering ─────────────────────────────────────────────────────────────

    protected override void DrawClientArea()
    {
        CustomToolbarDraw?.Invoke();
        // ── Open section ──────────────────────────────────────────────────────
        if (_documentManager is not null)
        {
            var vm = BuildOpenDocsViewModel(_documentManager);
            if (vm.Rows.Count > 0)
            {
                ImGuiNET.ImGui.SeparatorText($"OPEN ({vm.Rows.Count})");
                foreach (var row in vm.Rows)
                {
                    var activeMarker = row.IsActive ? "● " : "  ";
                    var dirtyMarker  = row.IsDirty  ? " *" : "";
                    var label = $"{activeMarker}{row.DisplayName}  <{row.KindTag}>{dirtyMarker}##open_{row.Document.Asset.AssetId}";
                    if (ImGuiNET.ImGui.Selectable(label))
                        HandleActivateRow(_documentManager, row.Document);

                    ImGuiNET.ImGui.SameLine();
                    if (ImGuiNET.ImGui.SmallButton($"[×]##close_{row.Document.Asset.AssetId}"))
                        HandleCloseRow(_documentManager, row.Document);
                }
                ImGuiNET.ImGui.Separator();
            }
        }

        // ── Catalog section ───────────────────────────────────────────────────
        if (_catalog.All.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No assets loaded.");
            return;
        }

        foreach (var asset in _catalog.All)
        {
            var liveCount = _liveProvider.GetActiveEntityCount(asset.AssetId);
            var label = liveCount > 0
                ? $"{asset.Name}  [{liveCount} live]"
                : asset.Name;

            bool doubleClicked = false;
            ImGuiNET.ImGui.Selectable(label);
            if (ImGuiNET.ImGui.IsItemHovered() &&
                ImGuiNET.ImGui.IsMouseDoubleClicked(ImGuiNET.ImGuiMouseButton.Left))
            {
                doubleClicked = true;
            }

            if (doubleClicked)
                HandleCatalogOpen(_documentManager, asset);

            var popupId = $"##bctx_{asset.AssetId}";
            if (ImGuiNET.ImGui.BeginPopupContextItem(popupId))
            {
                if (ImGuiNET.ImGui.MenuItem("Find References"))
                {
                    var refs = _refactorService.FindReferences(asset.Name);
                    _findResults.ShowReferences(asset.Name, refs);
                }
                if (ImGuiNET.ImGui.MenuItem("Rename..."))
                {
                    _pendingRenameAsset = asset;
                    _openBrowserRenameModal = true;
                    Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
                }
                if (ImGuiNET.ImGui.MenuItem("Delete (preview)..."))
                {
                    var deletePreview = _refactorService.PreviewDelete(
                        asset.AssetId, new DeleteOptions());
                    _findResults.ShowReferences(
                        $"Delete preview: {asset.Name}",
                        deletePreview.DanglingReferences);
                }
                ImGuiNET.ImGui.EndPopup();
            }
        }

        if (_openBrowserRenameModal)
        {
            ImGuiNET.ImGui.OpenPopup("Rename##browser");
            _openBrowserRenameModal = false;
        }

        if (_pendingRenameAsset != null)
        {
            var renameOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal("Rename##browser", ref renameOpen,
                ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiNET.ImGui.Text($"Rename: {_pendingRenameAsset.Name}");
                ImGuiNET.ImGui.Text("New name:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.InputText("##rname_browser", _browserRenameBuf,
                    (uint)_browserRenameBuf.Length);
                if (ImGuiNET.ImGui.Button("OK"))
                {
                    var newKey = System.Text.Encoding.UTF8.GetString(_browserRenameBuf)
                        .TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(newKey))
                    {
                        var preview = _refactorService.PreviewRename(
                            _pendingRenameAsset.Name, newKey, new RefactorOptions());
                        _findResults.ShowRenamePreview(preview);
                    }
                    _pendingRenameAsset = null;
                    Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                {
                    _pendingRenameAsset = null;
                    Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.EndPopup();
            }
            if (!renameOpen)
            {
                _pendingRenameAsset = null;
                Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
            }
        }
    }
}
