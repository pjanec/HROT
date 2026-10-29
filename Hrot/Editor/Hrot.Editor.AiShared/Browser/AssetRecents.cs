namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐⭐ CE-4098 (AQ108 D19) — <b>the naming of the user's Recent lists: ONE list per <see cref="AssetKind"/>.</b>
/// Every picker that offers a kind — the Open-Asset picker (<c>AssetPickerSource</c>), the asset browser
/// (<c>AssetBrowserPanel</c>), File › Open Scenario, File › Open Recording — names the same list here, so a pick in
/// one shows as Recent in the others and kinds never mix. ⛔ Never a second list for one kind.
///
/// <para>⭐ <b>The item key is the identity the picker already uses</b> — <see cref="IEditableAsset.AssetId"/>, which for a
/// scenario is derived from its path relative to the scenarios root (<c>ScenarioCatalogContributor.DeriveAssetId</c>) —
/// ⛔ never an absolute path, so moving the content folder (CE-4089) does not stale an entry. A recording's key is its
/// exercise id (<see cref="RecordingKey"/>).</para>
/// </summary>
public static class AssetRecents
{
    /// <summary>The Recent list of <paramref name="kind"/> (e.g. <c>"recent.Scenario"</c>, <c>"recent.Recording"</c>).</summary>
    public static string ContextFor(AssetKind kind) => "recent." + kind;

    /// <summary>The key an asset is remembered by — the identity the picker lists it with.</summary>
    public static string ItemKey(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        return asset.AssetId.ToString();
    }

    /// <summary>The key a recording (an exercise) is remembered by: its exercise id.</summary>
    public static string RecordingKey(Guid exerciseId) => exerciseId.ToString();
}
