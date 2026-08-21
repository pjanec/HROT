using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Time.Controllers;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// TEMPORARY DIAGNOSTIC (Batch 104 / AS-14 root-cause). Not a gate.
///
/// AS-14: MasterSyncController.Step returns early when _pendingAcks.Count > 0, so a step
/// requested while ACKs are outstanding is discarded. Two hypotheses were on the table:
///   (1) the inter-step settle is too short, so _pendingAcks has not cleared yet;
///   (2) the slave never ACKs in this harness, so only the FIRST step ever works.
/// This probe distinguishes them by sampling _pendingAcks / _expectedSlaves around each step.
/// </summary>
public sealed class TimeStepAckDiagnosticTests : IDisposable
{
    private const int PumpSleepMs  = 5;
    private const int SettleFrames = 60;

    private readonly ITestOutputHelper _out;
    private readonly HrotRunnerHarness _harness;
    private readonly ClusterMaster     _master;
    private readonly SimHostSubsystem  _simHost;
    private readonly OrchestratorSubsystem _orchestratorSvc;

    public TimeStepAckDiagnosticTests(ITestOutputHelper output)
    {
        _out             = output;
        _harness         = new HrotRunnerHarness();
        _master          = _harness.OrchestratorSvc.TestHook_ClusterMaster!;
        _simHost         = _harness.SimHost;
        _orchestratorSvc = _harness.OrchestratorSvc;
    }

    public void Dispose() => _harness.Dispose();

    // ── reflection probes into the master controller ──────────────────────────

    private static readonly FieldInfo MasterSyncField =
        typeof(OrchestratorSubsystem).GetField("_masterSync", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private object MasterSync => MasterSyncField.GetValue(_orchestratorSvc)!;

    private static ICollection<int> Set(object controller, string field) =>
        (ICollection<int>)controller.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(controller)!;

    private string Snapshot(string label)
    {
        var ms = MasterSync;
        var pending  = Set(ms, "_pendingAcks");
        var expected = Set(ms, "_expectedSlaves");
        var mode = ms.GetType().GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ms);
        return $"{label}: mode={mode} pendingAcks=[{string.Join(",", pending)}] " +
               $"expectedSlaves=[{string.Join(",", expected)}] " +
               $"masterSimTime={_orchestratorSvc.TestHook_CurrentSimTime:F3} " +
               $"simHostSimTime={_simHost.TestHook_CurrentSimTime:F3} " +
               $"simHostCtrlMode={_simHost.TestHook_TimeControllerMode} " +
               $"cgfCtrl={CgfControllerDescription()}";
    }

    /// <summary>
    /// Reaches into CgfSubsystem's HrotNodeContext → kernel → time controller and reports its
    /// type and mode.  If CGF never leaves Continuous, it never heard the pause at all.
    /// </summary>
    private string CgfControllerDescription()
    {
        var cgf = _harness.Cgf;
        if (cgf == null) return "<no cgf>";
        var ctxField = cgf.GetType().GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic);
        var ctx = ctxField?.GetValue(cgf);
        if (ctx == null) return "<no context>";
        var kernel = ctx.GetType().GetProperty("Kernel")?.GetValue(ctx);
        if (kernel == null) return "<no kernel>";
        var ctrl = kernel.GetType()
            .GetField("_timeController", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(kernel);
        if (ctrl == null) return "<no controller>";
        var modeMethod = ctrl.GetType().GetMethod("GetMode");
        var mode = modeMethod?.Invoke(ctrl, null);
        return $"{ctrl.GetType().Name}/{mode}";
    }

    private async Task SendTimeOpAsync(ClusterOpType opType, string payload = "")
    {
        await _master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = opType,
            PayloadJson   = payload,
        }).ConfigureAwait(false);

        _harness.PumpUntil(() => false, SettleFrames);

        if (opType == ClusterOpType.PauseTime)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(5000);
            while (DateTime.UtcNow < deadline &&
                   _simHost.TestHook_TimeControllerMode != Fdp.ModuleHost.Time.TimeMode.Deterministic)
            {
                _harness.PumpFrames(1);
                Thread.Sleep(PumpSleepMs);
            }
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Diagnostic_ThreeSteps_ReportPendingAckSequence()
    {
        const float StepDeltaSec = 1.0f;
        string stepPayload = "{\"FixedDelta\":1.000}";

        await SendTimeOpAsync(ClusterOpType.PauseTime).ConfigureAwait(false);
        _out.WriteLine(Snapshot("after PAUSE"));

        for (int i = 1; i <= 3; i++)
        {
            _out.WriteLine(Snapshot($"before STEP {i}"));
            await SendTimeOpAsync(ClusterOpType.StepTime, stepPayload).ConfigureAwait(false);
            _out.WriteLine(Snapshot($"just after STEP {i} request+settle"));

            _harness.PumpFrames(SettleFrames);
            Thread.Sleep(SettleFrames * PumpSleepMs);
            _out.WriteLine(Snapshot($"after STEP {i} extra settle"));

            // How long does it take for the ACK set to clear, if ever?
            var t0 = DateTime.UtcNow;
            var deadline = t0.AddMilliseconds(5000);
            while (DateTime.UtcNow < deadline && Set(MasterSync, "_pendingAcks").Count > 0)
            {
                _harness.PumpFrames(1);
                Thread.Sleep(PumpSleepMs);
            }
            var waited = (DateTime.UtcNow - t0).TotalMilliseconds;
            int remaining = Set(MasterSync, "_pendingAcks").Count;
            _out.WriteLine($"  STEP {i}: waited {waited:F0} ms for ACKs; remaining={remaining}");
        }

        _out.WriteLine(Snapshot("FINAL"));
        Assert.True(true);
    }
}
