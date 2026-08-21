using System;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Time;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>A component with one field the DESIGNER edits, at a known offset (ID 264).</summary>
[ComponentId(264)]
internal struct ToolbarPausedComp
{
    public int Edited;     // offset 0
    public int Untouched;  // offset 4
}

/// <summary>
/// ⭐⭐⭐ <b><c>MIN</c> — a live edit made while time is paused FROM THE TOOLBAR actually lands.</b>
///
/// <para>🔒 <b>The user's ruling <c>R-126</c>:</b> <i>"time is paused OR debugger hit a breakpoint — in
/// both cases the simulation is stopped and we can write new values."</i>
/// 📌 <b>The live failure:</b> <i>edit a working-state variable while paused from the toolbar → the
/// value does not change.</i></para>
///
/// <para>📐 <b>The chain that broke it, measured</b> *(<c>DESIGN_Time_Architecture.md</c> §1b)*:
/// <c>AS-3</c> — the session refused, because its gate was a SESSION-LOCAL <c>_isPaused</c> that a
/// toolbar pause never sets; and <c>AS-5</c> — even had it staged, <b>nothing drains</b> the queue under
/// a toolbar pause, because the drain runs only on breakpoint step/continue.</para>
///
/// <para>⭐⭐⭐ <b>Why this rail drives a REAL <see cref="ModuleHostKernel"/>.</b> <c>MIN §3b</c> chose the
/// immediate path by measurement: the write is recorded into the repository's own command buffer — ⭐
/// <b>the SAME surgical writer the breakpoint drain uses</b> *(<c>R-65</c>: one implementation of "patch
/// these bytes", not two)* — and the kernel plays that buffer back in <c>BeforeSync</c>
/// <b>unconditionally</b>, so it lands even at <c>dt = 0</c>.
/// ⚠⚠ <b>That is a dependency on kernel behaviour, and the handoff flagged it as the risk of this
/// choice</b> — <i>"A depends on a kernel behaviour that, if it changes, silently breaks the edit."</i>
/// ⇒ ⛔ <b>so it is pinned here rather than trusted:</b> if the flush ever becomes <c>dt</c>-gated or
/// moves, these rails redden instead of the editor quietly forgetting edits again.</para>
///
/// <para>📌 <c>M-29</c> — <b>what is faked:</b> only the time controller, which reports a fixed delta.
/// ⛔ The repository, the command buffer, the kernel's frame and <c>DataBreakpointManager</c> are all
/// the production types.</para>
/// </summary>
[Collection("ComponentRegistry")]
public sealed class TheToolbarPauseWriteLandsTests
{
    private const int Edited = 0;   // ToolbarPausedComp.Edited
    private const int Untouched = 4;

    /// <summary>⭐ A clock that reports whatever delta the test asks for. ⚠ It is the ONLY fake here —
    /// and it stands in for the toolbar, which sets the scale to zero.</summary>
    private sealed class FixedDeltaClock : ITimeController
    {
        private readonly float _dt;
        private long _frame;
        public FixedDeltaClock(float dt) => _dt = dt;

        public GlobalTime Update()
        {
            _frame++;
            return new GlobalTime
            {
                DeltaTime   = _dt,
                TotalTime   = _dt * _frame,
                FrameNumber = _frame,
                TimeScale   = _dt > 0f ? 1f : 0f,
            };
        }

        public void SetTimeScale(float scale) { }
        public float GetTimeScale() => _dt > 0f ? 1f : 0f;
        public TimeMode GetMode() => TimeMode.Continuous;
        public GlobalTime GetCurrentState() => new GlobalTime { DeltaTime = _dt, FrameNumber = _frame };
        public void SeedState(GlobalTime state) { }
        public void Dispose() { }
    }

    private sealed record Rig(
        DataBreakpointManager Manager, EntityRepository Live, ModuleHostKernel Kernel, Entity Target);

    /// <summary>
    /// ⭐ A world whose clock is stopped the way the TOOLBAR stops it: <c>DeltaTime = 0</c> pushed into
    /// the live world's own <c>GlobalTime</c> singleton every frame. ⛔ No breakpoint is hit, so
    /// <see cref="DataBreakpointManager.IsPaused"/> stays <c>false</c> and <c>ActiveView</c> IS the live
    /// repository — which is the whole reason the immediate arm is safe here.
    /// </summary>
    private static Rig Halted(float dt = 0f)
    {
        ComponentTypeRegistry.Clear();
        var live    = new EntityRepository();
        var preTick = new EntityRepository();
        live.RegisterComponent<GlobalTime>();
        live.RegisterComponent<ToolbarPausedComp>();
        preTick.RegisterComponent<GlobalTime>();
        preTick.RegisterComponent<ToolbarPausedComp>();
        live.SetSingletonUnmanaged(new GlobalTime { DeltaTime = dt });

        var entity = live.CreateEntity();
        live.AddComponent(entity, new ToolbarPausedComp { Edited = 1, Untouched = 10 });
        preTick.SyncFrom(live);

        var kernel = new ModuleHostKernel(live, new EventAccumulator());
        kernel.SetTimeController(new FixedDeltaClock(dt));
        kernel.Initialize();

        var manager = new DataBreakpointManager(
            live, preTick, new DebugSnapshotProvider(preTick), new MockDebugTimeController());

        return new Rig(manager, live, kernel, entity);
    }

    // ══ the clock is the one source of "paused" (R-126 / AS-1b) ══════════════

    /// <summary>
    /// ⭐⭐ <b><c>AS-1b</c> — "halted" is read off the LIVE WORLD's <c>GlobalTime</c>, so it answers
    /// differently for a stopped clock and a running one.</b>
    ///
    /// <para>⛔ The rejected alternative was the time controller's <c>GetCurrentState()</c>, which
    /// hard-codes its delta to <c>0</c> and would therefore answer <i>"halted"</i> for ever — ⚠ a gate
    /// that always says yes is not a gate. 📌 The sibling rail
    /// <c>ThePauseFlagOnTheClockIsFalseWhilePausedTests</c> pins the same distinction from the clock's
    /// side.</para>
    /// </summary>
    [Fact]
    public void TheClockAnswersHaltedOnlyWhenItIsActuallyStopped()
    {
        Assert.True(Halted(dt: 0f).Manager.IsClockHalted());
        Assert.False(Halted(dt: 0.016f).Manager.IsClockHalted());
    }

    /// <summary>
    /// ⚠ <b>A world with no clock at all counts as HALTED</b> — deliberate, and stated because it is the
    /// kind of default that would otherwise look accidental. ⭐ No <c>GlobalTime</c> means no source of
    /// ticks, so nothing can overwrite a direct write; ⛔ the other answer would refuse every edit in
    /// such a world and blame the designer for a simulation that is not running.
    /// </summary>
    [Fact]
    public void WithNoClockAtAll_ItCountsAsHalted()
    {
        ComponentTypeRegistry.Clear();
        var live    = new EntityRepository();
        var preTick = new EntityRepository();

        var manager = new DataBreakpointManager(
            live, preTick, new DebugSnapshotProvider(preTick), new MockDebugTimeController());

        Assert.True(manager.IsClockHalted());
    }

    // ══ THE ONE THAT MATTERS: it lands, and it STAYS ═════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The write lands, and it is still there N paused frames later.</b>
    ///
    /// <para>📌 <c>P6′</c> — nothing recomputes at <c>dt = 0</c>, so a value written under a toolbar
    /// pause must not drift. ⚠ <b>Both halves are asserted deliberately</b>: a rail that only checked
    /// frame 1 would pass for an implementation that lands the write and then lets the next frame
    /// overwrite it, which is precisely the failure mode a direct write into a ticking world would
    /// have.</para>
    ///
    /// <para>⭐ And the neighbouring field is checked too — 📌 <c>R-65</c>: <c>Blackboard1024</c> is ONE
    /// component shared by BTree, HSM and Blueprint at disjoint offsets, so a write that quietly took
    /// the whole-component path would revert another subsystem's bytes with no diagnostic.</para>
    /// </summary>
    [Fact]
    public void UnderAToolbarPause_TheWriteLands_AndStaysAcrossPausedFrames()
    {
        var rig = Halted();

        // ⛔ Precondition, asserted rather than assumed: this is a TOOLBAR pause, not a breakpoint.
        Assert.False(rig.Manager.IsPaused);
        Assert.True(rig.Manager.IsClockHalted());
        Assert.Same(rig.Live, rig.Manager.ActiveView);

        rig.Manager.WriteFieldNow(
            rig.Target, typeof(ToolbarPausedComp), Edited, BitConverter.GetBytes(4242));

        rig.Kernel.Update();
        Assert.Equal(4242, rig.Live.GetComponent<ToolbarPausedComp>(rig.Target).Edited);

        // ⭐ …and it is still 4242 several paused frames later.
        for (int i = 0; i < 5; i++) rig.Kernel.Update();
        var after = rig.Live.GetComponent<ToolbarPausedComp>(rig.Target);
        Assert.Equal(4242, after.Edited);

        // ⛔ R-65: the field beside it is untouched — the write was surgical, not a component clobber.
        Assert.Equal(10, after.Untouched);
    }

    /// <summary>
    /// ⛔⛔ <b>It does NOT go through the pending queue.</b> 📌 <c>AS-5</c>: nothing drains that queue
    /// under a toolbar pause — the drain runs only on <c>RequestStep</c>/<c>RequestContinue</c>. ⇒ ⭐ a
    /// staged write here would be a write that never happens, which is the exact symptom <c>MIN</c>
    /// exists to end, so "it landed" is not enough: it must not have queued.
    /// </summary>
    [Fact]
    public void TheImmediateWriteDoesNotQueueAnythingForADrainThatWillNeverRun()
    {
        var rig = Halted();

        rig.Manager.WriteFieldNow(
            rig.Target, typeof(ToolbarPausedComp), Edited, BitConverter.GetBytes(4242));

        Assert.Equal(0, rig.Manager.PendingMutationsCount);
    }

    /// <summary>
    /// ⭐⭐ <b>The BREAKPOINT arm still STAGES — <c>MIN</c>'s new branch did not take its work away.</b>
    ///
    /// <para>📌 <c>R-63</c>, and it is the reason the two arms exist: <c>OnHit</c> rewinds the live repo
    /// to the pre-tick snapshot, and <c>RequestStep</c>/<c>RequestContinue</c> restore it from the
    /// POST-tick snapshot and drain <b>afterwards</b>. ⇒ ⛔ a direct write under a breakpoint would be
    /// erased by that restore. ⚠ This rail is here because <c>MIN</c> introduced a second path out of
    /// the same method, and "the other arm still behaves" is exactly what a new branch can break.</para>
    /// </summary>
    [Fact]
    public void UnderABreakpoint_TheWriteIsStillStagedAndSurvivesTheResumeRestore()
    {
        var rig = Halted();

        // ⭐ A real hit, through the production path — this is what makes IsPaused true.
        var bpId = rig.Manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "min",
        });
        rig.Manager.OnHit(rig.Manager.AllBreakpoints.First(b => b.Id == bpId), rig.Target);
        Assert.True(rig.Manager.IsPaused);

        rig.Manager.StageFieldMutation(
            rig.Target, typeof(ToolbarPausedComp), Edited, BitConverter.GetBytes(4242));
        Assert.Equal(1, rig.Manager.PendingMutationsCount);

        // ⭐ Resume restores from the post-tick snapshot and THEN drains; the kernel's flush applies it.
        rig.Manager.RequestContinue();
        rig.Kernel.Update();

        Assert.Equal(4242, rig.Live.GetComponent<ToolbarPausedComp>(rig.Target).Edited);
    }

    // ══ the corruption gate applies to BOTH arms ═════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>An out-of-range write is MEMORY CORRUPTION, not a wrong value</b> *(📌 <c>Q32</c> §2.1)*.
    /// ⭐ <c>MIN</c> extracted the bounds check so the staging arm and the write-now arm share ONE
    /// notion of "in range" — ⛔ two copies would be two answers, and the wrong one scribbles into the
    /// next component.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1024)]
    public void AnOutOfRangeImmediateWrite_ThrowsRatherThanScribbling(int byteOffset)
    {
        var rig = Halted();

        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Manager.WriteFieldNow(
            rig.Target, typeof(ToolbarPausedComp), byteOffset, BitConverter.GetBytes(4242)));
    }

    /// <summary>⛔ A managed component has no byte layout to patch — loud, on both arms, because
    /// forwarding to a whole-component write is <c>R-65</c>'s clobber wearing the surgical path's
    /// name.</summary>
    [Fact]
    public void AManagedComponent_IsRefusedLoudlyByTheImmediateArmToo()
    {
        var rig = Halted();

        Assert.Throws<ArgumentException>(() => rig.Manager.WriteFieldNow(
            rig.Target, typeof(string), 0, BitConverter.GetBytes(4242)));
    }
}
