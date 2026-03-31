namespace Hrot.Orchestrator;

/// <summary>Active nodes keyed by DDS node id; stale entries removed by <see cref="ClusterMaster"/>.</summary>
public sealed class NodeRoster
{
    private readonly Dictionary<int, NodeHealthProfile> _active = new();
    private readonly List<int> _staleBuffer = new();

    public IReadOnlyDictionary<int, NodeHealthProfile> ActiveNodes => _active;

    public void Upsert(NodeHealthProfile profile)
    {
        _active[profile.NodeId] = profile;
    }

    public void Remove(int nodeId) => _active.Remove(nodeId);

    /// <summary>Removes nodes whose last heartbeat is older than <paramref name="maxSilenceSeconds"/>.</summary>
    public void PruneStale(double nowUtcSeconds, double maxSilenceSeconds)
    {
        _staleBuffer.Clear();
        foreach (var kv in _active)
        {
            if (nowUtcSeconds - kv.Value.LastHeartbeatUtcSeconds > maxSilenceSeconds)
                _staleBuffer.Add(kv.Key);
        }
        foreach (var id in _staleBuffer)
            _active.Remove(id);
    }
}
