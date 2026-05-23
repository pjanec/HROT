namespace Hrot.Editor.AiShared.Catalog;

public interface IAssetCatalog
{
    IReadOnlyList<IEditableAsset> All { get; }
    IEditableAsset? FindByAssetId(Guid assetId);
    IEditableAsset? FindByName(string name);
    IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId);
    event Action? Changed;
}
