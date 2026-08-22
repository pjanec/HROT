using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L1.4</c> — WHERE THE <i>"EXACTLY ONE"</i> RULE LIVES NOW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §3, <c>R-118</c>'s row, verbatim:
/// <i>"the bridge reports, never filters — <c>MapSelection</c> → a list; ⭐ <b>the <c>Count != 1</c>
/// rule reappears in ONE predicate</b>."</i> §6 <c>L1.4</c> names the shape:
/// <c>ctx.Selection is [BlueprintNodeSelection]</c>.
///
/// <para>⭐⭐ <b>This is the other half of <c>L0.2</c>.</b> <c>L0.2</c> deleted three refusals from three
/// bridges; ⛔ it did not delete the RULE — it moved it here, where a view says <i>"I am about exactly
/// one node"</i> instead of a bridge saying <i>"I refuse to report two."</i> ⇒ ⭐ the difference is that
/// the SET still reaches every other view, and 📌 <c>R-117</c>'s grey line is what a two-node selection
/// now gets.</para>
///
/// <para>⚠ <b>Helpers, not a base class.</b> A predicate is a <c>Func&lt;DetailsContext, bool&gt;</c> on
/// the descriptor *(<c>R-116</c>)*; these are the shapes that recur, ⛔ not a place hosts must route
/// through. A host with a one-off rule writes a lambda.</para>
/// </summary>
public static class DetailsViewPredicates
{
    /// <summary>
    /// ⭐⭐⭐ <b>Exactly one selected element, and it is a <typeparamref name="T"/>.</b>
    /// 📄 §6 <c>L1.4</c>'s <c>ctx.Selection is [BlueprintNodeSelection]</c>, generalised over the
    /// selection type so each host states its own.
    ///
    /// <para>⛔⛔ <b><c>Count == 1</c> is the point, not an accident.</b> Two nodes is NOT "the first
    /// one" — 📌 that is exactly the collapse <c>R-118</c> deletes. ⚠ A node-properties form cannot
    /// honestly edit two nodes at once, so it declines and the shell draws the grey line
    /// *(<c>R-117</c>)*.</para>
    /// </summary>
    public static bool ExactlyOne<T>(DetailsContext context) where T : IAssetSubSelection
        => context.Selection is { Count: 1 } one && one[0] is T;

    /// <summary>
    /// ⭐⭐ <b>At least one selected element is a <typeparamref name="T"/>.</b>
    /// ⚠ Distinct from <see cref="ExactlyOne{T}"/> **on purpose**: a view that can present a SET
    /// *(a list, a byte budget, a diff)* is exactly the case <c>L0.2</c>'s set exists to enable —
    /// ⛔ making everything single-selection would have wasted the change.
    /// </summary>
    public static bool Any<T>(DetailsContext context) where T : IAssetSubSelection
    {
        foreach (var s in context.Selection) if (s is T) return true;
        return false;
    }

    /// <summary>
    /// ⭐⭐ <b>The designer is working in the OUTLINE, not on the canvas.</b> 📄 §6 <c>L3</c>'s table
    /// gives the Variables view the predicate <i>"outline focus ∧ variable rows"</i>.
    ///
    /// <para>📌 <c>R-115</c>: focus and selection are INDEPENDENT fields, and this reads only the
    /// focus latch — ⭐ which is what lets a designer click into the Details panel to edit without the
    /// panel flipping away mid-edit *(the latch's own reason for existing)*.</para>
    /// </summary>
    public static bool FocusIs(DetailsContext context, SelectionOrigin origin)
        => context.Focus == origin;

    /// <summary>⭐ An asset is open — the weakest useful predicate, for views that are about the
    /// document as a whole *(§6 <c>L3</c>: "asset context")*.</summary>
    public static bool HasAsset(DetailsContext context) => context.Asset is not null;

    // ══ L6.5 — THE ENTITY CONTEXT ════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>L6.5</c> — EXACTLY ONE ENTITY IS SELECTED.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L6</c> stage 3:
    /// <i>"the entity/component predicate helper — <c>ctx.Entities is [{ }]</c> … so each entity-type
    /// view is a one-line predicate."</i>
    ///
    /// <para>⭐⭐ <b>Added HERE rather than in a new type</b> — 📌 <c>R-13</c>. This class already owns
    /// the <i>"exactly one"</i> rule for SUB-SELECTIONS *(<c>ExactlyOne&lt;T&gt;</c>, <c>R-118</c>'s
    /// home)*; the entity axis is the same rule on a different field, and a second predicates class
    /// would be two places that disagree about what "one" means.</para>
    ///
    /// <para>⛔⛔ <b>Why exactly one and not "at least one".</b> Both entity views present a SINGLE
    /// entity's data — <c>EntityInspectorPanel</c> renders components and <c>MissionPanel</c> selects
    /// by one <c>SelectedEntityId</c>. ⚠ Handing them a multi-selection would show the first and
    /// silently ignore the rest, which is precisely the collapse <c>R-118</c> deletes. ⭐ Two entities
    /// selected ⇒ no offer ⇒ <c>R-117</c>'s grey line, which is honest.</para>
    ///
    /// <para>⚠ <b><c>L0.4</c> already fills this field</b> *(as-built (a))* — every perspective's
    /// context builder reads <c>SelectionState</c> from the World. ⛔ Nothing to add to the context;
    /// this is the descriptor-side half.</para>
    /// </summary>
    public static bool ExactlyOneEntity(DetailsContext context)
        => context.Entities is { Count: 1 };

    /// <summary>
    /// ⭐⭐⭐ <b><c>L6.5</c> — the BRAIN SIGNAL, as a predicate factory.</b>
    ///
    /// <para>⚠⚠ <b>As-built (c), measured <c>2026-08-22</c>: there is NO <c>HasBrain</c>/
    /// <c>IsBrainEquipped</c> check in this codebase</b> — the design says so in its own words, and the
    /// enumeration agrees. ⭐ The behavioural signal is <c>IMissionEditorService.GetAvailableBehaviors</c>
    /// / <c>GetMissionSnapshot</c> returning <b>empty</b>.</para>
    ///
    /// <para>⛔⛔ <b>So this takes a DELEGATE, and that is a reference-wall fact, not a preference.</b>
    /// 📐 <c>IMissionEditorService</c> lives in <c>Hrot.Presentation</c>/<c>Hrot.UI.Common</c> — ABOVE
    /// <c>Hrot.Editor.AiShared</c> *(§3's reference wall, the same one that puts the two view adapters
    /// in the composition root)*. ⇒ ⭐ the host supplies the signal; this assembly never learns what a
    /// mission is. 📌 Exactly the shape <c>W4</c>'s <c>ResolveStagedField</c> uses for the same
    /// reason.</para>
    ///
    /// <para>⭐ <b>It composes rather than restating:</b> the entity half is
    /// <see cref="ExactlyOneEntity"/>, so a change to what "one entity" means reaches both views.</para>
    /// </summary>
    /// <param name="hasBrain">
    /// ⭐ Answers <i>"does this entity have behaviours?"</i> ⚠ <c>null</c> ⇒ the predicate is always
    /// <c>false</c>: a host that cannot ask must not claim yes. ⛔ NOT a silent default — the view
    /// simply never offers, which is the honest answer for a host with no mission service.
    /// </param>
    public static System.Func<DetailsContext, bool> OneEntityWithBrain(
        System.Func<Fdp.Core.Entity, bool>? hasBrain)
        => context => ExactlyOneEntity(context)
                   && hasBrain is not null
                   && hasBrain(context.Entities[0]);

    /// <summary>
    /// ⭐⭐⭐ <b>The open document is of this KIND — and this is the ONLY legal way to ask.</b>
    /// 📄 §3, <c>R-112</c> verbatim: <i>"⛔ <b><c>AssetKind</c> is never a view key</b> — <b>a host says
    /// so in its own predicate</b>."</i>
    ///
    /// <para>⚠⚠ <b>The distinction is not pedantry, and §4 measures the difference.</b>
    /// ⛔ <c>RuntimeInspectorWindow</c>'s <c>_panes.Find(p =&gt; p.TargetKind == asset.Kind)</c> made kind
    /// the <b>REGISTRY'S axis</b> — one pane per kind, chosen by a lookup the window owned ⇒ a second
    /// Blueprint view was unrepresentable and a *"this kind, only while running"* view was impossible.
    /// ⭐ Here kind is one CLAUSE inside one view's own predicate ⇒ any number of views may claim a
    /// kind, none, or a kind conjoined with anything else.</para>
    /// </summary>
    public static bool AssetKindIs(DetailsContext context, AssetKind kind)
        => context.Asset is { } asset && asset.Kind == kind;

    /// <summary>
    /// ⭐⭐ <b>The run state — 📌 <c>R-111</c>: <i>"the mode joins the context; one view, many
    /// modes."</i></b> ⭐ Offered as <b>both</b> senses because §6 <c>L3</c>'s Runtime row is stated
    /// NEGATIVELY *(<i>"<c>Mode != Planning</c>"</i>)*: ⚠ a runtime view is about **any** live mode, and
    /// spelling that as an OR over the positive cases would silently exclude a mode added later.
    /// </summary>
    public static bool ModeIs(DetailsContext context, VariableRunState mode)
        => context.Mode == mode;

    /// <inheritdoc cref="ModeIs"/>
    public static bool ModeIsNot(DetailsContext context, VariableRunState mode)
        => context.Mode != mode;
}
