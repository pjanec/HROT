using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Panels.Views;

/// <summary>
/// Built-in Details view shown when multiple nodes are selected.
/// Displays the intersection of shared properties across the selection.
/// </summary>
internal sealed class MultipleNodesDetailsView : IDetailsView
{
    private readonly IReadOnlyList<NodeId> _ids;

    /// <summary>Construct for the given multi-selection.</summary>
    public MultipleNodesDetailsView(IReadOnlyList<NodeId> ids) => _ids = ids;

    /// <inheritdoc/>
    public bool IsDirty => false;

    /// <inheritdoc/>
    public void Commit() { }

    /// <inheritdoc/>
    public void Revert() { }

    /// <inheritdoc/>
    public void Draw(IDetailsRenderContext ctx)
    {
        ImGui.TextColored(ctx.Theme.TextMuted,
            $"{_ids.Count} nodes selected");
        ImGui.Separator();
        ImGui.TextColored(ctx.Theme.TextMuted,
            "Shared properties will appear here.");
        ImGui.Spacing();

        // List selected node IDs (informational).
        if (ImGui.CollapsingHeader($"Selected Nodes ({_ids.Count})"))
        {
            foreach (var id in _ids)
                ImGui.BulletText(id.ToString());
        }
    }
}
