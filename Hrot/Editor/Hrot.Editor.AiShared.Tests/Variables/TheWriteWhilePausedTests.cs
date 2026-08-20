using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;
using StructEdit.Reflection;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 84 item 3 — the write target follows the run state, and the OK path EXISTS.</b>
///
/// <para>📌 <b>Ruling 15</b> <i>(user, and it NARROWS ruling 7)</i>: <i>"the change of runtime var
/// makes sense <b>ONLY if sim is paused on breakpoint or deterministic time step</b>. at that time
/// nothing else changes the blackboard."</i> ⇒ ⛔ <b>free-running REFUSES — a decision, not a later
/// batch.</b> · 📌 <b>Ruling 11:</b> the Watch SHARES this mechanism.</para>
///
/// <para>🔴🔴 <b>What measuring found before any of this was built.</b> <c>VariableEditCommit</c>
/// shipped complete and tested in Batch 83 with <b>ZERO production call sites</b> — 📐 measured: the
/// gesture binder opened a dialog session and <b>nothing ever committed it</b>. ⇒ ⛔ even the
/// NOT-RUNNING write Batch 83 reported as landed <b>could not land</b>: the dialog opened, the designer
/// typed, and the value went nowhere. ⚠⚠ <b>The twelfth instance of this programme's recurring shape,
/// and it was in my own previous batch — exactly what <c>R-67</c> predicts of rails that build the
/// thing they assert on.</b></para>
/// </summary>
public sealed class TheWriteWhilePausedTests
{
    // ══ the target matrix — ruling 15 ════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Planning ⇒ the declaration · Paused ⇒ the live blackboard · Running / Replay ⇒
    /// NOWHERE.</b> 📌 Ruling 15's narrowing of ruling 7, in one table.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Planning, VariableEditCommit.Target.InitialValue)]
    [InlineData(VariableRunState.Paused,   VariableEditCommit.Target.LiveBlackboard)]
    [InlineData(VariableRunState.Running,  VariableEditCommit.Target.Nowhere)]
    [InlineData(VariableRunState.Replay,   VariableEditCommit.Target.Nowhere)]
    public void TheWriteTargetFollowsTheRunState(VariableRunState run, VariableEditCommit.Target expected)
        => Assert.Equal(expected, VariableEditCommit.TargetFor(run));

    /// <summary>
    /// ⭐⭐ <b>And it still cannot disagree with the displayed value.</b> 📌 The Value column and the
    /// write target both descend from <see cref="VariableValue.ModeFor"/> — ⛔ if the cell shows the
    /// INITIAL value, the edit writes the initial value, in every run state.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Running)]
    [InlineData(VariableRunState.Paused)]
    [InlineData(VariableRunState.Replay)]
    public void TargetInitial_ExactlyWhenTheCellShowsTheInitialArm(VariableRunState run)
        => Assert.Equal(
            VariableValue.ModeFor(run) == VariableValueMode.Initial,
            VariableEditCommit.TargetFor(run) == VariableEditCommit.Target.InitialValue);

    // ══ the live arm ═════════════════════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>RED before Batch 84.</b> While FROZEN the edit goes to the live blackboard, as bytes,
    /// through the caller's writer. ⛔ Not to the declaration — the designer is changing THIS run.
    /// </summary>
    [Fact]
    public void WhilePaused_TheEditGoesToTheLiveBlackboard()
    {
        var asset = Asset();
        byte[]? written = null;

        using var session = new ComponentEditServiceBuilder().Build()
            .Open(4242, typeof(int), EditScope.WholeComponent);

        var outcome = VariableEditCommit.Commit(
            session, asset, Row("Health", typeof(int)), typeof(int), VariableRunState.Paused,
            writeLive: (row, bytes) => { written = bytes.ToArray(); return true; });

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        Assert.Equal(4242, BitConverter.ToInt32(written!));
        Assert.Empty(asset.WrittenJson);   // ⛔ the declaration is NOT touched by a live edit
    }

    /// <summary>
    /// ⛔⛔ <b>Free-running REFUSES.</b> 📌 Ruling 15 — <i>"nothing else changes the blackboard"</i> is
    /// only true while frozen; a write into a running sim races the systems that own the field.
    /// ⭐ And the writer is never even consulted.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Running)]
    [InlineData(VariableRunState.Replay)]
    public void WhileFreeRunning_TheLiveWriteRefuses_AndTheWriterIsNotCalled(VariableRunState run)
    {
        var called = false;
        using var session = new ComponentEditServiceBuilder().Build()
            .Open(1, typeof(int), EditScope.WholeComponent);

        var outcome = VariableEditCommit.Commit(
            session, Asset(), Row("Health", typeof(int)), typeof(int), run,
            writeLive: (_, __) => { called = true; return true; });

        Assert.Equal(VariableEditCommit.Outcome.RefusedRunning, outcome);
        Assert.False(called, "Ruling 15 forbids a live write while free-running; the writer must not run.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A missing live writer is <c>LiveWriteUnavailable</c>, ⛔ NOT a quiet refusal.</b>
    ///
    /// <para>📌 <c>CLAUDE.md</c>'s silent-default pattern: the run state SAID the write may land and
    /// the mechanism did not arrive. ⚠ Collapsing that into <c>RefusedRunning</c> would make an
    /// unwired host look like a correctly-refusing one — which is how four batches of this programme
    /// shipped capabilities nothing constructed.</para>
    /// </summary>
    [Fact]
    public void WhilePausedWithNoWriter_TheOutcomeNamesTheMissingMechanism()
    {
        using var session = new ComponentEditServiceBuilder().Build()
            .Open(1, typeof(int), EditScope.WholeComponent);

        Assert.Equal(
            VariableEditCommit.Outcome.LiveWriteUnavailable,
            VariableEditCommit.Commit(
                session, Asset(), Row("Health", typeof(int)), typeof(int),
                VariableRunState.Paused, writeLive: null));
    }

    /// <summary>⛔ A node-owned or passthrough row is not writable while paused either.</summary>
    [Theory]
    [InlineData(VariableRowKind.NodeOwned)]
    [InlineData(VariableRowKind.ReadOnlyPassthrough)]
    public void ANonWritableRow_IsNotWritableWhilePausedEither(VariableRowKind kind)
    {
        var called = false;
        using var session = new ComponentEditServiceBuilder().Build()
            .Open(1, typeof(int), EditScope.WholeComponent);

        var outcome = VariableEditCommit.Commit(
            session, Asset(), Row("Health", typeof(int), kind), typeof(int),
            VariableRunState.Paused, writeLive: (_, __) => { called = true; return true; });

        Assert.Equal(VariableEditCommit.Outcome.RefusedReadOnly, outcome);
        Assert.False(called);
    }

    /// <summary>
    /// ⚠ <b>A writer that REFUSES is reported, not swallowed.</b> 📌 The session-side writer answers
    /// <c>false</c> when the sim is not actually frozen — ⭐ the editor's run state and the session's
    /// pause flag are two observations, and disagreement must surface rather than look like success.
    /// </summary>
    [Fact]
    public void AWriterThatRefuses_IsReported()
    {
        using var session = new ComponentEditServiceBuilder().Build()
            .Open(1, typeof(int), EditScope.WholeComponent);

        Assert.Equal(
            VariableEditCommit.Outcome.LiveWriteUnavailable,
            VariableEditCommit.Commit(
                session, Asset(), Row("Health", typeof(int)), typeof(int),
                VariableRunState.Paused, writeLive: (_, __) => false));
    }

    // ══ the OK path — the seam that did not exist ════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before Batch 84, and it could not have been written before</b> — there was no
    /// <c>Accept</c>. ⭐ One gesture, one Accept, and the value lands.
    /// </summary>
    [Fact]
    public void AGestureThenAccept_LandsTheEdit_InPlanning()
    {
        var asset  = Asset();
        var binder = Binder(asset, () => VariableRunState.Planning, out _);

        binder.OnEditValue(Row("Health", typeof(int)));
        var outcome = binder.Accept();

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        Assert.Equal("0", asset.WrittenJson["Health"]);
    }

    /// <summary>⭐ And while frozen the same gesture lands on the live blackboard instead.</summary>
    [Fact]
    public void AGestureThenAccept_LandsLive_WhilePaused()
    {
        var asset  = Asset();
        var binder = Binder(asset, () => VariableRunState.Paused, out var live);

        binder.OnEditValue(Row("Health", typeof(int)));

        Assert.Equal(VariableEditCommit.Outcome.Ok, binder.Accept());
        Assert.Single(live);
        Assert.Empty(asset.WrittenJson);
    }

    /// <summary>⭐⭐ <b>Cancel lands NOTHING</b>, in either arm.</summary>
    [Fact]
    public void Cancel_LandsNothing()
    {
        var asset  = Asset();
        var binder = Binder(asset, () => VariableRunState.Planning, out var live);

        binder.OnEditValue(Row("Health", typeof(int)));
        binder.Cancel();

        Assert.Empty(asset.WrittenJson);
        Assert.Empty(live);
        Assert.Null(binder.ActiveSession);
    }

    /// <summary>
    /// ⭐⭐ <b>Accept is spent.</b> ⛔ A second Accept must not re-apply a stale edit — the session is
    /// closed either way, so the dialog cannot land twice from one open.
    /// </summary>
    [Fact]
    public void ASecondAccept_LandsNothingMore()
    {
        var asset  = Asset();
        var binder = Binder(asset, () => VariableRunState.Paused, out var live);

        binder.OnEditValue(Row("Health", typeof(int)));
        binder.Accept();
        var second = binder.Accept();

        Assert.Single(live);
        Assert.Equal(VariableEditCommit.Outcome.RefusedReadOnly, second);
    }

    /// <summary>⛔ Accept with no open session does nothing and does not throw.</summary>
    [Fact]
    public void AcceptWithNoSession_IsInert()
    {
        var binder = Binder(Asset(), () => VariableRunState.Planning, out var live);

        Assert.Equal(VariableEditCommit.Outcome.RefusedReadOnly, binder.Accept());
        Assert.Empty(live);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>⭐ The SAME fake the row-59 rails use — ⛔ not a second one that can drift.</summary>
    private static TheEditDialogReachesTheDesignerTests.FakeAsset Asset()
        => TheEditDialogReachesTheDesignerTests.FakeAsset.With(
               new BlackboardVariableEntry("Health", typeof(int), null));

    private static VariableEditGestureBinder Binder(
        TheEditDialogReachesTheDesignerTests.FakeAsset asset, Func<VariableRunState> runState, out List<byte[]> liveWrites)
    {
        var writes = new List<byte[]>();
        liveWrites = writes;
        return new VariableEditGestureBinder(
            new VariableEditLauncher(new ComponentEditServiceBuilder().Build()),
            entryResolver: row => new BlackboardVariableEntry(row.ShortName, typeof(int), null),
            runState:  runState,
            assetOf:   _ => asset,
            writeLive: (_, bytes) => { writes.Add(bytes.ToArray()); return true; });
    }

    private static VariableRow Row(string name, Type clr, VariableRowKind kind = VariableRowKind.Normal)
        => new(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "vars", name, "Asset"),
            ShortName: name,
            TypeText:  clr.Name,
            ClrType:   clr,
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   kind);

}
