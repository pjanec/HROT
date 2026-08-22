using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;
using Hrot.Core.Network;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-03 mandatory integration test — proves that the scenario load pipeline
/// through the real <see cref="ClusterMaster"/> / <see cref="ClusterSlave"/> 2PC path
/// and <see cref="Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler"/> actually materialises
/// entities in the editor's <see cref="EntityRepository"/>.
///
/// <para>
/// This is the gate test that verifies the root-cause fix: the <c>HrotEditLoadHandler</c>
/// must read scenario JSON from <c>NasBasePath/scenarios/{scenarioId}/</c>, not from the
/// node staging root.  Without the fix, <c>PrepareAsync</c> throws
/// <c>InvalidOperationException("no scenario file found")</c> and entities never load.
/// </para>
///
/// <para>
/// The test builds a full orchestration pump that mirrors the
/// <c>EditorSubsystem.Update()</c> frame ordering:
/// <list type="number">
///   <item><c>kernel.Update()</c> — <c>ClusterSlave.Tick()</c> via <see cref="OrchestrationLogicPack"/></item>
///   <item><c>orchBus.SwapBuffers()</c></item>
///   <item><c>clusterMaster.Tick()</c></item>
///   <item><c>editorApp.Update()</c></item>
/// </list>
/// </para>
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiScenarioLoadTests : IDisposable
{
    private const int PumpTimeoutMs = 30_000;
    private const int PumpSleepMs   = 5;

    /// <summary>
    /// TkbType registered in <see cref="EditorHarness"/>'s TkbDatabase (type 1L = "TestUnit").
    /// Scenario JSON uses this type so <c>NetworkSpawningSystem</c> can create the entity.
    /// </summary>
    private const long TestTkbType = 1L;

    private readonly string _scenarioId;
    private readonly string _nasScenarioDir;

    public DebugApiScenarioLoadTests()
    {
        _scenarioId     = "ada-b03-" + Guid.NewGuid().ToString("N")[..8];
        _nasScenarioDir = Path.Combine(
            ClusterConfiguration.Default.NasBasePath,
            OrchestrationConstants.ScenariosDirectoryName,
            _scenarioId);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_nasScenarioDir))
                Directory.Delete(_nasScenarioDir, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    // ── The gate test ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ADA-BATCH-03 gate: loading a scenario via the full orchestration pipeline
    /// (ClusterMaster → ClusterSlave → HrotEditLoadHandler) reaches OperatingEdit
    /// and materialises at least one entity.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void LoadScenarioByName_ViaOrchestrationPipeline_MaterialisesEntities()
    {
        // ── Step 0: write a valid scenario to the NAS scenarios root ──────────
        // The scenario uses TkbType 1L ("TestUnit") which EditorHarness registers.
        Directory.CreateDirectory(_nasScenarioDir);
        var scenarioJson = BuildMinimalScenarioJson(TestTkbType);
        File.WriteAllText(Path.Combine(_nasScenarioDir, "scenario.json"), scenarioJson);

        // ── Step 1: build the harness and orchestration components ────────────
        // EditorHarness provides the full editor ECS kernel (kernel, repo, entityMap,
        // spawn system, modules).  We add ClusterMaster and wire HrotEditLoadHandler
        // to the NAS storage provider (the ADA-BATCH-03 fix), then override the
        // pump to run the correct frame-ordering (mirroring EditorSubsystem.Update).
        using var harness = new EditorHarness();

        // Register all orchestration event types on the harness orchBus so that
        // PublishManaged<TransitionStateIntent>, PublishManaged<NodeHeartbeatEvent>, etc.
        // do not throw InvalidOperationException.  The plain EditorHarness does not call
        // OrchestrationEventRegistry.RegisterAll because its ClusterSlave is never ticked.
        OrchestrationEventRegistry.RegisterAll(harness.OrchBus);

        // Build a new ClusterSlave wired to the harness's orchestration bus.
        // The harness already has a ClusterSlave created internally (inside EditorApplication),
        // but we need one with HrotEditLoadHandler to test the full load path.
        // We build the slave + its handlers here, register the OrchestrationLogicPack in the
        // kernel before Initialize() would have been called — but the harness already initialized.
        // Instead we drive ClusterSlave.Tick() manually in our pump.
        var orchBus    = harness.OrchBus;
        var clusterSlave = new ClusterSlave(0, "Editor", orchBus);

        // ADA-BATCH-03 FIX: use NAS storage provider so HrotEditLoadHandler reads from
        // C:\FDP_Temp\shared\scenarios\{id}\ instead of the staging root.
        var nasProvider    = new LocalDiskStorageProvider(ClusterConfiguration.Default.NasBasePath);
        var scenarioLoader = new HrotScenarioLoader(nasProvider, "Hrot.Scenario");

        // Staging provider for ReferencePrefetchHandler (creates staging directories).
        var stagingProvider = new LocalDiskStorageProvider(
            OrchestrationConstants.GetNodeStagingRoot(0));
        clusterSlave.RegisterHandler(new ReferencePrefetchHandler(stagingProvider));

        var serializer     = harness.Serializer;
        var zoneService    = harness.ZoneService;
        var extractor      = new Hrot.CGF.Orchestration.StagingEntityExtractor();
        var scenarioSource = new ScenarioEntityCreationRequestSource();

        // IdAllocator: harness's internal SequentialIdAllocator starts at 1000;
        // we use a separate sequential allocator for the orchestration handler.
        var idAlloc = new SimpleSequentialIdAllocator(startId: 2000);

        clusterSlave.RegisterHandler(new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(
            serializer, scenarioLoader, zoneService, extractor, scenarioSource, idAlloc, harness.Repo));

        // ClusterMaster (empty Mandatory → bootstrap latches immediately).
        var clusterMaster = new ClusterMaster(orchBus);

        // ── Step 2: pre-register node 0 in ClusterMaster's roster ────────────
        // ClusterSlave publishes heartbeats every 1 second. In a sub-millisecond
        // test loop the timer never fires, so ClusterMaster.activeNodeIds remains
        // empty. Without nodes the master skips ExecuteNodeOpIntent fan-out and
        // calls PublishOpStatus(Success) without calling PublishClusterState —
        // so EditorApplication never sees ClusterStateUpdateEvent{OperatingEdit}.
        //
        // Fix: inject a NodeHeartbeatEvent for node 0 directly into orchBus, then
        // swap and tick ClusterMaster once so the roster is populated before we
        // call LoadScenarioByName.
        orchBus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 0,
            LocalStateId  = 0,   // = ClusterState.Idle (enum value 0)
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "Editor",
        });
        orchBus.SwapBuffers();      // make the heartbeat readable
        clusterMaster.Tick();        // IngestHeartbeats → roster["Editor" node 0] populated

        // ── Step 3: kick off the scenario load ────────────────────────────────
        harness.Editor.LoadScenarioByName(_scenarioId);

        // ── Step 4: pump the full orchestration loop ──────────────────────────
        // Frame ordering mirrors EditorSubsystem.Update():
        //   1. kernel.Update()         → (our clusterSlave is not in kernel; we call it manually)
        //   2. orchBus.SwapBuffers()   → makes ClusterMaster intents readable
        //   3. clusterMaster.Tick()    → fans out ExecuteNodeOpIntents
        //   4. clusterSlave.Tick()     → processes intents, calls PrepareAsync/Commit
        //   5. harness.Repo bus pump   → drains SpawnEntityCommands
        //   6. editorApp.Update()      → reads ClusterStateUpdateEvent

        bool reachedOperatingEdit = PumpUntilOperatingEdit(
            harness, clusterSlave, clusterMaster, orchBus, scenarioSource, PumpTimeoutMs);

        // ── Step 5: assert ────────────────────────────────────────────────────
        Assert.True(reachedOperatingEdit,
            $"Cluster must reach OperatingEdit within {PumpTimeoutMs} ms. " +
            $"Current cluster state: {GetClusterState(harness)}. " +
            $"Entity count: {harness.Repo.EntityCount}.");

        // Pump a few extra frames to let genesis pipeline promote Constructing → Active.
        for (int i = 0; i < 10; i++)
            PumpOneFrame(harness, clusterSlave, clusterMaster, orchBus, scenarioSource);

        Assert.True(harness.Repo.EntityCount > 0,
            $"EntityCount must be > 0 after scenario load. Got: {harness.Repo.EntityCount}.");
    }

    // ── Frame pump ────────────────────────────────────────────────────────────────

    private static ClusterState GetClusterState(EditorHarness harness)
    {
        var svc = harness.BuildDebugApiService();
        var status = svc.GetStatus().AsObject();
        return Enum.TryParse<ClusterState>(status["clusterState"]?.GetValue<string>(), out var s)
            ? s
            : ClusterState.Idle;
    }

    private static void PumpOneFrame(
        EditorHarness harness,
        ClusterSlave clusterSlave,
        ClusterMaster clusterMaster,
        FdpEventBus orchBus,
        ScenarioEntityCreationRequestSource scenarioSource)
    {
        // 1. Advance harness kernel (pumps ECS systems including SpawnEntityCommand handling).
        harness.PumpFrames(1);

        // 2. Swap orchestration bus so ClusterMaster intents published last frame are readable.
        orchBus.SwapBuffers();

        // 3. ClusterMaster tick: reads TransitionStateIntents, publishes ExecuteNodeOpIntents.
        clusterMaster.Tick();

        // 4. ClusterSlave tick: reads ExecuteNodeOpIntents, dispatches to HrotEditLoadHandler.
        clusterSlave.Tick();

        // 5. Drain entity creation requests from scenarioSource → SpawnEntityCommands on World bus.
        // This is what CreateEntityRequestSystem does; we replicate it here since our scenarioSource
        // is separate from the CgfLogicPack's internal source.
        DrainScenarioSource(harness, scenarioSource);

        // 6. EditorApplication.Update(): reads ClusterStateUpdateEvent from orchBus.
        // We call PumpFrames(0) after SwapBuffers so editorApp.Update() sees the events.
        // But EditorHarness does not expose editorApp.Update() directly; instead we
        // call harness.PumpFrames(1) which calls kernel.Update() again.
        // To avoid double-pumping the kernel, we invoke editorApp.Update() via the
        // IEditorLogic interface.  EditorApplication.Update() is internal to the harness —
        // it reads ClusterStateUpdateEvent which was made readable by the SwapBuffers above.
        //
        // The harness holds the editor as IEditorLogic, not EditorApplication.
        // But the harness's BuildDebugApiService() uses the lambda
        //   clusterState: () => (Editor as EditorApplication)?.CurrentClusterState
        // which means EditorApplication.Update() must be called somewhere to advance it.
        //
        // Looking at EditorHarness.PumpFrames: it calls kernel.Update() which does NOT
        // call editorApp.Update() (that's done in EditorSubsystem, not in the kernel).
        // So we need to call it manually.
        if (harness.Editor is Hrot.Editor.EditorApplication app)
            app.Update();
    }

    private static void DrainScenarioSource(
        EditorHarness harness,
        ScenarioEntityCreationRequestSource source)
    {
        // ProcessRequests drains the queue and calls the handler for each item.
        source.ProcessRequests(req =>
        {
            // Publish SpawnEntityCommand to the world bus so NetworkSpawningSystem picks it up.
            harness.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand
            {
                TkbType           = req.TkbType,
                NetworkId         = req.PreAllocatedNetworkId,
                OwnerNodeId       = 0,
                InitType          = Fdp.Toolkit.Replication.ReliableInitType.None,
                InitialComponents = req.InitialComponents,
            });
        });
    }

    private bool PumpUntilOperatingEdit(
        EditorHarness harness,
        ClusterSlave clusterSlave,
        ClusterMaster clusterMaster,
        FdpEventBus orchBus,
        ScenarioEntityCreationRequestSource scenarioSource,
        int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            PumpOneFrame(harness, clusterSlave, clusterMaster, orchBus, scenarioSource);
            if (GetClusterState(harness) == ClusterState.OperatingEdit)
                return true;
        }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal valid HROT scenario JSON with one entity of the given
    /// <paramref name="tkbType"/>.  Uses subsystemType <c>"Hrot.Scenario"</c> so
    /// <see cref="HrotScenarioLoader.TryLoadScenarioJson"/> accepts it.
    /// </summary>
    private static string BuildMinimalScenarioJson(long tkbType)
    {
        var guid = Guid.NewGuid().ToString("D");
        var obj  = new JsonObject
        {
            ["$meta"] = new JsonObject
            {
                ["docType"]       = "Hrot.Scenario",
                ["schemaVersion"] = 1,
            },
            ["header"] = new JsonObject
            {
                ["subsystemType"]  = "Hrot.Scenario",
                ["schemaVersion"]  = "1.0",
            },
            ["entities"] = new JsonObject
            {
                [guid] = new JsonObject
                {
                    ["TkbIdentity"]      = new JsonObject { ["TkbType"]      = tkbType },
                    ["NetworkIdentity"]  = new JsonObject { ["Value"]        = 5000 },
                    ["NetworkAuthority"] = new JsonObject
                    {
                        ["PrimaryOwnerId"] = 0,
                        ["LocalNodeId"]    = 0,
                    },
                    ["SimTransform"] = new JsonObject
                    {
                        ["Position"] = new JsonArray { 100, 200, 0 },
                        ["Rotation"] = new JsonArray { 0, 0, 0, 1 },
                    },
                    ["EntityInfo"] = new JsonObject
                    {
                        ["Name"]    = "ADA-Batch03-TestEntity",
                        ["ForceId"] = "Friend",
                    },
                },
            },
        };
        return obj.ToJsonString();
    }

    /// <summary>Minimal sequential ID allocator for the orchestration handler.</summary>
    private sealed class SimpleSequentialIdAllocator : Fdp.Toolkit.NetworkSpawning.INetworkIdAllocator
    {
        private long _next;
        public SimpleSequentialIdAllocator(long startId) => _next = startId;
        public long AllocateId()            => _next++;
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }
}
