using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.NED.Factory;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.IG;
using Hrot.ExCon;
using Hrot.Common;
using Hrot.Map.Common;
using Fdp.Toolkit.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Regression tests for CGF1-S0502: duplicate TransactionId fan-out.
///
/// <b>Regression guard for CGF1-S0502 / 2PC History fix:</b>
/// <c>ClusterMaster</c> fans out <c>PrepareXxx</c> and <c>CommitState</c> using the
/// same <c>tx.TransactionId</c> so that <see cref="ClusterMaster.ConsumeNodeOpStatuses"/>
/// can correlate ACKs and populate <c>NodeResponses</c> for the Orchestrator 2PC History UI.
/// Deduplication in <c>ClusterSlave</c> now uses a compound <c>(TransactionId, OperationId)</c>
/// key so both commands are accepted exactly once (the previous <c>HashSet&lt;Guid&gt;</c>
/// dropped <c>CommitState</c> as a duplicate, preventing slaves from advancing their state).
///
/// <para>These tests boot all four subsystems (Orchestrator, SimHost, IG, ExCon) in a
/// single headless in-process stack via <see cref="HeadlessTestExecutor"/> and assert
/// that ExCon's <c>ClusterSlave.LocalStateIdForTest</c> reaches the expected state.
/// This is only possible when <c>CommitState</c> is <em>not</em> dropped.</para>
/// </summary>
/// <remarks>
/// In the <c>HeavyE2ETests</c> collection so it runs sequentially with
/// <see cref="ClusterOpE2eScriptTests"/> to avoid CPU starvation:
/// both use wall-clock <c>Stopwatch</c> scheduling internally.
/// </remarks>
[Collection("HeavyE2ETests")]
public sealed class AllSubsystemsClusterTransitionTests
{
    // Use domain IDs starting at 160 to avoid collisions with ClusterOpE2eScriptTests (130+).
    private const int DomainBase = 160;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private static string ScriptPath(string fileName) =>
        Path.Combine("TestScripts", fileName);

    // â”€â”€ Shared harness builder â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static (SubsystemOrchestrator orchestrator, OrchestratorSubsystem orchestratorSvc, ExConSubsystem exConSvc, DdsParticipant participant)
        BuildOrchestrator(int domainId)
    {
        // Create a single shared DDS participant so all subsystems (including Orchestrator
        // and ExCon) have real DDS connectivity for heartbeats and cluster-op routing.
        var participant = HrotEnvironment.CreateParticipant(domainId);
        var factory = new NedNetworkFactory(
            participant:  participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.None);

        var orchestratorSvc = new OrchestratorSubsystem(factory);
        var simHostSvc      = new SimHostSubsystem(factory);
        var igSvc           = new IgSubsystem(factory);
        var exConSvc        = new ExConSubsystem(factory);

        var options = new RunnerOptions { Headless = true, DomainId = domainId };
        var orchestrator = new SubsystemOrchestrator(
            new ISubsystem[] { orchestratorSvc, simHostSvc, igSvc, exConSvc },
            options);

        return (orchestrator, orchestratorSvc, exConSvc, participant);
    }

    // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Regression for CGF1-S0502 â€” single Idleâ†’OperatingLive transition.
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
        var (orchestrator, orchestratorSvc, exConSvc, participant) = BuildOrchestrator(domainId);
        using var _ = participant;

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
        ScriptRunAssert.Passed(executor, result);
    }

    /// <summary>
    /// Full Idleâ†’OperatingLiveâ†’Idleâ†’OperatingLiveâ†’Idle round-trip, repeated twice.
    ///
    /// <para>Verifies that <c>ClusterSlave.LocalStateIdForTest</c> correctly tracks all
    /// state transitions across two complete load/operate/unload cycles.  Also validates
    /// that the cluster can return to <c>Idle</c> and be re-loaded without error.</para>
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle()
    {
        int domainId = NextDomainId();
        var (orchestrator, orchestratorSvc, exConSvc, participant) = BuildOrchestrator(domainId);
        using var _ = participant;

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
        ScriptRunAssert.Passed(executor, result);
    }

    // â”€â”€ Custom action handler â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Issues a <see cref="ClusterOpType.TransitionState"/> request and asserts that
    /// <see cref="ClusterSlave.LocalStateIdForTest"/> reaches the target value.
    ///
    /// <para>Does NOT poll for <c>ClusterOpStatus.Success</c> (not published for
    /// <c>TransitionState</c>).  Instead, polls the slave's <c>LocalStateIdForTest</c>
    /// directly â€” which only advances when <c>CommitState</c> is received and NOT
    /// dropped by the deduplication guard.</para>
    ///
    /// <para>Waits for at least one active node in the roster before sending the request,
    /// handling the case where heartbeats haven't arrived yet at test start.</para>
    ///
    /// Action name: <c>"assert_slave_transition"</c>.<br/>
    /// Args:
    /// <list type="bullet">
    ///   <item><c>TargetState</c> (string) â€” <see cref="ClusterState"/> name (e.g. "OperatingLive").</item>
    ///   <item><c>ExerciseId</c> (string, optional) â€” included in the request payload.</item>
    ///   <item><c>TimeoutSeconds</c> (double, default 6.0) â€” poll deadline.</item>
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
            // state where all nodes are already in the target â€” in that case the roster is
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
                // ⭐⭐ QA-027 — the enum NAME, not its integer. TransitionPayloadDto's TargetState carries
                //    [JsonConverter(typeof(StrictStringEnumConverter))] and OrchestrationJsonOptions
                //    documents itself as rejecting integer enum values "to avoid silent
                //    integer-as-enum bugs". An int here deserialised to null ⇒ the adapter threw ⇒
                //    ClusterMaster caught it into a Warn log ⇒ the cluster silently stayed at state 0.
                ["TargetState"] = targetState.ToString(),
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
            // Historical note (CGF1-S0502 / 2PC History fix):
            //   Previously, PrepareXxx and CommitState were sent with the same tx.TransactionId.
            //   ClusterSlave._seenTransactionIds (HashSet<Guid>) dropped CommitState as a dup,
            //   so _localStateId never advanced.  The workaround was Guid.NewGuid() per op,
            //   which broke NodeResponses correlation in ConsumeNodeOpStatuses.
            //   Fix: _seenTransactionIds now uses a compound (TransactionId, OperationId) key,
            //   accepting each operation once, and tx.TransactionId is used for both commands.
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (_exConSlave.LocalStateIdForTest == (int)targetState)
                    return null;

                await Task.Delay(50).ConfigureAwait(false);
            }

            // ⭐ QA-029 — report the MASTER's state alongside the slave's. Without it the red cannot
            //    distinguish "the master never transitioned" from "the master transitioned and the
            //    slave did not follow", which are different defects in different components.
            throw new InvalidOperationException(
                $"assert_slave_transition: ExCon ClusterSlave.LocalStateIdForTest is " +
                $"{_exConSlave.LocalStateIdForTest} (expected {(int)targetState} = {targetState}) " +
                $"after {timeoutSeconds}s. " +
                $"ClusterMaster.CurrentClusterState is {(int)_master.CurrentClusterState} " +
                $"({_master.CurrentClusterState}). " +
                $"Active roster nodes at timeout: {_master.NodeRoster.ActiveNodes.Count}. " +
                "Likely cause: compound-key deduplication in ClusterSlave not working, " +
                "or CommitState commands not being dispatched.");
        }
    }
}
