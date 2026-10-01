using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐ <b><c>CE-423</c> — a <c>Role=State</c> variable that would get NO storage, named instead of skipped.</b>
///
/// <para>
/// 🔴 Both bridge emitters skip a STANDALONE <c>Role=State</c> variable at <see cref="WorkingStateScope.Node"/>
/// (<c>BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray</c>'s standalone pass, <c>HsmBridgeEmitCore</c>'s State pass): the
/// Node key folds <c>assetId ++ nodeVisualId</c> and ignores the name, so a variable no node binds has nothing to key off.
/// That is sound — ⛔ the SILENCE is the defect. <c>Node</c> is the enum's default (value 0, omitted on save), so a
/// hand-edited or older file that says <c>"Role": "State"</c> and no scope lands here with no slot and no message.
/// <c>CE-435</c> closed the editor path (the model forces <c>Behavior</c>) and promised this check; it was not built.
/// </para>
///
/// <para>
/// ⭐ <b>Node-BOUND is legal and is not reported</b> — a composed node's working state (<c>AutoManagedVariables</c>) is a
/// <c>State</c>/<c>Node</c> variable named by the node's <c>WorkingStateTargetField</c> (else <c>ExpressionTargetField</c>),
/// exactly the variable <c>BTreeBridgeEmitCore</c>'s node-driven loop keys. The HSM has no node-bound State path at all.
/// </para>
/// </summary>
public static class StateVariableStorage
{
    /// <summary>The message both generators report, so the two tiers say the same thing.</summary>
    public static string Describe(string variableName)
        => $"State variable '{variableName}' has Scope=Node but no node binds it, so it gets no storage. " +
           "Set its Scope to Behavior (the only authorable scope).";

    /// <summary>BTree: the standalone <c>State</c>/<c>Node</c> variables — the ones no stateful node binds.</summary>
    public static IReadOnlyList<string> UnstoredStateVariables(BehaviorTreeAssetDto dto)
    {
        var vars = dto.Blackboard?.Variables;
        if (vars == null || vars.Count == 0) return Array.Empty<string>();

        // ⭐ The SAME rule as the node-driven loop: a stateful shape, scoped by its working-state variable,
        //   else its param field (BTreeBridgeEmitCore.StatefulScopeVariable).
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in dto.Nodes)
        {
            string? scopeVar = node switch
            {
                BTreeActionNodeDto a when a.Action != null && IsStateful(a.DelegateShape)
                    => BTreeBridgeEmitCore.StatefulScopeVariable(a.Action),
                BTreeConditionNodeDto c when c.Condition != null && IsStateful(c.DelegateShape)
                    => BTreeBridgeEmitCore.StatefulScopeVariable(c.Condition),
                _ => null,
            };
            if (!string.IsNullOrEmpty(scopeVar)) bound.Add(scopeVar!);
        }

        var unstored = new List<string>();
        foreach (var v in vars)
            if (v.Role == BlackboardVariableRole.State && v.Scope == WorkingStateScope.Node && !bound.Contains(v.Name))
                unstored.Add(v.Name);
        return unstored;
    }

    /// <summary>HSM: every <c>State</c>/<c>Node</c> variable — <c>HsmBridgeEmitCore</c> keeps only <c>Behavior</c>.</summary>
    public static IReadOnlyList<string> UnstoredStateVariables(HsmAssetDto dto)
    {
        var vars = dto.Blackboard?.Variables;
        if (vars == null || vars.Count == 0) return Array.Empty<string>();

        var unstored = new List<string>();
        foreach (var v in vars)
            if (v.Role == BlackboardVariableRole.State && v.Scope == WorkingStateScope.Node)
                unstored.Add(v.Name);
        return unstored;
    }

    private static bool IsStateful(BTreeDelegateShapeDto shape)
        => shape is BTreeDelegateShapeDto.ThreeParamReusableStateful or BTreeDelegateShapeDto.AiPrimitiveTickCore;
}
