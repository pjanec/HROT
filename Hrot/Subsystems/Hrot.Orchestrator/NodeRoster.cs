using Fdp.Core;

namespace Hrot.Orchestrator;

/// <summary>Active nodes keyed by DDS node id; stale entries removed by <see cref="ClusterMaster"/>.</summary>
public sealed class NodeRoster
{
    private readonly Dictionary<int, NodeHealthProfile> _active = new();
    private readonly List<int> _staleBuffer = new();

    public IReadOnlyDictionary<int, NodeHealthProfile> ActiveNodes => _active;

    /// <summary>P3: the ids of present nodes whose declared role mask intersects <paramref name="role"/>
    /// (<c>mask &amp; role != 0</c>). Because <see cref="NodeRole"/> is <c>[Flags]</c>, a multi-role node
    /// (e.g. <c>MuscleGround|Perception</c>) matches a query for EITHER role. The prerequisite membership
    /// query for the cross-node construction barrier (present nodes that must initialise a copy).</summary>
    public IEnumerable<int> NodesWithRole(NodeRole role)
    {
        foreach (var kv in _active)
            if ((kv.Value.Roles & role) != 0)
                yield return kv.Key;
    }

    /// <summary>CE-285 (C-cap): does the present node <paramref name="nodeId"/> advertise the capability
    /// <paramref name="token"/>? Membership test over the gathered token set; an unknown node or unknown
    /// token reads <c>false</c> (absence = unsupported), exactly the OpenGL-extension semantics (AQ-70 §Q70-B).
    /// The creator's reliable-init wait-set includes only nodes for which <c>Supports(nodeId,
    /// CapabilityTokens.ReliableInit)</c> is true.</summary>
    public bool Supports(int nodeId, string token)
        => _active.TryGetValue(nodeId, out var p) && p.Capabilities.Contains(token);

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
