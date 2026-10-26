using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fhsm.Kernel.Data;

namespace Hrot.Editor.Debugging;

/// <summary>
/// ⭐ <c>CE-1323</c> (Behaviour Debugging §9, DBG-9) — a <see cref="BehaviourStepPredicateDto"/> made concrete for ONE behaviour: the
/// state indices, transition triples, event id or BTree node indices its trace records carry. Built from the registry
/// (<see cref="MachineMetadata"/> + <see cref="HsmDefinitionBlob"/>, or the BTree blob's node metadata) — never from an open document, so
/// a behaviour built in C# resolves as well as an authored one.
/// </summary>
public sealed class ResolvedStep
{
    public const ushort Any = 0xFFFF;

    public BehaviourStepKind Kind { get; init; }

    /// <summary>StateEnter / StateExit: the state(s); InsideEnter: every state inside (the composite itself excluded).</summary>
    public HashSet<ushort> States { get; init; } = new();

    /// <summary>TransitionFires: (from, to, event), <see cref="Any"/> = any.</summary>
    public List<(ushort From, ushort To, ushort Event)> Transitions { get; init; } = new();

    /// <summary>EventHandled: the event.</summary>
    public ushort EventId { get; init; } = Any;

    /// <summary>NodeRuns: the BTree node indices.</summary>
    public HashSet<ushort> Nodes { get; init; } = new();

    /// <summary>Names for the hit's sentence.</summary>
    public MachineMetadata? Metadata { get; init; }

    /// <summary>"(or its twin)" when another transition has the same two ends and event (the trace cannot tell them apart).</summary>
    public bool Twin { get; init; }

    public string StateName(ushort i) => Metadata?.GetStateName(i) ?? $"State_{i}";
    public string EventName(ushort i) => Metadata?.GetEventName(i) ?? $"Event_{i}";
}

/// <summary>⭐ <c>CE-1323</c> — <see cref="ResolvedStep"/>s, from the registry. The API validates through the same call, so what it accepts
/// is what the evaluator can match.</summary>
public static class BehaviourStepResolver
{
    /// <summary>Resolves <paramref name="step"/> for behaviour <paramref name="hash"/>; null and <paramref name="why"/> when it cannot
    /// (unknown behaviour, wrong engine for the kind, a name not in it — the message lists what is).</summary>
    public static ResolvedStep? Resolve(BehaviourStepPredicateDto step, BehaviorRegistry registry, int hash, out string why)
    {
        why = "";
        if (!registry.TryGetDefinition(hash, out var def))
        { why = $"behaviour '{step.Behaviour}' is not registered"; return null; }

        if (step.Kind == BehaviourStepKind.NodeRuns)
        {
            if (!registry.TryGetTreeBlob(hash, out var tree) || tree.DebugMetadata is not { } nodes)
            { why = $"'{step.Behaviour}' is not a behaviour tree"; return null; }
            return ResolveNodes(step, nodes, out why);
        }

        if (def.HsmDefinition is not { } blob || def.HsmMetadata is not { } meta)
        { why = $"'{step.Behaviour}' is not a state machine"; return null; }
        return ResolveHsm(step, blob, meta, out why);
    }

    /// <summary>A BTree node step against the tree's node metadata (index → VisualId).</summary>
    public static ResolvedStep? ResolveNodes(BehaviourStepPredicateDto step, Fbt.NodeDebugMetadata[] nodes, out string why)
    {
        why = "";
        var set = new HashSet<ushort>();
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i] is { } n && Guid.TryParse(n.VisualId, out var v) && v == step.Node) set.Add((ushort)i);
        if (set.Count == 0) { why = $"node {step.Node:N} is not in '{step.Behaviour}'"; return null; }
        return new ResolvedStep { Kind = step.Kind, Nodes = set };
    }

    /// <summary>An HSM step against the machine's blob and metadata.</summary>
    public static ResolvedStep? ResolveHsm(BehaviourStepPredicateDto step, HsmDefinitionBlob blob, MachineMetadata meta, out string why)
    {
        why = "";
        switch (step.Kind)
        {
            case BehaviourStepKind.StateEnter:
            case BehaviourStepKind.StateExit:
            {
                var states = StatesOf(step, meta, out why);
                return states == null ? null : new ResolvedStep { Kind = step.Kind, States = states, Metadata = meta };
            }
            case BehaviourStepKind.InsideEnter:
            {
                var outer = StatesOf(step, meta, out why);
                if (outer == null) return null;
                var inside = new HashSet<ushort>();
                var all = blob.States;
                foreach (ushort s in outer)
                {
                    int slot = -1;
                    if (step.Region >= 0)
                    {
                        if (step.Region >= all[s].RegionCount)
                        { why = $"'{meta.GetStateName(s)}' has {all[s].RegionCount} region(s); region {step.Region} does not exist"; return null; }
                        var region = blob.Regions[all[s].RegionStartIndex + step.Region];
                        slot = all[region.InitialStateIndex].RegionSlot;
                    }
                    for (ushort i = 0; i < all.Length; i++)
                        if (i != s && IsInside(all, i, s) && (slot < 0 || all[i].RegionSlot == slot)) inside.Add(i);
                }
                if (inside.Count == 0) { why = $"nothing is inside '{step.State}'"; return null; }
                return new ResolvedStep { Kind = step.Kind, States = inside, Metadata = meta };
            }
            case BehaviourStepKind.TransitionFires:
                return Transition(step, blob, meta, out why);
            case BehaviourStepKind.EventHandled:
            {
                if (EventOf(step.Event, meta) is not { } e)
                { why = $"event '{step.Event}' is not in '{step.Behaviour}'; its events: {List(meta.EventNames.Values)}"; return null; }
                return new ResolvedStep { Kind = step.Kind, EventId = e, Metadata = meta };
            }
        }
        why = $"unknown kind {step.Kind}";
        return null;
    }

    private static ResolvedStep? Transition(BehaviourStepPredicateDto step, HsmDefinitionBlob blob, MachineMetadata meta, out string why)
    {
        why = "";
        var defs = blob.Transitions;
        if (step.Element != Guid.Empty && meta.TransitionVisualIds.FirstOrDefault(kv => kv.Value == step.Element) is { Value: var vid } hit
            && vid != Guid.Empty)
        {
            int k = hit.Key;
            (ushort, ushort, ushort) triple = k < defs.Length
                ? (defs[k].SourceStateIndex, defs[k].TargetStateIndex, defs[k].EventId)
                : (ResolvedStep.Any, blob.GlobalTransitions[k - defs.Length].TargetStateIndex, blob.GlobalTransitions[k - defs.Length].EventId);
            int same = 0;
            for (int i = 0; i < defs.Length; i++)
                if ((defs[i].SourceStateIndex, defs[i].TargetStateIndex, defs[i].EventId) == triple) same++;
            return new ResolvedStep { Kind = step.Kind, Transitions = { triple }, Metadata = meta, Twin = same > 1 };
        }

        HashSet<ushort>? from = null;
        if (!string.IsNullOrEmpty(step.From) && (from = Named(step.From, meta)) == null)
        { why = $"state '{step.From}' is not in '{step.Behaviour}'; its states: {List(meta.StateNames.Values)}"; return null; }
        var to = string.IsNullOrEmpty(step.To) ? null : Named(step.To, meta);
        if (to == null)
        { why = $"state '{step.To}' is not in '{step.Behaviour}'; its states: {List(meta.StateNames.Values)}"; return null; }
        ushort evt = ResolvedStep.Any;
        if (!string.IsNullOrEmpty(step.Event))
        {
            if (EventOf(step.Event, meta) is not { } e)
            { why = $"event '{step.Event}' is not in '{step.Behaviour}'; its events: {List(meta.EventNames.Values)}"; return null; }
            evt = e;
        }
        var list = new List<(ushort, ushort, ushort)>();
        foreach (var t in to)
        {
            if (from == null) list.Add((ResolvedStep.Any, t, evt));
            else foreach (var f in from) list.Add((f, t, evt));
        }
        return new ResolvedStep { Kind = step.Kind, Transitions = list, Metadata = meta };
    }

    /// <summary>The state(s) a step names: its element id when given, else every state of that name.</summary>
    private static HashSet<ushort>? StatesOf(BehaviourStepPredicateDto step, MachineMetadata meta, out string why)
    {
        why = "";
        if (step.Element != Guid.Empty)
            foreach (var (i, id) in meta.StateStableIds)
                if (id == step.Element) return new HashSet<ushort> { i };
        if (!string.IsNullOrEmpty(step.State) && Named(step.State, meta) is { } set) return set;
        why = $"state '{step.State}' is not in '{step.Behaviour}'; its states: {List(meta.StateNames.Values)}";
        return null;
    }

    private static HashSet<ushort>? Named(string name, MachineMetadata meta)
    {
        var set = new HashSet<ushort>();
        foreach (var (i, n) in meta.StateNames)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) set.Add(i);
        return set.Count > 0 ? set : null;
    }

    private static ushort? EventOf(string? name, MachineMetadata meta)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var (i, n) in meta.EventNames)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    /// <summary><paramref name="i"/> lies under <paramref name="outer"/> (its parent chain reaches it).</summary>
    private static bool IsInside(ReadOnlySpan<StateDef> all, ushort i, ushort outer)
    {
        for (int guard = 0, p = all[i].ParentIndex; p != 0xFFFF && p < all.Length && guard < 64; p = all[p].ParentIndex, guard++)
            if (p == outer) return true;
        return false;
    }

    private static string List(IEnumerable<string> names)
    {
        var l = names.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        return l.Count == 0 ? "(none)" : string.Join(", ", l.Take(40)) + (l.Count > 40 ? ", …" : "");
    }
}
