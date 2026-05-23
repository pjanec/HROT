namespace Hrot.Editor.AiShared.References;

/// <summary>
/// Contributor interface for populating the reference catalog from a host editor.
/// Implementations live in subsystem editor assemblies; used from Phase 5/6 onwards.
/// </summary>
public interface IReferenceCatalogContributor
{
    IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset);
    IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset);
}
