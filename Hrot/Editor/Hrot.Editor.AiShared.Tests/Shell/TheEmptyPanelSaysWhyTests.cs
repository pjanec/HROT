using System;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L2.3</c>'s rail, verbatim from the design:</b> 📄
/// <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L2</c> —
/// <i>"an empty offer set returns the grey string."</i>
///
/// <para>⭐⭐ <b>RETURNS</b>, not <i>draws</i> — 📌 §6: <i>"every task's rail asserts on a store or a
/// returned model; the draw is unrailed by construction"</i> *(<c>R-21</c>/<c>R-62</c>)*. ⇒ the string
/// is the deliverable and this rail is a value assertion.</para>
///
/// <para>⛔ 📌 <c>R-117</c>: <i>"a blank panel is a defect."</i> ⚠ The defect is the BLANK — an empty
/// offer set is a perfectly good answer, and §2b's first sequence ends on exactly that.</para>
/// </summary>
public sealed class TheEmptyPanelSaysWhyTests
{
    // ══ the two sentences are two FACTS ══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Nothing open and nothing applicable are DIFFERENT sentences.</b>
    /// 📌 <c>R-118</c>'s lesson applied to prose: that ruling deleted a <c>null</c> that meant three
    /// things at once. ⚠ Here the two facts have <b>different remedies</b> — open a document · select
    /// something else — so a designer told the wrong one looks in the wrong place.
    /// </summary>
    [Fact]
    public void NoDocument_AndNothingApplicable_AreDifferentSentences()
    {
        Assert.NotEqual(DetailsEmptyState.NoDocument, DetailsEmptyState.NothingForThisSelection);

        // ⛔ Neither may be blank — that IS the defect R-117 names.
        Assert.False(string.IsNullOrWhiteSpace(DetailsEmptyState.NoDocument));
        Assert.False(string.IsNullOrWhiteSpace(DetailsEmptyState.NothingForThisSelection));
    }

    /// <summary>⭐ No asset ⇒ the <i>"open something"</i> sentence.</summary>
    [Fact]
    public void WithNoAssetOpen_TheDocumentSentenceIsReturned()
        => Assert.Equal(
            DetailsEmptyState.NoDocument,
            DetailsEmptyState.For(DetailsContext.Empty("BTree")));

    /// <summary>⭐ A document IS open ⇒ the <i>"nothing claims this selection"</i> sentence.</summary>
    [Fact]
    public void WithADocumentOpen_TheSelectionSentenceIsReturned()
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };

        var ctx = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning);

        Assert.Equal(DetailsEmptyState.NothingForThisSelection, DetailsEmptyState.For(ctx));
    }

    /// <summary>
    /// ⚠ <b>A null context still answers</b> — ⛔ never a <c>NullReferenceException</c> and never an
    /// empty string. 📌 <c>R-117</c> is about what the designer SEES; a throw inside a draw shows them
    /// nothing at all, which is the blank by another route.
    /// </summary>
    [Fact]
    public void ANullContext_StillReturnsASentence()
        => Assert.Equal(DetailsEmptyState.NoDocument, DetailsEmptyState.For(null!));

    // ══ the float's own grey line — R-117's SECOND site ══════════════════════

    /// <summary>
    /// ⭐⭐ <b><c>R-117</c> names TWO sites</b>: <i>"empty offer set · a float whose predicate is
    /// false."</i> ⚠ The float's sentence NAMES THE VIEW, because §2's hosting table keeps it
    /// <i>"open, grey line"</i> — ⛔ a float that says only <i>"nothing to show"</i> reads as stuck.
    /// </summary>
    [Fact]
    public void AnInapplicableFloat_NamesTheViewItIsIdling()
    {
        var line = DetailsEmptyState.ForInapplicableFloat("Variables");

        Assert.Contains("Variables", line, StringComparison.Ordinal);
        Assert.NotEqual(DetailsEmptyState.NothingForThisSelection, line);
    }

    /// <summary>⭐ …and with no title to name, it falls back rather than printing an empty name.</summary>
    [Fact]
    public void AFloatWithNoTitle_FallsBackToTheGenericSentence()
        => Assert.Equal(
            DetailsEmptyState.NothingForThisSelection,
            DetailsEmptyState.ForInapplicableFloat("  "));

    // ⛔⛔ CE-303 — THE SECOND SITE IS GONE, and so are the two rails that covered it.
    //    They asserted RuntimeInspectorWindow.EmptyState() telling "no document" from
    //    "nothing claims this kind" apart. 🔒 DESIGN_Details_Panel_View_Switching.md §4
    //    (closed question Q-iii) says that window DISSOLVES into three predicated views,
    //    and 2026-09-21 finally applied it.
    // ⭐ The BEHAVIOUR did not move to nowhere: the shell now decides both sentences, and
    //    the rails above this line are exactly that — NoDocument_AndNothingApplicable_Are
    //    DifferentSentences and its two neighbours. ⚠ What is genuinely lost is the
    //    per-KIND clause, which is now a view PREDICATE and is railed as one
    //    (TheRegistryOffersWhatApplies / RuntimeDetailsViewDescriptor).

    private sealed class PaneFor : IRuntimeInspectorPane
    {
        public PaneFor(AssetKind kind) => TargetKind = kind;
        public AssetKind TargetKind { get; }
        public void Draw(Hrot.Editor.AiShared.Shell.DetailsContext context) { }
    }
}
