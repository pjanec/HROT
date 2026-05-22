namespace Hrot.Blueprints.Editor;

public sealed record AssetCatalogEntry(Guid AssetId, string Path);

public interface IAssetCatalog
{
    IEnumerable<AssetCatalogEntry> EnumerateAll();
}
