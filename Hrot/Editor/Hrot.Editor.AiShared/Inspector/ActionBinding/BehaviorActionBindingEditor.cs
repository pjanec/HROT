using System;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.Editor.AiShared.Inspector.ActionBinding;

/// <summary>What <see cref="BehaviorActionBindingEditor.Apply"/> needs from the host, per apply.</summary>
/// <param name="Asset">The open asset — where a composed params variable is created and an outgoing one removed.</param>
/// <param name="Catalog">Turns a picked blueprint NAME into its Guid at pick time. ⚠ Optional for headless fixtures: then
/// only the name is written (§7.1a ③ never-erase). ⛔ A production host HAS one and passes it.</param>
/// <param name="Exporter">Resolves a picked blueprint to its generated <c>Params</c> type for the <c>CE-414</c> compose.
/// ⚠ Optional for the same reason; without it a blueprint pick composes nothing.</param>
/// <param name="ComposeBaseName">The base name of the params variable a blueprint pick composes (<c>bpActivityParams</c>,
/// <c>bpGuardParams</c>); <see langword="null"/> for a slot that cannot bind a blueprint.</param>
/// <param name="KeepWhenEmpty">⭐ BTree: an action/condition NODE always carries its binding. HSM: an empty slot is unbound
/// (null) — the same rule as <see cref="BehaviorActionBindingMapping"/>'s.</param>
/// <param name="WorkingStateBaseName">⭐ <c>CE-417</c> slice 4c — BTree only: a composed BTree node also binds the blueprint's
/// generated <c>WorkingState</c> (its partition slot, E2), exactly as the palette drop does. Null for HSM, whose working
/// state lives in the occurrence slot.</param>
public readonly record struct ActionBindingApplyContext(
    IBlackboardManagedAsset Asset,
    IAssetCatalog? Catalog = null,
    IActionSchemaExporter? Exporter = null,
    string? ComposeBaseName = null,
    bool KeepWhenEmpty = false,
    string? WorkingStateBaseName = null);

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> slice 4b — THE applier: an edited <see cref="BehaviorActionBindingFacet"/> back into the model's
/// <see cref="BehaviorActionBinding"/>, every site, both hosts.</b> 📄 <c>DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>It holds the rules that were spelled once per slot in <c>HsmFacetDispatcher</c> (and not at all on BTree):
/// <list type="bullet">
/// <item>⭐ the blueprint's Guid is captured AT PICK TIME while the catalogue entry is in hand (§11.1a), through the one
/// shared <see cref="SubtreeReferenceResolver.ResolvePick"/> — never-erase when the name does not resolve;</item>
/// <item>⭐⭐ <c>CE-414</c> — a CHANGED blueprint pick drops the outgoing pick's editor-owned variable and composes ONE
/// variable typed by the new blueprint's generated <c>Params</c>, bound as THIS binding's target (B-2). ⚠ Only on a
/// change: an apply round-trips the facet every inspector edit, and composing each time would churn a variable per
/// keystroke;</item>
/// <item>a binding left holding nothing is dropped (HSM) or kept empty (BTree).</item>
/// </list></para>
/// </summary>
public static class BehaviorActionBindingEditor
{
    /// <summary>The facet the inspector shows for <paramref name="binding"/>.</summary>
    /// <param name="siteId">The site's Guid (node / transition / state id), used to name a promoted variable.</param>
    /// <param name="siteSlot">Which binding of the site this is when it has several; null for the primary one.</param>
    /// <param name="targetsWholeBlackboard">A BTree <c>FourParamFull</c> binding — no per-binding variable.</param>
    public static BehaviorActionBindingFacet ToFacet(
        BehaviorActionBinding? binding, string? siteId, string? siteSlot = null, bool targetsWholeBlackboard = false)
        => new()
        {
            MethodFqn              = binding?.MethodFqn,
            BlueprintName          = binding?.BlueprintName,
            ExpressionTargetField  = binding?.ExpressionTargetField,
            SiteId                 = siteId,
            SiteSlot               = siteSlot,
            TargetsWholeBlackboard = targetsWholeBlackboard,
        };

    /// <summary>
    /// Writes <paramref name="facet"/> into <paramref name="current"/> (mutated in place, so its working-state fields
    /// survive) and returns the binding the slot should now hold — possibly a new one, possibly null.
    /// </summary>
    public static BehaviorActionBinding? Apply(
        BehaviorActionBinding? current, in BehaviorActionBindingFacet facet, in ActionBindingApplyContext ctx)
    {
        if (ctx.Asset is null) throw new ArgumentNullException(nameof(ctx), "ActionBindingApplyContext.Asset");

        string? previousBlueprint = NullIfBlank(current?.BlueprintName);
        var b = current ?? new BehaviorActionBinding();

        b.MethodFqn             = NullIfEmpty(facet.MethodFqn);
        b.ExpressionTargetField = NullIfEmpty(facet.ExpressionTargetField);

        string? pickedBlueprint = NullIfBlank(facet.BlueprintName);
        if (pickedBlueprint is null)
        {
            b.BlueprintName    = null;
            b.BlueprintAssetId = Guid.Empty;
        }
        else
        {
            b.BlueprintName    = pickedBlueprint;
            b.BlueprintAssetId = SubtreeReferenceResolver.ResolvePick(
                ctx.Catalog, pickedBlueprint, AssetKind.Blueprint, b.BlueprintAssetId).Id;
        }

        if (ctx.ComposeBaseName is not null
            && !string.Equals(previousBlueprint, pickedBlueprint, StringComparison.Ordinal))
            ComposeBlueprintParams(b, pickedBlueprint, ctx);

        if (!ctx.KeepWhenEmpty && b.NamesNothing
            && b.ExpressionTargetField is null
            && string.IsNullOrEmpty(b.WorkingStateTypeId) && string.IsNullOrEmpty(b.WorkingStateTargetField))
            return null;
        return b;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — a picked blueprint brings its own params variable.</b> 📄
    /// <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6c · <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.2.
    ///
    /// <para>⭐ The outgoing pick's variable is dropped first — on a re-pick as well as on a clear, because the new
    /// blueprint's <c>Params</c> is a different type — but ⛔ only a variable the EDITOR owns (<c>IsAutoManaged</c>).</para>
    /// <para>⚠ <c>workingStateBaseName: null</c>: an HSM-hosted occurrence keeps its working state in its occurrence slot, so
    /// there is no variable to bind. ⛔ No exporter, or no matching AiPrimitive ⇒ the binding is left unbound (a
    /// parameterless blueprint legitimately has nothing to bind).</para>
    /// </summary>
    private static void ComposeBlueprintParams(BehaviorActionBinding b, string? pickedBlueprint, in ActionBindingApplyContext ctx)
    {
        if (AutoManagedVariables.RemoveIfAutoManaged(ctx.Asset, b.ExpressionTargetField))
            b.ExpressionTargetField = null;
        // ⭐ slice 4c — the outgoing working-state variable goes the same way (only one the editor owns).
        if (ctx.WorkingStateBaseName is not null)
        {
            if (AutoManagedVariables.RemoveIfAutoManaged(ctx.Asset, b.WorkingStateTargetField))
                b.WorkingStateTargetField = null;
            b.WorkingStateTypeId = null;
        }

        if (pickedBlueprint is null) return;
        if (!AiPrimitiveNaming.TryFindAiPrimitiveByName(ctx.Exporter, pickedBlueprint, out var entry)) return;

        var composed = AutoManagedVariables.ComposeForAiPrimitive(
            ctx.Asset, entry, paramsBaseName: ctx.ComposeBaseName!, workingStateBaseName: ctx.WorkingStateBaseName);
        b.ExpressionTargetField = composed.ParamsVariable;
        if (ctx.WorkingStateBaseName is not null)
        {
            b.WorkingStateTypeId      = composed.WorkingStateType?.FullName;
            b.WorkingStateTargetField = composed.WorkingStateVariable;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
