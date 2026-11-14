using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — the stub's one sentence, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
/// </summary>
public sealed record UtilityConsiderationDetailsViewPanelViewModel(
    string PanelId,
    string PanelKind,
    string? Description) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ <b><c>S3</c> — the UTILITY CONSIDERATION arm, moved out of <c>InspectorWindow</c>.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.6 ③, verbatim: <i>"that arm is a <b>STUB</b>
/// (a heading and <c>"Option N, Consideration M"</c>, then <c>// Curve inspector panel wired in a later
/// phase</c>) ⇒ ⭐ port it honestly as a stub, ⛔ do not pretend it is a feature."</i>
///
/// <para>⛔⛔ <b>AND IT IS UNREACHABLE TODAY — measured, not assumed.</b> 📐 <c>2026-08-22</c>,
/// <c>search_graph(name_pattern=".*UtilityConsideration.*")</c> plus a repo-wide grep:
/// <see cref="UtilityConsiderationSelection"/> has exactly <b>TWO</b> C# sites — its own declaration and
/// the <c>if</c> this view replaces. ⇒ ⚠ <b>nothing in this repo ever RAISES the selection</b>, so the
/// arm has never drawn. 📐 A second query — <c>.*Utility.*</c> under <c>Hrot/</c> — returns <b>zero</b>
/// nodes: there is no utility-AI editor surface here at all.</para>
///
/// <para>⭐⭐⭐ <b>Which is why it is PORTED and not DELETED</b> — 📌 the <c>2026-08-15</c> rule
/// *("unreferenced is not unintentional — search <c>docs/</c> first")*, and the design record answers
/// directly: <c>docs/designs/utility-ai/Utility_AI_Design_v1_1.md</c> is a live architecture document,
/// <c>.dev/_DONE/utility-ai/Utility_AI_Editor_Wireframes.md</c> specifies the two-pane option ×
/// consideration host this arm belongs to, and <c>.dev/_DONE/utility-ai/batches/BATCH-14-INSTRUCTIONS.md</c>
/// §1d is the batch that deliberately added <i>"<c>UtilityConsiderationSelection</c> + inspector dispatch
/// arm"</i>. ⇒ ⭐ this is <b>DORMANT</b>, not dead: a designed capability whose producer has not been
/// built. ⛔ Deleting it would remove a capability, not a mistake.</para>
///
/// <para>⚠ <b>The honest half.</b> The retired stub cited <c>"P5-02"</c> for the curve inspector; 📐 no
/// such phase exists in the utility-AI record *(the only <c>P5-02</c> hits in the corpus belong to
/// <c>group-maneuvers</c>)*. ⇒ ⭐ this view says the panel is not built and NAMES the design that
/// specifies it, rather than showing a heading that implies something is coming.</para>
///
/// <para>⭐ <b>No blank-panel risk</b> *(<c>R-117</c>)*: the predicate is the selection's existence, so
/// the view is offered only when there IS a consideration to describe — and today it never is. ⛔ It does
/// not claim the panel in order to apologise, which is the shell's job *(<c>DetailsEmptyState</c>)</para>
/// </summary>
public sealed class UtilityConsiderationDetailsView : IDetailsViewInstance
{
    /// <summary>
    /// ⭐⭐ <b>What the stub knows, as a MODEL rather than as pixels</b> — 📌 <c>R-21</c>/<c>R-62</c>:
    /// the draw is unrailed by construction, so the sentence a rail can assert lives here.
    /// ⚠ <see langword="null"/> when the context carries no consideration.
    /// </summary>
    public static string? Describe(DetailsContext context)
        => Selected(context) is { } sel
            ? $"Option {sel.OptionIndex}, Consideration {sel.ConsiderationIndex}"
            : null;

    /// <summary>
    /// ⭐⭐⭐ <b>The sentence that keeps this honest.</b> ⛔ The retired arm drew a heading and an index
    /// pair and stopped, which reads as <i>"loading"</i>. ⭐ This says the editor is not built and where
    /// its design lives, so a designer who reaches it is not left guessing.
    /// </summary>
    public const string NotBuiltNotice =
        "The consideration editor (input, context, curve, weight) is not built. "
      + "See docs/designs/utility-ai/Utility_AI_Design_v1_1.md.";

    /// <summary>⭐ The selected consideration, or <see langword="null"/>. ⚠ Reads the CONTEXT, never a
    /// store — 📌 §2: <i>"only the workspace builds a context."</i></summary>
    private static UtilityConsiderationSelection? Selected(DetailsContext context)
        => context.Selection is { Count: 1 } one ? one[0] as UtilityConsiderationSelection : null;

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. ⛔⛔ CORRECTED ORDER vs. the original body — the old code
    /// opened with the ImGui-context guard, so a headless call never even reached <see cref="Describe"/>.
    /// 📄 Same deviation as the design's own AS-BUILT ①: capture must precede the render guard, not
    /// follow it.</summary>
    private UtilityConsiderationDetailsViewPanelViewModel BuildAndPublish(DetailsContext context, string idScope)
    {
        var line     = Describe(context);
        var panelId  = $"{idScope}/{UtilityConsiderationDetailsViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        var vm = new UtilityConsiderationDetailsViewPanelViewModel(
            panelId, UtilityConsiderationDetailsViewDescriptor.ViewId, line);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal UtilityConsiderationDetailsViewPanelViewModel SimulateDraw(DetailsContext context, string idScope)
        => BuildAndPublish(context, idScope);

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        var vm = BuildAndPublish(context, idScope);

        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return;
        if (vm.Description is not { } line) return;

        ImGuiNET.ImGui.TextUnformatted(line);
        ImGuiNET.ImGui.Separator();
        ImGuiNET.ImGui.TextWrapped(NotBuiltNotice);
    }

    /// <summary>⭐ Nothing to release — ⛔ this view owns no session, no cache and no subscription, which
    /// is what a stub should cost. ⚠ Present because <see cref="IDetailsViewInstance"/> requires it, so
    /// a view that LATER grows state has the hook already in the right place.</summary>
    public void Dispose() { }
}

/// <summary>⭐ <c>S3</c> — the descriptor for <see cref="UtilityConsiderationDetailsView"/>.
/// 📄 §7.3's catalogue.</summary>
public static class UtilityConsiderationDetailsViewDescriptor
{
    /// <summary>⭐ §7.6 ③'s id.</summary>
    public const string ViewId = "details.utility";

    /// <summary>
    /// ⭐⭐ <b>Rank 20 — the same as node properties, and for the same reason:</b> a consideration IS the
    /// selected element, so when one is selected this is what the designer means.
    /// <para>⚠ <b>The two can never both apply</b> — <c>details.nodeproperties</c> needs its perspective's
    /// facet dispatcher to map the selection *(<c>NodePropertiesSource.CanShow</c>)* and a consideration
    /// is not a graph node, so an equal rank is not a tie waiting to happen. ⭐ 📌 <c>R-98</c>: rank
    /// decides only the DEFAULT, and the toolbar pick is remembered per context key either way.</para>
    /// </summary>
    public const int Rank = 20;

    /// <summary>⭐ Build the descriptor. ⚠ A FRESH view per window *(<c>R-120</c>)* — and this one owns
    /// no state at all, which is what a stub should cost.</summary>
    public static DetailsViewDescriptor For()
        => new(
            Id:        ViewId,
            Title:     "Utility Consideration",
            Rank:      Rank,
            AppliesTo: Applies,
            Create:    () => new UtilityConsiderationDetailsView());

    /// <summary>⭐ Exactly one selected element, and it is a consideration — 📌 the shape
    /// <c>R-118</c>'s rule takes in every view *(<c>DetailsViewPredicates.ExactlyOne{T}</c>)*.
    /// ⛔ No outline clause here: a consideration is never reachable FROM the variable outline, so
    /// <c>ExactlyOneNodeNotInTheOutline</c>'s extra term would be a rule about a case that cannot
    /// arise.</summary>
    public static bool Applies(DetailsContext context)
        => DetailsViewPredicates.ExactlyOne<UtilityConsiderationSelection>(context);
}
