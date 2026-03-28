namespace Bagira.Orchestrator;

/// <summary>Active nodes keyed by DDS node id; stale entries removed by <see cref="DrillMaster"/>.</summary>
public sealed class NodeRoster
{
    private readonly Dictionary<int, NodeHealthProfile> _active = new();

    public IReadOnlyDictionary<int, NodeHealthProfile> ActiveNodes => _active;

    public void Upsert(NodeHealthProfile profile)
    {
        _active[profile.NodeId] = profile;
    }

    public void Remove(int nodeId) => _active.Remove(nodeId);

    /// <summary>Removes nodes whose last heartbeat is older than <paramref name="maxSilenceSeconds"/>.</summary>
    public void PruneStale(double nowUtcSeconds, double maxSilenceSeconds)
    {
        var stale = new List<int>();
        foreach (var kv in _active)
        {
            if (nowUtcSeconds - kv.Value.LastHeartbeatUtcSeconds > maxSilenceSeconds)
                stale.Add(kv.Key);
        }
        foreach (var id in stale)
            _active.Remove(id);
    }
}
