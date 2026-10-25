using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.Behavior;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>One part of a running behaviour beyond its own block: a node's working state, or a hosted child's block.</summary>
/// <param name="Label">The node label (or the hosted child's name).</param>
/// <param name="Parameters">A hosted child's authored inputs; <c>null</c> for a working-state part.</param>
/// <param name="Variables">The part's values.</param>
public sealed record BrainBehaviourPart(string Label, object? Parameters, object? Variables, bool IsHostedChild);

/// <summary>One running behaviour as the Brain tab shows it — the task, the SOP, the paused task or an attached blueprint.</summary>
/// <param name="Parameters">The authored inputs, as their own CLR struct (drawn by the inspector's dump).</param>
/// <param name="Variables">The working values, as their own CLR struct.</param>
/// <param name="HiddenVariableMembers">Root members of <paramref name="Variables"/> the tab does not draw (a blueprint
/// instance's <c>State</c> carries its <c>Cursor</c> and <c>Params</c>, shown elsewhere).</param>
public sealed record RunningBehaviourView(
    SlotOwnerKind Owner, string Name, object? Parameters, object? Variables, IReadOnlyCollection<string> HiddenVariableMembers,
    IReadOnlyList<BrainBehaviourPart> Parts)
{
    /// <summary>What the expander header calls the owner.</summary>
    public string OwnerLabel => Owner switch
    {
        SlotOwnerKind.Task => "Task",
        SlotOwnerKind.Sop => "SOP",
        SlotOwnerKind.PausedTask => "Paused task",
        SlotOwnerKind.InstanceBlueprint => "Blueprint",
        _ => "Behaviour",
    };
}

/// <summary>
/// ⭐⭐ <c>CE-1315</c> (Brain View §12b BV-42…BV-44) — every behaviour running on a unit, built from the ONE slot reader
/// (<see cref="OccurrenceSlotCatalog"/>). Pure: groups the slots by owner, splits a behaviour block <c>{ In; St }</c> into its
/// parameters and variables, a blueprint instance's <c>State</c> into <c>Params</c> and the rest, and lists working-state and
/// hosted-child slots as parts. ⛔ Tree-state / HSM-instance slots are kernel internals — not shown here.
/// </summary>
public static class BrainRunningBehaviours
{
    private static readonly SlotOwnerKind[] Order =
        { SlotOwnerKind.Task, SlotOwnerKind.Sop, SlotOwnerKind.PausedTask, SlotOwnerKind.InstanceBlueprint };

    public static IReadOnlyList<RunningBehaviourView> From(IEnumerable<OccurrenceSlotView> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var known = slots.Where(s => s.Owner != SlotOwnerKind.OwnerUnknown && s.Behaviour is not null).ToList();
        var result = new List<RunningBehaviourView>();
        foreach (var owner in Order)
        {
            foreach (var group in known.Where(s => s.Owner == owner).GroupBy(s => s.Behaviour!))
            {
                object? parameters = null, variables = null;
                var hidden = Array.Empty<string>();
                var parts = new List<BrainBehaviourPart>();
                foreach (var s in group)
                {
                    switch (s.Role)
                    {
                        case SlotRole.Params:
                            (parameters, variables) = SplitBlock(s.Boxed);
                            break;
                        case SlotRole.State when owner == SlotOwnerKind.InstanceBlueprint:
                            parameters = Member(s.Boxed, "Params");
                            variables = s.Boxed;
                            hidden = new[] { "Cursor", "Params" };
                            break;
                        case SlotRole.Working when s.Boxed is not null:
                            parts.Add(new BrainBehaviourPart(s.Label ?? $"slot {s.Key:X8}", null, s.Boxed, false));
                            break;
                        case SlotRole.HostedChild:
                            var (p, v) = SplitBlock(s.Boxed);
                            parts.Add(new BrainBehaviourPart($"{s.Label ?? "hosted"} · {s.Behaviour}", p, v, true));
                            break;
                    }
                }
                result.Add(new RunningBehaviourView(owner, group.Key, parameters, variables, hidden, parts));
            }
        }
        return result;
    }

    /// <summary>A behaviour block <c>{ In; St }</c> as (parameters, variables); any other layout is the parameters alone (a C#
    /// behaviour's params DTO).</summary>
    public static (object? Parameters, object? Variables) SplitBlock(object? block)
    {
        if (block is null) return (null, null);
        var t = block.GetType();
        if (t.GetField("In") is { } inF && t.GetField("St") is { } stF) return (inF.GetValue(block), stF.GetValue(block));
        return (block, null);
    }

    private static object? Member(object? value, string name)
        => value?.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
}
