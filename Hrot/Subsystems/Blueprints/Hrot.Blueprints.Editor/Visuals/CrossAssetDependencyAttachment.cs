using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment pill for nodes that transitively depend on a peer Blueprint asset.
/// Shown when a WhenNode has ValueChangedSource = PeerBlueprintVariable.
/// Glyph: 🔗   Category: Custom   State: Normal.
/// </summary>
public sealed class CrossAssetDependencyAttachment : IAttachmentModel
{
    public AttachmentId Id { get; } = new(Guid.NewGuid());
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category => AttachmentCategory.Custom;
    public string? Glyph => Fdp.Presentation.Fonts.IconsFontAwesome6.Link; // CE-1063: 🔗 has no glyph in any baked font — Font Awesome's, merged into the canvas faces
    public string? Label { get; private set; }
    public string? Tooltip { get; private set; }
    public AttachmentState State => AttachmentState.Normal;
    public int StackIndex => 1;   // renders after ConditionSummaryAttachment (StackIndex 0)

    public CrossAssetDependencyAttachment(NodeId hostNodeId, string peerAssetName)
    {
        HostNodeId = hostNodeId;
        Label   = peerAssetName;
        Tooltip = $"Depends on peer Blueprint: {peerAssetName}";
    }
}
