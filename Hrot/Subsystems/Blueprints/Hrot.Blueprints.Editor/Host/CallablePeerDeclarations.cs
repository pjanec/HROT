using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-116 — <c>Stage2_Validate</c> requires every <see cref="CallPeerBlueprintNode"/>'s
/// <see cref="CallPeerBlueprintNode.PeerBlueprintId"/> to appear in
/// <see cref="BlueprintAsset.CallablePeers"/> (emitting BP1300 otherwise). Before this helper
/// existed, nothing in <c>Hrot.Blueprints.Editor</c> ever <b>wrote</b> to that list — the three
/// places that assign <c>PeerBlueprintId</c> (the palette drag/drop create-path, the
/// drag-to-canvas initial-properties path, and the Details-panel peer picker) all left
/// <c>CallablePeers</c> untouched, so a designer who wired up a CallPeer node from the editor
/// produced an asset that could never compile.
///
/// <para>
/// A <i>stale</i> entry is also a compile error: <c>Stage2_Validate</c> separately rejects a
/// declared peer that is not part of the compilation ("Add as &lt;AdditionalFiles&gt; or remove
/// from CallablePeers"). So this is not just an add-only fix — retracting a peer id once nothing
/// references it anymore matters just as much as declaring one when something starts to.
/// </para>
/// </summary>
internal static class CallablePeerDeclarations
{
    /// <summary>
    /// Adds <paramref name="peerBlueprintId"/> to <paramref name="asset"/>.<see cref="BlueprintAsset.CallablePeers"/>
    /// if it parses as a <see cref="Guid"/> and is not already present. Comparison is always on the
    /// <b>parsed</b> Guid — never the raw string — so "N" and "D" spellings of the same id can never
    /// produce two entries.
    /// </summary>
    /// <returns><see langword="true"/> only when a new entry was actually added.</returns>
    public static bool Declare(BlueprintAsset asset, string? peerBlueprintId)
    {
        if (asset == null) return false;
        if (!Guid.TryParse(peerBlueprintId, out var id)) return false;

        if (asset.CallablePeers.Contains(id)) return false;

        asset.CallablePeers.Add(id);
        return true;
    }

    /// <summary>
    /// Removes <paramref name="peerBlueprintId"/> from <paramref name="asset"/>.<see cref="BlueprintAsset.CallablePeers"/>,
    /// but only when no <see cref="CallPeerBlueprintNode"/> in ANY of <paramref name="asset"/>'s
    /// <see cref="BlueprintAsset.Graphs"/> still references it. Scans every graph / every node, so a
    /// peer referenced from a different graph than the one the caller is looking at is still found.
    /// </summary>
    /// <returns><see langword="true"/> only when an entry was actually removed.</returns>
    public static bool RetractIfUnreferenced(BlueprintAsset asset, string? peerBlueprintId)
    {
        if (asset == null) return false;
        if (!Guid.TryParse(peerBlueprintId, out var id)) return false;

        foreach (var graph in asset.Graphs)
        foreach (var node in graph.Nodes)
        {
            if (node is CallPeerBlueprintNode cpb
                && Guid.TryParse(cpb.PeerBlueprintId, out var referencedId)
                && referencedId == id)
            {
                return false; // still referenced — do not retract.
            }
        }

        return asset.CallablePeers.Remove(id);
    }
}
