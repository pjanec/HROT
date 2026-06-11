using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.References;

public sealed class ReferenceCatalog : IReferenceCatalog
{
    private readonly Dictionary<string, IAssetSubElement> _elements = new();
    private readonly List<AssetReference> _references = new();
    private readonly IAssetCatalog? _catalog;
    private readonly IEnumerable<IReferenceCatalogContributor> _contributors;

    public event Action? Changed;

    public ReferenceCatalog(IAssetCatalog? catalog = null, IEnumerable<IReferenceCatalogContributor>? contributors = null)
    {
        _catalog = catalog;
        _contributors = contributors ?? Enumerable.Empty<IReferenceCatalogContributor>();
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

    private void OnCatalogChanged(AssetKind kind)
    {
        if (kind == AssetKind.Scenario)
            return;

        _elements.Clear();
        _references.Clear();
        if (_catalog != null)
        {
            foreach (var asset in _catalog.All)
            {
                foreach (var contributor in _contributors)
                {
                    foreach (var el in contributor.EnumerateElements(asset))
                        _elements[el.Key] = el;
                    _references.AddRange(contributor.EnumerateReferences(asset));
                }
            }
        }
        Changed?.Invoke();
    }
}
