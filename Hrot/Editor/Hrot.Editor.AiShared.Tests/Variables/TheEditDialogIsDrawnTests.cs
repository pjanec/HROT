using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 87 item 2a (<c>BP-327</c>) — the dialog's DECISIONS, headlessly.</b>
///
/// <para>🔴 <b>What was missing.</b> Batch 84 built the whole path — gesture → launcher → session →
/// <c>Accept</c> → the run-state arm → the declaration. ⛔ <c>Open</c> returned an <c>IEditSession</c>
/// and <b>nothing drew it</b>, so the designer never saw a dialog at all.</para>
///
/// <para>⭐⭐ <b>Every branch of <c>Draw</c> asks a property, and the properties are what these rails
/// interrogate.</b> ⛔ A decision taken inline in an ImGui call is a decision no test can reach —
/// which is how a surface ships invisible. ⚠ <b>What they cannot prove</b>, stated: that ImGui paints
/// the button. They prove the dialog KNOWS what to paint and commits through the one path.</para>
/// </summary>
public sealed class TheEditDialogIsDrawnTests
{
#pragma warning disable CS0649   // fields exist for their LAYOUT; StructEdit reflects them
    private struct DemoVar { public int Count; }
#pragma warning restore CS0649

    private static VariableRow Row()
        => new(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), new Entity(1, 1), "Variables", "Count", "Alpha"),
            ShortName: "Count", TypeText: "DemoVar", ClrType: typeof(DemoVar),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   VariableRowKind.Normal, IsStale: false);

    /// <summary>
    /// ⭐ Built with an ASSET, because a commit with none has nowhere to write the declaration and
    /// refuses <c>RefusedReadOnly</c>. ⚠ <b>This rail caught that itself</b> — the first version of
    /// <c>AnAcceptedCommitClosesTheDialog</c> supplied no asset and went red, which is the rail doing
    /// its job on its own author.
    /// ⛔ <c>writeLive</c> is deliberately NOT supplied: the paused arm must reach
    /// <c>LiveWriteUnavailable</c> below, which is the outcome the dialog has to render.
    /// </summary>
    private static (VariableEditModal Modal, VariableEditGestureBinder Binder) Make(
        VariableRunState runState)
    {
        // ⭐ The SAME fake the row-59 rails use — ⛔ not a second one that can drift.
        var asset  = TheEditDialogReachesTheDesignerTests.FakeAsset.With(
                         new BlackboardVariableEntry("Count", typeof(DemoVar), null));
        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new ComponentEditServiceBuilder().Build()),
            entryResolver: _ => new BlackboardVariableEntry("Count", typeof(DemoVar), Comment: null),
            runState:      () => runState,
            assetOf:       _ => asset);
        return (new VariableEditModal(binder, () => runState), binder);
    }

    /// <summary>⭐ A row whose KIND can never be written — the read-only-view arm's input.</summary>
    private static VariableRow ReadOnlyRow(VariableRowKind kind)
        => Row() with { RowKind = kind };

    // ══ Batch 96 §3b — a VIEW is shaped as a VIEW ════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A node-owned or passthrough row opens a READ-ONLY VIEW, with no OK.</b>
    ///
    /// <para>🔴 <b>The user's report:</b> the dialog opened, they clicked OK, and it said <i>"this row
    /// cannot be written"</i>. ⚠ <b>Opening is deliberate</b> — <c>VariableEditLauncher.Open</c>'s own
    /// comment says a read-only row still opens so the values can be READ. ⛔ Offering an OK button
    /// that then refuses is the false expectation, and 📌 the user's rule is <i>"same information
    /// value, no false expectations."</i></para>
    /// </summary>
    [Theory]
    [InlineData(VariableRowKind.NodeOwned)]
    [InlineData(VariableRowKind.ReadOnlyPassthrough)]
    public void ARowThatCanNeverBeWrittenOpensAsAView(VariableRowKind kind)
    {
        var (modal, binder) = Make(VariableRunState.Planning);

        binder.OnEditValue(ReadOnlyRow(kind));

        Assert.True(modal.IsOpen);
        Assert.True(modal.IsReadOnlyView);
        Assert.False(modal.CanCommit);                       // ⛔ no OK is drawn at all
        Assert.False(string.IsNullOrWhiteSpace(modal.ReadOnlyReason));
    }

    /// <summary>
    /// ⭐⭐ <b>The OTHER refusal keeps its greyed OK and its tooltip</b> — 📌 the <c>2026-08-17</c>
    /// ruling. ⚠ <b>The two are different and must stay different:</b> a free-running refusal is
    /// ACTIONABLE *(pause and it works)*, so the button belongs; a row that can never be written has
    /// nothing to act on, so it does not.
    /// </summary>
    [Fact]
    public void AFreeRunningRefusalStillGreysOkRatherThanHidingIt()
    {
        var (modal, binder) = Make(VariableRunState.Running);

        binder.OnEditValue(Row());

        Assert.True(modal.IsOpen);
        Assert.False(modal.IsReadOnlyView);                  // ⭐ an editor, not a view
        Assert.False(modal.CanCommit);                       // ⭐ …with OK greyed
        Assert.False(string.IsNullOrWhiteSpace(modal.CommitRefusalReason));
        Assert.Null(modal.ReadOnlyReason);
    }

    /// <summary>⭐ An ordinary planning-state row is a plain editor — ⛔ the read-only shaping must not
    /// swallow the case the dialog exists for.</summary>
    [Fact]
    public void AnOrdinaryRowIsStillAnEditor()
    {
        var (modal, binder) = Make(VariableRunState.Planning);

        binder.OnEditValue(Row());

        Assert.False(modal.IsReadOnlyView);
        Assert.True(modal.CanCommit);
        Assert.Null(modal.ReadOnlyReason);
    }

    // ══ the dialog follows the session ══════════════════════════════════════

    /// <summary>⛔ Nothing open ⇒ nothing drawn. ⚠ The guard <c>Draw</c> returns on.</summary>
    [Fact]
    public void WithNoSessionTheDialogIsClosed()
        => Assert.False(Make(VariableRunState.Planning).Modal.IsOpen);

    /// <summary>⭐⭐ A gesture opens the dialog — the link that did not exist.</summary>
    [Fact]
    public void AGestureOpensTheDialog()
    {
        var (modal, binder) = Make(VariableRunState.Planning);

        binder.OnEditValue(Row());

        Assert.NotNull(binder.ActiveSession);
        Assert.True(modal.IsOpen);
    }

    // ══ refusals are GREYED, with a reason (user ruling, 2026-08-17) ════════

    /// <summary>
    /// ⭐⭐⭐ <b>Free-running greys OK and says why BEFORE the click.</b> 📌 <b>User,
    /// <c>2026-08-17</c>:</b> <i>"showing explanatory tooltip would be better than allowing user to
    /// click the button and then saying that it is not possible — same information value, no false
    /// expectations."</i>
    /// </summary>
    [Fact]
    public void WhileRunningTheCommitIsGreyedWithAReason()
    {
        var (modal, binder) = Make(VariableRunState.Running);
        binder.OnEditValue(Row());

        Assert.False(modal.CanCommit);
        Assert.False(string.IsNullOrWhiteSpace(modal.CommitRefusalReason));
    }

    /// <summary>⭐ Planning and Paused both COMMIT — ⛔ the greying must be the exception, or the dialog
    /// is useless in the states ruling 15 allows.</summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Paused)]
    public void WhenTheRunStateAllowsIt_TheCommitIsLive(VariableRunState runState)
    {
        var (modal, binder) = Make(runState);
        binder.OnEditValue(Row());

        Assert.True(modal.CanCommit);
        Assert.Null(modal.CommitRefusalReason);
    }

    /// <summary>
    /// ⚠⚠ <b><c>LiveWriteUnavailable</c> is rendered AFTER the attempt, not before — and that ordering
    /// is the honest one.</b> The run state ALLOWED the write; the mechanism did not arrive. ⛔ Greying
    /// up front would have claimed to know something the dialog cannot know until it tries.
    /// </summary>
    [Fact]
    public void APausedCommitWithNoLiveWriter_ReportsItAfterTheAttempt()
    {
        var (modal, binder) = Make(VariableRunState.Paused);
        binder.OnEditValue(Row());

        Assert.True(modal.CanCommit);                 // ⭐ not greyed — the state allowed it
        var outcome = modal.Ok();

        Assert.Equal(VariableEditCommit.Outcome.LiveWriteUnavailable, outcome);
        Assert.True(modal.IsOpen, "a refused commit must keep the dialog up so the designer is told");
        Assert.False(string.IsNullOrWhiteSpace(modal.RefusalMessage));
    }

    // ══ OK / Cancel ═════════════════════════════════════════════════════════

    /// <summary>⭐ A successful commit closes the dialog and leaves no refusal behind.</summary>
    [Fact]
    public void AnAcceptedCommitClosesTheDialog()
    {
        var (modal, binder) = Make(VariableRunState.Planning);
        binder.OnEditValue(Row());

        var outcome = modal.Ok();

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        Assert.False(modal.IsOpen);
        Assert.Null(modal.RefusalMessage);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Guide <c>D7</c> — Cancel leaves the declaration UNTOUCHED.</b> ⭐ True by construction:
    /// the dialog never touches a declaration, it routes to
    /// <see cref="VariableEditGestureBinder.Cancel"/> which disposes the session uncommitted.
    /// </summary>
    [Fact]
    public void CancelDiscardsTheSessionAndCommitsNothing()
    {
        var (modal, binder) = Make(VariableRunState.Planning);
        binder.OnEditValue(Row());

        modal.Cancel();

        Assert.Null(binder.ActiveSession);
        Assert.False(modal.IsOpen);
        Assert.Null(binder.LastOutcome);   // ⛔ nothing was committed, not even a refusal
    }

    /// <summary>⭐ Both scopes drive ONE dialog — 📌 design §3, same lifecycle and same OK/Cancel.</summary>
    [Fact]
    public void BothGesturesDriveTheSameDialog()
    {
        var (modal, binder) = Make(VariableRunState.Planning);

        binder.OnProperties(Row());
        Assert.True(modal.IsOpen);
        modal.Cancel();
        Assert.False(modal.IsOpen);

        binder.OnEditValue(Row());
        Assert.True(modal.IsOpen);
    }
}
