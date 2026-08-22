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
    /// ⭐⭐⭐ <b><c>W3</c> INVERTED THIS RAIL, and it is the user-visible half of the whole batch.</b>
    ///
    /// <para>⚠⚠ It used to assert <i>"a free-running refusal still greys OK rather than hiding it"</i>.
    /// 📌 <c>R-126</c>, the user: <i>"I do not understand how comes that something can be unwritable…
    /// we should be able to write anything anywhere"</i> ⇒ ⛔ <b>there is no free-running refusal any
    /// more</b>: the edit STAGES and the kernel's drain applies it at the next advancing tick.</para>
    ///
    /// <para>⭐ <b>The distinction the old rail protected SURVIVES and is still asserted</b> — a row that
    /// can never be written opens as a VIEW with no OK at all *(the rail above)*, which is a different
    /// thing from an editor whose OK is live. ⛔ That was the pairing worth keeping.</para>
    /// </summary>
    [Fact]
    public void WhileFreeRunning_TheDialogIsAnEditorWithALiveOk()
    {
        var (modal, binder) = Make(VariableRunState.Running);

        binder.OnEditValue(Row());

        Assert.True(modal.IsOpen);
        Assert.False(modal.IsReadOnlyView);                  // ⭐ an editor, not a view
        Assert.True(modal.CanCommit);                        // ⭐⭐ …and OK is LIVE (W3)
        Assert.Null(modal.CommitRefusalReason);
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
    /// <summary>
    /// ⛔ <b><c>Replay</c> offers no live OK.</b> ⚠ 📐 It has no production producer
    /// *(<c>RunStateSource.Resolve</c> yields only Planning/Paused/Running)*, so this pins a DECISION,
    /// not a reachable path.
    /// </summary>
    [Fact]
    public void WhileReplaying_TheDialogOffersNoLiveOk()
    {
        var (modal, binder) = Make(VariableRunState.Replay);
        binder.OnEditValue(Row());

        Assert.False(modal.CanCommit);
    }

    /// <summary>
    /// ⚠⚠⚠ <b>A FINDING, RAILED: after <c>W3</c> NO run state produces a greyed-OK-with-tooltip.</b>
    ///
    /// <para>📐 <b>Measured.</b> <c>CommitRefusalReason</c> needs an ACTIVE SESSION <b>and</b>
    /// <c>TargetFor(...) == Nowhere</c>. ⭐ <c>W3</c> left <c>Nowhere</c> to <c>Replay</c> alone, and
    /// <c>VariableEditPolicy.Resolve</c> answers <c>Denied</c> for <c>Replay</c> — so no session opens
    /// and the tooltip can never be produced. ⇒ ⛔ <b>the greying path is now unreachable for the value
    /// dialog.</b></para>
    ///
    /// <para>⭐⭐ <b>It is NOT deleted, and that is deliberate</b> — 📌 <c>CLAUDE.md</c>'s
    /// <i>"unreferenced is not unintentional"</i>: the affordance exists because of a USER RULING
    /// *(<c>2026-08-17</c>: "showing explanatory tooltip would be better than allowing user to click the
    /// button and then saying that it is not possible")*, which nothing retracts. ⚠ Filed as
    /// <c>BP-411</c> for a decision, ⛔ not removed on my own initiative.</para>
    ///
    /// <para>⭐ <b>This rail is the tripwire:</b> it reddens the moment a run state routes to
    /// <c>Nowhere</c> AND opens a session again — which is exactly when the machinery would matter.</para>
    /// </summary>
    [Fact]
    public void AfterW3_NoRunStateProducesAGreyedOkTooltip()
    {
        foreach (VariableRunState run in Enum.GetValues(typeof(VariableRunState)))
        {
            var (modal, binder) = Make(run);
            binder.OnEditValue(Row());

            Assert.Null(modal.CommitRefusalReason);
        }
    }

    /// <summary>⭐ Planning, Paused AND (since <c>W3</c>) Running all COMMIT — ⛔ the greying must be the
    /// exception, or the dialog is useless in the states the design allows.</summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Paused)]
    [InlineData(VariableRunState.Running)]
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

    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED, Batch 99 (<c>99a</c>) — <c>VariableEditModal</c> is the VALUE dialog, and
    /// "Properties…" must NOT reach it.</b>
    ///
    /// <para>📌 <c>R-108</c>: <i>"the two menu items are TWO OBJECTS, not two SCOPES"</i> · 📌
    /// <c>R-109</c>: the declaration <b>cannot be a StructEdit document</b>, because <c>Name</c> is a
    /// RENAME and <c>Type</c> is a RETYPE MIGRATION. ⇒ ⛔ <b>the old assertion — <i>"both scopes drive
    /// ONE dialog"</i> — was <c>BP-359</c> written down as a requirement</b>, and it was the design of
    /// the day *(§3: <i>two menu items = the two <c>EditScope</c>s</i>)*, superseded.</para>
    ///
    /// <para>⭐ <b>What survives is the lifecycle claim</b>, which was always the useful half: ONE
    /// dialog, reopenable, with one OK/Cancel. ⛔ It is now asserted on the value gesture ALONE, and
    /// the Properties gesture is fenced with its absence in the same rail — so a fallthrough that
    /// restored the old behaviour reddens HERE, not only in the binder's own file.</para>
    /// </summary>
    [Fact]
    public void ThePropertiesGestureNeverReachesTheValueDialog()
    {
        var (modal, binder) = Make(VariableRunState.Planning);

        binder.OnProperties(Row());
        Assert.False(modal.IsOpen);        // ⛔ the declaration is not a struct — no value document

        // ⭐ …and the VALUE gesture still drives the one dialog, open → Cancel → open again.
        binder.OnEditValue(Row());
        Assert.True(modal.IsOpen);
        modal.Cancel();
        Assert.False(modal.IsOpen);

        binder.OnEditValue(Row());
        Assert.True(modal.IsOpen);
    }
}
