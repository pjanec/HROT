using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;

namespace Hrot.Blueprints.Editor.Catalog;

/// <summary>
/// Implements <see cref="IReferenceCatalogContributor"/> for the Blueprint subsystem.
/// Exposes the Blueprint file asset's identity as a referenceable sub-element so that
/// other assets can track a cross-asset reference to it by <see cref="AssetKind.Blueprint"/>.
/// <para>
/// Phase 1 limitation: the contributor operates on the header-only
/// <see cref="BlueprintFileAsset"/> (loaded from the asset catalog) and therefore
/// cannot enumerate per-node references inside the Blueprint graph without
/// deserializing the full document. Full per-node reference tracking is deferred to
/// Phase 2 (AIE-053 and beyond) once the document manager hydrates the full
/// <see cref="Hrot.Blueprints.Core.Assets.BlueprintAsset"/>.
/// </para>
/// </summary>
public sealed class BlueprintReferenceContributor : IReferenceCatalogContributor
{
    /// <inheritdoc/>
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
    {
        if (asset is not BlueprintFileAsset bp)
            return Array.Empty<IAssetSubElement>();

        // Expose the Blueprint asset itself as a referenceable element by asset-id key,
        // so that cross-asset peer-call references from other assets can resolve it.
        return new IAssetSubElement[]
        {
            new BlueprintAssetSubElement(bp.AssetId, bp.Name),
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        // Header-only asset: cannot enumerate per-node references without deserializing.
        // Returns empty — full graph references are tracked when the document is loaded.
        return Array.Empty<AssetReference>();
    }
}

/// <summary>
/// Represents a Blueprint file asset as a referenceable sub-element.
/// Key is the asset's <see cref="Guid"/> formatted as <c>D</c>, which is the same format
/// stored in <c>CallPeerBlueprintNode.PeerBlueprintId</c> fields.
/// </summary>
internal sealed class BlueprintAssetSubElement : IAssetSubElement
{
    /// <summary>Asset id formatted as <c>{assetId:D}</c>.</summary>
    public string Key { get; }

    /// <inheritdoc/>
    public SubElementKind Kind => SubElementKind.AssetReference;

    /// <inheritdoc/>
    public string DisplayName { get; }

    /// <inheritdoc/>
    public Guid? SourceAssetId { get; }

    public BlueprintAssetSubElement(Guid assetId, string assetName)
    {
        SourceAssetId = assetId;
        DisplayName   = assetName;
        Key           = assetId.ToString("D");
    }
}
