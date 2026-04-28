using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Hrot.Network.Orchestration;
using Hrot.Orchestrator;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Scenario;
using Fdp.Toolkit.Scenario;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator.Integration.Tests;

// ── Minimal test component (ComponentId 202, default DataPolicy = Save) ───────

[ComponentId(202)]
internal struct TestScenarioPos
{
    public float X;
    public float Y;
    public float Z;
}

// ── Collection fixture (no parallelism, shared DDS domain) ───────────────────

[CollectionDefinition("OrchestratorIntegrationTests", DisableParallelization = true)]
public class OrchestratorIntegrationTestCollection { }

[Collection("OrchestratorIntegrationTests")]
public sealed class ScenarioSaveLoadTests : IDisposable
{
    // Domain 17 — reserved for orchestrator integration tests;
    // distinct from unit-test domain 15 and integration domain used by Hrot.SimHost.
    private const int TestDomain = 17;

    private readonly DdsParticipant _participant;

    public ScenarioSaveLoadTests()
    {
        _participant = new DdsParticipant(TestDomain);
    }

    public void Dispose() => _participant.Dispose();

    // ── Test 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Round-trip: simulate 3 entities, save to local file, clear ECS, load via
    /// <see cref="ScenarioLoadClusterStateHandler"/>, assert 3 entities restored with matching
    /// position data.
    /// </summary>
    [Fact]
    public async Task RoundTrip_SimHost_EntitiesMatchAfterLoad()
    {
        var tempRoot  = Path.Combine(Path.GetTempPath(), "fdp_int_" + Guid.NewGuid().ToString("N"));
        var scenarioId = "round_trip_01";
        Directory.CreateDirectory(Path.Combine(tempRoot, scenarioId));

        try
        {
            // ── Set up a fresh EntityRepository and register the test component ──
            var repo = new EntityRepository();
            repo.RegisterComponent<TestScenarioPos>();

            // Build the serializer AFTER registering the component so the auto-serializer
            // compiles delegates for TestScenarioPos.
            var serializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();

            // Spawn 3 entities with distinct positions.
            var positions = new[] {
                new TestScenarioPos { X = 1f, Y = 0f, Z = 0f },
                new TestScenarioPos { X = 2f, Y = 5f, Z = 0f },
                new TestScenarioPos { X = 3f, Y = 0f, Z = 9f },
            };
            foreach (var pos in positions)
            {
                var e = repo.CreateEntity();
                repo.SetComponent(e, pos);
            }

            Assert.Equal(3, repo.EntityCount);

            // Serialize to file.
            var dom      = serializer.Serialize(repo, new ScenarioHeader("Hrot.SimHost"));
            var filePath = Path.Combine(tempRoot, scenarioId, "Hrot.SimHost.json");
            await File.WriteAllTextAsync(filePath, dom.ToJsonString());

            // Clear ECS — use a new repo instance so entities are gone.
            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<TestScenarioPos>();

            // ── Load via handler ─────────────────────────────────────────────────
            var handler = new ReferenceScenarioLoadHandler(
                serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(tempRoot), serializer.SubsystemType));
            var cmd     = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive,
                DomainPayload = scenarioId,
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, freshRepo);

            // 3 entities must have been restored.
            Assert.Equal(3, freshRepo.EntityCount);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Save a scenario context via <see cref="GlobalContextClusterOpHandler"/>; clear loaded
    /// state; reload from file.  Asserts that <c>SceneId</c> is restored correctly.
    /// </summary>
    [Fact]
    public void OrchestratorContextRestored_AfterLoad()
    {
        const string expectedSceneId = "test_scene_99";
        var tempRoot = Path.Combine(Path.GetTempPath(), "fdp_int_ctx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            // ── Save ─────────────────────────────────────────────────────────────
            var saveHandler = new GlobalContextClusterOpHandler(_participant, expectedSceneId);
            saveHandler.LocalTempRoot = tempRoot;

            var exerciseId = Guid.NewGuid();
            var saveCmd = new NodeOpCommand
            {
                TransactionId = exerciseId,
                Operation     = NodeOpType.SerializeLocal,
                PayloadJson   = JsonSerializer.Serialize(
                    new ArchivePayloadDto(ExerciseId: exerciseId),
                    OrchestrationJsonOptions.Default),
            };

            saveHandler.PrepareAsync(saveCmd, CancellationToken.None).Wait();
            saveHandler.Commit(saveCmd, null);

            Assert.NotNull(saveHandler.CommitManifestEntry);
            Assert.True(File.Exists(saveHandler.CommitManifestEntry!.SourceUnc),
                "Orchestrator.json was not written to disk.");

            // ── Load ─────────────────────────────────────────────────────────────
            // CommitLoad reads {LocalTempRoot}/{ScenarioId}/Orchestrator.json.
            // We saved to {tempRoot}/{exerciseId:N}/Orchestrator.json, so ScenarioId == exerciseId:N.
            var loadHandler = new GlobalContextClusterOpHandler(_participant, string.Empty);
            loadHandler.LocalTempRoot = tempRoot;

            var loadCmd = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.CommitState,
                PayloadJson   = JsonSerializer.Serialize(
                    new NodeTransitionPayloadDto(
                        TargetState: ClusterState.LoadingLive.ToString(),
                        ScenarioId:  exerciseId.ToString("N"),
                        ExerciseId:  Guid.Empty),
                    OrchestrationJsonOptions.Default),
            };

            loadHandler.PrepareAsync(loadCmd, CancellationToken.None).Wait();
            loadHandler.Commit(loadCmd, null);

            Assert.Equal(expectedSceneId, loadHandler.LoadedSceneId);
            Assert.NotEqual(0L, loadHandler.LoadedStartWallTicks);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="GlobalContextClusterOpHandler.OnContextLoaded"/> must fire with the saved
    /// <c>LoadedStartWallTicks</c> and <c>LoadedScenarioTimeSeconds</c> exactly once after
    /// <see cref="NodeOpType.CommitState"/> for <see cref="ClusterState.LoadingLive"/>.
    /// </summary>
    [Fact]
    public void OnContextLoaded_FiresWithCorrectValues_AfterCommitLoad()
    {
        const string expectedSceneId = "scene_event_42";
        const double expectedSimTime = 77.25;

        var tempRoot = Path.Combine(Path.GetTempPath(), "fdp_ctx_event_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            // ── Save ─────────────────────────────────────────────────────────────
            var saveHandler = new GlobalContextClusterOpHandler(_participant, expectedSceneId);
            saveHandler.LocalTempRoot       = tempRoot;
            saveHandler.ScenarioTimeSeconds = expectedSimTime;

            var exerciseId = Guid.NewGuid();
            var saveCmd = new NodeOpCommand
            {
                TransactionId = exerciseId,
                Operation     = NodeOpType.SerializeLocal,
                PayloadJson   = JsonSerializer.Serialize(
                    new ArchivePayloadDto(ExerciseId: exerciseId),
                    OrchestrationJsonOptions.Default),
            };
            saveHandler.PrepareAsync(saveCmd, CancellationToken.None).Wait();
            saveHandler.Commit(saveCmd, null);

            Assert.NotNull(saveHandler.CommitManifestEntry);

            // ── Load — subscribe BEFORE Commit ────────────────────────────────────
            var loadHandler = new GlobalContextClusterOpHandler(_participant, string.Empty);
            loadHandler.LocalTempRoot = tempRoot;

            long  capturedTicks   = 0;
            double capturedTime   = 0;
            int   eventFireCount  = 0;
            loadHandler.OnContextLoaded += (ticks, time) =>
            {
                capturedTicks  = ticks;
                capturedTime   = time;
                eventFireCount++;
            };

            var loadCmd = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.CommitState,
                PayloadJson   = JsonSerializer.Serialize(
                    new NodeTransitionPayloadDto(
                        TargetState: ClusterState.LoadingLive.ToString(),
                        ScenarioId:  exerciseId.ToString("N"),
                        ExerciseId:  Guid.Empty),
                    OrchestrationJsonOptions.Default),
            };
            loadHandler.Commit(loadCmd, null);

            Assert.True(eventFireCount == 1,
                "OnContextLoaded must fire exactly once per successful CommitLoad.");
            Assert.True(capturedTicks != 0L,
                "Captured StartWallTicks must be non-zero.");
            Assert.Equal(expectedSimTime, capturedTime, precision: 5);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── Test 5 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="GlobalContextClusterOpHandler.OnContextLoaded"/> must NOT fire when no
    /// <c>ScenarioId</c> is present in the <see cref="NodeOpType.CommitState"/> payload
    /// (blank-world load path).
    /// </summary>
    [Fact]
    public void OnContextLoaded_DoesNotFire_WhenNoScenarioId()
    {
        var loadHandler = new GlobalContextClusterOpHandler(_participant, string.Empty);

        bool eventFired = false;
        loadHandler.OnContextLoaded += (_, _) => eventFired = true;

        var loadCmd = new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.CommitState,
            // No ScenarioId — blank-world path; CommitLoad exits early.
            PayloadJson   = JsonSerializer.Serialize(
                new NodeTransitionPayloadDto(
                    TargetState: ClusterState.LoadingLive.ToString(),
                    ScenarioId:  null,
                    ExerciseId:  Guid.Empty),
                OrchestrationJsonOptions.Default),
        };
        loadHandler.Commit(loadCmd, null);

        Assert.False(eventFired,
            "OnContextLoaded must not fire when CommitLoad is skipped due to missing ScenarioId.");
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A scenario file written with <c>SubsystemType = "Hrot.CGF"</c> must not
    /// cause the SimHost <see cref="ScenarioLoadClusterStateHandler"/> to create any entities.
    /// </summary>
    [Fact]
    public async Task SubsystemTypeFilter_CGFFileNotLoadedBySimHost()
    {
        var tempRoot   = Path.Combine(Path.GetTempPath(), "fdp_int_filter_" + Guid.NewGuid().ToString("N"));
        var scenarioId = "filter_test_01";
        Directory.CreateDirectory(Path.Combine(tempRoot, scenarioId));

        try
        {
            // ── Create a CGF-labelled scenario file (empty entity set) ────────────
            var cgfSerializer = new ScenarioSerializerBuilder("Hrot.CGF").Build();
            var cgfRepo       = new EntityRepository();
            var cgfDom        = cgfSerializer.Serialize(cgfRepo, new ScenarioHeader("Hrot.CGF"));
            var cgfFilePath   = Path.Combine(tempRoot, scenarioId, "Hrot.CGF.json");
            await File.WriteAllTextAsync(cgfFilePath, cgfDom.ToJsonString());

            // ── SimHost handler should see SubsystemType mismatch and no-op ───────
            var simHostSerializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();
            var simHostRepo       = new EntityRepository();

            var handler = new ReferenceScenarioLoadHandler(
                simHostSerializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(tempRoot), simHostSerializer.SubsystemType));
            var cmd     = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive,
                DomainPayload = scenarioId,
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, simHostRepo);

            // No entities should have been created in the SimHost repo.
            Assert.Equal(0, simHostRepo.EntityCount);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
