using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Services;
using FDP.Toolkit.Orchestration;
using Hrot.Orchestrator;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Regression tests for CGF1-S0502: duplicate TransactionId fan-out.
///
/// When <c>ClusterMaster.ProcessSingleClusterOpRequest</c> fans out <c>PrepareXxx</c>
/// and <c>CommitState</c> with the <b>same</b> <c>TransactionId</c>, the
/// <c>ClusterSlave._seenTransactionIds</c> deduplication guard drops <c>CommitState</c>
/// (and any subsequent commands in the same trajectory step that share the ID).
/// As a result, slave nodes never advance their local state and the transition hangs
/// indefinitely — observed as repeated "Duplicate TransactionId dropped" debug lines
/// followed by heartbeat timeouts.
///
/// <para>These tests boot all four subsystems (Orchestrator, SimHost, IG, ExCon) in a
/// single headless in-process stack via <see cref="HeadlessTestExecutor"/> and assert
/// that ExCon's <c>ClusterSlave.LocalStateIdForTest</c> reaches the expected state.
/// This is only possible when <c>CommitState</c> is <em>not</em> dropped.</para>
/// </summary>
public sealed class AllSubsystemsClusterTransitionTests
{
    // Use domain IDs starting at 160 to avoid collisions with ClusterOpE2eScriptTests (130+).
    private const int DomainBase = 160;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private static string ScriptPath(string fileName) =>
        Path.Combine("TestScripts", fileName);

    // ── Shared harness builder ────────────────────────────────────────────────

    private static (SubsystemOrchestrator orchestrator, OrchestratorSubsystem orchestratorSvc, ExConSubsystem exConSvc)
        BuildOrchestrator(int domainId)
    {
        var orchestratorSvc = new OrchestratorSubsystem();
        var simHostSvc      = new SimHostSubsystem();
        var igSvc           = new IgSubsystem();
        var exConSvc        = new ExConSubsystem();

        var options = new RunnerOptions { Headless = true, DomainId = domainId };
        var orchestrator = new SubsystemOrchestrator(
            new ISubsystem[] { orchestratorSvc, simHostSvc, igSvc, exConSvc },
            options);

        return (orchestrator, orchestratorSvc, exConSvc);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression for CGF1-S0502 — single Idle→OperatingLive transition.
    ///
    /// <para>Boots Orchestrator + SimHost + IG + ExCon and verifies that
    /// <c>ExConSubsystem.TestHook_ClusterSlave.LocalStateIdForTest</c> reaches
    /// <c>(int)ClusterState.OperatingLive</c> (31).  Fails with the original bug
    /// because <c>CommitState</c> is silently dropped as a duplicate TransactionId.</para>
    /// </summary>
    [Fact(Timeout = 25_000)]
    public async Task AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate()
    {
        int domainId = NextDomainId();
        var (orchestrator, orchestratorSvc, exConSvc) = BuildOrchestrator(domainId);

        var executor = new HeadlessTestExecutor(
            orchestrator,
            ScriptPath("e2e_all_subsystems_loading_live.json"),
            NullLogger.Instance);

        executor.AfterInitialize = () =>
        {
            executor.RegisterHandler(new ClusterTransitionAssertionHandler(
                orchestratorSvc.TestHook_ClusterMaster!,
                exConSvc.TestHook_ClusterSlave!));
        };

        int result = await executor.RunAsync().ConfigureAwait(false);
        Assert.Equal(0, result);
    }

    /// <summary>
    /// Full Idle→OperatingLive→Idle→OperatingLive→Idle round-trip, repeated twice.
    ///
    /// <para>Verifies that <c>ClusterSlave.LocalStateIdForTest</c> correctly tracks all
    /// state transitions across two complete load/operate/unload cycles.  Also validates
    /// that the cluster can return to <c>Idle</c> and be re-loaded without error.</para>
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle()
    {
        int domainId = NextDomainId();
        var (orchestrator, orchestratorSvc, exConSvc) = BuildOrchestrator(domainId);

        var executor = new HeadlessTestExecutor(
            orchestrator,
            ScriptPath("e2e_all_subsystems_full_cycle.json"),
            NullLogger.Instance);

        executor.AfterInitialize = () =>
        {
            executor.RegisterHandler(new ClusterTransitionAssertionHandler(
                orchestratorSvc.TestHook_ClusterMaster!,
                exConSvc.TestHook_ClusterSlave!));
        };

        int result = await executor.RunAsync().ConfigureAwait(false);
        Assert.Equal(0, result);
    }

    // ── Custom action handler ─────────────────────────────────────────────────

    /// <summary>
    /// Issues a <see cref="ClusterOpType.TransitionState"/> request and asserts that
    /// <see cref="ClusterSlave.LocalStateIdForTest"/> reaches the target value.
    ///
    /// <para>Does NOT poll for <c>ClusterOpStatus.Success</c> (not published for
    /// <c>TransitionState</c>).  Instead, polls the slave's <c>LocalStateIdForTest</c>
    /// directly — which only advances when <c>CommitState</c> is received and NOT
    /// dropped by the deduplication guard.</para>
    ///
    /// <para>Waits for at least one active node in the roster before sending the request,
    /// handling the case where heartbeats haven't arrived yet at test start.</para>
    ///
    /// Action name: <c>"assert_slave_transition"</c>.<br/>
    /// Args:
    /// <list type="bullet">
    ///   <item><c>TargetState</c> (string) — <see cref="ClusterState"/> name (e.g. "OperatingLive").</item>
    ///   <item><c>ExerciseId</c> (string, optional) — included in the request payload.</item>
    ///   <item><c>TimeoutSeconds</c> (double, default 6.0) — poll deadline.</item>
    /// </list>
    /// </summary>
    private sealed class ClusterTransitionAssertionHandler : ITestActionHandler
    {
        private readonly ClusterMaster _master;
        private readonly ClusterSlave  _exConSlave;

        public string ActionName => "assert_slave_transition";

        public ClusterTransitionAssertionHandler(ClusterMaster master, ClusterSlave exConSlave)
        {
            _master     = master     ?? throw new ArgumentNullException(nameof(master));
            _exConSlave = exConSlave ?? throw new ArgumentNullException(nameof(exConSlave));
        }

        public async Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            string targetStateStr = args.TryGetValue("TargetState", out var ts)
                ? Convert.ToString(ts) ?? string.Empty
                : string.Empty;

            if (!Enum.TryParse(targetStateStr, ignoreCase: true, out ClusterState targetState))
                throw new ArgumentException(
                    $"assert_slave_transition: cannot parse TargetState '{targetStateStr}' as ClusterState.");

            double timeoutSeconds = args.TryGetValue("TimeoutSeconds", out var tout)
                ? Convert.ToDouble(tout)
                : 6.0;

            string? exerciseId = args.TryGetValue("ExerciseId", out var ei)
                ? Convert.ToString(ei) : null;

            // Wait for at least one active roster node before sending the transition request.
            // Slaves send heartbeats at 1 Hz; the first heartbeat may not have arrived yet if
            // this step fires immediately at startup (before t=1s has elapsed).
            // Also ensures we don't send the request when transitioning back to Idle from a
            // state where all nodes are already in the target — in that case the roster is
            // already populated so this loop exits immediately.
            var rosterDeadline = DateTime.UtcNow.AddSeconds(5.0);
            while (_master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
                await Task.Delay(100).ConfigureAwait(false);

            if (_master.NodeRoster.ActiveNodes.Count == 0)
                throw new InvalidOperationException(
                    $"assert_slave_transition: no nodes appeared in the roster within 5 s. " +
                    "Check that all subsystems are sending heartbeats.");

            // Build and issue the transition request.
            var payload = new Dictionary<string, object>
            {
                ["TargetState"] = (int)targetState,
            };
            if (!string.IsNullOrEmpty(exerciseId))
                payload["ExerciseId"] = exerciseId!;

            await _master.HandleClusterOpRequestAsync(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = JsonSerializer.Serialize(payload),
            }).ConfigureAwait(false);

            // Poll ClusterSlave.LocalStateIdForTest until it reaches the expected value.
            //
            // With the CGF1-S0502 bug present:
            //   PrepareXxx(TxID=X) is dispatched → X added to _seenTransactionIds.
            //   CommitState(TxID=X) arrives → Add(X) returns false → dropped
            //   → _localStateId never advances → timeout.
            //
            // After fix (unique TransactionId per FanOutNodeOp):
            //   CommitState(TxID=Y, Y≠X) passes the guard → _localStateId = target.
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (_exConSlave.LocalStateIdForTest == (int)targetState)
                    return null;

                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                $"assert_slave_transition: ExCon ClusterSlave.LocalStateIdForTest is " +
                $"{_exConSlave.LocalStateIdForTest} (expected {(int)targetState} = {targetState}) " +
                $"after {timeoutSeconds}s. " +
                $"Active roster nodes at timeout: {_master.NodeRoster.ActiveNodes.Count}. " +
                "Likely cause: CommitState commands dropped as duplicate TransactionIds " +
                "(CGF1-S0502) — PrepareXxx and CommitState in the fan-out loop share the same " +
                "tx.TransactionId; ClusterSlave._seenTransactionIds drops the second command.");
        }
    }
}
