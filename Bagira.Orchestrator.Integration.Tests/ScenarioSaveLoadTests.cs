using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Orchestrator;
using Bagira.SimHost.Modules.Orchestration.Handlers;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;

namespace Bagira.Orchestrator.Integration.Tests;

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
    // distinct from unit-test domain 15 and integration domain used by Bagira.SimHost.
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
    /// <see cref="ScenarioLoadDsmHandler"/>, assert 3 entities restored with matching
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
            var serializer = new ScenarioSerializerBuilder("Bagira.SimHost").Build();

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
            var dom      = serializer.Serialize(repo, new ScenarioHeader("Bagira.SimHost"));
            var filePath = Path.Combine(tempRoot, scenarioId, "Bagira.SimHost.json");
            await File.WriteAllTextAsync(filePath, dom.ToJsonString());

            // Clear ECS — use a new repo instance so entities are gone.
            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<TestScenarioPos>();

            // ── Load via handler ─────────────────────────────────────────────────
            var handler = new ScenarioLoadDsmHandler(serializer, tempRoot);
            var cmd     = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.PrepareLive,
                PayloadJson   = $"{{\"ScenarioId\":\"{scenarioId}\"}}",
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
    /// Save a scenario context via <see cref="GlobalContextDsmHandler"/>; clear loaded
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
            var saveHandler = new GlobalContextDsmHandler(_participant, expectedSceneId);
            saveHandler.LocalTempRoot = tempRoot;

            var drillId = Guid.NewGuid();
            var saveCmd = new NodeOpCommand
            {
                TransactionId = drillId,
                Operation     = NodeOpType.SerializeLocal,
                PayloadJson   = $"{{\"DrillId\":\"{drillId:N}\"}}",
            };

            saveHandler.PrepareAsync(saveCmd, CancellationToken.None).Wait();
            saveHandler.Commit(saveCmd, null);

            Assert.NotNull(saveHandler.CommitManifestEntry);
            Assert.True(File.Exists(saveHandler.CommitManifestEntry!.SourceUnc),
                "Orchestrator.json was not written to disk.");

            // ── Load ─────────────────────────────────────────────────────────────
            // CommitLoad reads {LocalTempRoot}/{ScenarioId}/Orchestrator.json.
            // We saved to {tempRoot}/{drillId:N}/Orchestrator.json, so ScenarioId == drillId:N.
            var loadHandler = new GlobalContextDsmHandler(_participant, string.Empty);
            loadHandler.LocalTempRoot = tempRoot;

            var loadCmd = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.CommitState,
                PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}," +
                                $"\"ScenarioId\":\"{drillId:N}\"}}",
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

    // ── Test 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A scenario file written with <c>SubsystemType = "Bagira.CGF"</c> must not
    /// cause the SimHost <see cref="ScenarioLoadDsmHandler"/> to create any entities.
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
            var cgfSerializer = new ScenarioSerializerBuilder("Bagira.CGF").Build();
            var cgfRepo       = new EntityRepository();
            var cgfDom        = cgfSerializer.Serialize(cgfRepo, new ScenarioHeader("Bagira.CGF"));
            var cgfFilePath   = Path.Combine(tempRoot, scenarioId, "Bagira.CGF.json");
            await File.WriteAllTextAsync(cgfFilePath, cgfDom.ToJsonString());

            // ── SimHost handler should see SubsystemType mismatch and no-op ───────
            var simHostSerializer = new ScenarioSerializerBuilder("Bagira.SimHost").Build();
            var simHostRepo       = new EntityRepository();

            var handler = new ScenarioLoadDsmHandler(simHostSerializer, tempRoot);
            var cmd     = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.PrepareLive,
                PayloadJson   = $"{{\"ScenarioId\":\"{scenarioId}\"}}",
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
