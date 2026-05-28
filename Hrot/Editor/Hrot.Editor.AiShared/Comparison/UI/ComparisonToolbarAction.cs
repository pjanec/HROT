using System.Numerics;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Comparison.UI;

/// <summary>
/// Coordinator class that owns the "Compare with..." toolbar button, the asset selection
/// dialog, the export delivery modal, and the paste response modal for a single editor window.
/// Instantiate one per editor window; inject the singleton services via constructor.
/// See design §7.1.
/// </summary>
public sealed class ComparisonToolbarAction
{
    private static readonly Vector4 OrangeColor = new(1.0f, 0.55f, 0.1f, 1.0f);

    private readonly SanitizerRegistry _sanitizerRegistry;
    private readonly ComparisonExportBuilder _exportBuilder;
    private readonly ComparisonSessionRegistry _sessionRegistry;
    private readonly ExitComparisonAction _exitAction;

    private readonly AssetSelectionDialog _selectionDialog = new();
    private readonly ExportDeliveryModal _deliveryModal = new();
    private readonly PasteResponseModal _pasteModal = new();

    private AssetKind _pendingKind;
    private string _pendingAssetPath = "";

    public ComparisonToolbarAction(
        SanitizerRegistry sanitizerRegistry,
        ComparisonExportBuilder exportBuilder,
        ComparisonSessionRegistry sessionRegistry)
    {
        _sanitizerRegistry = sanitizerRegistry ?? throw new ArgumentNullException(nameof(sanitizerRegistry));
        _exportBuilder = exportBuilder ?? throw new ArgumentNullException(nameof(exportBuilder));
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _exitAction = new ExitComparisonAction(sessionRegistry);
    }

    /// <summary>
    /// Renders the "Compare with..." button plus all associated modals.
    /// Call this from each editor's toolbar rendering method every ImGui frame.
    /// </summary>
    /// <param name="activeAssetId">The stable identifier of the asset currently open in the editor.</param>
    /// <param name="activeAssetPath">The absolute path to the asset's main source file.</param>
    /// <param name="kind">The asset kind (BTree, HSM, Blueprint, or Blackboard).</param>
    public void Render(Guid activeAssetId, string activeAssetPath, AssetKind kind)
    {
        if (ImGui.Button("Compare with..."))
        {
            _pendingKind = kind;
            _pendingAssetPath = activeAssetPath;
            _selectionDialog.RequestOpen(activeAssetPath);
        }

        ImGui.SameLine();

        if (ImGui.Button("Paste LLM Response..."))
            _pasteModal.RequestOpen();

        // Show "Exit Comparison" button and stale chip when a session is active.
        var session = _sessionRegistry.GetSession(activeAssetId);
        if (session != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Exit Comparison"))
                _exitAction.Exit(activeAssetId);

            if (session.IsStale)
            {
                ImGui.SameLine();
                ImGui.TextColored(OrangeColor, "[STALE]");
            }
        }

        // Process the selection dialog every frame.
        var selectionResult = _selectionDialog.Render(kind);
        if (selectionResult != null)
        {
            // Run the export pipeline.
            if (_sanitizerRegistry.TryGet(_pendingKind, out var sanitizer) && sanitizer != null)
            {
                var exportText = _exportBuilder.Build(sanitizer, selectionResult.VersionA, selectionResult.VersionB);
                var assetName = Path.GetFileNameWithoutExtension(_pendingAssetPath);
                _deliveryModal.Open(exportText, assetName);
            }
        }

        // Render the export delivery modal every frame.
        _deliveryModal.Render();

        // Render the paste modal every frame.
        _pasteModal.Render(activeAssetId, _sessionRegistry);
    }
}
