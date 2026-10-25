using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Hsm.Editor.Model;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Hsm.Editor.Debug;

/// <summary>
/// ⭐ <c>CE-1323</c> (Behaviour Debugging §9, DBG-10) — the HSM elements as BEHAVIOUR STEPS (<see cref="BehaviourStepPredicateDto"/>):
/// the one place an editor gesture turns a state, a region, a transition or an event into the condition the evaluator matches. Names
/// AND the element's id are filled, so the evaluator is exact on the authored graph and the API's names resolve the same way.
/// </summary>
public static class HsmBreakpointSteps
{
    public static BehaviourStepPredicateDto Enter(HsmAsset asset, StateNode s)
        => new() { Behaviour = asset.Name, Kind = BehaviourStepKind.StateEnter, State = s.Name, Element = s.StableId, Label = s.Name };

    public static BehaviourStepPredicateDto Exit(HsmAsset asset, StateNode s)
        => new() { Behaviour = asset.Name, Kind = BehaviourStepKind.StateExit, State = s.Name, Element = s.StableId, Label = s.Name };

    /// <summary>Anything inside <paramref name="s"/> entered — or inside its region <paramref name="region"/> (a parallel state).</summary>
    public static BehaviourStepPredicateDto Inside(HsmAsset asset, StateNode s, RegionNode? region = null)
        => new()
        {
            Behaviour = asset.Name, Kind = BehaviourStepKind.InsideEnter, State = s.Name, Element = s.StableId,
            Region = region?.RegionIndex ?? -1, Label = region == null ? s.Name : $"{s.Name} / {region.Name}",
        };

    public static BehaviourStepPredicateDto Fires(HsmAsset asset, TransitionNode t)
        => new()
        {
            Behaviour = asset.Name, Kind = BehaviourStepKind.TransitionFires, From = t.Source.Name, To = t.Target.Name,
            Event = string.IsNullOrEmpty(t.EventName) ? null : t.EventName, Element = t.VisualId,
            Label = $"{t.Source.Name} -> {t.Target.Name}" + (string.IsNullOrEmpty(t.EventName) ? "" : $" on {t.EventName}"),
        };

    public static BehaviourStepPredicateDto Fires(HsmAsset asset, GlobalTransitionNode t)
        => new()
        {
            Behaviour = asset.Name, Kind = BehaviourStepKind.TransitionFires, From = null, To = t.Target.Name,
            Event = string.IsNullOrEmpty(t.EventName) ? null : t.EventName, Element = t.VisualId,
            Label = $"any state -> {t.Target.Name}" + (string.IsNullOrEmpty(t.EventName) ? "" : $" on {t.EventName}"),
        };

    public static BehaviourStepPredicateDto Handled(HsmAsset asset, EventDefinition e)
        => new() { Behaviour = asset.Name, Kind = BehaviourStepKind.EventHandled, Event = e.Name, Label = e.Name };

    /// <summary>The manager's breakpoint for the same step (kind, behaviour, element / event), if one exists — so a menu offers Remove.</summary>
    public static Breakpoint? Existing(IDataBreakpointManager manager, BehaviourStepPredicateDto step)
        => manager.AllBreakpoints.FirstOrDefault(bp => Same(StepOf(bp.Condition), step) && bp.FilterEntity == null);

    /// <summary>The behaviour step a condition holds, alone or in a compound.</summary>
    public static BehaviourStepPredicateDto? StepOf(SearchPredicateDto? c) => c switch
    {
        BehaviourStepPredicateDto s => s,
        CompoundPredicateDto cp     => cp.Conditions.OfType<BehaviourStepPredicateDto>().FirstOrDefault(),
        _                           => null,
    };

    private static bool Same(BehaviourStepPredicateDto? a, BehaviourStepPredicateDto b)
        => a != null && a.Kind == b.Kind && string.Equals(a.Behaviour, b.Behaviour, StringComparison.Ordinal) && a.Region == b.Region
           && (b.Element != Guid.Empty ? a.Element == b.Element : string.Equals(a.Event, b.Event, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The breakpoint items of the HSM canvas's menus — a state, a transition line, an event row. ⭐ <c>CE-1323</c> (DBG-13): ROUTED from the
/// whole-ring <see cref="TraceBufferScanPredicateDto"/> (it refired every tick, checked no behaviour and was reached with state index 0)
/// to <see cref="BehaviourStepPredicateDto"/>; an item whose breakpoint exists offers to remove it.
/// </summary>
public static class HsmBreakpointMenuPopulator
{
    /// <summary>A state's items: on enter, on exit, anything inside (a composite; each region of a parallel state), and the conditional.</summary>
    /// <param name="onOpenConditionalInspector">Invoked after a conditional breakpoint is created, so the caller can open the Details
    /// Inspector for the user to configure its second condition.</param>
    public static void PopulateStateMenu(
        HsmAsset asset,
        StateNode state,
        IContextMenuBuilder builder,
        IDataBreakpointManager manager,
        Action<BreakpointId, SearchPredicateDto>? onOpenConditionalInspector = null)
    {
        var sub = builder.BeginSubmenu("Add Breakpoint");
        Item(sub, manager, "Break on Enter", HsmBreakpointSteps.Enter(asset, state), state.StableId);
        Item(sub, manager, "Break on Exit", HsmBreakpointSteps.Exit(asset, state), state.StableId);
        if (state.Children.Count > 0)
        {
            Item(sub, manager, "Break when anything inside is entered", HsmBreakpointSteps.Inside(asset, state), state.StableId);
            foreach (var region in state.RegionNodes)
                Item(sub, manager, $"Break when anything in region '{region.Name}' is entered", HsmBreakpointSteps.Inside(asset, state, region),
                    state.StableId);
        }
        sub.EndSubmenu();

        builder.AddItem("Add Conditional Data Breakpoint...", () =>
        {
            // Branch A (read-only): the state entered; Branch B: a condition for the user to configure
            var compound = new CompoundPredicateDto
            {
                Operator             = LogicalOperator.And,
                Conditions           = new List<SearchPredicateDto> { HsmBreakpointSteps.Enter(asset, state), new BehaviorParamPredicateDto() },
                ReadOnlyChildIndices = new List<int> { 0 },
            };
            var bpId = manager.AddBreakpoint(compound, displayName: $"{state.Name} entered, if …", sourceElementId: state.StableId);
            onOpenConditionalInspector?.Invoke(bpId, compound);
        });
    }

    /// <summary>A transition line's item: break when it fires.</summary>
    public static void PopulateTransitionMenu(HsmAsset asset, TransitionNode transition, IContextMenuBuilder builder, IDataBreakpointManager manager)
        => Item(builder, manager, "Break when it fires", HsmBreakpointSteps.Fires(asset, transition), transition.VisualId);

    /// <summary>An event's item: break when the machine takes it off its queue.</summary>
    public static void PopulateEventMenu(HsmAsset asset, EventDefinition ev, IContextMenuBuilder builder, IDataBreakpointManager manager)
        => Item(builder, manager, "Break when handled", HsmBreakpointSteps.Handled(asset, ev), null);

    /// <summary>Adds <paramref name="step"/>, or removes it when it is already set.</summary>
    private static void Item(IContextMenuBuilder b, IDataBreakpointManager manager, string label, BehaviourStepPredicateDto step, Guid? element)
    {
        if (HsmBreakpointSteps.Existing(manager, step) is { } existing)
            b.AddItem($"Remove breakpoint: {BreakpointConditionSummarizer.Step(step)}", () => manager.Remove(existing.Id));
        else
            b.AddItem(label, () => manager.AddBreakpoint(step, displayName: BreakpointConditionSummarizer.Step(step), sourceElementId: element));
    }
}
