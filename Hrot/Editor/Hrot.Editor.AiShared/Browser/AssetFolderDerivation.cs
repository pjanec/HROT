namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Derives logical subfolder knowledge from the asset catalog, independent of
/// the real filesystem. Used to seed <see cref="FolderPickerState"/> for the
/// New-Asset / Save-As name+folder modal.
/// </summary>
public static class AssetFolderDerivation
{
    /// <summary>
    /// Returns the distinct logical subfolder relative paths that already exist
    /// for <paramref name="kind"/>, derived from the catalog assets'
    /// <see cref="AssetRelPath.RelPath"/> directory parts (NOT the filesystem).
    /// </summary>
    /// <param name="assets">
    /// The asset catalog snapshot (typically <see cref="Catalog.IAssetCatalog.All"/>).
    /// </param>
    /// <param name="kind">The asset kind to filter by.</param>
    /// <param name="baseFolderResolver">
    /// Returns the base folder for a given <see cref="AssetKind"/> (used for
    /// relative-path computation). Typically
    /// <see cref="AssetBrowserPanel.BaseFolderFor"/>.
    /// </param>
    /// <returns>
    /// A sorted, case-insensitive-distinct list of subfolder relative paths
    /// (using <c>/</c> separators). Always includes <c>""</c> (root).
    /// When no assets of <paramref name="kind"/> exist, returns <c>[""]</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The subfolder extraction mirrors <see cref="AssetPickerSource.ToEntry"/>:
    /// take the directory part of <see cref="AssetRelPath.RelPath"/> (everything
    /// before the last <c>/</c>, or <c>""</c> when there is no separator).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> KnownSubfolders(
        IReadOnlyList<IEditableAsset> assets,
        AssetKind kind,
        Func<AssetKind, string?> baseFolderResolver)
    {
        if (assets == null)
            throw new ArgumentNullException(nameof(assets));
        if (baseFolderResolver == null)
            throw new ArgumentNullException(nameof(baseFolderResolver));

        var baseFolder = baseFolderResolver(kind);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };

        foreach (var asset in assets)
        {
            if (asset.Kind != kind)
                continue;

            var rel = AssetRelPath.RelPath(asset, baseFolder);

            // Extract the subfolder (directory part) from the relative path.
            // Mirrors AssetPickerSource.ToEntry (lines 138–142).
            int lastSlash = rel.LastIndexOf('/');
            string dir = lastSlash >= 0 ? rel.Substring(0, lastSlash) : "";

            distinct.Add(dir);
        }

        var result = distinct.ToList();
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result.AsReadOnly();
    }
}
