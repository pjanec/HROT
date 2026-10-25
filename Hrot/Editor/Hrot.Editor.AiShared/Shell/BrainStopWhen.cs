using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>One thing the Brain tab offers to stop on: what it says, and the step it sets.</summary>
public sealed record StopChoice(BehaviourStepKind Kind, string Label, BehaviourStepPredicateDto Step);

/// <summary>
/// ⭐ <c>CE-1323</c> (Behaviour Debugging §9; user: <i>"not from the editor only - form the UI on the brain node"</i>) — the Brain tab's
/// <b>Stop when…</b>: every step the unit's RUNNING behaviour can take — its states (entered / left / anything inside), its transitions,
/// its events, or its BTree nodes — read from the registry (a C#-built commander has no document), set on THIS unit by default, through
/// the one breakpoint manager (the same breakpoint the canvas and the API set: its dot, its row in the one list).
/// </summary>
public static class BrainStopWhen
{
    /// <summary>The editor's breakpoint manager (set by the host that has one; null = the section says there is none).</summary>
    public static Func<IDataBreakpointManager?>? Manager { get; set; }

    public static readonly (BehaviourStepKind Kind, string Label)[] Kinds =
    {
        (BehaviourStepKind.StateEnter, "it enters a state"),
        (BehaviourStepKind.StateExit, "it leaves a state"),
        (BehaviourStepKind.InsideEnter, "anything inside a state is entered"),
        (BehaviourStepKind.TransitionFires, "a transition fires"),
        (BehaviourStepKind.EventHandled, "it handles an event"),
        (BehaviourStepKind.NodeRuns, "a tree node runs"),
    };

    /// <summary>The steps behaviour <paramref name="name"/> offers, by kind (pure: the rail reads it). Empty for a kind it has none of.</summary>
    public static IReadOnlyList<StopChoice> Choices(string name, BehaviorDefinition? def, BehaviourStepKind kind)
    {
        var list = new List<StopChoice>();
        if (def == null) return list;
        if (kind == BehaviourStepKind.NodeRuns)
        {
            if (def.BTreeInterpreter?.Blob?.DebugMetadata is { } nodes)
                for (int i = 0; i < nodes.Length; i++)
                    if (nodes[i] is { } n && Guid.TryParse(n.VisualId, out var v))
                    {
                        string label = n.Label.Length > 0 ? n.Label : $"node {i}";
                        list.Add(new StopChoice(kind, label, new BehaviourStepPredicateDto { Behaviour = name, Kind = kind, Node = v, Label = label }));
                    }
            return list;
        }
        if (def.HsmDefinition is not { } blob || def.HsmMetadata is not { } meta) return list;
        var states = blob.States;
        switch (kind)
        {
            case BehaviourStepKind.StateEnter:
            case BehaviourStepKind.StateExit:
            case BehaviourStepKind.InsideEnter:
                foreach (var (i, stateName) in meta.StateNames.OrderBy(kv => kv.Key))
                {
                    if (i >= states.Length || stateName.StartsWith("__", StringComparison.Ordinal)) continue;
                    bool composite = states[i].ChildCount > 0;
                    if (kind == BehaviourStepKind.InsideEnter && !composite) continue;
                    Guid element = meta.StateStableIds.TryGetValue(i, out var sid) ? sid : Guid.Empty;
                    list.Add(Step(name, kind, stateName, element, s => s.State = stateName));
                    if (kind == BehaviourStepKind.InsideEnter)
                        for (int r = 0; r < states[i].RegionCount && states[i].RegionCount > 1; r++)
                        {
                            int region = r;
                            list.Add(Step(name, kind, $"{stateName} / region {r}", element, s => { s.State = stateName; s.Region = region; }));
                        }
                }
                break;
            case BehaviourStepKind.TransitionFires:
            {
                var defs = blob.Transitions;
                for (int k = 0; k < defs.Length; k++)
                {
                    string from = meta.GetStateName(defs[k].SourceStateIndex), to = meta.GetStateName(defs[k].TargetStateIndex);
                    string? on = defs[k].EventId != 0 && meta.EventNames.TryGetValue(defs[k].EventId, out var e) ? e : null;
                    Guid element = meta.TransitionVisualIds.TryGetValue((ushort)k, out var vid) ? vid : Guid.Empty;
                    list.Add(Step(name, kind, $"{from} -> {to}{(on != null ? $" on {on}" : "")}", element, s => { s.From = from; s.To = to; s.Event = on; }));
                }
                var globals = blob.GlobalTransitions;
                for (int g = 0; g < globals.Length; g++)
                {
                    string to = meta.GetStateName(globals[g].TargetStateIndex);
                    string? on = globals[g].EventId != 0 && meta.EventNames.TryGetValue(globals[g].EventId, out var e) ? e : null;
                    Guid element = meta.TransitionVisualIds.TryGetValue((ushort)(defs.Length + g), out var vid) ? vid : Guid.Empty;
                    list.Add(Step(name, kind, $"any state -> {to}{(on != null ? $" on {on}" : "")}", element, s => { s.To = to; s.Event = on; }));
                }
                break;
            }
            case BehaviourStepKind.EventHandled:
                foreach (var (_, ev) in meta.EventNames.OrderBy(kv => kv.Value, StringComparer.Ordinal))
                    list.Add(Step(name, kind, ev, Guid.Empty, s => s.Event = ev));
                break;
        }
        return list;
    }

    private static StopChoice Step(string behaviour, BehaviourStepKind kind, string label, Guid element, Action<BehaviourStepPredicateDto> fill)
    {
        var s = new BehaviourStepPredicateDto { Behaviour = behaviour, Kind = kind, Element = element, Label = label };
        fill(s);
        return new StopChoice(kind, label, s);
    }

    /// <summary>The behaviour-step breakpoints on <paramref name="behaviour"/> that apply to <paramref name="unit"/> (its own, and the
    /// ones on every unit).</summary>
    public static IEnumerable<Breakpoint> On(IDataBreakpointManager manager, string behaviour, Entity unit)
        => manager.AllBreakpoints.Where(bp => StepOf(bp.Condition) is { } s && s.Behaviour == behaviour
                                              && (bp.FilterEntity == null || bp.FilterEntity == unit));

    private static BehaviourStepPredicateDto? StepOf(SearchPredicateDto? c) => c switch
    {
        BehaviourStepPredicateDto s => s,
        CompoundPredicateDto cp     => cp.Conditions.OfType<BehaviourStepPredicateDto>().FirstOrDefault(),
        _                           => null,
    };

    // ── the section ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static int _kind;
    private static int _choice;
    private static bool _thisUnitOnly = true;

    /// <summary>Draws the section for <paramref name="unit"/> running <paramref name="behaviour"/>.</summary>
    public static void Draw(Entity unit, string behaviour, BehaviorDefinition? def, Func<Entity, string> unitLabel)
    {
        if (Manager?.Invoke() is not { } manager) { ImGui.TextDisabled("No breakpoints on this host."); return; }
        var kinds = Kinds.Where(k => Choices(behaviour, def, k.Kind).Count > 0).ToArray();
        if (kinds.Length == 0) { ImGui.TextDisabled($"{behaviour} has nothing to stop on."); return; }
        _kind = Math.Clamp(_kind, 0, kinds.Length - 1);
        ImGui.TextDisabled("Stop when");
        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo("##stopkind", ref _kind, kinds.Select(k => k.Label).ToArray(), kinds.Length)) _choice = 0;
        var choices = Choices(behaviour, def, kinds[_kind].Kind);
        _choice = Math.Clamp(_choice, 0, Math.Max(0, choices.Count - 1));
        ImGui.SetNextItemWidth(260f);
        ImGui.Combo("##stopwhat", ref _choice, choices.Select(c => c.Label).ToArray(), choices.Count);
        ImGui.Checkbox("only this unit", ref _thisUnitOnly);
        ImGui.SameLine();
        if (ImGui.Button("Add breakpoint") && choices.Count > 0)
        {
            var step = choices[_choice].Step;
            manager.AddBreakpoint(step, _thisUnitOnly ? unit : null, 1,
                BreakpointConditionSummarizer.Step(step) + (_thisUnitOnly ? $" · {unitLabel(unit)}" : ""),
                step.Element != Guid.Empty ? step.Element : step.Node != Guid.Empty ? step.Node : null);
        }
        Breakpoint? remove = null;
        foreach (var bp in On(manager, behaviour, unit))
        {
            ImGui.PushID(bp.Id.ToString());
            if (ImGui.SmallButton("x")) remove = bp;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete this breakpoint");
            ImGui.SameLine();
            string scope = bp.FilterEntity == null ? "every unit" : "this unit";
            ImGui.TextUnformatted($"{BreakpointConditionSummarizer.Summarize(bp.Condition)} — {scope}{(bp.HitCount > 0 ? $", hit {bp.HitCount}x" : "")}"
                                  + (bp.Enabled ? "" : " (disabled)"));
            ImGui.PopID();
        }
        if (remove != null) manager.Remove(remove.Id);
    }
}
