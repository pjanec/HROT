using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for ReadEqsResultNode: shows the variable name it reads from.
/// Glyph: 📊   Category: Custom   State: Normal (set) or Warning (empty).
/// </summary>
public sealed class ReadEqsResultAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => "📊";
    public string? Label { get; private set; }
    public string? Tooltip => Label;
    public AttachmentState State { get; private set; }
    public int StackIndex => 0;

    public ReadEqsResultAttachment(ReadEqsResultNode node)
    {
        HostNodeId = new NodeId(node.Id);
        Refresh(node);
    }

    public void Refresh(ReadEqsResultNode node)
    {
        if (string.IsNullOrWhiteSpace(node.SensorVariableName))
        {
            Label = "(no variable)";
            State = AttachmentState.Warning;
        }
        else
        {
            Label = node.SensorVariableName;
            State = AttachmentState.Normal;
        }
    }
}
