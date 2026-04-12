using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.ClusterRunner.Configuration;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.ExCon;
using Hrot.ClusterRunner.Testing;
using CycloneDDS.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// End-to-end cluster test scripts (CGF1-S0310).
/// Each test boots a minimal in-process stack (OrchestratorSubsystem + SimHostSubsystem),
/// loads a JSON script from TestScripts/, and asserts that
/// <see cref="HeadlessTestExecutor.RunAsync"/> returns 0 (pass).
/// </summary>
/// <remarks>
/// In the <c>HeavyE2ETests</c> collection so it runs sequentially with
/// <see cref="AllSubsystemsClusterTransitionTests"/> to avoid CPU starvation:
/// both use wall-clock <c>Stopwatch</c> scheduling internally.
/// </remarks>
[Collection("HeavyE2ETests")]
public sealed class ClusterOpE2eScriptTests
{
    // Unique DDS domain IDs so parallel test runs don't interfere.
    // Must be above:
    //   HrotRunnerHarness auto-counter (DomainIdBase=100, 46 runtime usages → 100–145)
    //   AllSubsystemsClusterTransitionTests own counter (DomainBase=160 → 160–161)
    private const int DomainBase = 170;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private static string ScriptPath(string fileName) =>
        Path.Combine("TestScripts", fileName);

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Boots a minimal two-subsystem stack, loads <paramref name="scriptFileName"/>
    /// and runs it to completion via <see cref="HeadlessTestExecutor.RunAsync"/>.
    /// Returns the int exit code (0 = pass, 1 = fail).
    /// </summary>
    private static async Task<int> RunScriptAsync(string scriptFileName)
    {
        int domainId = NextDomainId();

        var orchestratorSvc = new OrchestratorSubsystem();
        var simHostSvc      = new SimHostSubsystem();

        var options = new RunnerOptions { Headless = true, DomainId = domainId };
        var orchestrator = new SubsystemOrchestrator(
            new ISubsystem[] { orchestratorSvc, simHostSvc },
            options);

        ILogger logger = NullLogger.Instance;

        var executor = new HeadlessTestExecutor(orchestrator, ScriptPath(scriptFileName), logger);

        // Create a DDS participant + SysOpStatus reader BEFORE Initialize so that
        // the subscription is in place before the first SysOpStatus publication.
        using var testParticipant = new DdsParticipant((uint)domainId);
        using var statusReader    = new DdsReader<ClusterOpStatus>(testParticipant);

        // Wire everything that requires a live world/ClusterMaster inside AfterInitialize,
        // which runs after SubsystemOrchestrator.Initialize() but before the run loop.
        executor.AfterInitialize = () =>
        {
            var world       = simHostSvc.World!;
            var clusterMaster = orchestratorSvc.TestHook_ClusterMaster!;

            // Register the test-only MovingTestTag component and install the system
            // that advances entity positions each tick.
            world.RegisterComponent<MovingTestTag>();
            simHostSvc.TestHook_AddSystem(new MovingEntitySystem());

            // Register all action handlers used by the E2E scripts.
            executor.RegisterHandler(new SpawnActionHandler(world, logger));
            executor.RegisterHandler(new MoveActionHandler(world, logger));
            executor.RegisterHandler(new AssertPositionActionHandler(world, logger));
            executor.RegisterHandler(new AssertEntityCountActionHandler(world, logger));
            executor.RegisterHandler(new AddMovingTagActionHandler(world, logger));
            executor.RegisterHandler(new ClusterOpActionHandler(clusterMaster, statusReader, logger, timeoutSeconds: 30.0));
        };

        return await executor.RunAsync().ConfigureAwait(false);
    }

    // ── Test facts ────────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions to RunningLive, records a moving entity for ~6 s,
    /// switches to RunningReplay, seeks to T=30 s, and asserts position ≈ 30 m.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task RecordAndReplaySeek_Passes()
    {
        int result = await RunScriptAsync("e2e_record_and_replay_seek.json");
        Assert.Equal(0, result);
    }

    /// <summary>
    /// Transitions to RunningEdit, spawns an entity, enters RunningPreview,
    /// moves and re-spawns, then returns to RunningEdit and asserts the state reverts.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task PreviewStateRestore_Passes()
    {
        int result = await RunScriptAsync("e2e_preview_state_restore.json");
        Assert.Equal(0, result);
    }

    /// <summary>
    /// Records from RunningLive with a moving entity, switches to RunningReplay,
    /// branches to a new RunningLive exercise, spawns a post-branch entity, and
    /// asserts it has the correct position (no ID-allocator collision).
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task LiveFromReplayBranch_Passes()
    {
        int result = await RunScriptAsync("e2e_live_from_replay_branch.json");
        Assert.Equal(0, result);
    }

    /// <summary>
    /// Issues two rapid overlapping TakeCheckpoint requests while RunningLive,
    /// waits for both to complete, and asserts entity state is intact.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task OverlappingCheckpoints_Passes()
    {
        int result = await RunScriptAsync("e2e_overlapping_checkpoints.json");
        Assert.Equal(0, result);
    }
}
