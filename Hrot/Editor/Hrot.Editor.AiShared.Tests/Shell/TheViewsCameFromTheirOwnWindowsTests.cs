using System;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L3</c>'s rail — the migrated views reach the catalogue, and each claims only what its
/// own predicate says.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L3</c>'s table · §4's <i>"3 panes → 3
/// predicated views"</i> · §3's <c>R-112</c> / <c>R-111</c> / <c>R-116</c> rows.
///
/// <para>⛔⛔ <b>PRODUCTION-BUILT</b> — 📌 <c>R-67</c>. ⚠ A hand-built registry would pass while the
/// editor offered nothing.</para>
/// </summary>
public sealed class TheViewsCameFromTheirOwnWindowsTests
{
    private static PerspectiveWorkspaceRegistrar Production(string perspective, EditorSelectionStore store)
    {
        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false);

        return services.CreateRegistrar(
            perspective, store, validators: Array.Empty<IAssetValidator>());
    }

    private static DetailsContext Ctx(
        EditorSelectionStore store, string perspective, VariableRunState mode)
        => DetailsContextBuilder.Build(store, perspective, mode);

    private sealed class PaneFor : IRuntimeInspectorPane
    {
        public PaneFor(AssetKind kind) => TargetKind = kind;
        public AssetKind TargetKind { get; }
        public int Draws { get; private set; }
        public void Draw() => Draws++;
    }

    // ══ L3.1 — the runtime pane becomes a predicated view ════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Registering a pane contributes its Details view — through the seam the ROOT ALREADY
    /// CALLS.</b> 📌 <c>R-67</c>: <c>EditorSubsystem</c>'s three <c>RegisterPane</c> lines are
    /// unchanged, so there is nothing new for a composition root to forget.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    [InlineData("Blueprint")]
    public void RegisteringAPane_AlsoRegistersItsDetailsView(string perspective)
    {
        var registrar = Production(perspective, new EditorSelectionStore());

        registrar.RuntimeInspector.RegisterPane(new PaneFor(AssetKind.BTree));

        Assert.Contains(
            registrar.DetailsViews.All,
            d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§6 <c>L3</c>'s predicate, both clauses: <c>Mode != Planning</c> ∧ its asset kind.</b>
    ///
    /// <para>⭐⭐ The <c>Mode</c> clause is what stops a view claiming the panel in order to apologise —
    /// 📐 the old window drew its pane while PLANNING and the pane then said <i>"No live BTree
    /// state."</i> from inside, which is <c>R-117</c>'s blank one level down.</para>
    /// </summary>
    [Fact]
    public void TheRuntimeView_NeedsBothALiveModeAndItsOwnKind()
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset
                { Kind = AssetKind.BTree } };

        // ⛔ PLANNING — nothing live to show.
        Assert.False(RuntimeDetailsViewDescriptor.Applies(
            Ctx(store, "BTree", VariableRunState.Planning), AssetKind.BTree));

        // ⭐ live ∧ the right kind.
        Assert.True(RuntimeDetailsViewDescriptor.Applies(
            Ctx(store, "BTree", VariableRunState.Running), AssetKind.BTree));

        // ⛔ live, WRONG kind — 📌 R-112: the kind clause lives in the view's own predicate.
        Assert.False(RuntimeDetailsViewDescriptor.Applies(
            Ctx(store, "BTree", VariableRunState.Running), AssetKind.Hsm));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The thing the OLD registry made impossible: TWO views claiming ONE kind.</b>
    /// 📄 §4 — <c>_panes.Find(p =&gt; p.TargetKind == asset.Kind)</c> put kind on the REGISTRY'S axis, so
    /// exactly one pane per kind could ever be reached. ⭐ As a predicate, kind is one clause and any
    /// number of views may claim it.
    ///
    /// <para>⚠ This is the rail that shows <c>R-112</c> bought something, ⛔ rather than moving a
    /// lookup from one file to another.</para>
    /// </summary>
    [Fact]
    public void TwoViewsMayClaimTheSameAssetKind_WhichTheOldRegistryForbade()
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset
                { Kind = AssetKind.BTree } };
        var registrar = Production("BTree", store);
        registrar.RuntimeInspector.RegisterPane(new PaneFor(AssetKind.BTree));

        // ⭐ a SECOND, unrelated view that also claims BTree — the old shape had no way to express this
        registrar.DetailsViews.Add(new DetailsViewDescriptor(
            "test.second.btree", "Second", 40,
            c => DetailsViewPredicates.AssetKindIs(c, AssetKind.BTree),
            () => new NullInstance()));

        var offered = registrar.DetailsViews.OfferSet(Ctx(store, "BTree", VariableRunState.Running));

        Assert.Contains(offered, d => d.Id == RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree));
        Assert.Contains(offered, d => d.Id == "test.second.btree");
    }

    /// <summary>
    /// ⛔⛔ <b>TWO panes for ONE kind now THROW — it used to be silent.</b>
    /// 📐 Measured: <c>_panes.Find</c> returned the FIRST match, so the second pane simply never drew.
    /// ⚠ That is a wiring bug wearing a working editor. 📌 The <c>G4</c> precedent: an id collision
    /// must fail where it is wired.
    /// </summary>
    [Fact]
    public void TwoPanesForOneKind_ThrowAtRegistration()
    {
        var registrar = Production("BTree", new EditorSelectionStore());
        registrar.RuntimeInspector.RegisterPane(new PaneFor(AssetKind.BTree));

        var ex = Assert.Throws<InvalidOperationException>(
            () => registrar.RuntimeInspector.RegisterPane(new PaneFor(AssetKind.BTree)));
        Assert.Contains("details.runtime", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ …and three panes for three DIFFERENT kinds — what production actually does — is
    /// fine, and yields three views.</summary>
    [Fact]
    public void ThreePanesForThreeKinds_YieldThreeViews()
    {
        var registrar = Production("BTree", new EditorSelectionStore());
        foreach (var k in new[] { AssetKind.BTree, AssetKind.Hsm, AssetKind.Blueprint })
            registrar.RuntimeInspector.RegisterPane(new PaneFor(k));

        Assert.Equal(3, registrar.DetailsViews.All.Count(d => d.Id.StartsWith("details.runtime", StringComparison.Ordinal)));
    }

    /// <summary>
    /// ⭐⭐ <b>The view DELEGATES to the pane — ⛔ it is not a second renderer</b> *(ruling 9)*.
    /// ⚠ Asserted through the descriptor's factory, so the rail exercises what the shell would build.
    /// </summary>
    [Fact]
    public void TheRuntimeView_DrawsThroughTheOnePane()
    {
        var pane = new PaneFor(AssetKind.BTree);
        var view = RuntimeDetailsViewDescriptor.For(pane).Create();

        view.Draw(DetailsContext.Empty("BTree"), "scope");

        Assert.Equal(1, pane.Draws);
    }

    // ══ L3.3 — the Blackboard window becomes a view ══════════════════════════

    /// <summary>
    /// ⭐⭐ <b><c>L3.3</c> — the Blackboard view reaches the catalogue through the PRODUCTION
    /// registrar</b>, on every perspective *(it is built unconditionally, unlike <c>Details</c>)*.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    [InlineData("Blueprint")]
    public void TheProductionRegistrar_RegistersTheBlackboardView(string perspective)
        => Assert.Contains(
            Production(perspective, new EditorSelectionStore()).DetailsViews.All,
            d => d.Id == BlackboardDetailsViewDescriptor.ViewId);

    /// <summary>
    /// ⭐ Its predicate is §6 <c>L3</c>'s <i>"asset context"</i> — ⛔ nothing open ⇒ it declines, and
    /// 📌 <c>R-117</c>'s grey line answers instead.
    /// </summary>
    [Fact]
    public void TheBlackboardView_NeedsAnOpenDocument()
    {
        var store     = new EditorSelectionStore();
        var registrar = Production("BTree", store);

        Assert.DoesNotContain(
            registrar.DetailsViews.OfferSet(Ctx(store, "BTree", VariableRunState.Planning)),
            d => d.Id == BlackboardDetailsViewDescriptor.ViewId);

        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();

        Assert.Contains(
            registrar.DetailsViews.OfferSet(Ctx(store, "BTree", VariableRunState.Planning)),
            d => d.Id == BlackboardDetailsViewDescriptor.ViewId);
    }

    // ══ ranks — the DEFAULT a live session gets ══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>While a session is LIVE, Runtime is the default</b> — 📌 <c>R-98</c>: rank decides the
    /// default only, and §2b's toolbar pick still wins.
    ///
    /// <para>⚠ Asserted on the production registry with the real ranks, ⛔ not on the constants — a
    /// rank table that agrees with itself but not with the offer set would pass a constant check.</para>
    /// </summary>
    [Fact]
    public void WhileRunning_TheRuntimeViewIsTheDefault()
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset
                { Kind = AssetKind.BTree } };
        var registrar = Production("BTree", store);
        registrar.RuntimeInspector.RegisterPane(new PaneFor(AssetKind.BTree));

        var running = registrar.DetailsViews.Default(Ctx(store, "BTree", VariableRunState.Running));
        Assert.Equal(RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree), running!.Id);

        // ⭐ …and while PLANNING it is not even offered, so the Blackboard view leads.
        var planning = registrar.DetailsViews.Default(Ctx(store, "BTree", VariableRunState.Planning));
        Assert.Equal(BlackboardDetailsViewDescriptor.ViewId, planning!.Id);
    }

    private sealed class NullInstance : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }
}
