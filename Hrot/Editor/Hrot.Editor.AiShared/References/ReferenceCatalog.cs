using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.References;

public sealed class ReferenceCatalog : IReferenceCatalog
{
    private readonly Dictionary<string, IAssetSubElement> _elements = new();
    private readonly List<AssetReference> _references = new();

    public event Action? Changed;

    public ReferenceCatalog(IAssetCatalog? catalog = null)
    {
        if (catalog != null)
            catalog.Changed += OnCatalogChanged;
    }

    public IReadOnlyList<IAssetSubElement> AllElements => _elements.Values.ToList();

    public IAssetSubElement? FindElement(string key) =>
        _elements.GetValueOrDefault(key);

    public IReadOnlyList<AssetReference> FindReferences(string targetKey) =>
        _references.Where(r => r.TargetKey == targetKey).ToList();

    public IReadOnlyList<AssetReference> AllReferencesIn(Guid hostAssetId) =>
        _references.Where(r => r.HostAssetId == hostAssetId).ToList();

    /// <summary>
    /// Directly contributes an element and its associated references. Used for testing
    /// and for Phase 1 population without subsystem contributors.
    /// </summary>
    public void Contribute(IAssetSubElement element, IReadOnlyList<AssetReference> refs)
    {
        _elements[element.Key] = element;
        _references.AddRange(refs);
        Changed?.Invoke();
    }

    private void OnCatalogChanged()
    {
        // Full rebuild from contributors will be wired here in Phase 5/6.
        Changed?.Invoke();
    }
}
