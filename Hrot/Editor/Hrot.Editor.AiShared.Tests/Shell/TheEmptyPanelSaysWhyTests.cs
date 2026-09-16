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

    // ══ the second SITE the design names by line number ══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>RuntimeInspectorWindow</c>'s two arms said the SAME sentence for two DIFFERENT
    /// facts.</b> 📄 §6 <c>L2.3</c> names both by line — <c>:54</c> and <c>:67</c> — and both read
    /// <i>"No active session."</i>
    ///
    /// <para>📐 Measured: <c>:54</c> fires when <c>ActiveAsset</c> is null *(nothing open)*; <c>:67</c>
    /// when a document IS open and no pane claims its kind. ⚠ <b>Neither is about a session.</b>
    /// ⇒ ⭐ this rail is what stops them collapsing back together.</para>
    /// </summary>
    [Fact]
    public void TheRuntimeInspector_TellsTheTwoEmptyCasesApart()
    {
        var store  = new EditorSelectionStore();
        var window = new RuntimeInspectorWindow(store, new DebugSessionRegistry());

        // :54 — nothing is open at all.
        Assert.Equal(DetailsEmptyState.NoDocument, window.EmptyState());

        // :67 — a document is open, and no pane claims its kind.
        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset
            { Kind = AssetKind.Blueprint };
        Assert.Equal(DetailsEmptyState.NothingForThisSelection, window.EmptyState());
    }

    /// <summary>
    /// ⭐ …and with a matching pane there is no grey line at all — ⛔ <see langword="null"/> means
    /// <i>"draw the pane"</i>, which keeps the empty state a single decision rather than two.
    /// </summary>
    [Fact]
    public void TheRuntimeInspector_WithAMatchingPane_HasNoEmptyState()
    {
        var store  = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset
                { Kind = AssetKind.BTree } };
        var window = new RuntimeInspectorWindow(store, new DebugSessionRegistry());
        window.RegisterPane(new PaneFor(AssetKind.BTree));

        Assert.Null(window.EmptyState());
    }

    private sealed class PaneFor : IRuntimeInspectorPane
    {
        public PaneFor(AssetKind kind) => TargetKind = kind;
        public AssetKind TargetKind { get; }
        public void Draw() { }
    }
}
