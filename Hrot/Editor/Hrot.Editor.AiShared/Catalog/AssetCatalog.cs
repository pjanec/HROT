namespace Hrot.Editor.AiShared.Catalog;

public sealed class AssetCatalog : IAssetCatalog
{
    private readonly List<IAssetCatalogContributor> _contributors = new();
    private List<IEditableAsset> _cache = new();
    private Dictionary<Guid, IEditableAsset> _byId = new();

    public IReadOnlyList<IEditableAsset> All => _cache;

    public event Action? Changed;

    public void AddContributor(IAssetCatalogContributor contributor)
    {
        _contributors.Add(contributor);
        contributor.ContributorChanged += OnContributorChanged;
        Rebuild();
    }

    public IEditableAsset? FindByAssetId(Guid assetId) =>
        _byId.GetValueOrDefault(assetId);

    public IEditableAsset? FindByName(string name) =>
        _cache.FirstOrDefault(a => a.Name == name);

    // Returns empty list for now; reverse-dependency tracking comes in Phase 5/6.
    public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
        Array.Empty<IEditableAsset>();

    private void OnContributorChanged()
    {
        Rebuild();
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        var merged = new List<IEditableAsset>();
        foreach (var contributor in _contributors)
            merged.AddRange(contributor.Enumerate());

        _cache = merged;
        _byId = new Dictionary<Guid, IEditableAsset>(merged.Count);
        foreach (var asset in merged)
            _byId[asset.AssetId] = asset;
    }
}
