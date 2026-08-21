using System.Collections.Generic;
using Hrot.Editor.AiShared.Selection;

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
}
