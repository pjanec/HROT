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
    public string? Glyph => NodeEditor.Core.Widgets.Icons.Get("bp/cross-asset").Glyph;   // ⭐ U-3: the ONE icon map (CE-1063: a Font Awesome glyph, merged into the canvas faces)
    public System.Numerics.Vector4? GlyphColor => NodeEditor.Core.Widgets.Icons.Get("bp/cross-asset").Color;
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
