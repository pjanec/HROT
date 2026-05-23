using System.Collections.Generic;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// Editor-side implementation of a single attachment pinned to a state node.
// Attachments are created via GraphCommand.AddAttachment and stored in HsmAsset.
internal sealed class HsmAttachment : IAttachmentModel
{
    public AttachmentId Id { get; }
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category { get; }
    public string? Glyph { get; }
    public string? Label { get; }
    public string? Tooltip { get; }
    public AttachmentState State { get; set; }
    public int StackIndex { get; }

    internal HsmAttachment(
        AttachmentId id,
        NodeId hostNodeId,
        AttachmentCategory category,
        string? glyph,
        string? label,
        string? tooltip,
        int stackIndex,
        IReadOnlyDictionary<string, object?>? hostProperties)
    {
        Id = id;
        HostNodeId = hostNodeId;
        Category = category;
        Glyph = glyph;
        Label = label;
        Tooltip = tooltip;
        StackIndex = stackIndex;
        // hostProperties reserved for future host-defined extension data; not used in v1.
    }
}
