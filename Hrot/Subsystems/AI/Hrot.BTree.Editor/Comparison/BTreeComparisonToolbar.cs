using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.BTree.Editor.Comparison;

/// <summary>
/// Hosts a <see cref="ComparisonToolbarAction"/> for the BTree editor canvas.
/// Instantiate once per host window and call <see cref="DrawToolbar"/> each frame.
/// </summary>
public sealed class BTreeComparisonToolbar
{
    private readonly ComparisonToolbarAction _toolbarAction;

    public BTreeComparisonToolbar(
        SanitizerRegistry sanitizerRegistry,
        ComparisonExportBuilder exportBuilder,
        ComparisonSessionRegistry sessionRegistry)
    {
        _toolbarAction = new ComparisonToolbarAction(sanitizerRegistry, exportBuilder, sessionRegistry);
    }

    /// <summary>
    /// Renders the comparison toolbar buttons and all associated modals for a BTree asset.
    /// Call from the host window's ImGui draw loop every frame.
    /// </summary>
    /// <param name="asset">The currently active BTree asset, or null when none is open.</param>
    public void DrawToolbar(IEditableAsset? asset)
    {
        if (asset == null)
            return;

        _toolbarAction.Render(asset.AssetId, asset.SourceFilePath, AssetKind.BTree);
    }
}
