using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fhsm.Kernel.Data;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.Debugging;

/// <summary>A BTree node marked as a breakpoint in an open document: the behaviour it belongs to and its node.</summary>
public readonly record struct MarkedNode(string Behaviour, Guid VisualId, string Label);

/// <summary>⭐ <c>CE-1323</c> — an element marked in an open document (a BTree node, an HSM state or transition whose Properties
/// "Breakpoint" is ticked) as the step it means, and the element the canvas draws its dot on.</summary>
public readonly record struct MarkedStep(BehaviourStepPredicateDto Step, Guid SourceElement)
{
    public static MarkedStep Of(MarkedNode n) => new(
        new BehaviourStepPredicateDto { Behaviour = n.Behaviour, Kind = BehaviourStepKind.NodeRuns, Node = n.VisualId, Label = n.Label },
        n.VisualId);
}

/// <summary>
/// ⭐⭐ <c>CE-1291</c> / <c>CE-1323</c> (Behaviour Debugging DBG-7, §9) — BEHAVIOUR BREAKPOINTS BREAK. After the tick, for every unit whose
/// trace rings are armed, the BTree and HSM records written SINCE THE LAST LOOK are matched against every mounted
/// <see cref="BehaviourStepPredicateDto"/> (from the canvas menus, the Brain tab, the API, or a ticked "Breakpoint" in an open document,
/// which this system keeps as one manager breakpoint each); a match is <see cref="IDataBreakpointManager.OnBehaviourStepHit"/> — the
/// same pause and rewind as every other breakpoint (BV-29), honouring the unit filter and the rest of a compound.
/// <para>⭐ "Since the last look" is the point: a continue replays the tick and finds its cursor already at the end, so one arrival stops
/// once (the whole-ring <see cref="TraceBufferScanPredicateDto"/> stopped again every tick). ⭐ A breakpoint scoped to ONE unit arms
/// that unit's traces while it exists (DBG-12) — an unarmed unit writes nothing to read. ⛔ Not inside the kernel: a brain tick
/// cannot be split (BV-31).</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class NodeBreakpointEvaluator : IEcsModuleSystem
{
    private readonly Func<IEnumerable<MarkedStep>> _marked;
    private readonly IDataBreakpointManager _manager;
    private readonly Func<BehaviorRegistry?> _registry;
    private readonly Func<int, NodeDebugMetadata[]?> _metadataOf;
    private readonly Func<int, (HsmDefinitionBlob Blob, MachineMetadata Meta)?> _hsmOf;
    private readonly Func<EntityRepository, Entity, bool, bool>? _arm;
    private readonly Dictionary<string, BreakpointId> _registered = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _gone = new();
    private readonly Dictionary<Entity, ushort> _btreePos = new();
    private readonly Dictionary<Entity, ushort> _hsmPos = new();
    private readonly Dictionary<BreakpointId, (int Hash, object? Def, ResolvedStep? Step)> _resolved = new();
    private readonly HashSet<Entity> _armedHere = new();
    private int _epoch;   // WorldEpoch generation last seen (R-3039)
    private readonly List<(Breakpoint Bp, ResolvedStep Step)> _forUnit = new();

    /// <summary>Why a mounted step resolves to nothing for the behaviour it names (shown by the API / list), by breakpoint.</summary>
    public IReadOnlyDictionary<BreakpointId, string> Unresolved => _unresolved;
    private readonly Dictionary<BreakpointId, string> _unresolved = new();

    /// <param name="metadataOf">Names a running tree's nodes (node index → VisualId); defaults to the registry's tree blob — the ONE
    /// lookup the inspector and the BTree debug session share (<see cref="BehaviorRegistry.TryGetTreeBlob"/>).</param>
    /// <param name="hsmOf">A running machine's blob and names; defaults to the registry's definition.</param>
    /// <param name="arm">Arms (true) / disarms (false) a unit's traces (<c>AiTraceActions.Set</c>, EnableTraceBuffer) and says whether it
    /// changed anything — arming a unit that is already armed (its Brain tab, Debug this unit) returns false, so it is never disarmed
    /// here; null = never arms.</param>
    public NodeBreakpointEvaluator(Func<IEnumerable<MarkedStep>> marked, IDataBreakpointManager manager, Func<BehaviorRegistry?> registry,
        Func<int, NodeDebugMetadata[]?>? metadataOf = null,
        Func<int, (HsmDefinitionBlob Blob, MachineMetadata Meta)?>? hsmOf = null,
        Func<EntityRepository, Entity, bool, bool>? arm = null)
    {
        _marked = marked ?? throw new ArgumentNullException(nameof(marked));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _metadataOf = metadataOf ?? (hash => _registry() is { } r && r.TryGetTreeBlob(hash, out var blob) ? blob.DebugMetadata : null);
        _hsmOf = hsmOf ?? (hash => _registry() is { } r && r.TryGetDefinition(hash, out var d) && d.HsmDefinition is { } b && d.HsmMetadata is { } m
            ? (b, m) : null);
        _arm = arm;
    }

    /// <summary>The CE-1291 form: BTree nodes marked in open documents.</summary>
    public NodeBreakpointEvaluator(Func<IEnumerable<MarkedNode>> marked, IDataBreakpointManager manager, Func<BehaviorRegistry?> registry,
        Func<int, NodeDebugMetadata[]?>? metadataOf = null)
        : this(() => Steps(marked()), manager, registry, metadataOf) { }

    private static IEnumerable<MarkedStep> Steps(IEnumerable<MarkedNode> nodes) { foreach (var n in nodes) yield return MarkedStep.Of(n); }

    /// <summary>The key a marked element's breakpoint is kept under.</summary>
    public static string KeyOf(BehaviourStepPredicateDto s)
        => $"{s.Kind}:{s.Behaviour}:{(s.Kind == BehaviourStepKind.NodeRuns ? s.Node : s.Element):N}";

    /// <summary>The marked elements' breakpoints in the manager, by key.</summary>
    public IReadOnlyDictionary<string, BreakpointId> Registered => _registered;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        // ⭐ R-3039 — the ring cursors and the armed-units set are keyed by Entity; a new world reuses those ids, so a stale cursor would misread another unit's ring
        //   ("first sight" would not fire) and a stale armed entry would never re-arm. Forget them at the world boundary / a restore.
        if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch)) { _btreePos.Clear(); _hsmPos.Clear(); _armedHere.Clear(); }
        SyncMarked();
        var mounted = _manager.MountedBehaviourSteps;
        Arm(repo, mounted);
        if (!repo.IsComponentTypeRegistered<BehaviorState>()) return;
        bool bt = repo.IsComponentTypeRegistered<BTreeTraceWorkingMemory1024>();
        bool hsm = repo.IsComponentTypeRegistered<HsmTraceWorkingMemory1024>();
        if (!bt && !hsm) return;
        if (mounted.Count == 0)
        {
            // ⭐ nothing to stop on — but keep every cursor at the ring's end, so a breakpoint added now sees the NEXT tick's records
            //   (🔴 the rail caught it: the cursors were cleared, the first tick after an add was "first sight" and its records skipped)
            foreach (var unit in repo.Query().With<BehaviorState>().Build())
            {
                if (bt && repo.HasComponent<BTreeTraceWorkingMemory1024>(unit)) _btreePos[unit] = repo.GetComponentRO<BTreeTraceWorkingMemory1024>(unit).WritePos;
                if (hsm && repo.HasComponent<HsmTraceWorkingMemory1024>(unit)) _hsmPos[unit] = repo.GetComponentRO<HsmTraceWorkingMemory1024>(unit).WritePos;
            }
            return;
        }

        foreach (var unit in repo.Query().With<BehaviorState>().Build())
        {
            bool hasBt = bt && repo.HasComponent<BTreeTraceWorkingMemory1024>(unit);
            bool hasHsm = hsm && repo.HasComponent<HsmTraceWorkingMemory1024>(unit);
            if (!hasBt && !hasHsm) continue;
            int hash = repo.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash;
            _forUnit.Clear();
            foreach (var (bp, step) in mounted)
            {
                if (!bp.Enabled || (bp.FilterEntity is { } only && only != unit)) continue;
                if (BehaviorHash.FromName(step.Behaviour) != hash) continue;
                if (Resolve(bp, step, hash) is { } r) _forUnit.Add((bp, r));
            }
            if ((hasBt && ScanBTree(repo, unit)) || (hasHsm && ScanHsm(repo, unit)) || _manager.IsPaused) return;   // paused: stop reading
        }
    }

    // ── the matchers ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private unsafe bool ScanBTree(EntityRepository repo, Entity unit)
    {
        ref readonly var trace = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(unit);
        ushort end = trace.WritePos;
        if (!_btreePos.TryGetValue(unit, out ushort pos)) { _btreePos[unit] = end; return false; }   // first sight: from now on
        if (pos == end) return false;
        _btreePos[unit] = end;
        if (_forUnit.Count == 0) return false;
        var t = (BTreeTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.AsRef(in trace));
        byte* buf = t->Buffer;
        while (pos != end)
        {
            var rec = (BTreeTraceRecord*)(buf + pos);
            if (rec->OpCode == BTreeTraceOpCode.NodeEvaluated)
                foreach (var (bp, step) in _forUnit)
                    if (step.Kind == BehaviourStepKind.NodeRuns && step.Nodes.Contains(rec->NodeIndex)
                        && Hit(bp, unit, $"node {Label(bp)} ran ({rec->Status})")) return true;
            pos = (ushort)((pos + BTreeTraceWorkingMemory1024.RecordStride) % BTreeTraceWorkingMemory1024.PayloadBytes);
        }
        return false;
    }

    private unsafe bool ScanHsm(EntityRepository repo, Entity unit)
    {
        ref readonly var trace = ref repo.GetComponentRO<HsmTraceWorkingMemory1024>(unit);
        ushort end = trace.WritePos;
        if (!_hsmPos.TryGetValue(unit, out ushort pos)) { _hsmPos[unit] = end; return false; }
        if (pos == end) return false;
        _hsmPos[unit] = end;
        if (_forUnit.Count == 0) return false;
        var t = (HsmTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.AsRef(in trace));
        byte* buf = t->Buffer;
        while (pos != end)
        {
            var hdr = (TraceRecordHeader*)(buf + pos);
            switch (hdr->OpCode)
            {
                case TraceOpCode.StateEnter:
                case TraceOpCode.StateExit:
                {
                    ushort s = ((TraceStateChange*)(buf + pos))->StateIndex;
                    bool enter = hdr->OpCode == TraceOpCode.StateEnter;
                    foreach (var (bp, step) in _forUnit)
                    {
                        bool match = step.Kind switch
                        {
                            BehaviourStepKind.StateEnter => enter && step.States.Contains(s),
                            BehaviourStepKind.StateExit => !enter && step.States.Contains(s),
                            BehaviourStepKind.InsideEnter => enter && step.States.Contains(s),
                            _ => false,
                        };
                        if (match && Hit(bp, unit, step.Kind switch
                            {
                                BehaviourStepKind.StateExit => $"left {step.StateName(s)}",
                                BehaviourStepKind.InsideEnter => $"entered {step.StateName(s)} (inside {Label(bp)})",
                                _ => $"entered {step.StateName(s)}",
                            })) return true;
                    }
                    break;
                }
                case TraceOpCode.Transition:
                {
                    var tr = (TraceTransition*)(buf + pos);
                    foreach (var (bp, step) in _forUnit)
                    {
                        if (step.Kind != BehaviourStepKind.TransitionFires) continue;
                        foreach (var (f, to, e) in step.Transitions)
                        {
                            if ((f != ResolvedStep.Any && f != tr->FromState) || to != tr->ToState
                                || (e != ResolvedStep.Any && e != tr->TriggerEventId)) continue;
                            string on = tr->TriggerEventId is 0 or ResolvedStep.Any ? "" : $" on {step.EventName(tr->TriggerEventId)}";
                            if (Hit(bp, unit, $"{step.StateName(tr->FromState)} -> {step.StateName(tr->ToState)} fired{on}"
                                              + (step.Twin ? " (or its twin: same ends and event)" : ""))) return true;
                            break;
                        }
                    }
                    break;
                }
                case TraceOpCode.EventHandled:
                {
                    var ev = (TraceEventHandled*)(buf + pos);
                    foreach (var (bp, step) in _forUnit)
                        if (step.Kind == BehaviourStepKind.EventHandled && step.EventId == ev->EventId
                            && Hit(bp, unit, $"event {step.EventName(ev->EventId)} handled ({(ev->Result == 1 ? "deferred" : "consumed")})"))
                            return true;
                    break;
                }
            }
            pos = (ushort)((pos + HsmTraceWorkingMemory1024.RecordStride) % HsmTraceWorkingMemory1024.PayloadBytes);
        }
        return false;
    }

    private bool Hit(Breakpoint bp, Entity unit, string what)
    {
        _manager.OnBehaviourStepHit(bp, unit, what);
        return _manager.IsPaused;
    }

    private static string Label(Breakpoint bp)
    {
        var step = bp.Condition as BehaviourStepPredicateDto
                   ?? (bp.Condition as CompoundPredicateDto)?.Conditions.Find(c => c is BehaviourStepPredicateDto) as BehaviourStepPredicateDto;
        return step is { Label.Length: > 0 } ? step.Label : step?.State ?? bp.DisplayName;
    }

    /// <summary>The step for this behaviour (once per breakpoint and definition: a hot reload re-resolves).</summary>
    private ResolvedStep? Resolve(Breakpoint bp, BehaviourStepPredicateDto step, int hash)
    {
        object? def = null;
        if (_registry() is { } reg && reg.TryGetDefinition(hash, out var d)) def = d;
        if (_resolved.TryGetValue(bp.Id, out var c) && c.Hash == hash && ReferenceEquals(c.Def, def)) return c.Step;
        ResolvedStep? r;
        string why;
        if (step.Kind == BehaviourStepKind.NodeRuns)
            r = _metadataOf(hash) is { Length: > 0 } nodes ? BehaviourStepResolver.ResolveNodes(step, nodes, out why) : Fail("not a behaviour tree", out why);
        else
            r = _hsmOf(hash) is { } h ? BehaviourStepResolver.ResolveHsm(step, h.Blob, h.Meta, out why) : Fail("not a state machine", out why);
        _resolved[bp.Id] = (hash, def, r);
        if (r == null) _unresolved[bp.Id] = why; else _unresolved.Remove(bp.Id);
        return r;
    }

    private static ResolvedStep? Fail(string message, out string why) { why = message; return null; }

    // ── the marked elements and the arming ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Keeps one manager breakpoint per marked element: added when it is marked, removed when it is not any more.</summary>
    private void SyncMarked()
    {
        _seen.Clear();
        foreach (var m in _marked())
        {
            var key = KeyOf(m.Step);
            _seen.Add(key);
            if (!_registered.ContainsKey(key))
                _registered[key] = _manager.AddBreakpoint(m.Step,
                    displayName: BreakpointConditionSummarizer.Step(m.Step),
                    sourceElementId: m.SourceElement);
        }
        _gone.Clear();
        foreach (var key in _registered.Keys)
            if (!_seen.Contains(key)) _gone.Add(key);
        foreach (var key in _gone)
        {
            _manager.Remove(_registered[key]);
            _registered.Remove(key);
        }
    }

    /// <summary>DBG-12 — a step breakpoint scoped to one unit arms that unit's traces; disarmed (only what this armed) when it goes.</summary>
    private void Arm(EntityRepository repo, IReadOnlyList<(Breakpoint Breakpoint, BehaviourStepPredicateDto Step)> mounted)
    {
        if (_arm == null) return;
        var want = new HashSet<Entity>();
        foreach (var (bp, _) in mounted)
            if (bp.Enabled && bp.FilterEntity is { } u && repo.IsAlive(u)) want.Add(u);
        foreach (var u in want)
            if (!_armedHere.Contains(u) && _arm(repo, u, true)) _armedHere.Add(u);
        _armedHere.RemoveWhere(u =>
        {
            if (want.Contains(u)) return false;
            if (repo.IsAlive(u)) _arm(repo, u, false);   // only what this armed
            return true;
        });
    }
}
