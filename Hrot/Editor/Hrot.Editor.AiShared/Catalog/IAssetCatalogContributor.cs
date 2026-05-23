namespace Hrot.Editor.AiShared.Catalog;

public interface IAssetCatalogContributor
{
    AssetKind Kind { get; }
    IReadOnlyList<IEditableAsset> Enumerate();

    // Fires when this contributor's asset list changes.
    event Action? ContributorChanged;
}
