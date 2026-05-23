namespace Hrot.Editor.AiShared.References;

public interface IReferenceCatalog
{
    IReadOnlyList<IAssetSubElement> AllElements { get; }
    IAssetSubElement? FindElement(string key);
    IReadOnlyList<AssetReference> FindReferences(string targetKey);
    IReadOnlyList<AssetReference> AllReferencesIn(Guid hostAssetId);
    event Action? Changed;
}
