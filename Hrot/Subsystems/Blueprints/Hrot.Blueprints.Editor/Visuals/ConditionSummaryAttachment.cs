using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for WhenNode: shows the active mode's compact summary (e.g., "Health ↑").
/// Glyph: ⚡   Category: Custom   State: Normal (healthy) or Warning (no edge selected).
/// </summary>
public sealed class ConditionSummaryAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => NodeEditor.Core.Widgets.Icons.Get("bp/condition").Glyph;   // ⭐ U-3: the ONE icon map (CE-1063: a Font Awesome glyph, merged into the canvas faces)
    public System.Numerics.Vector4? GlyphColor => NodeEditor.Core.Widgets.Icons.Get("bp/condition").Color;
    public string? Label { get; private set; }
    public string? Tooltip { get; private set; }
    public AttachmentState State { get; private set; }
    public int StackIndex => 0;

    public ConditionSummaryAttachment(WhenNode node)
    {
        HostNodeId = new NodeId(node.Id);
        Refresh(node);
    }

    /// <summary>Updates Label/State when the node is edited.</summary>
    public void Refresh(WhenNode node)
    {
        Label   = PreviewSynthesizer.Synthesize(node, maxLength: 36);
        State   = node.Edges == WhenEdge.None
                    ? AttachmentState.Warning
                    : AttachmentState.Normal;
        Tooltip = $"Mode: {node.Mode}  Edges: {node.Edges}";
    }
}
