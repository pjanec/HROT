using Hrot.Blueprints.Core.Compiler;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// One callable peer Blueprint, as offered by the <c>CallPeerBlueprint</c> picker (BP-08).
/// </summary>
/// <param name="AssetId">The peer's asset GUID — what <c>CallPeerBlueprintNode.PeerBlueprintId</c> stores.</param>
/// <param name="Name">Display name from the peer's own signature; may be empty for an unnamed asset.</param>
/// <param name="ExportedFunctions">Names of the peer's exported function graphs, in declaration order.</param>
public sealed record BlueprintPeerInfo(
    Guid AssetId,
    string Name,
    IReadOnlyList<string> ExportedFunctions);

/// <summary>
/// Supplies the callable-peer list to <see cref="CallPeerBlueprintNodeDrawer"/>.
///
/// <para>
/// A provider seam rather than a direct <c>BlueprintPeerSource</c> dependency, mirroring
/// <see cref="IComponentTypeProvider"/> and <see cref="ISharedStructTypeProvider"/>: peers are
/// discovered by scanning a directory for <c>*.bp.json</c>, which a headless test must be able to
/// replace, and the drawer registry is built at startup where no asset root is in scope.
/// </para>
/// </summary>
public interface IBlueprintPeerProvider
{
    /// <summary>
    /// All discoverable peer Blueprints. Implementations should be resilient — an unreadable or
    /// malformed asset is skipped, never thrown, so one bad file cannot empty the picker.
    /// </summary>
    IReadOnlyList<BlueprintPeerInfo> GetPeers();
}

/// <summary>
/// Default <see cref="IBlueprintPeerProvider"/>: no peers. Used when the composition root has no
/// asset root to scan, so the drawer renders an explicit "none discovered" state instead of an
/// empty combo that looks broken.
/// </summary>
public sealed class EmptyBlueprintPeerProvider : IBlueprintPeerProvider
{
    public static EmptyBlueprintPeerProvider Instance { get; } = new();
    private EmptyBlueprintPeerProvider() { }
    public IReadOnlyList<BlueprintPeerInfo> GetPeers() => Array.Empty<BlueprintPeerInfo>();
}

/// <summary>
/// Production <see cref="IBlueprintPeerProvider"/>: enumerates <c>*.bp.json</c> under a root via
/// <see cref="BlueprintPeerSource"/> and parses each one's <see cref="BlueprintSignature"/> — the
/// same read <c>QuickReloadService.BuildSiblingSignatures</c> and
/// <c>BlueprintDocumentFactory.BuildPeerSignatureLookup</c> perform for this very node kind.
/// </summary>
public sealed class BlueprintPeerSourceProvider : IBlueprintPeerProvider
{
    private readonly BlueprintPeerSource _source;

    public BlueprintPeerSourceProvider(BlueprintPeerSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public IReadOnlyList<BlueprintPeerInfo> GetPeers()
    {
        var peers = new List<BlueprintPeerInfo>();

        foreach (var (assetId, path) in _source.EnumerateAll())
        {
            if (assetId == Guid.Empty) continue;

            BlueprintSignature sig;
            try
            {
                sig = BlueprintSignatureParser.Parse(path, File.ReadAllText(path));
            }
            catch
            {
                // Unreadable or malformed peer — skip it rather than emptying the whole picker.
                continue;
            }

            peers.Add(new BlueprintPeerInfo(
                assetId,
                sig.Name,
                sig.ExportedFunctions.Select(f => f.Name).ToList()));
        }

        // Stable order so the picker does not reshuffle between opens (directory enumeration order
        // is filesystem-dependent).
        return peers
            .OrderBy(p => string.IsNullOrEmpty(p.Name) ? p.AssetId.ToString() : p.Name, StringComparer.Ordinal)
            .ToList();
    }
}
