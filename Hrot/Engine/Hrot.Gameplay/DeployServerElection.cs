namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join S1-e (D-3: "a second one stands down") — ONE deploy server per cluster: the LOWEST node id that advertises
/// <c>hrot.feature.gameplay.deploy-server</c> AND reports it ON serves; the others stand down. Deterministic with no extra wire — every node reads the same
/// cluster cache. The local node is always a candidate (its own feature is on, or its system would not run). The cluster cache is reached through delegates
/// so this assembly does not reference the NED network layer.
/// </summary>
public sealed class DeployServerElection
{
    private readonly Func<IReadOnlyList<int>> _allNodeIds;
    private readonly Func<int, string, bool> _supports;
    private readonly Func<int, string, bool> _featureOn;

    /// <param name="allNodeIds"><c>SimpleClusterStateCache.AllNodeIds</c>.</param>
    /// <param name="supports"><c>SimpleClusterStateCache.Supports(node, token)</c>.</param>
    /// <param name="featureOn"><c>SimpleClusterStateCache.FeatureOn(node, featureId)</c>.</param>
    public DeployServerElection(Func<IReadOnlyList<int>> allNodeIds, Func<int, string, bool> supports, Func<int, string, bool> featureOn)
    {
        _allNodeIds = allNodeIds ?? throw new ArgumentNullException(nameof(allNodeIds));
        _supports = supports ?? throw new ArgumentNullException(nameof(supports));
        _featureOn = featureOn ?? throw new ArgumentNullException(nameof(featureOn));
    }

    /// <summary>
    /// ⭐ Player Join D-3 / R-4103 — does ANY node serve Deploy: advertises <c>hrot.feature.gameplay.deploy-server</c> AND reports it ON? The Player's "is there a server"
    /// (none ⇒ it observes). Same delegates as <see cref="IsServer"/> — one definition of "serving". (Allocates — about once a second.)
    /// </summary>
    public bool AnyServer()
    {
        string token = GameplayFeatures.DeployServerToken;
        foreach (int id in _allNodeIds())
            if (_supports(id, token) && _featureOn(id, GameplayFeatures.DeployServerId)) return true;
        return false;
    }

    /// <summary>Is <paramref name="localNode"/> the server? (Allocates — call it about once a second, not per frame.)</summary>
    public bool IsServer(int localNode)
    {
        string token = GameplayFeatures.DeployServerToken;
        foreach (int id in _allNodeIds())
            if (id < localNode && _supports(id, token) && _featureOn(id, GameplayFeatures.DeployServerId)) return false;
        return true;
    }
}
