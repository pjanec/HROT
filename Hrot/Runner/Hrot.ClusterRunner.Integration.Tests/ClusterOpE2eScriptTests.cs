using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Map.Common;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.NED.Factory;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.ExCon;
using Hrot.ClusterRunner.Testing;
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
    //   HrotRunnerHarness auto-counter (DomainIdBase=100, 46 runtime usages â†’ 100â€“145)
    //   AllSubsystemsClusterTransitionTests own counter (DomainBase=160 â†’ 160â€“161)
    private const int DomainBase = 170;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private static string ScriptPath(string fileName) =>
        Path.Combine("TestScripts", fileName);

    // â”€â”€ Shared helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Boots a minimal two-subsystem stack, loads <paramref name="scriptFileName"/>
    /// and runs it to completion via <see cref="HeadlessTestExecutor.RunAsync"/>.
    /// Returns the int exit code (0 = pass, 1 = fail).
    /// </summary>
    private static async Task<int> RunScriptAsync(string scriptFileName)
    {
        int domainId = NextDomainId();

        // Create the shared participant first so both the factory and the status reader use it.
        // This gives OrchestratorSubsystem real DDS connectivity (HEXAG2-S009).
        using var testParticipant = new DdsParticipant((uint)domainId);
        var factory = new NedNetworkFactory(
            participant:  testParticipant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.None);

        var orchestratorSvc = new OrchestratorSubsystem(factory);
        var simHostSvc      = new SimHostSubsystem(factory);

        var options = new RunnerOptions { Headless = true, DomainId = domainId };
        var orchestrator = new SubsystemOrchestrator(
            new ISubsystem[] { orchestratorSvc, simHostSvc },
            options);

        ILogger logger = NullLogger.Instance;

        var executor = new HeadlessTestExecutor(orchestrator, ScriptPath(scriptFileName), logger);

        // ⭐⭐ QA-018 — QUEUE the position-advancing system BEFORE the orchestrator initialises.
        //
        // ⛔ This used to sit in AfterInitialize, calling the old TestHook_AddSystem — which documented
        //    "must be called AFTER InitializeEmbedded" while ModuleHostKernel.RegisterGlobalSystem
        //    throws once Initialize() has run. Both guards could not hold, so all four cases in this
        //    class died on "Cannot register systems after Initialize() called".
        //
        // ⭐ MovingEntitySystem builds its query LAZILY on first Execute (see its own comment), so
        //    registering it here and registering MovingTestTag below is the order it was designed for.
        simHostSvc.TestHook_QueueSystem(new MovingEntitySystem());

        // Create a SysOpStatus reader on the shared participant BEFORE Initialize so that
        // the subscription is in place before the first SysOpStatus publication.
        using var statusReader    = new DdsReader<ClusterOpStatus>(testParticipant);

        // Wire everything that requires a live world/ClusterMaster inside AfterInitialize,
        // which runs after SubsystemOrchestrator.Initialize() but before the run loop.
        executor.AfterInitialize = () =>
        {
            var world       = simHostSvc.World!;
            var clusterMaster = orchestratorSvc.TestHook_ClusterMaster!;

            // Register the test-only MovingTestTag component. ⭐ QA-018: the SYSTEM that consumes it was
            // queued before Initialize (above); only the component registration belongs here.
            world.RegisterComponent<MovingTestTag>();

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

    // â”€â”€ Test facts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Transitions to RunningLive, records a moving entity for ~6 s,
    /// switches to RunningReplay, seeks to T=30 s, and asserts position â‰ 30 m.
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
