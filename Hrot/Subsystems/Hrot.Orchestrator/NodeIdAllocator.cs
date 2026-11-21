using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ <c>C1</c> (Q91 §4-O, <c>R-4009</c>/<c>R-4016</c>) — the master's node-id authority for its session.
/// <list type="bullet">
///   <item>A process is identified by its computer name + process id; asking again gets the SAME answer.</item>
///   <item>A forced id is granted when no OTHER live process holds it, refused (with the holder named) when one does.</item>
///   <item>Otherwise the preferred id if free, else the next DYNAMIC id — from <see cref="FirstDynamicId"/>, never reused in the
///     session (a possession or ownership record may still name an id after its process is gone).</item>
/// </list>
/// ⚠ Pure: the caller says which ids are live (the roster) — the allocator owns no timing.
/// </summary>
public sealed class NodeIdAllocator
{
    /// <summary>Clear of every configured range (runner base + offsets 1..700).</summary>
    public const int FirstDynamicId = 10_000;

    private readonly Dictionary<(string Computer, int Pid), int> _granted = new();
    private readonly Dictionary<int, (string Computer, int Pid)> _holderOf = new();
    private int _next = FirstDynamicId;

    /// <summary>The answer for one request.</summary>
    public readonly record struct Outcome(NodeId NodeId, bool Refused, string Reason);

    /// <param name="isLive">Whether a node id is currently live in the roster.</param>
    /// <param name="subsystemName">⭐ CE-4331 ⑥ (§9b) — the asking node's kind: with no free preferred id it gets the LOWEST free id of the kind's range
    /// (<see cref="NodeIdRanges"/>) — so a relaunched cluster gets the same ids and a replacement computer the vacant one. Null / a full range ⇒ a dynamic id.</param>
    public Outcome Grant(string computerName, int processId, int forcedId, int preferredId, Func<int, bool> isLive, string? subsystemName = null)
    {
        var who = ((computerName ?? string.Empty).ToUpperInvariant(), processId);
        if (_granted.TryGetValue(who, out var already)) return new Outcome((Fdp.Core.NodeId)(already), false, "already granted");

        if (forcedId > 0)
        {
            if (_holderOf.TryGetValue(forcedId, out var holder) && holder != who && isLive(forcedId))
                return new Outcome((Fdp.Core.NodeId)(0), true,
                    $"node id {forcedId} is held by a live process ({holder.Computer} pid {holder.Pid})");
            return Record(who, forcedId);
        }

        if (preferredId > 0 && (!_holderOf.TryGetValue(preferredId, out var prev) || prev == who) && !isLive(preferredId))
            return Record(who, preferredId);

        if (!string.IsNullOrEmpty(subsystemName))
        {
            int first = NodeIdRanges.OffsetOf(subsystemName);
            for (int id = first; id < first + NodeIdRanges.Width; id++)
                if (!_holderOf.ContainsKey(id) && !isLive(id)) return Record(who, id);   // ⛔ never an id granted earlier in the session (its records may still name it)
        }
        while (_holderOf.ContainsKey(_next) || isLive(_next)) _next++;
        return Record(who, _next++);
    }

    private Outcome Record((string, int) who, int id)
    {
        _granted[who] = id;
        _holderOf[id] = who;
        return new Outcome((Fdp.Core.NodeId)(id), false, string.Empty);
    }
}
