namespace Hrot.Editor.AiShared.Catalog;

public sealed class AssetCatalog : IAssetCatalog
{
    private readonly List<IAssetCatalogContributor> _contributors = new();
    private List<IEditableAsset> _cache = new();
    private Dictionary<Guid, IEditableAsset> _byId = new();

    public IReadOnlyList<IEditableAsset> All => _cache;

    public event Action<AssetKind>? Changed;

    public void AddContributor(IAssetCatalogContributor contributor)
    {
        _contributors.Add(contributor);
        contributor.ContributorChanged += () => OnContributorChanged(contributor.Kind);
        Rebuild();
    }

    public IEditableAsset? FindByAssetId(Guid assetId) =>
        _byId.GetValueOrDefault(assetId);

    public IEditableAsset? FindByName(string name) =>
        _cache.FirstOrDefault(a => a.Name == name);

    // Returns empty list for now; reverse-dependency tracking comes in Phase 5/6.
    public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
        Array.Empty<IEditableAsset>();

    private void OnContributorChanged(AssetKind kind)
    {
        Rebuild();
        Changed?.Invoke(kind);
    }

    private void Rebuild()
    {
        // Build _byId with last-writer-wins semantics (JSON contributors are added after
        // assembly contributors by AiAssetCatalogBuilder, so JSON wins the AssetId collision).
        var byId = new Dictionary<Guid, IEditableAsset>();
        foreach (var contributor in _contributors)
            foreach (var asset in contributor.Enumerate())
                byId[asset.AssetId] = asset;

        // Build _cache as a deduped list (same last-writer order as _byId).
        // Preserves stable ordering: first occurrence of each AssetId wins the slot,
        // but the VALUE stored is the last-writer's instance (from byId lookup).
        var seen  = new HashSet<Guid>(byId.Count);
        var cache = new List<IEditableAsset>(byId.Count);
        foreach (var contributor in _contributors)
        {
            foreach (var asset in contributor.Enumerate())
            {
                if (seen.Add(asset.AssetId))
                    cache.Add(byId[asset.AssetId]); // resolved to last-writer instance
            }
        }

        _cache = cache;
        _byId  = byId;
    }
}
