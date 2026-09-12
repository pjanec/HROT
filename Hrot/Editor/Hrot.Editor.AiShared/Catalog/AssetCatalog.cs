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

    /// <summary>
    /// ⭐⭐⭐ <b>Resolve an asset by its SOURCE FILE PATH — the human-readable address.</b>
    /// 📄 <c>DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md</c> §3a.
    ///
    /// <para>⛔⛔ <b>Why not <see cref="FindByName"/>.</b> Assets organise into SUBFOLDERS, so two folders
    /// may legitimately hold <c>blueprint1.bp.json</c> ⇒ ⭐ <b>the name is not an address</b>; the relative
    /// path is. <see cref="Identity.IEditableAsset.SourceFilePath"/> already preserves it, so this is a
    /// lookup over data that exists — ⛔ no new identity concept.</para>
    ///
    /// <para>⭐⭐ <b>Matching is SUFFIX-based on normalised separators, and deliberately so.</b> 📐 A
    /// contributor may store an ABSOLUTE path while a caller naturally knows the relative one
    /// *(<c>blueprint/subfolder/blueprint1.bp.json</c>)*. ⇒ an exact-equality lookup would answer null for
    /// the address a human would actually type. ⚠ The suffix must start at a SEGMENT boundary, so
    /// <c>…/my_blueprint1.bp.json</c> never matches a query for <c>blueprint1.bp.json</c>.</para>
    ///
    /// <para>⚠ <b>Ambiguity is reported, not resolved.</b> When two assets match the same suffix the
    /// caller was under-specific — ⛔ returning the first would be the silent wrong-asset bug this method
    /// exists to prevent. ⭐ Use <see cref="FindAllBySourceFilePath"/> to see the candidates and say which.</para>
    /// </summary>
    /// <returns>The single match, or <see langword="null"/> when there is none — <b>or more than one</b>.</returns>
    public IEditableAsset? FindBySourceFilePath(string path)
    {
        var matches = FindAllBySourceFilePath(path);
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// ⭐ Every asset whose <c>SourceFilePath</c> ends with <paramref name="path"/> at a segment boundary.
    /// ⭐⭐ Exists so an ambiguous address can be REPORTED with its candidates — 📌 the API answers
    /// <i>"2 assets match; say which"</i> and lists them, rather than a bare 404.
    /// </summary>
    public IReadOnlyList<IEditableAsset> FindAllBySourceFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<IEditableAsset>();

        var needle = Normalize(path);
        var hits   = new List<IEditableAsset>();

        foreach (var asset in _cache)
        {
            var hay = Normalize(asset.SourceFilePath);
            if (hay.Length == 0) continue;

            if (string.Equals(hay, needle, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(asset);
                continue;
            }

            // ⛔ The segment-boundary guard: "a/my_x.json".EndsWith("x.json") is TRUE and wrong.
            if (hay.Length > needle.Length
             && hay.EndsWith(needle, StringComparison.OrdinalIgnoreCase)
             && hay[hay.Length - needle.Length - 1] == '/')
                hits.Add(asset);
        }

        return hits;
    }

    /// <summary>⭐ One separator, no trailing slash, so Windows and POSIX paths compare equal.</summary>
    private static string Normalize(string path)
        => (path ?? string.Empty).Replace('\\', '/').Trim('/');

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
