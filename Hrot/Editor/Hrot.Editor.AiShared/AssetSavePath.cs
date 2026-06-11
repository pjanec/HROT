using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.AiShared;

/// <summary>
/// Composes and validates file-system paths for persisting file-based editor assets
/// (Blueprint, BTree, HSM) under the <see cref="AssetRoots.AssetsFor"/> root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Compose:</b> <c>Compose(kind, relPath, baseName)</c> returns an absolute path
/// of the form <c>AssetsFor(kind)/relPath/baseName.ext</c> where <c>.ext</c> is the
/// kind's canonical compound suffix (<c>.bp.json</c> / <c>.btree.json</c> /
/// <c>.hsm.json</c>). Separators are normalized and the resulting path is validated
/// against root-escape rules (no <c>..</c>, no absolute sub-path components).
/// </para>
/// <para>
/// <b>Wiring (MTB-P6-T7):</b> the per-kind <c>CreateNew</c> implementations use this
/// helper so that assets land in the correct subfolder; the existing <c>Save</c> path
/// uses <c>SourceFilePath</c> directly, which already encodes any subfolder when the
/// asset was created via <see cref="Compose"/>.
/// </para>
/// <para>
/// <b>Root override:</b> the optional <paramref name="assetRootOverride"/> parameter
/// on <see cref="Compose"/> allows tests to point at a temporary directory without
/// changing the global <see cref="AppContext.BaseDirectory"/>.
/// </para>
/// </remarks>
public static class AssetSavePath
{
    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Combines <paramref name="basePath"/> with each segment in order,
    /// normalizing directory separators.
    /// </summary>
    private static string CombineSegments(string basePath, string[] segments)
    {
        var result = basePath;
        foreach (var seg in segments)
        {
            if (!string.IsNullOrEmpty(seg))
                result = Path.Combine(result, seg);
        }
        return result;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the canonical compound file extension for <paramref name="kind"/>
    /// (including the leading dot): <c>.bp.json</c>, <c>.btree.json</c>, or
    /// <c>.hsm.json</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for kinds that have no file extension (<see cref="AssetKind.Scenario"/>,
    /// <see cref="AssetKind.Blackboard"/>, <see cref="AssetKind.Utility"/>).
    /// </exception>
    public static string GetExtension(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint => ".bp.json",
        AssetKind.BTree     => ".btree.json",
        AssetKind.Hsm       => ".hsm.json",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, $"AssetKind.{kind} has no canonical file extension.")
    };

    /// <summary>
    /// Composes the absolute file path for a new asset of <paramref name="kind"/>:
    /// <c><paramref name="assetRootOverride"/> ?? AssetsFor(kind) / relPath / baseName.ext</c>.
    /// </summary>
    /// <param name="kind">The asset kind (must be a file-based kind).</param>
    /// <param name="relPath">
    /// The subfolder relative to the kind's Assets root (using <c>/</c> separators).
    /// Pass <c>""</c> or <see langword="null"/> to place the file directly under the root.
    /// </param>
    /// <param name="baseName">
    /// The logical asset name (without extension). Must not be empty.
    /// </param>
    /// <param name="assetRootOverride">
    /// Optional absolute path to use in place of <see cref="AssetRoots.AssetsFor"/>.
    /// Pass <see langword="null"/> to use the production default.
    /// </param>
    /// <returns>
    /// The absolute file path (normalized separators).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="baseName"/> is empty or <paramref name="relPath"/>
    /// fails root-bounding validation (contains <c>..</c>, is absolute, etc.).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="kind"/> has no Assets root (Scenario, etc.).
    /// </exception>
    public static string Compose(
        AssetKind kind,
        string   relPath,
        string   baseName,
        string?  assetRootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            throw new ArgumentException("Base name must not be empty.", nameof(baseName));

        var assetRoot = assetRootOverride ?? AssetRoots.AssetsFor(kind);
        var ext = GetExtension(kind);

        // Reject absolute / root-escaping paths BEFORE normalization.
        // Must match FolderPickerState.IsAbsolutePath semantics: leading /, \, drive letter,
        // or Path.IsPathRooted.
        var rawRel = relPath ?? "";
        if (rawRel.Length > 0 && (rawRel[0] == '/' || rawRel[0] == '\\'))
            throw new ArgumentException(
                $"Relative path '{relPath}' is not valid (must not escape the root).",
                nameof(relPath));
        if (rawRel.Length >= 2 && rawRel[1] == ':')
            throw new ArgumentException(
                $"Relative path '{relPath}' is not valid (must not escape the root).",
                nameof(relPath));
        if (rawRel.Contains(".."))
            throw new ArgumentException(
                $"Relative path '{relPath}' is not valid (must not escape the root).",
                nameof(relPath));
        if (Path.IsPathRooted(rawRel))
            throw new ArgumentException(
                $"Relative path '{relPath}' is not valid (must not escape the root).",
                nameof(relPath));

        // Normalize relPath: forward slashes, no leading/trailing slashes.
        var sanitized = rawRel.Replace('\\', '/').Trim('/');

        // Validate via FolderPickerState's bounded-check (catches edge cases).
        if (!FolderPickerState.IsBounded(sanitized))
            throw new ArgumentException(
                $"Relative path '{relPath}' is not valid (must not escape the root).",
                nameof(relPath));

        var fileName = baseName + ext;
        var dir = string.IsNullOrEmpty(sanitized)
            ? assetRoot
            : CombineSegments(assetRoot, sanitized.Split('/'));

        return Path.Combine(dir, fileName);
    }
}
