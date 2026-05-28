using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Comparison.UI;

namespace Hrot.Hsm.Editor.Comparison;

/// <summary>
/// Hosts a <see cref="ComparisonToolbarAction"/> for the HSM editor canvas.
/// Instantiate once per host window and call <see cref="DrawToolbar"/> each frame.
/// </summary>
public sealed class HsmComparisonToolbar
{
    private readonly ComparisonToolbarAction _toolbarAction;

    public HsmComparisonToolbar(
        SanitizerRegistry sanitizerRegistry,
        ComparisonExportBuilder exportBuilder,
        ComparisonSessionRegistry sessionRegistry)
    {
        _toolbarAction = new ComparisonToolbarAction(sanitizerRegistry, exportBuilder, sessionRegistry);
    }

    /// <summary>
    /// Renders the comparison toolbar buttons and all associated modals for an HSM asset.
    /// Call from the host window's ImGui draw loop every frame.
    /// </summary>
    /// <param name="asset">The currently active HSM asset, or null when none is open.</param>
    public void DrawToolbar(IEditableAsset? asset)
    {
        if (asset == null)
            return;

        _toolbarAction.Render(asset.AssetId, asset.SourceFilePath, AssetKind.Hsm);
    }
}
