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
        private long _frame;
        public FixedDeltaClock(float dt) => Dt = dt;

        /// <summary>⭐ <c>W3</c>/<c>W5</c> made this SETTABLE: the whole point of a staged write is that
        /// it waits for an ADVANCING frame, so a rail has to be able to start the clock.</summary>
        public float Dt { get; set; }

        public GlobalTime Update()
        {
            _frame++;
            return new GlobalTime
            {
                DeltaTime   = Dt,
                TotalTime   = Dt * _frame,
                FrameNumber = _frame,
                TimeScale   = Dt > 0f ? 1f : 0f,
            };
        }

        public void SetTimeScale(float scale) { }
        public float GetTimeScale() => Dt > 0f ? 1f : 0f;
        public TimeMode GetMode() => TimeMode.Continuous;
        public GlobalTime GetCurrentState() => new GlobalTime { DeltaTime = Dt, FrameNumber = _frame };
        public void SeedState(GlobalTime state) { }
        public void Dispose() { }
    }

    private sealed record Rig(
        DataBreakpointManager Manager, EntityRepository Live, ModuleHostKernel Kernel, Entity Target,
        FixedDeltaClock Clock);

    /// <summary>
    /// ⭐ A world whose clock is stopped the way the TOOLBAR stops it: <c>DeltaTime = 0</c> pushed into
    /// the live world's own <c>GlobalTime</c> singleton every frame. ⛔ No breakpoint is hit, so
    /// <see cref="DataBreakpointManager.IsPaused"/> stays <c>false</c> and <c>ActiveView</c> IS the live
    /// repository — which is the whole reason the immediate arm is safe here.
    /// </summary>
    /// <param name="drain">
    /// ⭐⭐⭐ <c>W3</c>/<c>W5</c> — whether this host registers the kernel's <c>ResumeAndDrainSystem</c>,
    /// exactly as <c>EditorSubsystem</c> does *(design §8)*. ⛔ <c>false</c> is not a convenience: it is
    /// the NEGATIVE control for a host that forgot the wire, and one rail below drives it deliberately.
    /// </param>
    private static Rig Halted(float dt = 0f, bool drain = true)
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

        var clock  = new FixedDeltaClock(dt);
        var kernel = new ModuleHostKernel(live, new EventAccumulator());
        kernel.SetTimeController(clock);

        var manager = new DataBreakpointManager(
            live, preTick, new DebugSnapshotProvider(preTick), new MockDebugTimeController());

        // ⭐⭐ The production wire, mirrored — 📌 R-67: a rail that builds its own composition root
        //    cannot see a composition-root defect, so this rig registers what EditorSubsystem:1139
        //    registers rather than reaching past it.
        if (drain) kernel.RegisterGlobalSystem(new ResumeAndDrainSystem(manager));

        kernel.Initialize();

        return new Rig(manager, live, kernel, entity, clock);
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

    // ══ THE ONE THAT MATTERS: it STAGES, and it lands when the clock moves ═══

    /// <summary>
    /// ⭐⭐⭐ <b><c>W3</c> — a toolbar-paused edit STAGES, stays staged while the clock is stopped, and
    /// lands on the first ADVANCING frame.</b>
    ///
    /// <para>⚠⚠ <b>THIS RAIL WAS INVERTED BY <c>W3</c>, deliberately, and the old claim was not
    /// wrong — it was superseded.</b> It used to assert
    /// <i>"<c>WriteFieldNow</c> lands immediately and stays across N paused frames"</i>.
    /// 📄 <c>DESIGN_Staged_Live_Write.md</c> §1's table replaces that behaviour on purpose:
    /// <b>paused ⇒ stages → 🟡 yellow → drains on the next step/resume</b>. 📌 <c>R-130</c> is why —
    /// <i>"yellow is an indication of staged change; makes no sense if the value is directly written
    /// now"</i> — a direct write is never in the pending set, so the panel could never show it.</para>
    ///
    /// <para>⭐ <b>Both halves still matter, for the mirrored reason.</b> The old rail checked the value
    /// did not DRIFT after landing; this one checks it does not LAND EARLY — a drain that ignored
    /// <c>deltaTime</c> would apply the edit while the designer was still paused, which defeats
    /// staging and would make the yellow lie in the other direction.</para>
    ///
    /// <para>⭐ And the neighbouring field is checked too — 📌 <c>R-65</c>: <c>Blackboard1024</c> is ONE
    /// component shared by BTree, HSM and Blueprint at disjoint offsets, so a write that quietly took
    /// the whole-component path would revert another subsystem's bytes with no diagnostic.</para>
    /// </summary>
    [Fact]
    public void UnderAToolbarPause_TheEditStages_AndLandsOnTheFirstAdvancingFrame()
    {
        var rig = Halted();

        // ⛔ Precondition, asserted rather than assumed: this is a TOOLBAR pause, not a breakpoint.
        Assert.False(rig.Manager.IsPaused);
        Assert.True(rig.Manager.IsClockHalted());
        Assert.Same(rig.Live, rig.Manager.ActiveView);

        rig.Manager.StageFieldMutation(
            rig.Target, typeof(ToolbarPausedComp), Edited, BitConverter.GetBytes(4242));

        // ⭐⭐⭐ STAGED, NOT APPLIED — and it stays that way for as long as the clock is stopped.
        //    📌 R-130: this is exactly the window in which StagedWriteView paints the row yellow.
        Assert.Equal(1, rig.Manager.PendingMutationsCount);
        for (int i = 0; i < 5; i++) rig.Kernel.Update();
        Assert.Equal(1,    rig.Manager.PendingMutationsCount);
        Assert.Equal(1,    rig.Live.GetComponent<ToolbarPausedComp>(rig.Target).Edited);

        // ⭐⭐ The designer un-pauses. ONE advancing frame is enough.
        rig.Clock.Dt = 0.016f;
        rig.Kernel.Update();

        var after = rig.Live.GetComponent<ToolbarPausedComp>(rig.Target);
        Assert.Equal(4242, after.Edited);
        Assert.Equal(0,    rig.Manager.PendingMutationsCount);   // ⭐ the auto-clear W4's yellow rides on

        // ⛔ R-65: the field beside it is untouched — the write was surgical, not a component clobber.
        Assert.Equal(10, after.Untouched);
    }

    /// <summary>
    /// ⛔⛔ <b>A HOST THAT DID NOT WIRE THE DRAIN NEVER APPLIES THE EDIT — and that is the cost of
    /// <c>W3</c>, stated rather than discovered later.</b>
    ///
    /// <para>⚠⚠ <b>This rail replaces <c>TheImmediateWriteDoesNotQueueAnythingForADrainThatWillNeverRun</c>,
    /// whose premise <c>W3</c> reversed.</b> That rail existed because <c>AS-5</c> measured that
    /// <b>nothing drained the queue under a toolbar pause</b> — so <c>MIN</c> wrote immediately rather
    /// than queue into a void. ⭐ The drain now exists, so queueing is right; ⛔ <b>but only where it is
    /// registered.</b></para>
    ///
    /// <para>📐 <b>Measured, and it is a real second host:</b> <c>CgfSubsystem</c> builds a
    /// <c>DataBreakpointManager</c> and registers <c>DebugSnapshotProvider</c> + <c>DataBreakpointSystem</c>
    /// — 📌 <b>this batch added its <c>ResumeAndDrainSystem</c> registration for exactly this
    /// reason.</b> ⭐ A third host added later reddens here rather than losing edits silently.</para>
    /// </summary>
    [Fact]
    public void WithNoDrainRegistered_AStagedEditNeverLands()
    {
        var rig = Halted(dt: 0.016f, drain: false);

        rig.Manager.StageFieldMutation(
            rig.Target, typeof(ToolbarPausedComp), Edited, BitConverter.GetBytes(4242));

        for (int i = 0; i < 5; i++) rig.Kernel.Update();

        Assert.Equal(1, rig.Manager.PendingMutationsCount);   // ⛔ still waiting, for ever
        Assert.Equal(1, rig.Live.GetComponent<ToolbarPausedComp>(rig.Target).Edited);
    }

    /// <summary>
    /// ⭐⭐ <b>The BREAKPOINT path still works, and <c>W5</c> made its old caveat obsolete.</b>
    ///
    /// <para>📌 <c>R-63</c>: <c>OnHit</c> rewinds the live repo to the pre-tick snapshot, and
    /// <c>RequestContinue</c> restores it from the POST-tick snapshot. ⇒ ⛔ a direct write under a
    /// breakpoint would be erased by that restore, which is why staging was always right here.</para>
    ///
    /// <para>⭐⭐⭐ <b><c>W5</c> RESOLVED THE CAVEAT THIS RAIL USED TO CARRY.</b> It said, at length:
    /// <i>"no production code outside <c>DataBreakpointManager</c> calls either request — the editor's
    /// own Continue goes through <c>_timeController.RequestResume()</c> and never tells the queue ⇒ this
    /// rail proves the MANAGER's path works; it cannot prove the designer's Continue button reaches
    /// it."</i> ⚠ That was true while the drain lived INSIDE <c>RequestContinue</c>. ⭐ <c>W5</c> moved
    /// it out: the drain is a <b>PULL from the tick loop</b> *(<c>R-126</c>)*, so it does not matter
    /// which path un-paused the clock — ⛔ <b>there is no longer a button that can fail to reach
    /// it.</b></para>
    /// </summary>
    [Fact]
    public void UnderABreakpoint_TheEditIsStagedAndSurvivesTheResumeRestore()
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

        // ⭐⭐ Resume restores from the post-tick snapshot and does NOT drain (W5). The clock then
        //    advances and the kernel's PreFrame system pulls the edit in.
        rig.Manager.RequestContinue();
        Assert.Equal(1, rig.Manager.PendingMutationsCount);   // ⛔ the resume path wrote nothing

        rig.Clock.Dt = 0.016f;
        rig.Kernel.Update();

        Assert.Equal(4242, rig.Live.GetComponent<ToolbarPausedComp>(rig.Target).Edited);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>W5</c> — THE DRAIN SKIPS WHILE A BREAKPOINT HOLDS A REWOUND VIEW.</b>
    /// 📌 <c>R-63</c>: while paused, <c>_liveRepo</c> IS the pre-tick snapshot, and
    /// <c>RequestStep</c>/<c>RequestContinue</c> overwrite it wholesale from the post-tick one.
    /// ⇒ ⛔ bytes drained into it before the restore are erased with no diagnostic.
    ///
    /// <para>⚠ Reachable for real: deterministic stepping advances the clock <b>while</b> a breakpoint
    /// holds. ⭐ <c>ResumeAndDrainSystem</c> asks <c>IsRewound</c> for exactly this, and this rail is
    /// what stops that guard being deleted as "defensive".</para>
    /// </summary>
    [Fact]
    public void WhileABreakpointHoldsARewoundView_TheDrainWaits()
    {
        var rig = Halted(dt: 0.016f);

        var bpId = rig.Manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "w5",
        });
        rig.Manager.OnHit(rig.Manager.AllBreakpoints.First(b => b.Id == bpId), rig.Target);
        Assert.True(rig.Manager.IsPaused);

        rig.Manager.StageFieldMutation(
            rig.Target, typeof(ToolbarPausedComp), Edited, BitConverter.GetBytes(4242));

        // ⛔ The clock is advancing, but the view is rewound — the drain must NOT run.
        for (int i = 0; i < 3; i++) rig.Kernel.Update();
        Assert.Equal(1, rig.Manager.PendingMutationsCount);

        rig.Manager.RequestContinue();
        rig.Kernel.Update();
        Assert.Equal(4242, rig.Live.GetComponent<ToolbarPausedComp>(rig.Target).Edited);
    }

    // ══ the corruption gate applies to BOTH arms ═════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>An out-of-range write is MEMORY CORRUPTION, not a wrong value</b> *(📌 <c>Q32</c> §2.1)*.
    ///
    /// <para>⚠ <b><c>W3</c> re-pointed this at the staging arm</b>, which is now the only arm. ⭐ The
    /// guard itself is unmoved: <c>MIN</c> extracted <c>GuardFieldWrite</c> so both arms shared ONE
    /// notion of "in range", and removing the second arm did not change the first.</para>
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1024)]
    public void AnOutOfRangeStagedWrite_ThrowsRatherThanScribbling(int byteOffset)
    {
        var rig = Halted();

        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Manager.StageFieldMutation(
            rig.Target, typeof(ToolbarPausedComp), byteOffset, BitConverter.GetBytes(4242)));
    }

    /// <summary>⛔ A managed component has no byte layout to patch — loud, because forwarding to a
    /// whole-component write is <c>R-65</c>'s clobber wearing the surgical path's name.
    /// ⚠ <c>W3</c> re-pointed this at the staging arm; the refusal is the same one.</summary>
    [Fact]
    public void AManagedComponent_IsRefusedLoudlyByTheStagingArm()
    {
        var rig = Halted();

        Assert.Throws<ArgumentException>(() => rig.Manager.StageFieldMutation(
            rig.Target, typeof(string), 0, BitConverter.GetBytes(4242)));
    }
}
