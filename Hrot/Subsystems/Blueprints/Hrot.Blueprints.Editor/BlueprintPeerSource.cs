using System.Text.Json;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Thin directory scanner that provides a contributor-style
/// <c>(Guid AssetId, string Path)</c> enumeration over Blueprint
/// <c>.bp.json</c> files under a root directory.
///
/// <para>
/// Used by <c>BlueprintDocumentFactory.BuildPeerSignatureLookup</c> and
/// <c>QuickReloadService.BuildSiblingSignatures</c> to resolve peer blueprint
/// signatures by AssetId without depending on the now-retired <c>IAssetCatalog</c>
/// / <c>AssetCatalogEntry</c> types.
/// </para>
/// </summary>
/// <remarks>
/// <para>Design (DEC-13, MTB-P7-T5): the retired Blueprints <c>IAssetCatalog</c> +
/// <c>FileSystemAssetCatalog</c> backed both the browser AND the
/// peer-signature lookup for <c>CallPeerBlueprintNode</c>s.  When the browser
/// window was retired, the peer-signature path was extracted into this thin
/// directory scanner that yields tuples instead of <c>AssetCatalogEntry</c>
/// records — preserving the CallPeer/quick-reload behaviour while deleting the
/// now-unnecessary interface and record types.</para>
///
/// <para>Scanning logic mirrors <see cref="BlueprintAssetContributor.Refresh"/>:
/// enumerate <c>*.bp.json</c>, read only the <c>AssetId</c> header, skip
/// unreadable files.</para>
/// </remarks>
public sealed class BlueprintPeerSource
{
    private readonly string _rootDirectory;

    /// <param name="rootDirectory">
    /// Root directory to scan recursively for <c>*.bp.json</c> files.
    /// </param>
    public BlueprintPeerSource(string rootDirectory)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    }

    /// <summary>
    /// Enumerates all <c>.bp.json</c> files under the root directory,
    /// yielding <c>(Guid AssetId, string Path)</c> pairs for every readable
    /// file whose header contains a valid <c>AssetId</c> field.
    /// </summary>
    public IEnumerable<(Guid AssetId, string Path)> EnumerateAll()
    {
        if (!Directory.Exists(_rootDirectory))
            yield break;

        // Robust recursive enumeration: skip inaccessible subdirectories (e.g. a system
        // temp dir like %TEMP%\WinSAT) instead of throwing UnauthorizedAccessException mid-walk,
        // which would abort quick-reload's sibling-signature scan. (The old FileSystemAssetCatalog
        // used the SearchOption overload; this preserves the scan while tolerating denied dirs.)
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible    = true,
            // Case-insensitive match: *.bp.json files may have drifted extension casing
            // when authored on Windows; PlatformDefault would silently miss them on Linux.
            MatchCasing           = MatchCasing.CaseInsensitive,
        };

        foreach (var filePath in Directory.EnumerateFiles(
            _rootDirectory, "*.bp.json", options))
        {
            Guid assetId;
            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("AssetId", out var idEl) ||
                    !idEl.TryGetGuid(out assetId))
                    continue;
            }
            catch
            {
                continue;
            }

            yield return (assetId, filePath);
        }
    }
}
