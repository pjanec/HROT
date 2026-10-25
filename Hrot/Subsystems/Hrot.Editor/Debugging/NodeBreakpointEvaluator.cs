using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.Debugging;

/// <summary>A BTree node marked as a breakpoint in an open document: the behaviour it belongs to and its node.</summary>
public readonly record struct MarkedNode(string Behaviour, Guid VisualId, string Label);

/// <summary>
/// ⭐⭐ <c>CE-1291</c> (Behaviour Debugging DBG-7) — A BREAKPOINT ON A BTREE NODE BREAKS. 🔴 Measured: <c>IsBreakpoint</c> was stored on the
/// asset node and drawn, but nothing at run time read it (no production caller of <c>BTreeDebugSession.RaiseBreakpointHit</c>).
/// <para>⭐ After the tick, for every unit whose trace ring is armed (a Brain tab views it, or the debug switch / observe-all arms it), the
/// records written since the last frame are read; a node that RAN and is marked ⇒ a hit through the breakpoint manager's existing
/// external-hit path — the same pause and rewind as every other breakpoint (BV-29). Each marked node is one breakpoint in the manager
/// (tag <c>btree:&lt;hash&gt;:&lt;visualId&gt;</c>), so it shows in the one breakpoint list (DBG-6). ⛔ Not inside the kernel: a brain tick cannot
/// be split (BV-31).</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class NodeBreakpointEvaluator : IEcsModuleSystem
{
    private readonly Func<IEnumerable<MarkedNode>> _marked;
    private readonly IDataBreakpointManager _manager;
    private readonly Func<BehaviorRegistry?> _registry;
    private readonly Dictionary<string, BreakpointId> _registered = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _gone = new();
    private readonly Dictionary<Entity, ushort> _readPos = new();

    private readonly Func<int, Fbt.NodeDebugMetadata[]?> _metadataOf;

    /// <param name="metadataOf">Names a running tree's nodes (node index → VisualId); defaults to the registry's tree blob — the ONE
    /// lookup the inspector and the BTree debug session share (<see cref="BehaviorRegistry.TryGetTreeBlob"/>).</param>
    public NodeBreakpointEvaluator(Func<IEnumerable<MarkedNode>> marked, IDataBreakpointManager manager, Func<BehaviorRegistry?> registry,
        Func<int, Fbt.NodeDebugMetadata[]?>? metadataOf = null)
    {
        _marked = marked ?? throw new ArgumentNullException(nameof(marked));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _metadataOf = metadataOf ?? (hash => _registry() is { } r && r.TryGetTreeBlob(hash, out var blob) ? blob.DebugMetadata : null);
    }

    /// <summary>The tag a marked node's breakpoint carries.</summary>
    public static string TagOf(int behaviourHash, Guid visualId) => $"btree:{behaviourHash}:{visualId:N}";

    /// <summary>The marked nodes' breakpoints in the manager, by tag.</summary>
    public IReadOnlyDictionary<string, BreakpointId> Registered => _registered;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        SyncBreakpoints();
        if (_registered.Count == 0) return;
        if (!repo.IsComponentTypeRegistered<BTreeTraceWorkingMemory1024>() || !repo.IsComponentTypeRegistered<BehaviorState>()) return;

        foreach (var unit in repo.Query().With<BTreeTraceWorkingMemory1024>().With<BehaviorState>().Build())
        {
            if (Scan(repo, unit) is { } tag)
            {
                _manager.OnExternalHit(tag, unit);   // pauses and rewinds to before this tick: stop scanning
                return;
            }
        }
    }

    /// <summary>The tag of the first marked node this unit's trace shows running since the last frame, or null.</summary>
    private unsafe string? Scan(EntityRepository repo, Entity unit)
    {
        ref readonly var trace = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(unit);
        ushort end = trace.WritePos;
        if (!_readPos.TryGetValue(unit, out ushort pos)) { _readPos[unit] = end; return null; }   // first sight: from now on
        if (pos == end) return null;
        _readPos[unit] = end;

        int hash = repo.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash;
        if (_metadataOf(hash) is not { Length: > 0 } meta) return null;

        {
            var t = (BTreeTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(
                ref System.Runtime.CompilerServices.Unsafe.AsRef(in trace));
            byte* buf = t->Buffer;
            while (pos != end)
            {
                var rec = (BTreeTraceRecord*)(buf + pos);
                if (rec->OpCode == BTreeTraceOpCode.NodeEvaluated && rec->NodeIndex < meta.Length
                    && Guid.TryParse(meta[rec->NodeIndex].VisualId, out var visual))
                {
                    var tag = TagOf(hash, visual);
                    if (_registered.ContainsKey(tag)) return tag;
                }
                pos = (ushort)((pos + BTreeTraceWorkingMemory1024.RecordStride) % BTreeTraceWorkingMemory1024.PayloadBytes);
            }
        }
        return null;
    }

    /// <summary>Keeps one manager breakpoint per marked node: added when a node is marked, removed when it is not any more.</summary>
    private void SyncBreakpoints()
    {
        _seen.Clear();
        foreach (var m in _marked())
        {
            var tag = TagOf(BehaviorHash.FromName(m.Behaviour), m.VisualId);
            _seen.Add(tag);
            if (!_registered.ContainsKey(tag))
                _registered[tag] = _manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = tag },
                    displayName: $"Node {(m.Label.Length > 0 ? m.Label : m.VisualId.ToString("N")[..8])} in {m.Behaviour}",
                    sourceElementId: m.VisualId);
        }
        _gone.Clear();
        foreach (var tag in _registered.Keys)
            if (!_seen.Contains(tag)) _gone.Add(tag);
        foreach (var tag in _gone)
        {
            _manager.Remove(_registered[tag]);
            _registered.Remove(tag);
        }
    }
}
