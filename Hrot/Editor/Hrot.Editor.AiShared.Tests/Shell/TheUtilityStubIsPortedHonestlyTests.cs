using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>S3</c> — <c>details.utility</c>, AND IT IS HONEST ABOUT BEING A STUB.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.6 ③, verbatim: <i>"port it honestly as a stub,
/// ⛔ do not pretend it is a feature."</i>
///
/// <para>⛔⛔ <b>The rail that carries the finding:</b> the arm was UNREACHABLE — 📐 measured
/// <c>2026-08-22</c>, <see cref="UtilityConsiderationSelection"/> has two C# sites in the whole repo and
/// neither RAISES it. ⇒ ⭐ these rails are the first time its behaviour is asserted at all, and the
/// registration rail below is what makes the view reachable the day a producer lands.</para>
///
/// <para>⚠ <b>What they do NOT prove</b> *(📌 <c>R-21</c>/<c>R-62</c>)*: that anything is drawn. ⭐ They
/// prove the MODEL — when the view is offered, what it says, and that it is registered on every
/// perspective. ⛔ The pixels stay with the visual check, and there is nothing to look at until the
/// utility canvas exists.</para>
/// </summary>
public sealed class TheUtilityStubIsPortedHonestlyTests
{
    // ══ the predicate ════════════════════════════════════════════════════════

    /// <summary>⭐⭐ It claims a selected consideration.</summary>
    [Fact]
    public void ItAppliesToExactlyOneConsideration()
        => Assert.True(UtilityConsiderationDetailsViewDescriptor.Applies(
            ContextWith(new UtilityConsiderationSelection(2, 5))));

    /// <summary>⛔ <b>And nothing else</b> — the anti-vacuity half. ⚠ Without it the rail above would
    /// pass against a predicate that returned <c>true</c> for every selection, which is exactly the
    /// blank-panel defect <c>R-117</c> names one floor down.</summary>
    [Fact]
    public void ItDoesNotClaimAGraphNode()
        => Assert.False(UtilityConsiderationDetailsViewDescriptor.Applies(
            ContextWith(new BTreeNodeSelection(Guid.NewGuid()))));

    /// <summary>⛔ Empty selection ⇒ no claim; the shell draws <c>R-117</c>'s grey line instead.</summary>
    [Fact]
    public void ItDoesNotClaimAnEmptySelection()
        => Assert.False(UtilityConsiderationDetailsViewDescriptor.Applies(ContextWith()));

    /// <summary>
    /// ⛔⛔ <b>TWO considerations is not "the first one"</b> — 📌 <c>R-118</c>: the bridge REPORTS the
    /// set and the <c>Count == 1</c> rule lives in the predicate. ⚠ A single-consideration form cannot
    /// honestly describe two, so it declines.
    /// </summary>
    [Fact]
    public void TwoConsiderations_AreNotClaimed()
        => Assert.False(UtilityConsiderationDetailsViewDescriptor.Applies(ContextWith(
            new UtilityConsiderationSelection(0, 0),
            new UtilityConsiderationSelection(0, 1))));

    // ══ what it says ═════════════════════════════════════════════════════════

    /// <summary>⭐⭐ The index pair the retired arm showed, kept — 📌 §7.4's <c>..></c>: EXTRACTED, not
    /// reinvented. ⛔ Asserted on the MODEL, so it is railable without ImGui.</summary>
    [Fact]
    public void ItNamesTheOptionAndTheConsideration()
    {
        var line = UtilityConsiderationDetailsView.Describe(
            ContextWith(new UtilityConsiderationSelection(3, 7)));

        Assert.NotNull(line);
        Assert.Contains("3", line, StringComparison.Ordinal);
        Assert.Contains("7", line, StringComparison.Ordinal);
    }

    /// <summary>⛔ Nothing selected ⇒ nothing to describe. ⚠ The pair with the rail above: a stub that
    /// invented a line for an empty context would draw on a frame it must not.</summary>
    [Fact]
    public void WithoutAConsideration_ThereIsNothingToDescribe()
        => Assert.Null(UtilityConsiderationDetailsView.Describe(ContextWith()));

    /// <summary>
    /// ⭐⭐⭐ <b>THE HONESTY RAIL — it SAYS the editor is not built, and it POINTS at the design.</b>
    /// 📄 §7.6 ③'s <i>"do not pretend it is a feature."</i> ⛔ The retired arm drew a heading and an
    /// index pair and stopped, which reads as <i>"loading"</i>. ⚠ It also cited a phase id
    /// (<c>P5-02</c>) that does not exist in the utility-AI record — ⇒ ⭐ the replacement names a
    /// document a reader can actually open.
    /// </summary>
    [Fact]
    public void TheNoticeSaysItIsNotBuilt_AndNamesTheDesign()
    {
        Assert.Contains("not built",
            UtilityConsiderationDetailsView.NotBuiltNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("utility-ai",
            UtilityConsiderationDetailsView.NotBuiltNotice, StringComparison.Ordinal);
    }

    // ══ the descriptor ═══════════════════════════════════════════════════════

    /// <summary>⭐ §7.6 ③'s id and title, pinned — ⚠ the id is what the toolbar remembers per context
    /// key (<c>R-98</c>), so renaming it silently forgets every designer's pick.</summary>
    [Fact]
    public void TheDescriptorCarriesTheDesignsIdAndRank()
    {
        var d = UtilityConsiderationDetailsViewDescriptor.For();

        Assert.Equal("details.utility", d.Id);
        Assert.Equal(UtilityConsiderationDetailsViewDescriptor.Rank, d.Rank);
        Assert.False(string.IsNullOrWhiteSpace(d.Title));
    }

    /// <summary>
    /// ⭐⭐ <b>A FRESH instance per window</b> — 📌 <c>R-120</c>: a view owns no shared state, and two
    /// windows showing this view must not be one object. ⚠ Cheap to hold here even though this stub has
    /// no state: it is the property that must stay true when it grows one.
    /// </summary>
    [Fact]
    public void EachCreateYieldsItsOwnInstance()
    {
        var d = UtilityConsiderationDetailsViewDescriptor.For();
        Assert.NotSame(d.Create(), d.Create());
    }

    // ── helper ──────────────────────────────────────────────────────────────

    /// <summary>⭐ A context carrying just a selection — the only field this view reads.</summary>
    private static DetailsContext ContextWith(params IAssetSubSelection[] selection)
        => new(
            Focus:       SelectionOrigin.GraphCanvas,
            Selection:   selection,
            Entities:    Array.Empty<Fdp.Core.Entity>(),
            Asset:       null,
            Perspective: "BehaviorTree",
            Mode:        VariableRunState.Planning);
}
