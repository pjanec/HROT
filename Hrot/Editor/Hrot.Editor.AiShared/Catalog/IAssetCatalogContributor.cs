namespace Hrot.Editor.AiShared.Catalog;

public interface IAssetCatalogContributor
{
    AssetKind Kind { get; }
    IReadOnlyList<IEditableAsset> Enumerate();

    /// <summary>
    /// The absolute base folder for this contributor's assets.
    /// File-backed contributors return the kind's <c>Assets/&lt;Kind&gt;</c> root
    /// (e.g. <c>AssetRoots.AssetsFor(Kind)</c>); non-file contributors (assembly,
    /// test fakes, scenarios) return <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>Used by consumers (e.g. <see cref="Hrot.Editor.AiShared.Browser.AssetRelPath"/>)
    /// to compute an asset's logical path relative to its browse root (§10.2).</para>
    /// <para>The default is <see langword="null"/> — every existing implementor that
    /// does not override this property stays backward-compatible.</para>
    /// </remarks>
    string? BaseFolder => null;

    // Fires when this contributor's asset list changes.
    event Action? ContributorChanged;
}
