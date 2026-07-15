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
        //
        // Phase C (AIE-053): also expose the FQN a BTree node composes onto when it places
        // this blueprint as an AiPrimitive TickCore node — "{SanitizedName}_{BlueprintId:X8}_Bp.TickCore"
        // (see ComposedBlueprintResolver). The header-only BlueprintFileAsset doesn't know whether this
        // blueprint is actually AiPrimitive-dispatch (that requires the "dispatch" field, not parsed by
        // BlueprintAssetContributor), so the element is exposed unconditionally; it is harmless for
        // non-AiPrimitive blueprints since the key is derived solely from this asset's own identity and
        // nothing can ever reference it unless a BTree node was actually composed onto it.
        return new IAssetSubElement[]
        {
            new BlueprintAssetSubElement(bp.AssetId, bp.Name),
            new BlueprintComposedFqnSubElement(bp.AssetId, bp.Name, bp.GeneratedClassName),
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

/// <summary>
/// Phase C (AIE-053): represents the generated AiPrimitive TickCore FQN a BTree node composes
/// onto when it places this Blueprint asset as a host-BTree node. Key format matches
/// <see cref="ComposedBlueprintResolver.ElementKey"/>: <c>{GeneratedClassName}.TickCore</c>.
/// The generated class name is precomputed on this (compiler-referencing) editor side and passed
/// in, so the shared reference layer never recomputes the id hash.
/// </summary>
internal sealed class BlueprintComposedFqnSubElement : IAssetSubElement
{
    public string Key { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// A composed AiPrimitive may be placed as either an Action or a Condition node; the
    /// element itself doesn't know which shape a future BTree node will use, so it picks
    /// <see cref="SubElementKind.ActionFqn"/> as the nominal kind. This has no effect on
    /// <see cref="Hrot.Editor.AiShared.Refactor.RefactorService"/>'s delete-block classification,
    /// which classifies by the referencing <see cref="AssetReference.TargetKind"/> (set by the
    /// BTree-side contributor to ActionFqn/ConditionFqn per the actual node kind), not by this
    /// element's Kind.
    /// </remarks>
    public SubElementKind Kind => SubElementKind.ActionFqn;

    public string DisplayName { get; }

    public Guid? SourceAssetId { get; }

    public BlueprintComposedFqnSubElement(Guid assetId, string assetName, string generatedClassName)
    {
        SourceAssetId = assetId;
        DisplayName   = assetName;
        Key           = ComposedBlueprintResolver.ElementKey(generatedClassName);
    }
}
