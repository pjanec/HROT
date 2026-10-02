using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Time.Controllers;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.ExCon;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// End-to-end integration tests for exercise clock control (Pause / Resume / Step).
///
/// <para>All four subsystems boot in the same process using CycloneDDS loopback — no
/// intra-process shortcuts.  SimHost, IG, and CGF all own simulation kernels and must
/// swap to <c>SteppedSlaveController</c> on Pause and back on Resume.  ExCon is a
/// pure presentation node and is NOT wired for lockstep.</para>
///
/// <para>Observable state: <see cref="SimHostSubsystem.TestHook_CurrentSimTime"/> is
/// the ground-truth sim-time source; <see cref="ClusterUiCache.IsPaused"/>
/// (via <c>OrchestratorSubsystem.UiCacheForTest</c>) is the pause-state flag driven
/// by the bus pipeline (HEXAG2-S011).</para>
/// </summary>
public sealed class TimeControlIntegrationTests : IDisposable
{
    // Domain IDs starting at 190 to avoid collisions with other integration test classes.
    private const int DomainBase = 190;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    // Frame-pump constants.
    private const int PumpSleepMs   = 5;   // sleep between pump frames (ms)
    private const int SettleFrames  = 60;  // frames to pump after a DDS operation to allow loopback

    private readonly HrotRunnerHarness _harness;
    private readonly ClusterMaster     _master;
    private readonly SimHostSubsystem  _simHost;
    private readonly OrchestratorSubsystem _orchestratorSvc;

    public TimeControlIntegrationTests()
    {
        _harness         = new HrotRunnerHarness();
        _master          = _harness.OrchestratorSvc.TestHook_ClusterMaster!;
        _simHost         = _harness.SimHost;
        _orchestratorSvc = _harness.OrchestratorSvc;
    }

    public void Dispose() => _harness.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends <paramref name="opType"/> to the cluster master and pumps frames until
    /// the operation is accepted (no status polling — time-control ops fire synchronously
    /// via the <c>TimeControlRequested</c> event).
    /// </summary>
    private async Task SendTimeOpAsync(ClusterOpType opType, string payload = "")
    {
        await _master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = opType,
            PayloadJson   = payload,
        }).ConfigureAwait(false);

        // PumpUntil sleeps PumpSleepMs per frame, giving real wall-clock time for
        // MasterSyncController's barrier (LookaheadWallTicks = 200 ms) to be crossed
        // before the next op is issued.  PumpFrames (tight loop, no sleep) is too fast
        // and leaves the master in BarrierPending when the next Step() is called.
        _harness.PumpUntil(() => false, SettleFrames);

        // On machines where Thread.Sleep(PumpSleepMs) wakes early (e.g. 1-2 ms instead
        // of 5 ms), SettleFrames * actual_sleep can be less than 200 ms — not enough to
        // cross the lookahead barrier.  After a Pause, stay in the pump loop until the
        // slave's SlaveSyncController actually enters Stepping mode (barrier crossed),
        // which is the necessary precondition for Step() to have any effect.
        if (opType == ClusterOpType.PauseTime)
        {
            PumpUntil(
                () => _simHost.TestHook_TimeControllerMode == Fdp.ModuleHost.Time.TimeMode.Deterministic,
                timeoutMs: 5000);
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> becomes true or the timeout expires.
    /// Returns <c>true</c> when the condition was met.
    /// </summary>
    private bool PumpUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            _harness.PumpFrames(1);
            Thread.Sleep(PumpSleepMs);
        }
        return condition();
    }

    /// <summary>
    /// Samples <see cref="SimHostSubsystem.TestHook_CurrentSimTime"/>, pumps
    /// <paramref name="observeMs"/> milliseconds, then samples again and returns the delta.
    /// </summary>
    private double ObserveSimTimeDelta(int observeMs = 500)
    {
        double before = _simHost.TestHook_CurrentSimTime;
        int frames = Math.Max(1, observeMs / PumpSleepMs);
        // Use PumpUntil (5 ms sleep per frame) so real wall-clock time passes at a predictable
        // rate. PumpFrames (tight loop, no sleep) is too fast and produces an unreliably small
        // delta on machines where DDS loopback overhead is near zero.
        _harness.PumpUntil(() => false, frames);
        return _simHost.TestHook_CurrentSimTime - before;
    }

    // ── Scenario A: single Pause → Resume ────────────────────────────────────

    /// <summary>
    /// Verifies that a single Pause freezes sim-time and a subsequent Resume restores
    /// free-running time.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task PauseResume_SimTimeFreezes_ThenAdvances()
    {
        // Arrange: let the clock run for a moment and confirm it is advancing.
        double deltaBeforePause = ObserveSimTimeDelta(600);
        Assert.True(deltaBeforePause > 0.1,
            $"Clock should be advancing before Pause; delta={deltaBeforePause:F3}s");

        // Act: Pause
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);

        // Assert: IsPaused flag
        bool pauseReached = PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
        Assert.True(pauseReached, "UiCacheForTest.IsPaused should become true after PauseTime");

        // Assert: sim-time is frozen
        double deltaWhilePaused = ObserveSimTimeDelta(600);
        Assert.True(deltaWhilePaused < 0.05,
            $"Sim-time should be frozen while paused; delta={deltaWhilePaused:F3}s");

        // Act: Resume
        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);

        // Assert: IsPaused flag cleared
        bool resumeReached = PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
        Assert.True(resumeReached, "UiCacheForTest.IsPaused should become false after ResumeTime");

        // Assert: sim-time is advancing again
        double deltaAfterResume = ObserveSimTimeDelta(800);
        Assert.True(deltaAfterResume > 0.3,
            $"Sim-time should advance after Resume; delta={deltaAfterResume:F3}s");
    }

    // ── Scenario B: Pause → 3×Step → Resume ─────────────────────────────────

    /// <summary>
    /// Verifies that three 1-second step commands each advance sim-time by ≈1 s and that
    /// Resume restores free-running time afterwards.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task PauseStepResume_SimTimeAdvancesByStepAmount()
    {
        const float StepDeltaSec = 1.0f;
        const int   Steps         = 3;
        string stepPayload = $"{{\"FixedDelta\":{StepDeltaSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";

        // Pause
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        bool paused = PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
        Assert.True(paused, "Should be paused before stepping");

        // Record sim-time at pause
        double simTimeAtPause = _simHost.TestHook_CurrentSimTime;

        // Issue 3 step commands
        for (int i = 0; i < Steps; i++)
        {
            await SendTimeOpAsync(ClusterOpType.StepTime, stepPayload).ConfigureAwait(false);
            // Brief settle so DDS FrameOrder loopback completes before next step.
            _harness.PumpFrames(SettleFrames);
            Thread.Sleep(SettleFrames * PumpSleepMs);
        }

        // Assert: sim-time advanced by ≈ Steps * StepDeltaSec
        double simTimeAfterSteps = _simHost.TestHook_CurrentSimTime;
        double advanced = simTimeAfterSteps - simTimeAtPause;
        Assert.True(advanced > Steps * StepDeltaSec * 0.5,
            $"Sim-time should have advanced by ~{Steps * StepDeltaSec}s after {Steps} steps; actual delta={advanced:F3}s");

        // Resume
        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        bool resumed = PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
        Assert.True(resumed, "Should be running after Resume");

        // Confirm time advances freely
        double deltaAfterResume = ObserveSimTimeDelta(800);
        Assert.True(deltaAfterResume > 0.3,
            $"Sim-time should advance after Resume; delta={deltaAfterResume:F3}s");
    }

    // ── Scenario C: 3 successive Pause/Resume cycles ─────────────────────────

    /// <summary>
    /// Verifies that three successive Pause → Resume cycles each correctly freeze and
    /// then restore sim-time, with no degradation across cycles.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task MultiCyclePauseResume_AllCyclesWorkCorrectly()
    {
        for (int cycle = 1; cycle <= 3; cycle++)
        {
            // Let clock advance
            double deltaRunning = ObserveSimTimeDelta(400);
            Assert.True(deltaRunning > 0.05,
                $"Cycle {cycle}: clock should be advancing; delta={deltaRunning:F3}s");

            // Pause
            await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
            bool paused = PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
            Assert.True(paused, $"Cycle {cycle}: should be paused");

            // Confirm frozen
            double deltaFrozen = ObserveSimTimeDelta(500);
            Assert.True(deltaFrozen < 0.05,
                $"Cycle {cycle}: sim-time should be frozen; delta={deltaFrozen:F3}s");

            // Resume
            await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
            bool resumed = PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
            Assert.True(resumed, $"Cycle {cycle}: should be running after Resume");
        }

        // Final confirmation: clock still runs after 3 cycles
        double finalDelta = ObserveSimTimeDelta(600);
        Assert.True(finalDelta > 0.3,
            $"Sim-time must advance after 3 Pause/Resume cycles; delta={finalDelta:F3}s");
    }

    // ── Scenario D: Pause/Step/Resume interleaved with pause again ───────────

    /// <summary>
    /// Combined scenario: Pause → 2×Step → Resume → Pause → 1×Step → Resume.
    /// Verifies the controller-swap machinery is idempotent across mixed sequences.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task MixedSequence_PauseStepPauseStep_AllCorrect()
    {
        const float StepDelta = 1.0f;
        string payload = $"{{\"FixedDelta\":{StepDelta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";

        // ── First pause block ─────────────────────────────────────────────────
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused), "Block 1: should be paused");

        double t0 = _simHost.TestHook_CurrentSimTime;

        await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
        _harness.PumpFrames(SettleFrames);
        Thread.Sleep(SettleFrames * PumpSleepMs);

        await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
        _harness.PumpFrames(SettleFrames);
        Thread.Sleep(SettleFrames * PumpSleepMs);

        double t1 = _simHost.TestHook_CurrentSimTime;
        Assert.True(t1 - t0 > StepDelta * 0.5 * 2,
            $"Block 1: expected ~{2 * StepDelta}s advance; got {t1 - t0:F3}s");

        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused), "Block 1: should resume");

        // Let clock run briefly
        Thread.Sleep(300);
        _harness.PumpFrames(30);

        // ── Second pause block ────────────────────────────────────────────────
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused), "Block 2: should be paused");

        double t2 = _simHost.TestHook_CurrentSimTime;

        await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
        _harness.PumpFrames(SettleFrames);
        Thread.Sleep(SettleFrames * PumpSleepMs);

        double t3 = _simHost.TestHook_CurrentSimTime;
        Assert.True(t3 - t2 > StepDelta * 0.5,
            $"Block 2: expected ~{StepDelta}s advance; got {t3 - t2:F3}s");

        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused), "Block 2: should resume");

        // Final advancing check
        double finalDelta = ObserveSimTimeDelta(600);
        Assert.True(finalDelta > 0.3,
            $"Should advance freely after mixed sequence; delta={finalDelta:F3}s");
    }

    // ── Scenario E: SimHost kernel must restore MasterTimeController on Resume ─

    /// <summary>
    /// Regression test for the old bug where <see cref="Fdp.Toolkit.Time.Controllers.SlaveTimeModeListener"/> installed
    /// <see cref="Fdp.Toolkit.Time.Controllers.SlaveTimeController"/> on every node on Resume, including SimHost which
    /// must restore the continuous-time controller so that
    /// <c>TimePulseDescriptor</c> publication (and therefore ExCon display) is restored.
    ///
    /// <para>With the unified <see cref="Fdp.Toolkit.Time.Controllers.SlaveSyncController"/>,
    /// the mode is managed internally.  This test now verifies that the controller's
    /// reported mode transitions correctly: Continuous → Deterministic → Continuous.</para>
    ///
    /// <para>Also verifies idempotency: multiple DDS loopback echoes of
    /// <c>SwitchTimeModeEvent(Continuous)</c> must NOT incorrectly change state.</para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task PauseResume_SimHostKernelRestoresMasterTimeController()
    {
        // Arrange: confirm kernel uses SlaveSyncController starting in Continuous mode.
        Assert.Equal(typeof(SlaveSyncController), _simHost.TestHook_TimeControllerType);
        Assert.Equal(Fdp.ModuleHost.Time.TimeMode.Continuous, _simHost.TestHook_TimeControllerMode);

        // Act: Pause
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        bool paused = PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
        Assert.True(paused, "Should be paused");

        // During Pause: SlaveSyncController must be in Deterministic mode.
        Assert.Equal(typeof(SlaveSyncController), _simHost.TestHook_TimeControllerType);

        // Act: Resume
        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        bool resumed = PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused, timeoutMs: 3000);
        Assert.True(resumed, "Should be running after Resume");

        // After Resume: SlaveSyncController must be back in Continuous mode.
        Assert.Equal(typeof(SlaveSyncController), _simHost.TestHook_TimeControllerType);
        Assert.Equal(Fdp.ModuleHost.Time.TimeMode.Continuous, _simHost.TestHook_TimeControllerMode);

        // Pump extra frames to let any DDS loopback echoes of SwitchTimeModeEvent(Continuous)
        // arrive.  With the idempotent guard in SlaveSyncController they must NOT cause any issue.
        _harness.PumpFrames(SettleFrames * 2);
        Thread.Sleep(SettleFrames * 2 * PumpSleepMs);

        // Guard must hold: still SlaveSyncController in Continuous after echo settle.
        Assert.Equal(typeof(SlaveSyncController), _simHost.TestHook_TimeControllerType);
        Assert.Equal(Fdp.ModuleHost.Time.TimeMode.Continuous, _simHost.TestHook_TimeControllerMode);
    }

    // ── Scenario F: second Pause/Step cycle after Resume works ───────────────

    /// <summary>
    /// Regression test for the production scenario where the second Pause → Step sequence
    /// failed because:
    /// <list type="number">
    ///   <item><see cref="SlaveTimeModeListener"/> had installed <see cref="SlaveTimeController"/>
    ///   on SimHost after the first Resume, so the second Pause installed
    ///   <see cref="SteppedSlaveController"/> seeded from a corrupted or zero-based
    ///   <c>TotalWallTicks</c>.</item>
    ///   <item>The double <c>SwitchToDeterministic</c> call in
    ///   <see cref="OrchestratorSubsystem.Update"/> used all roster IDs (including ExCon)
    ///   instead of only kernel-owning nodes, causing
    ///   <see cref="SteppedMasterController"/> to wait for an ACK from ExCon forever.</item>
    /// </list>
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task SecondCyclePauseStep_AdvancesTimeCorrectly()
    {
        const float StepDelta = 1.0f;
        string payload = $"{{\"FixedDelta\":{StepDelta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";

        // ── First Pause/Resume cycle ──────────────────────────────────────────
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused), "Cycle 1: paused");

        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused), "Cycle 1: resumed");

        // Let clock advance between cycles
        double betweenDelta = ObserveSimTimeDelta(400);
        Assert.True(betweenDelta > 0.05, $"Clock must advance between cycles; delta={betweenDelta:F3}s");

        // ── Second Pause/Step cycle ───────────────────────────────────────────
        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused), "Cycle 2: paused");

        double simBeforeStep = _simHost.TestHook_CurrentSimTime;

        // Issue a step — must complete without hanging on missing ACKs.
        await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
        _harness.PumpFrames(SettleFrames);
        Thread.Sleep(SettleFrames * PumpSleepMs);

        double advanced = _simHost.TestHook_CurrentSimTime - simBeforeStep;
        Assert.True(advanced > StepDelta * 0.5,
            $"Second-cycle step must advance sim-time by ~{StepDelta}s; got {advanced:F3}s");

        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused), "Cycle 2: resumed");

        double finalDelta = ObserveSimTimeDelta(600);
        Assert.True(finalDelta > 0.3,
            $"Sim-time must advance freely after second cycle Resume; delta={finalDelta:F3}s");
    }

    // ── Scenario G: CGF is a lockstep participant, not just a roster entry ────

    /// <summary>
    /// AS-14 regression rail (Batch 104). The orchestrator puts every kernel-owning node in the
    /// lockstep roster — <c>SubsystemName is "SimHost" or "IG" or "CGF"</c> — so the master blocks
    /// each step on a FrameAck from CGF as much as from SimHost.
    ///
    /// <para>CGF's live composition path (<c>CgfSubsystem</c> → <c>HrotNodeBuilder</c>) used to wire
    /// no time translators at all: the node held a <c>SlaveSyncController</c> that never heard
    /// <c>SwitchTimeModeEvent</c> and never saw a <c>FrameOrder</c>, so it stayed Continuous and
    /// never ACKed. The master's ACK set therefore never cleared and every step after the first was
    /// discarded — measured as "3 steps ⇒ 1.000 s". Nothing observed the CGF side before this rail:
    /// the two failing tests only saw the missing sim time, not the reason.</para>
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task PauseStep_CgfNodeEntersLockstepAndAcksEveryStep()
    {
        const float StepDelta = 1.0f;
        string payload = $"{{\"FixedDelta\":{StepDelta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";

        Assert.NotNull(_harness.Cgf);
        Assert.Equal(Fdp.ModuleHost.Time.TimeMode.Continuous, _harness.Cgf!.TestHook_TimeControllerMode);

        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => _orchestratorSvc.UiCacheForTest!.IsPaused), "Should be paused");

        // Both kernel-owning nodes must reach Deterministic — CGF is the one that did not.
        bool cgfInLockstep = PumpUntil(
            () => _harness.Cgf!.TestHook_TimeControllerMode == Fdp.ModuleHost.Time.TimeMode.Deterministic,
            timeoutMs: 5000);
        Assert.True(cgfInLockstep,
            $"CGF must enter Deterministic on Pause; mode={_harness.Cgf!.TestHook_TimeControllerMode}");
        Assert.Equal(Fdp.ModuleHost.Time.TimeMode.Deterministic, _simHost.TestHook_TimeControllerMode);

        // Three steps must produce three steps' worth of time. If CGF stops ACKing, this is 1.000 s.
        double before = _simHost.TestHook_CurrentSimTime;
        for (int i = 0; i < 3; i++)
        {
            await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
            _harness.PumpFrames(SettleFrames);
            Thread.Sleep(SettleFrames * PumpSleepMs);
        }
        double advanced = _simHost.TestHook_CurrentSimTime - before;
        Assert.True(advanced > 3 * StepDelta * 0.5,
            $"3 steps must advance ~{3 * StepDelta}s while CGF is in the roster; actual={advanced:F3}s");

        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        Assert.True(PumpUntil(() => !_orchestratorSvc.UiCacheForTest!.IsPaused), "Should resume");

        // And CGF must come back out of lockstep, or the next running phase is frozen on that node.
        bool cgfResumed = PumpUntil(
            () => _harness.Cgf!.TestHook_TimeControllerMode == Fdp.ModuleHost.Time.TimeMode.Continuous,
            timeoutMs: 5000);
        Assert.True(cgfResumed,
            $"CGF must return to Continuous on Resume; mode={_harness.Cgf!.TestHook_TimeControllerMode}");
    }

    // ── Scenario H: SetTimeScale over the cluster op path ─────────────────────

    /// <summary>
    /// Closes 104d's first measured gap: the suite exercised Pause/Resume/Step but never
    /// <see cref="ClusterOpType.SetTimeScale"/>, even though time scale is the other lever on the
    /// master clock.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task SetTimeScale_HalfSpeed_ReachesTheMasterController()
    {
        Assert.Equal(1.0f, _orchestratorSvc.TestHook_TimeScale, 3);

        await SendTimeOpAsync(ClusterOpType.SetTimeScale, "{\"TimeScale\":0.5}").ConfigureAwait(false);

        bool applied = PumpUntil(() => Math.Abs(_orchestratorSvc.TestHook_TimeScale - 0.5f) < 0.001f);
        Assert.True(applied, $"SetTimeScale(0.5) must reach the master; scale={_orchestratorSvc.TestHook_TimeScale}");

        // Sim time must still be advancing — a scale change is not a halt.
        double delta = ObserveSimTimeDelta(600);
        Assert.True(delta > 0.05, $"Clock must still advance at half speed; delta={delta:F3}s");

        await SendTimeOpAsync(ClusterOpType.SetTimeScale, "{\"TimeScale\":1.0}").ConfigureAwait(false);
        Assert.True(PumpUntil(() => Math.Abs(_orchestratorSvc.TestHook_TimeScale - 1.0f) < 0.001f),
            "SetTimeScale(1.0) must restore full speed");
    }

    /// <summary>
    /// The measured half of the same gap, pinned so the refactor does not assume otherwise:
    /// <c>TimeScale = 0</c> — the OTHER way to halt the clock — is NOT reachable through the cluster
    /// op path. <c>ClusterMaster</c> maps any non-positive payload to <c>1f</c>
    /// (<c>scale = dto != null &amp;&amp; dto.TimeScale &gt; 0f ? dto.TimeScale : 1f</c>), so a
    /// "set scale to zero" request silently becomes "resume full speed".
    ///
    /// <para>This rail records the behaviour rather than endorsing it. It matters to <c>M-42</c>:
    /// <c>GlobalTime.IsPaused</c> is defined as <c>TimeScale == 0</c>, and nothing on any pause path
    /// — including this one — ever sets it, so that flag stays false while the sim is paused. The
    /// predicate to read is <c>DeltaTime</c>.</para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task SetTimeScale_Zero_IsCoercedToOne_ByTheClusterOpPath()
    {
        await SendTimeOpAsync(ClusterOpType.SetTimeScale, "{\"TimeScale\":0.0}").ConfigureAwait(false);
        _harness.PumpUntil(() => false, SettleFrames);

        Assert.Equal(1.0f, _orchestratorSvc.TestHook_TimeScale, 3);

        // And therefore the clock keeps running: TimeScale is not a usable halt over this path.
        double delta = ObserveSimTimeDelta(600);
        Assert.True(delta > 0.05,
            $"TimeScale=0 over the cluster op path does not halt the clock; delta={delta:F3}s");
    }
}
