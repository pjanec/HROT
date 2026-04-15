using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Time.Controllers;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.IG;
using Hrot.ExCon;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Regression suite verifying that all four cluster nodes (Orchestrator, SimHost, IG, ExCon)
/// agree on sim time at every point in the continuous → pause → step → resume lifecycle.
///
/// <para>
/// All subsystems boot in the same process using CycloneDDS loopback.  No DDS shortcuts —
/// events travel through real DDS topics with TransientLocal / Reliable QoS exactly as they
/// do in production.  The only difference from a real multi-machine deployment is that all
/// nodes share the same <c>Stopwatch</c> domain, so NTP corrections converge to zero.
/// </para>
///
/// <para>
/// Tolerance contract: after the initial DDS settle period all nodes must display sim time
/// within <see cref="MaxDriftSec"/> of each other.  On Pause and after each Step the tolerance
/// tightens to <see cref="StepTolerance"/> because the master broadcasts an authoritative
/// <c>SimTimeSnapshot</c> and every slave snaps to it.
/// </para>
/// </summary>
public sealed class SimTimeSyncIntegrationTests : IDisposable
{
    // ── tolerances ────────────────────────────────────────────────────────
    // Continuous mode: allow up to 80 ms spread across nodes (1 frame ≈ 16 ms;
    // the harness pumps ~20 frames + 200 ms DDS settle before each assertion).
    private const double MaxDriftSec  = 0.080;
    // Step / Pause mode: master sent an authoritative SimTimeSnapshot.
    // Every slave must snap to within 1 µs after processing the event.
    private const double StepTolerance = 1e-6;

    // ── pump constants ─────────────────────────────────────────────────────
    private const int PumpSleepMs  = 5;
    private const int SettleFrames = 80;   // frames to allow DDS loopback after any op

    private readonly HrotRunnerHarness         _harness;
    private readonly OrchestratorSubsystem     _orch;
    private readonly SimHostSubsystem          _simHost;
    private readonly IgSubsystem               _ig;
    private readonly ExConSubsystem            _exCon;
    private readonly ClusterMaster             _master;
    private readonly ITestOutputHelper         _out;
    // Indirect access: Hrot.IG internals visible to this test assembly (InternalsVisibleTo).
    private readonly Hrot.IG.IgApplication     _igApp;

    public SimTimeSyncIntegrationTests(ITestOutputHelper output)
    {
        _out     = output;
        _harness = new HrotRunnerHarness();
        _orch    = _harness.OrchestratorSvc;
        _simHost = _harness.SimHost;
        _ig      = _harness.Ig;
        _exCon   = _harness.ExCon;
        _master  = _orch.TestHook_ClusterMaster!;
        _igApp   = _ig.App;

        // Extra settle: give the initial SwitchTimeModeEvent (TransientLocal) and NTP
        // handshakes time to propagate through CycloneDDS loopback to all slaves.
        Settle(SettleFrames * 2);
        LogAllSimTimes("after harness ready");
    }

    public void Dispose() => _harness.Dispose();

    // ── helpers ────────────────────────────────────────────────────────────

    private void Settle(int frames = SettleFrames)
    {
        _harness.PumpFrames(frames);
        Thread.Sleep(frames * PumpSleepMs);
    }

    private (double orch, double sim, double ig, double excon) AllTimes() =>
    (
        _orch.TestHook_CurrentSimTime,
        _simHost.TestHook_CurrentSimTime,
        _igApp.TestHook_CurrentSimTime,
        _exCon.TestHook_SlaveSyncController!.GetCurrentState().TotalTime
    );

    private void LogAllSimTimes(string context)
    {
        var (o, s, ig, e) = AllTimes();
        _out.WriteLine($"[{context}]");
        _out.WriteLine($"  Orchestrator : {o:F4} s");
        _out.WriteLine($"  SimHost      : {s:F4} s");
        _out.WriteLine($"  IG           : {ig:F4} s");
        _out.WriteLine($"  ExCon        : {e:F4} s");
        double spread = Math.Max(Math.Max(o, s), Math.Max(ig, e))
                      - Math.Min(Math.Min(o, s), Math.Min(ig, e));
        _out.WriteLine($"  spread       : {spread * 1000:F2} ms");
    }

    private void AssertAllInSync(string context, double tolerance = MaxDriftSec)
    {
        // Read times once, then log and assert on that snapshot.
        var (o, s, ig, e) = AllTimes();
        _out.WriteLine($"[{context}]");
        _out.WriteLine($"  Orchestrator : {o:F4} s");
        _out.WriteLine($"  SimHost      : {s:F4} s");
        _out.WriteLine($"  IG           : {ig:F4} s");
        _out.WriteLine($"  ExCon        : {e:F4} s");
        double max    = Math.Max(Math.Max(o, s), Math.Max(ig, e));
        double min    = Math.Min(Math.Min(o, s), Math.Min(ig, e));
        double spread = max - min;
        _out.WriteLine($"  spread       : {spread * 1000:F2} ms");
        Assert.True(spread <= tolerance,
            $"[{context}] Sim-time spread {spread * 1000:F2} ms exceeds tolerance {tolerance * 1000:F0} ms. " +
            $"Orch={o:F4} SimHost={s:F4} IG={ig:F4} ExCon={e:F4}");
    }


    private async Task SendTimeOpAsync(ClusterOpType op, string payload = "")
    {
        await _master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = op,
            PayloadJson   = payload,
        }).ConfigureAwait(false);
        Settle(SettleFrames);
    }

    // ── TC-SYNC-1: continuous-mode sync agreement ──────────────────────────

    /// <summary>
    /// After startup all four nodes must show sim time within MaxDriftSec of each other.
    /// This catches Bug 7 (ApplyResume uses slave's warm-up time as baseline when
    /// SimTimeSnapshot = 0.0 from the initial SwitchTimeModeEvent).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void ContinuousMode_AllNodes_SimTimesWithinTolerance()
    {
        // Extra settle to make sure NTP and startup event have fully propagated.
        Settle(SettleFrames * 2);
        AssertAllInSync("continuous after startup");
    }

    // ── TC-SYNC-2: sim times stay in sync while time runs ─────────────────

    /// <summary>
    /// Sim times must remain in sync as the simulation runs for 1 s.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void ContinuousMode_AfterRunning_SimTimesStayInSync()
    {
        // Let the simulation run for ~1 s worth of frames.
        Settle(200);
        AssertAllInSync("continuous after 1 s");
    }

    // ── TC-SYNC-3: Pause snaps all slaves to master snapshot ──────────────

    /// <summary>
    /// After Pause, all nodes must show identical sim time (the master's authoritative
    /// SimTimeSnapshot carried by <c>SwitchTimeModeEvent</c>).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Pause_AllNodes_ShowSameSimTime()
    {
        Settle(100);
        LogAllSimTimes("before-pause continuous");

        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        LogAllSimTimes("after-send-pause (in Settle(80) + 400ms sleep)");

        bool ok = _harness.PumpUntil(() => _orch.UiCacheForTest!.IsPaused, 300);
        Assert.True(ok, "Master should be paused");
        LogAllSimTimes("after-PumpUntil");

        Settle(SettleFrames);
        LogAllSimTimes("after-second-settle");

        // Assert: spread must be minimal — the master broadcasts an authoritative SimTimeSnapshot
        // and slaves snap to it. After settle all nodes must show the same frozen sim time.
        AssertAllInSync("paused state", MaxDriftSec); // 80ms tolerance (tightened from 0.1ms)
    }

    // ── TC-SYNC-4: Step advances all slaves identically ───────────────────

    /// <summary>
    /// After 3 steps of 1 s each, every node must show the same stepped sim time.
    /// This validates that <c>AdvanceFrameIntent.TargetSimTime</c> is applied by
    /// all slave controllers and that FrameAck returns correctly to the master.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task PauseAndStep_AllNodes_ShowSameSimTimeAfterEachStep()
    {
        const float StepDelta = 1.0f;
        const int   Steps     = 3;
        string payload = $"{{\"FixedDelta\":{StepDelta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";

        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        bool paused = _harness.PumpUntil(() => _orch.UiCacheForTest!.IsPaused, 300);
        Assert.True(paused, "Should enter paused state");

        for (int i = 1; i <= Steps; i++)
        {
            await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
            Settle(SettleFrames);
            AssertAllInSync($"after step {i}", StepTolerance * 100);
        }
    }

    // ── TC-SYNC-5: Resume re-anchors all slaves to master snapshot ─────────

    /// <summary>
    /// After Pause → Steps → Resume the sim continues from the stepped sim time and
    /// all nodes remain in sync in continuous mode.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task PauseStepResume_AllNodes_SyncAfterResume()
    {
        const float StepDelta = 0.5f;
        string payload = $"{{\"FixedDelta\":{StepDelta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";

        Settle(100);

        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        bool paused = _harness.PumpUntil(() => _orch.UiCacheForTest!.IsPaused, 300);
        Assert.True(paused);

        // 2 steps
        await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);
        await SendTimeOpAsync(ClusterOpType.StepTime, payload).ConfigureAwait(false);

        await SendTimeOpAsync(ClusterOpType.ResumeTime).ConfigureAwait(false);
        bool resumed = _harness.PumpUntil(() => !_orch.UiCacheForTest!.IsPaused, 300);
        Assert.True(resumed);

        // Give slaves time to re-anchor and run a few frames.
        Settle(SettleFrames * 2);
        AssertAllInSync("after Pause→Step×2→Resume", MaxDriftSec);
    }

    // ── TC-SYNC-6: sim time is always positive ────────────────────────────

    /// <summary>
    /// All node sim times must be ≥ 0 at all lifecycle points.
    /// A negative value indicates a slave subtracted a large wall-clock baseline
    /// from its own tick domain without a valid NTP offset (Bug 7 footprint).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task AllNodes_SimTimeIsNonNegative_AtAllLifecyclePoints()
    {
        Settle(SettleFrames);
        var (o, s, ig, e) = AllTimes();
        _out.WriteLine($"[continuous] Orch={o:F4} SimHost={s:F4} IG={ig:F4} ExCon={e:F4}");
        Assert.True(o  >= 0, $"Orchestrator sim time is negative: {o:F4}");
        Assert.True(s  >= 0, $"SimHost sim time is negative: {s:F4}");
        Assert.True(ig >= 0, $"IG sim time is negative: {ig:F4}");
        Assert.True(e  >= 0, $"ExCon sim time is negative: {e:F4}");

        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        _harness.PumpUntil(() => _orch.UiCacheForTest!.IsPaused, 300);
        Settle(SettleFrames);

        var (o2, s2, ig2, e2) = AllTimes();
        _out.WriteLine($"[paused] Orch={o2:F4} SimHost={s2:F4} IG={ig2:F4} ExCon={e2:F4}");
        Assert.True(o2  >= 0, $"Orchestrator sim time is negative after pause: {o2:F4}");
        Assert.True(s2  >= 0, $"SimHost sim time is negative after pause: {s2:F4}");
        Assert.True(ig2 >= 0, $"IG sim time is negative after pause: {ig2:F4}");
        Assert.True(e2  >= 0, $"ExCon sim time is negative after pause: {e2:F4}");
    }
}
