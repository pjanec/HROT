using System;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L3.1</c> — THE RUNTIME VIEW: three panes stop being a KIND LOOKUP and become three
/// PREDICATED views.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L3</c>'s table
/// *(<i>"Runtime | the 3 <c>RuntimeInspectorPane</c>s | <c>Mode != Planning</c> ∧ its asset kind"</i>)* ·
/// §4's verdict on <c>RuntimeInspectorWindow</c> *(<i>"⛔ registry on the wrong axis (<c>R-112</c>) ⇒
/// <b>dissolves</b>: 3 panes → 3 predicated views"</i>, approved as closed question <c>Q-iii</c>).
///
/// <para>⭐⭐⭐ <b>What actually changes, and it is not "where the code lives".</b>
/// ⛔ <c>_panes.Find(p =&gt; p.TargetKind == asset.Kind)</c> made asset kind the <b>REGISTRY'S AXIS</b>:
/// exactly one pane per kind, chosen by a lookup the window owned. ⇒ ⚠ a second Blueprint view was
/// <b>unrepresentable</b>, and *"this kind, but only while running"* could not be said at all.
/// ⭐ As a predicate, kind is one CLAUSE among others — 📌 <c>R-112</c>: <i>"a host says so in its own
/// predicate."</i></para>
///
/// <para>⭐⭐ <b>The <c>Mode</c> clause is NEW, and it is the design's, not mine.</b> 📐 Measured: the old
/// window drew its pane in every mode, and each pane then said <i>"No live BTree state."</i> from
/// inside. ⇒ ⛔ that is <c>R-117</c>'s blank-shaped defect one level down — a view claiming the panel in
/// order to apologise. ⭐ Now the view <b>declines while PLANNING</b> and the shell's own grey line
/// answers, in one voice for every host.</para>
///
/// <para>⚠ <b>The pane is BORROWED, exactly as <c>L1.3</c>'s section is.</b> The composition root builds
/// it, hands it its debug session and its resolvers, and keeps it alive for the editor's lifetime ⇒
/// ⛔ <see cref="Dispose"/> must not touch it.</para>
/// </summary>
public sealed class RuntimeDetailsView : IDetailsViewInstance
{
    private readonly IRuntimeInspectorPane _pane;

    public RuntimeDetailsView(IRuntimeInspectorPane pane)
        => _pane = pane ?? throw new ArgumentNullException(nameof(pane));

    /// <summary>
    /// ⭐ Delegates to the pane's existing <c>Draw()</c> — ⛔ not a rewrite. 📌 ruling 9: the pane is the
    /// one implementation of *"what a live BTree/HSM/Blueprint looks like"*, and it already had a
    /// window-less draw because <c>RuntimeInspectorWindow</c> owned the chrome.
    /// </summary>
    /// <remarks>
    /// ⚠ <paramref name="context"/> is not passed through: the pane reads its own debug session, which
    /// the root wired. ⭐ Re-pointing panes at the context is not in §6 — ⛔ and inventing it here would
    /// be design work in an implementation batch.
    /// </remarks>
    public void Draw(DetailsContext context, string idScope) => _pane.Draw();

    /// <summary>⛔ Deliberately empty — the pane is BORROWED. See the class remarks.</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐ <b><c>L3.1</c> — the descriptor for a runtime pane.</b> ⭐ One per pane, so the predicate lives
/// beside the thing it guards *(<c>R-116</c>)*.
/// </summary>
public static class RuntimeDetailsViewDescriptor
{
    /// <summary>
    /// ⭐⭐ <b>The id carries the KIND, and that is load-bearing.</b> ⚠ Two panes claiming one kind is a
    /// <b>wiring bug</b> — 📐 the old <c>_panes.Find</c> silently picked the FIRST — ⇒ ⭐ with the kind in
    /// the id, <c>DetailsViewRegistry.Add</c>'s duplicate guard turns that silence into a throw at the
    /// wiring, which is 📌 exactly what the <c>G4</c> precedent is for.
    /// </summary>
    public static string ViewIdFor(AssetKind kind) => $"details.runtime.{kind}";

    /// <summary>
    /// ⭐ Rank <b>50</b> — above <c>Variables</c>' <c>10</c>, deliberately: ⚠ while a session is LIVE,
    /// what the designer most likely wants is the live state. 📌 <c>R-98</c> — rank decides only the
    /// DEFAULT, and the toolbar's pick wins *(and is remembered per §2's key)*.
    /// </summary>
    public const int Rank = 50;

    /// <summary>
    /// ⭐⭐ Build the descriptor for a pane the editor already owns.
    /// <para>⭐ §6 <c>L3</c>'s predicate verbatim — <b><c>Mode != Planning</c> ∧ its asset kind</b>.</para>
    /// </summary>
    public static DetailsViewDescriptor For(IRuntimeInspectorPane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        var instance = new RuntimeDetailsView(pane);
        var kind     = pane.TargetKind;

        return new DetailsViewDescriptor(
            Id:        ViewIdFor(kind),
            Title:     "Runtime",
            Rank:      Rank,
            AppliesTo: ctx => Applies(ctx, kind),
            // ⚠ The SAME wrapper, because the pane is shared and borrowed — the factory SHAPE is what
            //   lets L4.2 hand a float its own instance without touching this call site.
            Create:    () => instance);
    }

    /// <summary>⭐ Extracted so a rail can assert the predicate directly, without a pane.</summary>
    public static bool Applies(DetailsContext context, AssetKind kind)
        => DetailsViewPredicates.ModeIsNot(context, VariableRunState.Planning)
        && DetailsViewPredicates.AssetKindIs(context, kind);
}
