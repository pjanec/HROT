using Fdp.Core;

namespace Hrot.Orchestrator;

/// <summary>⭐ <c>CE-3354</c> ② — a heartbeat the roster REFUSED because its node name is held by a different live node.</summary>
public sealed record NameConflict(string Name, int HolderNodeId, int RefusedNodeId, string RefusedSubsystem, double LastSeenUtcSeconds);

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

    private readonly Dictionary<(string Name, int NodeId), NameConflict> _conflicts = new();

    /// <summary>⭐ <c>CE-3354</c> ② — the heartbeats refused for a name another LIVE node holds (current ones only: an entry goes when the holder leaves, the refused
    /// node takes another name or falls silent).</summary>
    public IReadOnlyCollection<NameConflict> NameConflicts => _conflicts.Values;

    /// <summary>
    /// ⭐ <c>CE-3354</c> ② — <see cref="Upsert"/> with the NAME-UNIQUENESS rule: a heartbeat whose <see cref="NodeHealthProfile.NodeName"/> is held by a DIFFERENT node id that is
    /// still on the roster is REFUSED — nothing is added or refreshed — and recorded as a <see cref="NameConflict"/>. The same node re-heartbeating (or rejoining under its
    /// own id) is normal; a node whose entry was pruned has freed its name. <paramref name="firstTime"/> is true the first time this (name, node) pair is refused, so the
    /// caller logs it once.
    /// </summary>
    public bool TryAdmit(NodeHealthProfile profile, out NameConflict? conflict, out bool firstTime)
    {
        conflict = null; firstTime = false;
        foreach (var kv in _active)
        {
            if (kv.Key == profile.NodeId) continue;
            if (!string.Equals(kv.Value.NodeName, profile.NodeName, StringComparison.OrdinalIgnoreCase)) continue;
            var key = (profile.NodeName.ToLowerInvariant(), profile.NodeId);
            firstTime = !_conflicts.ContainsKey(key);
            conflict = new NameConflict(profile.NodeName, kv.Key, profile.NodeId, profile.SubsystemName, profile.LastHeartbeatUtcSeconds);
            _conflicts[key] = conflict;
            return false;
        }
        foreach (var k in _conflicts.Keys.Where(k => k.NodeId == profile.NodeId).ToList()) _conflicts.Remove(k);   // it holds a free name now
        Upsert(profile);
        return true;
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
        // a refusal lasts while the refused node keeps heartbeating (it is re-recorded each time) and the holder is still here
        foreach (var k in _conflicts.Where(c => nowUtcSeconds - c.Value.LastSeenUtcSeconds > maxSilenceSeconds
                                               || !_active.ContainsKey(c.Value.HolderNodeId)).Select(c => c.Key).ToList())
            _conflicts.Remove(k);
    }
}
