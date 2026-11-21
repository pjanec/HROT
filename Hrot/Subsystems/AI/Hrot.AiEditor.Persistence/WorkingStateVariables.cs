using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence;

/// <summary>
/// ⭐⭐ <c>CE-3193</c> — which variables are WORKING STATE, derived from the asset instead of authored.
///
/// <para>A variable is working state exactly when some binding names it as its <c>WorkingStateTargetField</c>: a stateful
/// node's memory, shared by every node that names the same variable. Working state lives in the block's <c>St</c> half
/// (laid out by the CLR, reached by name, so a type the generator cannot size — fixed buffers — still fits); every other
/// variable is a plain one in <c>In</c> (offsets the generator computes, so actions, the manifest and a host can address it).
/// 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10 ⑦.</para>
///
/// <para>⛔ HISTORY — this was the authored pair <c>Role</c> (<c>Input</c>/<c>State</c>) + <c>Scope</c> (<c>Node</c>/<c>Behavior</c>)
/// on every variable. The author never had a reason to choose it (<c>Scope=Node</c> stopped being authorable with CE-435), so
/// it went. An old file's <c>Role</c>/<c>Scope</c> keys are ignored on load.</para>
///
/// <para>⚠ The flag is <see cref="BlackboardVariableDto.IsWorkingState"/>, never persisted. It is set on load
/// (<c>BTreeJsonServices</c> / <c>HsmJsonServices</c>) and again at each generator entry, so an asset edited in memory is
/// re-derived before anything is emitted.</para>
/// </summary>
public static class WorkingStateVariables
{
    /// <summary>The variable names a BTree's bindings name as working state.</summary>
    public static HashSet<string> NamesOf(BehaviorTreeAssetDto dto)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (dto?.Nodes == null) return names;
        foreach (var node in dto.Nodes)
        {
            var b = node switch
            {
                BTreeActionNodeDto a    => a.Action,
                BTreeConditionNodeDto c => c.Condition,
                _                       => null,
            };
            Add(names, b);
        }
        return names;
    }

    /// <summary>The variable names an HSM's bindings (states, transitions, global transitions) name as working state.</summary>
    public static HashSet<string> NamesOf(HsmAssetDto dto)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (dto == null) return names;
        foreach (var s in dto.States ?? new List<StateNodeDto>())
        {
            Add(names, s.OnEntry); Add(names, s.OnExit); Add(names, s.Activity); Add(names, s.Timer);
        }
        foreach (var t in dto.Transitions ?? new List<TransitionNodeDto>())       { Add(names, t.Guard); Add(names, t.Action); }
        foreach (var g in dto.GlobalTransitions ?? new List<GlobalTransitionNodeDto>()) { Add(names, g.Guard); Add(names, g.Action); }
        return names;
    }

    /// <summary>Sets <see cref="BlackboardVariableDto.IsWorkingState"/> on every variable from the bindings. Idempotent.</summary>
    public static BehaviorTreeAssetDto Mark(BehaviorTreeAssetDto dto)
    {
        if (dto?.Blackboard?.Variables == null) return dto!;
        var names = NamesOf(dto);
        foreach (var v in dto.Blackboard.Variables) v.IsWorkingState = names.Contains(v.Name);
        return dto;
    }

    /// <summary>Sets <see cref="HsmBlackboardVariableDto.IsWorkingState"/> on every variable from the bindings. Idempotent.</summary>
    public static HsmAssetDto Mark(HsmAssetDto dto)
    {
        if (dto?.Blackboard?.Variables == null) return dto!;
        var names = NamesOf(dto);
        foreach (var v in dto.Blackboard.Variables) v.IsWorkingState = names.Contains(v.Name);
        return dto;
    }

    private static void Add(HashSet<string> names, BehaviorActionBindingDto? b)
    {
        string? root = BindingPath.Root(b?.WorkingStateTargetField);
        if (!string.IsNullOrEmpty(root)) names.Add(root!);
    }
}
