using System;

namespace Hrot.Editor.AiShared.Inspector.ActionBinding;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> slice 4b — ONE inspector facet for every binding site, both hosts.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>A facet field of this type is made ONE StructEdit leaf by <see cref="BehaviorActionBindingFieldEditor"/> and drawn
/// by ONE <see cref="ActionBindingDrawer"/>, which therefore sees the method, the blueprint and the target variable
/// together. ⇒ the variable list filters by THIS binding's own method — no side channel carrying "the selected node's
/// method" from the mapper to a per-field drawer (the retired <c>BTreeFacetFqnContext</c> / <c>HsmFacetFqnContext</c>).</para>
///
/// <para>⭐ Each site has its own facet, so each slot edits its OWN target variable (design B-2).</para>
/// </summary>
public struct BehaviorActionBindingFacet
{
    /// <summary>The C# method (empty = none).</summary>
    public string? MethodFqn;

    /// <summary>The blueprint's catalogue name (empty = none). The applier captures its Guid at pick time.</summary>
    public string? BlueprintName;

    /// <summary>The host variable this binding reads/writes.</summary>
    public string? ExpressionTargetField;

    /// <summary>⭐ <c>CE-2099</c> — the <c>Role=State</c> variable a STATEFUL C# method's working state lives in; empty = the
    /// node's own state (no variable). Two nodes naming the same Behavior-scoped variable share one state (e.g. a
    /// <c>ChooseOption</c> action and its <c>IsOption</c> guards). Ignored for every other form.</summary>
    public string? WorkingStateTargetField;

    /// <summary>Read-only for the drawer: the site's identity, used to name a promoted variable (<c>_auto_{id}</c>).</summary>
    public string? SiteId;

    /// <summary>Read-only for the drawer: which binding of the site this is, when the site has several (an HSM state's
    /// <c>entry</c>/<c>exit</c>/<c>timer</c>, a transition's <c>guard</c>); <see langword="null"/> for the site's primary
    /// binding. Names a promoted variable <c>_auto_{id}_{slot}</c> so two bindings of one node never share it (B-2).</summary>
    public string? SiteSlot;

    /// <summary>Read-only for the drawer: the bound method takes no variable (a BTree param-less <c>NoParams</c> node,
    /// <c>CE-504</c>), so the binding has none to pick or promote. ⚠ The name predates CE-504's retirement of the
    /// whole-blackboard shape it was first written for.</summary>
    public bool TargetsWholeBlackboard;

    /// <summary>What the inspector shows when the drawer is not registered (plain text fallback, headless dumps).</summary>
    public override string ToString()
    {
        string what = !string.IsNullOrEmpty(BlueprintName) ? $"blueprint {BlueprintName}"
                    : !string.IsNullOrEmpty(MethodFqn)     ? MethodFqn!
                    : "(none)";
        return string.IsNullOrEmpty(ExpressionTargetField) ? what : $"{what} → {ExpressionTargetField}";
    }
}

/// <summary>Which list of methods a binding site offers.</summary>
public enum BindingSlotKind
{
    /// <summary>An action / activity / entry / exit / timer / BTree action.</summary>
    Action,

    /// <summary>A guard / BTree condition.</summary>
    Guard,
}

/// <summary>
/// ⭐ <c>CE-417</c> — marks a <see cref="BehaviorActionBindingFacet"/> facet field with its slot kind and whether the site may
/// run a blueprint (HSM activity and guard today; a BTree node gains it with B-1, slice 4c).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ActionBindingAttribute : Attribute
{
    public ActionBindingAttribute(BindingSlotKind kind, bool allowsBlueprint = false)
    {
        Kind = kind;
        AllowsBlueprint = allowsBlueprint;
    }

    public BindingSlotKind Kind { get; }
    public bool AllowsBlueprint { get; }
}
