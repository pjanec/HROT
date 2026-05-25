using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for SpawnEqsSensorNode: shows the chosen EQS template name.
/// Glyph: 📡   Category: Custom   State: Normal (template set) or Warning (none).
/// </summary>
public sealed class EqsTemplateAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => "📡";
    public string? Label { get; private set; }
    public string? Tooltip => Label;
    public AttachmentState State { get; private set; }
    public int StackIndex => 0;

    public EqsTemplateAttachment(SpawnEqsSensorNode node, EqsTemplateRegistry templates)
    {
        HostNodeId = new NodeId(node.Id);
        Refresh(node, templates);
    }

    public void Refresh(SpawnEqsSensorNode node, EqsTemplateRegistry templates)
    {
        if (node.TemplateAssetId == Guid.Empty)
        {
            Label = "(no template)";
            State = AttachmentState.Warning;
            return;
        }
        var entry = templates.TryGet(node.TemplateAssetId);
        Label = entry?.DisplayName ?? "(template not found)";
        State = entry is not null ? AttachmentState.Normal : AttachmentState.Warning;
    }
}
