using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — what this hosted view shows, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⭐ <b>The ADDRESS is composed, not owned</b> — this view has no id of its own *(the CALLER-owns
/// -identity shape the queue names for group 2)*: <c>PanelId = idScope + "/" + ViewId</c>, where
/// <c>idScope</c> is the HOSTING window's own scope string *(<c>DetailsWindow._drawId</c> for the docked
/// shell, <c>DetailsViewWindow.Id</c> for a float/pin)*. ⇒ ⭐⭐ a docked instance and a floating instance
/// of this same view are two DIFFERENT addresses, exactly as two live panels must be *(<c>U1d</c>)*.
/// <c>PanelKind = ViewId</c> — identical across hosts, matching <c>DetailsViewWindowPanelViewModel</c>'s
/// own resolution of the same question.</para>
/// </summary>
public sealed record VariablesDetailsViewPanelViewModel(
    string PanelId,
    string PanelKind,
    bool   HasContent,
    string? Heading) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐⭐ <b><c>L1.3</c> — THE FIRST DESCRIPTOR: the variables table becomes a Details VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L1.3</c>
/// *(<i>"<c>VariableDetailsSection</c> becomes the first descriptor"</i>)* and §6 <c>L3</c>'s table,
/// which gives this view the predicate <i>"outline focus ∧ variable rows"</i>.
///
/// <para>⭐⭐ <b>A WRAPPER, not a rewrite.</b> <c>VariableDetailsSection</c> keeps its job, its state
/// and its <c>Draw</c>; this adapts it to <see cref="IDetailsViewInstance"/>. ⛔ 📌 ruling 9 — the
/// alternative *(a second variables table living in <c>Shell/</c>)* would be two implementations of one
/// concept, and they would drift on exactly the thing that matters: which rows are shown.</para>
///
/// <para>⚠⚠ <b>THE INSTANCE DOES NOT OWN THE SECTION, and that is deliberate.</b> The section is built
/// and wired by the registrar *(run-state source, edit gestures, live projection — four services it
/// has forgotten before, 📌 <c>R-67</c>)*. ⇒ ⭐ this instance BORROWS it, and <see cref="Dispose"/> does
/// NOT dispose it — ⛔ disposing a borrowed, still-wired panel when a float window closes would take
/// the docked one down with it.</para>
///
/// <para>⚠ <b>The <c>L4</c> consequence, stated now rather than discovered later:</b> because the
/// section is shared, two windows showing THIS view share its scroll and selection. 📌 <c>R-120</c>
/// says a view owns no SHARED state — ⇒ ⭐ when <c>L4.2</c> makes a second window real, this view needs
/// a per-instance section, and the seam for that is <see cref="VariablesDetailsViewDescriptor"/>'s
/// factory, which already returns a fresh instance per window.</para>
/// </summary>
public sealed class VariablesDetailsView : IDetailsViewInstance
{
    private readonly VariableDetailsSection _section;

    public VariablesDetailsView(VariableDetailsSection section)
        => _section = section ?? throw new ArgumentNullException(nameof(section));

    /// <summary>
    /// ⭐⭐⭐ <b>U-obs-5: BUILD · CAPTURE.</b> ⛔⛔ No ImGui — <see cref="VariableDetailsSection.HasContent"/>
    /// and <see cref="VariableDetailsSection.Heading"/> are already pure, published before the section's
    /// own (guarded) render.
    ///
    /// <para>⚠ <b>Declared on first DRAW, not at construction</b> — the deviation this whole family
    /// shares: the address needs <paramref name="idScope"/>, which a hosted view only learns when the
    /// host calls <see cref="Draw"/>. ⛔ There is no meaningful "constructed but never drawn" gap here —
    /// <see cref="DetailsViewDescriptor.Create"/> builds this instance immediately before the host draws
    /// it (see <c>DetailsWindow.InstanceFor</c>) — so declaring here is still unconditional and still
    /// independent of <see cref="PanelSnapshot.CaptureEnabled"/>, which is the property the recipe's
    /// obligation actually protects.</para>
    /// </summary>
    private VariablesDetailsViewPanelViewModel BuildAndPublish(string idScope)
    {
        var panelId = $"{idScope}/{VariablesDetailsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        var vm = new VariablesDetailsViewPanelViewModel(
            panelId, VariablesDetailsViewDescriptor.ViewId, _section.HasContent, _section.Heading);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion. ⚠ Unlike its siblings this one is not
    /// strictly required for headless safety (<see cref="VariableDetailsSection.Draw"/> already guards
    /// on the ImGui context itself) — kept anyway so every view in this family exposes the same shape.
    /// </summary>
    internal VariablesDetailsViewPanelViewModel SimulateDraw(string idScope) => BuildAndPublish(idScope);

    /// <summary>
    /// ⭐ Delegates to the existing panel.
    /// ⚠ <paramref name="context"/> is not passed through: the section reads the SAME store the context
    /// was built from, so handing it both would be two answers to one question. ⛔ Re-pointing the
    /// section at the context is <c>L3</c>'s job *(§6: "migrate the views — the delegation layer")*,
    /// ⭐ and doing it here would make <c>L1</c> depend on <c>L3</c>.
    /// </summary>
    public void Draw(DetailsContext context, string idScope)
    {
        BuildAndPublish(idScope);
        _section.Draw(idScope);
    }

    /// <summary>⛔ Deliberately empty — the section is BORROWED. See the class remarks.</summary>
    public void Dispose() { }
}

/// <summary>
/// ⭐⭐ <b><c>L1.3</c> — the descriptor for <see cref="VariablesDetailsView"/>.</b> ⭐ Its own type so a
/// host registers it with one line and the predicate lives beside the view it guards
/// *(<c>R-116</c>)*.
/// </summary>
public static class VariablesDetailsViewDescriptor
{
    /// <summary>⭐ The stable id — the layout key and the "remember my pick" key *(§2's context key)*.</summary>
    public const string ViewId = "details.variables";

    /// <summary>
    /// ⭐ Rank <b>10</b>: above nothing in particular today, and deliberately not <c>0</c> so a later
    /// view can sit either side without renumbering this one. ⚠ 📌 <c>R-98</c> — rank only decides the
    /// DEFAULT; the designer's pick wins.
    /// </summary>
    public const int Rank = 10;

    /// <summary>
    /// ⭐⭐ Build the descriptor for a section this perspective already owns.
    ///
    /// <para>⭐ <b>The predicate is §6 <c>L3</c>'s, in two halves:</b> the designer is working in the
    /// OUTLINE *(<c>R-115</c>'s focus latch)*, ⛔ OR nothing else claims the panel — ⚠ the second half
    /// matters because the variables table is what a designer sees with no canvas selection at all,
    /// and 📌 <c>R-117</c> forbids answering that with a blank.</para>
    /// </summary>
    public static DetailsViewDescriptor For(VariableDetailsSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var instance = new VariablesDetailsView(section);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Variables",
            Rank:      Rank,
            // ⭐⭐⭐ L2.3 — TWO halves, and the second one is what keeps R-117 honest.
            //   📐 Measured: VariableDetailsSection.Draw is `if (!HasContent) return;` — ⇒ a view that
            //      claimed the panel with an empty section would render a BLANK, which is exactly the
            //      defect R-117 names. ⛔ Answering that in the shell would be a special case about
            //      variables living in a type that must not know what a variable is.
            //   ⭐ 📌 R-116 — "the predicate ships with the view": the view knows it has nothing to
            //      show, so it does not claim, and the shell's ordinary empty-offer path draws the
            //      grey line.
            AppliesTo: ctx => Applies(ctx) && section.HasContent,
            // ⚠ Returns the SAME wrapper today because the section is shared and borrowed — see
            //   VariablesDetailsView's remarks and the L4.2 note there. ⛔ The factory shape is what
            //   lets L4 fix that without touching any caller.
            Create:    () => instance);
    }

    /// <summary>⭐ Extracted so a rail can assert the predicate directly, without a section.</summary>
    public static bool Applies(DetailsContext context)
        => DetailsViewPredicates.FocusIs(context, SelectionOrigin.VariableOutline)
        || context.Selection.Count == 0;
}
