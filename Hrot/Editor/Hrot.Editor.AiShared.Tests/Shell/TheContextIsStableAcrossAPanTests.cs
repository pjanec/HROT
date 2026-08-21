using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Tests.Selection;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L0</c>'s rail, the CONTEXT half.</b> 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6
/// <c>L0</c>, verbatim: <i>"a marquee of two yields a 2-item context; <b>a pan yields the same context
/// object as the frame before</b>."</i>
///
/// <para>⭐⭐ <b>Asserted on the returned MODEL</b> — 📌 §6: <i>"every task's rail asserts on a store or
/// a returned model; the draw is unrailed by construction"</i> *(<c>R-21</c>/<c>R-62</c>)*.</para>
/// </summary>
public sealed class TheContextIsStableAcrossAPanTests
{
    private sealed record NodeSel(int Id) : IAssetSubSelection;

    private static EditorSelectionStore Store()
        => new() { ActiveAsset = new EditorSelectionStoreTests.FakeAsset() };

    private static DetailsContext Build(EditorSelectionStore s)
        => DetailsContextBuilder.Build(s, "Blueprint", VariableRunState.Planning);

    /// <summary>⭐⭐ <b>A marquee of two yields a 2-item context</b> — the design's first clause.</summary>
    [Fact]
    public void AMarqueeOfTwo_YieldsATwoItemContext()
    {
        var store = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        var ctx = Build(store);

        Assert.Equal(2, ctx.Selection.Count);
        Assert.Equal("Blueprint", ctx.Perspective);
    }

    /// <summary>
    /// ⛔⛔⛔ <b>THE PAN — the design's second clause, and the defect <c>L0.2</c> exists to fix.</b>
    ///
    /// <para>⭐ The bridge re-reports the identical selection in a FRESH list *(as it does every
    /// frame)*, and the context built afterwards must be indistinguishable from the one before.</para>
    ///
    /// <para>⭐⭐ <b>Both equalities are asserted, and they catch different failures:</b> value equality
    /// would still hold if the store re-stored an equal-but-new list, ⇒ ⛔ the SELECTION INSTANCE check
    /// is what pins §6 <c>L0.4</c>'s <i>"same list instance when unchanged"</i> and stops every view
    /// rebuilding per frame.</para>
    /// </summary>
    [Fact]
    public void APan_YieldsAnEqualContext_WithTheSameSelectionInstance()
    {
        var store = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };
        var before = Build(store);

        // ⭐ the pan: same selection, freshly allocated, exactly as a bridge reports it
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };
        var after = Build(store);

        Assert.Equal(before, after);                              // ⭐ record value equality
        Assert.Same(before.Selection, after.Selection);           // ⛔ the instance did not churn
    }

    /// <summary>⭐ …and a REAL selection change yields a DIFFERENT context — so the rail above is not
    /// passing because everything compares equal.</summary>
    [Fact]
    public void AGenuineChange_YieldsADifferentContext()
    {
        var store = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1) };
        var before = Build(store);

        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        Assert.NotEqual(before, Build(store));
    }

    /// <summary>
    /// ⚠ <b>A FOCUS change alone yields a different context, with the selection untouched.</b>
    /// 📌 <c>R-115</c>: <i>"context = focus + selection"</i> as TWO independent fields — ⛔ a context
    /// that ignored focus would route the panel to the wrong surface mid-edit.
    /// </summary>
    [Fact]
    public void AFocusChangeAlone_ChangesTheContext_ButNotTheSelection()
    {
        var store = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1) };
        var before = Build(store);

        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);
        var after = Build(store);

        Assert.NotEqual(before, after);
        Assert.Equal(SelectionOrigin.VariableOutline, after.Focus);
        Assert.Same(before.Selection, after.Selection);   // ⭐ the selection did not move
    }

    /// <summary>
    /// ⭐ <b>All five live sources are present</b> — §6 <c>L0.3</c>'s acceptance clause.
    /// ⚠ <c>Entities</c> is fed from the store's single selected entity until <c>L0.4</c> re-points it
    /// at the World *(<c>R-122</c>)*; ⛔ this rail pins that it is WIRED, not that it is final.
    /// </summary>
    [Fact]
    public void AllFiveSourcesReachTheContext()
    {
        var store  = Store();
        var entity = new Entity(7, 1);
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1) };
        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);
        store.SelectedEntity = entity;

        var ctx = DetailsContextBuilder.Build(store, "HSM", VariableRunState.Paused);

        Assert.Equal(SelectionOrigin.GraphCanvas, ctx.Focus);      // ① focus
        Assert.Single(ctx.Selection);                              // ② selection
        Assert.Equal(entity, Assert.Single(ctx.Entities));         // ③ entities
        Assert.NotNull(ctx.Asset);                                 // ④ asset
        Assert.Equal(VariableRunState.Paused, ctx.Mode);           // ⑤ mode
        Assert.Equal("HSM", ctx.Perspective);
    }

    /// <summary>⚠ The entity list is stable too — ⛔ otherwise the pan guarantee would fail through the
    /// ENTITY field while the selection field held.</summary>
    [Fact]
    public void TheEntityListIsStable_WhenTheEntityDidNotChange()
    {
        var store = Store();
        store.SelectedEntity = new Entity(7, 1);

        Assert.Same(Build(store).Entities, Build(store).Entities);
    }
}
