using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Simple mutable attachment model for demo scenarios.</summary>
public sealed class FakeAttachmentModel : IAttachmentModel
{
    public AttachmentId Id { get; }
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category { get; set; }
    public string? Glyph { get; set; }
    public string? Label { get; set; }
    public string? Tooltip { get; set; }
    public AttachmentState State { get; set; }
    public int StackIndex { get; set; }

    public FakeAttachmentModel(
        AttachmentId id,
        NodeId hostNodeId,
        AttachmentCategory category,
        string? glyph,
        string? label,
        int stackIndex = 0)
    {
        Id         = id;
        HostNodeId = hostNodeId;
        Category   = category;
        Glyph      = glyph;
        Label      = label;
        StackIndex = stackIndex;
        State      = AttachmentState.Normal;
    }
}
