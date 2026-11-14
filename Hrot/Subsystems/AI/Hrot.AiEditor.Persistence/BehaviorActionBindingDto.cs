using System;
using System.Text.Json.Serialization;

namespace Hrot.AiEditor.Persistence;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> — ONE persisted binding for every action / condition / activity / guard site, in both hosts.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §3 (Q75 decision B).
///
/// <para>Eight sites carry it: a BTree action node and condition node; an HSM state's OnEntry / OnExit / Activity / Timer;
/// a transition's and a global transition's Guard / Action. ⛔ It replaces <c>BTreeActionPayloadDto</c>,
/// <c>BTreeConditionPayloadDto</c> and the HSM's flat <c>*Action</c> / <c>*Function</c> / <c>*BlueprintAssetId</c> fields.</para>
///
/// <para>⭐ A binding names EITHER a C# method (<see cref="MethodFqn"/>) OR a blueprint (<see cref="BlueprintAssetId"/> +
/// <see cref="BlueprintName"/>, the name only to heal a rename — <c>Q36-B</c>). ⛔ <c>DelegateShape</c> is NOT here: it is a
/// BTree interpreter arity and stays on the BTree node (Q75 §5.1b).</para>
///
/// <para>⭐ <see cref="ExpressionTargetField"/> is this binding's OWN host variable (design B-2) — no state-wide base.</para>
/// </summary>
public sealed class BehaviorActionBindingDto
{
    /// <summary>The C# method, e.g. <c>Hrot.Game.Combat.CombatActions.AimAndFire</c>. For a BTree blueprint binding written
    /// before B-1's heal, the blueprint's generated <c>TickCore</c> FQN.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MethodFqn { get; set; }

    /// <summary>The blueprint asset this binding runs. <see cref="Guid.Empty"/> when it runs a C# method.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Guid BlueprintAssetId { get; set; }

    /// <summary>The blueprint's name beside its Guid, so a rename heals. ⚠ Display/heal only — the Guid resolves.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlueprintName { get; set; }

    /// <summary>The host variable this binding reads and writes, by name, in the host's own blackboard.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpressionTargetField { get; set; }

    /// <summary>For a stateful binding, the CLR FQN of its working-state struct.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingStateTypeId { get; set; }

    /// <summary>For a stateful binding whose working-state variable is not <see cref="ExpressionTargetField"/>, its name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingStateTargetField { get; set; }

    /// <summary>True when the binding names neither a method nor a blueprint.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(MethodFqn) && BlueprintAssetId == Guid.Empty;
}
