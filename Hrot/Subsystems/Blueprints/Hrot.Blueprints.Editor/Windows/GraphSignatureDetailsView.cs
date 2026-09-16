using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — the hosted-view identity, dumped.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⭐ <b>Deliberately thin</b> — the borrowed <see cref="GraphSignatureWindow"/> already
/// self-publishes its FULL content under its OWN address every time <c>DrawContent()</c> runs
/// (including from here). ⛔ Duplicating that whole model under a second address would be ruling 9's
/// mistake. ⭐ What this record adds is WHICH hosted slot is showing it, via
/// <see cref="HostWindowId"/> — the same shape <c>BlackboardDetailsViewPanelViewModel</c> uses.</para>
/// </summary>
public sealed record GraphSignatureDetailsViewPanelViewModel(
    string PanelId,
    string PanelKind,
    string HostWindowId) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐⭐ <b><c>L3.2</c> — the Graph-signature panel becomes a Details VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L3</c>'s table
/// *(<i>"Graph signature | <c>GraphSignatureWindow</c> (388 ln) | Blueprint ∧ a graph row"</i>)*.
///
/// <para>⭐⭐ <b>A WRAPPER over the window's own content, not a rewrite</b> — 📌 ruling 9. ⚠ The
/// signature editor is 388 lines of parameter add/remove/retype with a headless
/// <c>GraphSignatureEditModel</c> behind it; ⛔ a second one would drift on exactly the thing that
/// matters — which edits are legal.</para>
///
/// <para>⭐⭐⭐ <b>This view lives in <c>Hrot.Blueprints.Editor</c>, ON PURPOSE.</b> 📌 <c>R-116</c> — the
/// predicate ships with the view — and this predicate must ask <i>"is this a <c>BlueprintAsset</c>
/// with editable graphs?"</i>, a question <c>AiShared</c> cannot express and must not learn.
/// ⇒ ⭐ that is the same reason <c>R-112</c> forbids an <c>AssetKind</c> switch in the registry: the
/// knowledge belongs to the host, not to the shell.</para>
///
/// <para>⚠ <b>The window is BORROWED</b> — the composition root builds it, wires its canvas-graph
/// resolver and registers it with the <c>WindowManager</c>. ⛔ <see cref="Dispose"/> must not touch it.
/// ⭐ Same contract as <c>L1.3</c>'s section and <c>L3.1</c>'s pane.</para>
/// </summary>
public sealed class GraphSignatureDetailsView : IDetailsViewInstance
{
    private readonly GraphSignatureWindow _window;

    public GraphSignatureDetailsView(GraphSignatureWindow window)
        => _window = window ?? throw new ArgumentNullException(nameof(window));

    private const string ViewId = GraphSignatureDetailsViewDescriptor.ViewId;

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE — this view's own thin address, ahead of the borrowed
    /// window's body. ⚠ Declared on first draw, not at construction — see
    /// <c>VariablesDetailsView.BuildAndPublish</c>'s remarks for why that is still the recipe's
    /// obligation, just resolved at the point the address becomes known.</summary>
    private GraphSignatureDetailsViewPanelViewModel BuildAndPublish(string idScope)
    {
        var panelId = $"{idScope}/{ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        var vm = new GraphSignatureDetailsViewPanelViewModel(panelId, ViewId, _window.Id);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion.</summary>
    internal GraphSignatureDetailsViewPanelViewModel SimulateDraw(string idScope) => BuildAndPublish(idScope);

    /// <summary>
    /// ⭐ Draws the window's own client-area body through the <c>DrawContent</c> seam <c>L3.2</c>
    /// opened. ⛔ There is still exactly ONE body: the window's <c>DrawClientArea</c> calls the same
    /// method.
    /// </summary>
    /// <remarks>
    /// ⚠ <paramref name="context"/> is not passed through: the window resolves its own graph from the
    /// canvas *(<c>ResolveSelectedGraph</c>, <c>BP-72</c>)*, and the context carries no graph — see
    /// <see cref="GraphSignatureWindow.AppliesTo"/> for the measurement.
    /// </remarks>
    public void Draw(DetailsContext context, string idScope)
    {
        BuildAndPublish(idScope);
        _window.DrawContent();
    }

    /// <summary>⛔ Deliberately empty — the window is BORROWED. See the class remarks.</summary>
    public void Dispose() { }
}

/// <summary>⭐⭐ <b><c>L3.2</c> — the descriptor.</b> ⭐ Its own type so the host registers it in one line.</summary>
public static class GraphSignatureDetailsViewDescriptor
{
    /// <summary>⭐ The stable id — the layout key and §2's <i>"remember my pick"</i> key.</summary>
    public const string ViewId = "details.graphsignature";

    /// <summary>
    /// ⭐ Rank <b>20</b> — above <c>Variables</c>' <c>10</c> *(a designer who opened a blueprint with
    /// graphs is usually working on one)*, below <c>Runtime</c>'s <c>50</c> *(a live session wins)*.
    /// 📌 <c>R-98</c>: rank decides only the DEFAULT.
    /// </summary>
    public const int Rank = 20;

    public static DetailsViewDescriptor For(GraphSignatureWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var instance = new GraphSignatureDetailsView(window);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Graph Signature",
            Rank:      Rank,
            AppliesTo: window.AppliesTo,
            Create:    () => instance);
    }
}
