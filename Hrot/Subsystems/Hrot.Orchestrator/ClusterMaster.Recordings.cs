using System;
using System.Collections.Generic;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ CE-4330 ③ fact ③ (docs/DESIGN_Recording_Storage.md §7.2) — which node holds which exercise: each node's heartbeat carries the exercise recordings in its OWN staging
/// (<c>NodeHeartbeatEvent.LocalRecordings</c>, <c>"&lt;exerciseGuid&gt;:&lt;totalBytes&gt;"</c> each); the master keeps the latest set per node id, so it knows who needs an import (and which nodes an archive can be asked of).
/// ⛔ The heartbeat is frequent: a beat whose list equals the last one costs one comparison and allocates nothing — the set is re-parsed only when the list CHANGED.
/// </summary>
public sealed partial class ClusterMaster
{
    private sealed class NodeHoldings
    {
        public List<string>? Raw;
        public readonly Dictionary<Guid, long> Bytes = new();
    }

    private readonly Dictionary<int, NodeHoldings> _holdings = new();

    /// <summary>Remembers the recordings node <paramref name="nodeId"/> reported on its heartbeat (null / empty = none).</summary>
    private void NoteLocalRecordings(int nodeId, List<string>? reported)
    {
        if (!_holdings.TryGetValue(nodeId, out var h))
        {
            if (reported == null || reported.Count == 0) return;   // nothing held, nothing known: no entry
            _holdings[nodeId] = h = new NodeHoldings();
        }
        int hn = h.Raw?.Count ?? 0, rn = reported?.Count ?? 0;
        bool same = hn == rn;
        for (int i = 0; same && i < rn; i++) same = string.Equals(h.Raw![i], reported![i], StringComparison.Ordinal);
        if (same) return;                                          // the common beat: nothing changed
        h.Raw = rn == 0 ? null : new List<string>(reported!);
        h.Bytes.Clear();
        for (int i = 0; i < rn; i++)
        {
            string entry = reported![i];
            int colon = entry.IndexOf(':');
            if (colon <= 0 || !Guid.TryParse(entry.AsSpan(0, colon), out var id)) continue;   // a malformed entry is skipped, not a throw
            long.TryParse(entry.AsSpan(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long bytes);
            h.Bytes[id] = bytes;
        }
    }

    /// <summary>
    /// The nodes that report holding exercise <paramref name="exerciseId"/> in their own staging, ascending by node id — only nodes still in the roster (a node that left holds nothing the master can ask of).
    /// A new list per call (the callers are the storage job's decisions, not a frame loop).
    /// </summary>
    public IReadOnlyList<Fdp.Core.NodeId> NodesHolding(Guid exerciseId)
    {
        var nodes = new List<Fdp.Core.NodeId>();
        foreach (var kv in _holdings)
            if (kv.Value.Bytes.ContainsKey(exerciseId) && _roster.ActiveNodes.ContainsKey(kv.Key)) nodes.Add((Fdp.Core.NodeId)kv.Key);
        nodes.Sort((a, b) => a.Value.CompareTo(b.Value));
        return nodes;
    }

    /// <summary>The bytes node <paramref name="nodeId"/> reports for exercise <paramref name="exerciseId"/>, or null when it reports none.</summary>
    public long? HeldBytes(int nodeId, Guid exerciseId)
        => _holdings.TryGetValue(nodeId, out var h) && h.Bytes.TryGetValue(exerciseId, out var b) ? b : null;
}
