using System;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> — ONE editor-model binding for every action / condition / activity / guard site, in both hosts.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §3, §5.4 (slice 4a).
///
/// <para>The editor twin of <c>Hrot.AiEditor.Persistence.BehaviorActionBindingDto</c>, field for field, so both mappers are
/// straight copies. Eight sites carry it: a BTree action node and condition node; an HSM state's OnEntry / OnExit /
/// Activity / Timer; a transition's and a global transition's Guard / Action. ⛔ It replaces <c>BTreeActionPayload</c>,
/// <c>BTreeConditionPayload</c> and the HSM model's flat <c>*Action</c> / <c>*Function</c> / <c>*Blueprint*</c> fields.</para>
///
/// <para>⭐ A binding names EITHER a C# method (<see cref="MethodFqn"/>) OR a blueprint (<see cref="BlueprintAssetId"/> +
/// <see cref="BlueprintName"/>, the name only to heal a rename). ⛔ <c>DelegateShape</c> is NOT here: it is a BTree
/// interpreter arity and stays on the BTree node (Q75 §5.1b, B-5).</para>
///
/// <para>⭐ <see cref="ExpressionTargetField"/> is this binding's OWN host variable (B-2) — no state-wide base.</para>
/// </summary>
public sealed class BehaviorActionBinding
{
    /// <summary>The C# method, e.g. <c>Hrot.Game.Combat.CombatActions.AimAndFire</c>; for a BTree blueprint binding, the
    /// blueprint's generated <c>TickCore</c> FQN (until B-1's heal, slice 4c of the design's plan).</summary>
    public string? MethodFqn;

    /// <summary>The blueprint asset this binding runs. <see cref="Guid.Empty"/> when it runs a C# method.</summary>
    public Guid BlueprintAssetId;

    /// <summary>The blueprint's name beside its Guid, so a rename heals. ⚠ Display/heal only — the Guid resolves.</summary>
    public string? BlueprintName;

    /// <summary>The host variable this binding reads and writes, by name, in the host's own blackboard.</summary>
    public string? ExpressionTargetField;

    /// <summary>For a stateful binding, the CLR FQN of its working-state struct.</summary>
    public string? WorkingStateTypeId;

    /// <summary>For a stateful binding whose working-state variable is not <see cref="ExpressionTargetField"/>, its name.</summary>
    public string? WorkingStateTargetField;

    /// <summary>True when the binding names neither a method nor a blueprint (the DTO's rule, exactly).</summary>
    public bool IsEmpty => string.IsNullOrEmpty(MethodFqn) && BlueprintAssetId == Guid.Empty;

    /// <summary>
    /// ⭐ The EDITOR's test for "this slot is unbound": no method, no blueprint Guid AND no blueprint NAME. ⚠ Stricter than
    /// <see cref="IsEmpty"/> on purpose — a picked blueprint whose Guid could not be resolved (no catalogue, §7.1a ③'s
    /// never-erase branch) still names something the author picked, so the editor must keep it. Saving drops such a name
    /// (<c>BehaviorActionBindingMapping</c>), exactly as before.
    /// </summary>
    public bool NamesNothing => IsEmpty && string.IsNullOrEmpty(BlueprintName);

    /// <summary>A binding to a C# method, or <c>null</c> when <paramref name="methodFqn"/> is empty.</summary>
    public static BehaviorActionBinding? ForMethod(string? methodFqn, string? expressionTargetField = null)
        => string.IsNullOrEmpty(methodFqn)
            ? null
            : new BehaviorActionBinding { MethodFqn = methodFqn, ExpressionTargetField = NullIfEmpty(expressionTargetField) };

    /// <summary>A shallow copy (every member is a value or an immutable string).</summary>
    public BehaviorActionBinding Clone() => (BehaviorActionBinding)MemberwiseClone();

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
