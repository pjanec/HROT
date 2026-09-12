namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L2.3</c> — THE GREY LINE. One place decides what an empty Details panel SAYS.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L2.3</c> *(<i>"the grey empty state,
/// replacing <c>AiDetailsWindow:128</c> and <c>RuntimeInspectorWindow:54</c>/<c>:67</c>"</i>)* ·
/// §2b's first sequence, which draws it as <i>"intentionally empty for the current selection"</i>.
///
/// <para>⛔⛔ 📌 <b><c>R-117</c>: a BLANK panel is a defect.</b> ⚠ The distinction is not pedantry —
/// a blank reads as <i>"this is broken"</i>, and the designer's next move is to click around looking
/// for the thing that failed. ⭐ A grey line reads as <i>"there is nothing to show here"</i>, which is
/// a fact about their selection, not about the editor.</para>
///
/// <para>⭐⭐ <b>TWO strings, not one — and that is <c>R-118</c>'s lesson applied to prose.</b>
/// 📌 <c>R-118</c> deleted a <c>null</c> that meant three different things. ⛔ Collapsing <i>"no
/// document is open"</i> and <i>"nothing applies to this selection"</i> into one sentence would rebuild
/// exactly that mistake in the UI layer: the first is fixed by opening a document, the second by
/// selecting something else, and a designer told the wrong one looks in the wrong place.</para>
///
/// <para>⚠ <b>The strings live here, not in the windows</b>, so a rail can assert them without a draw
/// *(§6: "every task's rail asserts on a store or a returned model")* — 📌 <c>R-21</c>/<c>R-62</c>.</para>
/// </summary>
public static class DetailsEmptyState
{
    /// <summary>⭐ No document is open at all. ⚠ Fixed by OPENING something — a different action from
    /// the sentence below, which is why they are different sentences.</summary>
    public const string NoDocument = "No document is open.";

    /// <summary>
    /// ⭐⭐ A document IS open and no view claims this selection. 📄 §2b: <i>"intentionally empty for
    /// the current selection"</i> — ⭐ the wording says the emptiness is a DECISION, ⛔ not a failure.
    /// </summary>
    public const string NothingForThisSelection = "Nothing to show for the current selection.";

    /// <summary>
    /// ⭐⭐⭐ <b>What the shell draws when the offer set is empty.</b>
    ///
    /// <para>⭐ Returns a STRING rather than drawing, so §6's rail — <i>"an empty offer set returns the
    /// grey string"</i> — is a value assertion. ⛔ Never null and never empty: that is the blank
    /// <c>R-117</c> forbids, and a caller that got one would draw nothing.</para>
    /// </summary>
    public static string For(DetailsContext context)
        => context is null || context.Asset is null ? NoDocument : NothingForThisSelection;

    /// <summary>
    /// ⭐⭐ <b>What a FLOAT draws when its own predicate is false</b> — 📌 <c>R-117</c>'s row names
    /// <b>two</b> sites: <i>"empty offer set · a float whose predicate is false"</i>.
    ///
    /// <para>⚠ A float is deliberately KEPT OPEN in that case *(§2's hosting table: "stays open, grey
    /// line")* — ⛔ closing it would lose the designer's placement, which §6 <c>L4.2</c> is explicit
    /// about. ⭐ So it must say why it is idle, naming the view, or it looks stuck.</para>
    /// </summary>
    public static string ForInapplicableFloat(string viewTitle)
        => string.IsNullOrWhiteSpace(viewTitle)
            ? NothingForThisSelection
            : $"{viewTitle} does not apply to the current selection.";
}
