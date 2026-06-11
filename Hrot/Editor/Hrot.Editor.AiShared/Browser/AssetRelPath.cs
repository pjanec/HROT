namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Computes an asset's logical relative path for tree placement (§10.2, §3.9).
/// </summary>
/// <remarks>
/// <para>
/// <b>File asset</b> (non-empty <see cref="IEditableAsset.SourceFilePath"/> and non-null
/// <paramref name="baseFolder"/>): the <c>SourceFilePath</c> made relative to
/// <paramref name="baseFolder"/>, with backslashes normalized to <c>/</c> and any
/// leading <c>/</c> trimmed.
/// </para>
/// <para>
/// <b>Non-file asset</b> (empty <c>SourceFilePath</c> or null <paramref name="baseFolder"/>):
/// the asset's <see cref="IEditableAsset.Name"/> verbatim — which for scenarios already
/// encodes a relative path (§19).
/// </para>
/// </remarks>
public static class AssetRelPath
{
    /// <summary>
    /// Returns the logical relative path for <paramref name="asset"/> for use in folder
    /// tree construction.
    /// </summary>
    /// <param name="asset">The asset (never <see langword="null"/>).</param>
    /// <param name="baseFolder">
    /// The contributor's <see cref="Catalog.IAssetCatalogContributor.BaseFolder"/>, or
    /// <see langword="null"/> for non-file contributors.
    /// </param>
    /// <returns>
    /// For file assets: <c>SourceFilePath</c> relative to <paramref name="baseFolder"/>,
    /// normalized to <c>/</c> separators and without a leading <c>/</c>.
    /// For non-file assets: <see cref="IEditableAsset.Name"/> verbatim.
    /// </returns>
    public static string RelPath(IEditableAsset asset, string? baseFolder)
    {
        if (asset == null)
            throw new ArgumentNullException(nameof(asset));

        if (string.IsNullOrEmpty(asset.SourceFilePath) || string.IsNullOrEmpty(baseFolder))
            return asset.Name;

        var relPath = Path.GetRelativePath(baseFolder, asset.SourceFilePath)
            .Replace('\\', '/').TrimStart('/');

        // The asset may physically live under a DIFFERENT absolute root than baseFolder — e.g. the
        // contributor scanned the source project dir (…/Hrot/Subsystems/Hrot.AI.Behaviors/Assets/
        // Blueprints/…) while baseFolder resolves to the bin/output dir (…/bin/…/Assets/Blueprints).
        // GetRelativePath then emits "../../../…", surfacing as bogus ".." tree levels in the browser.
        // Recover the logical relpath by anchoring on baseFolder's trailing segment chain
        // (e.g. "Assets/Blueprints") within the source path, independent of the absolute prefix.
        if (relPath.StartsWith("../", StringComparison.Ordinal) || relPath == "..")
        {
            var baseSegs = baseFolder.Replace('\\', '/').TrimEnd('/').Split('/');
            var anchor = baseSegs.Length >= 2
                ? baseSegs[^2] + "/" + baseSegs[^1]
                : baseSegs[^1];
            var src = asset.SourceFilePath.Replace('\\', '/');
            int idx = src.LastIndexOf(anchor + "/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return src.Substring(idx + anchor.Length + 1);
        }

        return relPath;
    }
}
