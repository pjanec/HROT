using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Attachment provider for WhenNode when its source is PeerBlueprintVariable.
/// Requires a peer-name resolver callback (host supplies Blueprint asset name lookup).
/// </summary>
public sealed class CrossAssetDependencyAttachmentProvider : IAttachmentProvider
{
    private readonly Func<Guid, string?> _peerNameResolver;

    /// <summary>
    /// <paramref name="peerNameResolver"/> receives a Blueprint AssetId and returns
    /// its display name, or null if not found.
    /// </summary>
    public CrossAssetDependencyAttachmentProvider(Func<Guid, string?> peerNameResolver)
        => _peerNameResolver = peerNameResolver;

    public bool Handles(Node node)
        => node is WhenNode w
           && w.Mode == WhenMode.ValueChanged
           && w.ValueChanged?.Source == ValueChangedSource.PeerBlueprintVariable
           && w.ValueChanged?.PeerBlueprintAssetId is not null;

    public IAttachmentModel? CreateOrRefresh(Node node, IAttachmentModel? existing)
    {
        var when = (WhenNode)node;
        var peerId = when.ValueChanged!.PeerBlueprintAssetId!.Value;
        var name   = _peerNameResolver(peerId) ?? peerId.ToString("N")[..8];

        if (existing is CrossAssetDependencyAttachment cad
            && cad.HostNodeId == new NodeId(node.Id))
        {
            // CrossAssetDependencyAttachment is immutable once created; recreate if name changed.
            if (cad.Label == name) return cad;
        }
        return new CrossAssetDependencyAttachment(new NodeId(node.Id), name);
    }
}
