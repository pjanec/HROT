using System;
using System.Linq;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>S2</c> (<c>BP-399</c>) — SELECTING A NODE MAKES ITS PROPERTIES THE DEFAULT VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue *(Rank 20)* · §7.5's sequence ·
/// §7.6 ②.
///
/// <para>🔒 <b>The user's ask, verbatim (<c>2026-08-22</c>):</b> <i>"the 'Inspector' window there shows
/// the selected-node details — and this is exactly what should be the default view shown in the Details
/// window for BTree. The 'Blackboard Variables' view would be the other view selectable via Details
/// window toolbar."</i> ⇒ ⭐ <b>this file is that sentence, as assertions.</b></para>
///
/// <para>⚠ <b>What it does NOT prove</b> *(📌 <c>R-21</c>/<c>R-62</c>)*: that the node's fields render on
/// screen. ⭐ It proves the SELECTOR reaches the node view and that the other views stay offered — the
/// half a headless run owns. ⛔ The pixels stay with the visual check *(<c>R-27</c>)</para>
/// </summary>
public sealed class TheNodePropertiesViewIsTheDefaultTests
{
    // ── the pieces, built the way the registrar builds them ──────────────────

    /// <summary>⭐ A dispatcher that maps any sub-selection to a facet — the minimum a perspective needs
    /// to have node properties at all.</summary>
    private sealed class AlwaysFacet : IFacetDispatcher
    {
        public object? Applied { get; private set; }
        public object? GetFacet(IAssetSubSelection selection) => "a-facet";
        public void ApplyFacet(IAssetSubSelection selection, object facet) => Applied = facet;
    }

    /// <summary>⛔ A dispatcher that maps NOTHING — a perspective whose selection it cannot read.</summary>
    private sealed class NeverFacet : IFacetDispatcher
    {
        public object? GetFacet(IAssetSubSelection selection) => null;
        public void ApplyFacet(IAssetSubSelection selection, object facet) { }
    }

    private static DetailsContext Context(SelectionOrigin focus, params IAssetSubSelection[] selection)
        => new(focus, selection, Array.Empty<Fdp.Core.Entity>(),
               new OpenDocument(), "BTree", VariableRunState.Planning);

    private static IAssetSubSelection Node()
        => new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid());

    private static DetailsViewRegistry RegistryWith(NodePropertiesSource source)
    {
        var views = new DetailsViewRegistry();
        // ⭐ In the same order the registrar registers them, so a tie-break here would behave as it
        //   does in production (measured — see TheAiOfferSetsAreUnchangedTests).
        views.Add(NodePropertiesDetailsViewDescriptor.For(source));
        views.Add(new DetailsViewDescriptor(
            "details.blackboard", "Blackboard Variables", 5,
            DetailsViewPredicates.HasAsset, () => new Nothing()));
        return views;
    }

    private sealed class Nothing : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    // ══ THE ASK ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail: a node selected on the canvas ⇒ node properties is the DEFAULT, and
    /// Blackboard Variables is still OFFERED as the other toolbar entry.</b>
    ///
    /// <para>⭐ <b>Both halves matter and the second is the one people forget:</b> making the node view
    /// win by REMOVING Blackboard would satisfy "it is the default" and destroy the toolbar switch the
    /// user asked for in the same sentence.</para>
    /// </summary>
    [Fact]
    public void WithANodeSelectedOnTheCanvas_NodePropertiesIsTheDefault_AndBlackboardIsStillOffered()
    {
        var source = new NodePropertiesSource();
        source.SetFacetDispatcher(new AlwaysFacet());
        var views = RegistryWith(source);

        var context = Context(SelectionOrigin.GraphCanvas, Node());

        Assert.Equal(NodePropertiesDetailsViewDescriptor.ViewId, views.Default(context)?.Id);
        Assert.Equal(
            new[] { "details.nodeproperties", "details.blackboard" },
            views.OfferSet(context).Select(d => d.Id));
    }

    /// <summary>
    /// ⛔⛔ <b>THE ANTI-VACUITY HALF, and it is the reason the predicate has two clauses.</b>
    /// 📐 A perspective whose dispatcher cannot map the selection *(or that has none at all)* must
    /// <b>not</b> offer the view — ⚠ otherwise it claims the panel at Rank 20 and then renders nothing,
    /// which is <c>R-117</c>'s blank one level down. 📌 <c>R-116</c>: the predicate ships with the view.
    /// </summary>
    [Theory]
    [InlineData(false)]   // no dispatcher wired at all
    [InlineData(true)]    // a dispatcher that maps nothing
    public void WithNoFacetForTheSelection_TheViewIsNotOffered(bool wireADeadDispatcher)
    {
        var source = new NodePropertiesSource();
        if (wireADeadDispatcher) source.SetFacetDispatcher(new NeverFacet());
        var views = RegistryWith(source);

        var context = Context(SelectionOrigin.GraphCanvas, Node());

        Assert.DoesNotContain(views.OfferSet(context), d => d.Id == NodePropertiesDetailsViewDescriptor.ViewId);
        // ⭐ …and the panel is NOT blank: Blackboard still claims it.
        Assert.Equal("details.blackboard", views.Default(context)?.Id);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-429</c> on the SHARED view: working in the variable outline hands the panel
    /// back.</b> ⚠ Rank 20 would otherwise beat the variables list while the designer clicks rows in
    /// the outline — 📌 <c>Q32</c> ruling 2, the routing batches 79–87 built.
    /// </summary>
    [Fact]
    public void WorkingInTheOutline_TheNodeViewYieldsThePanel()
    {
        var source = new NodePropertiesSource();
        source.SetFacetDispatcher(new AlwaysFacet());

        var node = Node();

        Assert.True (NodePropertiesDetailsViewDescriptor.Applies(Context(SelectionOrigin.GraphCanvas,    node)));
        Assert.False(NodePropertiesDetailsViewDescriptor.Applies(Context(SelectionOrigin.VariableOutline, node)));
    }

    /// <summary>⭐ The rank relationship, asserted as an ORDER so renumbering the scale cannot silently
    /// invert the meaning. 📄 §7.3: node properties outranks Blackboard (5) and Variables (10).</summary>
    [Fact]
    public void TheNodeViewOutranksBlackboardAndVariables()
    {
        Assert.True(NodePropertiesDetailsViewDescriptor.Rank > VariablesDetailsViewDescriptor.Rank);
        Assert.True(NodePropertiesDetailsViewDescriptor.Rank > 5);
    }

    /// <summary>
    /// ⚠⚠ <b>AND THE ONE RANK THE USER WAS ASKED ABOUT, pinned rather than assumed.</b>
    /// 📄 §7.3: <c>details.runtime.*</c> is <b>50</b>, so a LIVE session still outranks node properties.
    /// 🔒 <b>Approved by the user, <c>2026-08-22</c>:</b> <i>"runtime having higher rank during live
    /// session is OK."</i> ⇒ ⭐ railed, so a later batch cannot flip it by accident.
    /// </summary>
    [Fact]
    public void ARunningSessionStillOutranksNodeProperties()
        => Assert.True(RuntimeDetailsViewDescriptor.Rank > NodePropertiesDetailsViewDescriptor.Rank);

    // ══ the commit path ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>An edited facet reaches the dispatcher, and the cache is dropped.</b>
    /// ⛔ Not a draw: 📌 <c>R-21</c> — every decision this view makes is on the SOURCE, which is what
    /// makes it railable at all.
    /// </summary>
    [Fact]
    public void CommittingAFacet_ReachesTheDispatcher_AndInvalidatesTheCache()
    {
        var dispatcher = new AlwaysFacet();
        var source     = new NodePropertiesSource();
        source.SetFacetDispatcher(dispatcher);

        var context = Context(SelectionOrigin.GraphCanvas, Node());
        var before  = source.Generation;

        source.CommitFacet(context, "edited");

        Assert.Equal("edited", dispatcher.Applied);
        Assert.True(source.Generation > before,
            "a commit must invalidate, or the next frame would re-render the pre-commit facet");
    }

    /// <summary>
    /// ⭐⭐ <b><c>B-3</c>: the view names the variable the selected node WRITES</b> — the arm that came
    /// across with the facet arm *(<c>BP-431</c>)*. ⛔ Answered without ImGui, which is the point.
    /// </summary>
    [Fact]
    public void TheBoundVariableName_ComesFromTheInjectedAccessor()
    {
        var source = new NodePropertiesSource();
        source.SetFacetDispatcher(new AlwaysFacet());
        using var view = new NodePropertiesDetailsView(source);
        var context = Context(SelectionOrigin.GraphCanvas, Node());

        // ⛔ No accessor wired ⇒ no bound variable, and the arm draws nothing. Honest, not empty.
        Assert.Null(view.BoundVariableName(context));

        source.SetExpressionTargetFieldAccessor(facet => facet is null ? null : "Health");

        Assert.Equal("Health", view.BoundVariableName(context));
    }

    /// <summary>⭐ The minimum an open document has to be for the Blackboard predicate to hold.</summary>
    private sealed class OpenDocument : IEditableAsset
    {
        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "OpenDocument";
        public AssetKind Kind           => AssetKind.BTree;
        public string    SourceFilePath => "/open.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed { add { } remove { } }
    }
}
