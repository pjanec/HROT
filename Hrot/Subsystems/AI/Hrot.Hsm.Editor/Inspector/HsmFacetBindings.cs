using System;
using Hrot.Editor.AiShared;

namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// ⭐ <c>CE-417</c> slice 4a — the flat-facet ↔ binding translation, at the ONE place it still exists: the inspector
/// boundary. 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>The model holds bindings; the HSM facets keep their pre-4a shape (one method field per slot, one target field
/// per node) until slice 4b gives every site its own binding facet. These are the slice-2 migration rules, unchanged:
/// a node's one target field is read from its action (else its guard) and written to every binding that exists.</para>
/// <para>⛔ Retired by 4b — do not add callers.</para>
/// </summary>
internal static class HsmFacetBindings
{
    /// <summary>The single target field a transition facet shows: the action's, else the guard's.</summary>
    public static string? TransitionTargetField(BehaviorActionBinding? guard, BehaviorActionBinding? action)
        => NullIfEmpty(action?.ExpressionTargetField) ?? NullIfEmpty(guard?.ExpressionTargetField);

    /// <summary>Writes a facet's method into a slot. A slot left naming neither a method nor a blueprint is dropped.</summary>
    public static BehaviorActionBinding? WithMethod(BehaviorActionBinding? b, string? methodFqn)
    {
        if (string.IsNullOrEmpty(methodFqn))
        {
            if (b is null) return null;
            b.MethodFqn = null;
            // ⚠ keep a binding that still carries a target field — its holder role is decided by the caller's field write
            return b.NamesNothing && string.IsNullOrEmpty(b.ExpressionTargetField) ? null : b;
        }
        b ??= new BehaviorActionBinding();
        b.MethodFqn = methodFqn;
        return b;
    }

    /// <summary>Writes a facet's blueprint (name + the Guid resolved at pick time) into a slot; an empty name clears both.</summary>
    public static BehaviorActionBinding? WithBlueprint(BehaviorActionBinding? b, string? name, Guid assetId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (b is null) return null;
            b.BlueprintName = null;
            b.BlueprintAssetId = Guid.Empty;
            return b.NamesNothing ? null : b;
        }
        b ??= new BehaviorActionBinding();
        b.BlueprintName = name;
        b.BlueprintAssetId = assetId;
        return b;
    }

    /// <summary>Writes a transition facet's one target field to every binding that exists. ⚠ A field authored BEFORE any
    /// guard or action is held on an Action that names nothing — exactly as a state keeps one on an empty Activity — so
    /// typing the field first and picking the action next frame does not lose it.</summary>
    public static void SetTransitionTargetField(
        ref BehaviorActionBinding? guard, ref BehaviorActionBinding? action, string? field)
    {
        string? v = NullIfEmpty(field);
        if (guard is null && action is null)
        {
            if (v is not null) action = new BehaviorActionBinding { ExpressionTargetField = v };
            return;
        }
        // an empty Action holder only exists while NOTHING else can carry the field
        if (action is { NamesNothing: true } && (guard is not null || v is null)) action = null;
        if (guard  is not null) guard.ExpressionTargetField  = v;
        if (action is not null) action.ExpressionTargetField = v;
        else if (guard is null && v is not null) action = new BehaviorActionBinding { ExpressionTargetField = v };
    }

    /// <summary>A state slot naming neither a method nor a blueprint is dropped — the state's field is then re-held by
    /// <c>StateNode.StateWideTargetField</c>'s own rule (an Activity that names nothing, only when no slot is bound).</summary>
    public static BehaviorActionBinding? DropIfEmpty(BehaviorActionBinding? b) => b is { NamesNothing: true } ? null : b;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
