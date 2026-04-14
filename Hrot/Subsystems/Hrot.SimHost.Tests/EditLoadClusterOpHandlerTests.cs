using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Fdp.Kernel;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Scenario;
using Fdp.Toolkit.Scenario;
using Xunit;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.SimHost.Tests
{
    // ── Minimal test component (ComponentId 303, default DataPolicy = Save) ──────

    [ComponentId(204)]
    internal struct EditLoadTestPos
    {
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>
    /// Unit tests for <see cref="ReferenceEditLoadHandler"/> — CGF1-S0302 success conditions.
    /// </summary>
    public sealed class EditLoadClusterOpHandlerTests : IDisposable
    {
        private readonly string            _tempDir;
        private readonly EntityRepository  _repo;
        private readonly ScenarioSerializer _serializer;

        public EditLoadClusterOpHandlerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "fdp_edit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _repo = new EntityRepository();
            _repo.RegisterComponent<EditLoadTestPos>();

            // Serializer built AFTER component registration so FdpAutoSerializer compiles
            // delegates for EditLoadTestPos.
            _serializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private ExecuteNodeOpIntent MakePrepareStateCmd(string? scenarioId, bool isNew = false) =>
            new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareState,
                DomainPayload = new EditLoadHandlerPayload(scenarioId, IsNewScenario: isNew),
            };

        private ReferenceEditLoadHandler CreateHandler() =>
            new ReferenceEditLoadHandler(
                _serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(_tempDir), _serializer.SubsystemType));

        private System.Collections.Generic.HashSet<(float X, float Y, float Z)>
            CollectPositions(EntityRepository repo)
        {
            var positions = new System.Collections.Generic.HashSet<(float X, float Y, float Z)>();
            var query = repo.Query().With<EditLoadTestPos>().Build();
            foreach (var e in query)
            {
                var pos = repo.GetComponentRO<EditLoadTestPos>(e);
                positions.Add((pos.X, pos.Y, pos.Z));
            }
            return positions;
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// When <c>IsNewScenario = true</c>, <see cref="EditLoadClusterOpHandler.Commit"/> must
        /// leave the repository empty — no entities are spawned for a blank-world scenario.
        /// </summary>
        [Fact]
        public async Task NewScenario_SpawnsNoEntities()
        {
            var handler = CreateHandler();
            var cmd = MakePrepareStateCmd(null, isNew: true);

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _repo);

            Assert.Equal(0, _repo.EntityCount);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writing a scenario file with 3 entities and calling
        /// <see cref="EditLoadClusterOpHandler.Commit"/> must produce exactly 3 entities in the
        /// repository whose <see cref="EditLoadTestPos"/> component values match the
        /// serialized source data (CGF1-S0302 second success condition).
        /// </summary>
        [Fact]
        public async Task LoadExistingScenario_SpawnsCorrectEntityCount()
        {
            const string scenarioId = "edit_load_test_01";
            var scenarioDir = Path.Combine(_tempDir, scenarioId);
            Directory.CreateDirectory(scenarioDir);

            // Spawn 3 entities and serialise to file.
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<EditLoadTestPos>();

            var positions = new[]
            {
                new EditLoadTestPos { X = 1f, Y = 2f, Z = 3f },
                new EditLoadTestPos { X = 4f, Y = 5f, Z = 6f },
                new EditLoadTestPos { X = 7f, Y = 8f, Z = 9f },
            };
            foreach (var pos in positions)
            {
                var e = sourceRepo.CreateEntity();
                sourceRepo.SetComponent(e, pos);
            }

            var dom      = _serializer.Serialize(sourceRepo, new ScenarioHeader("Hrot.SimHost"));
            var filePath = Path.Combine(scenarioDir, "Hrot.SimHost.json");
            await File.WriteAllTextAsync(filePath, dom.ToJsonString());

            // Load via EditLoadClusterOpHandler.
            var handler = CreateHandler();
            var cmd = MakePrepareStateCmd(scenarioId);

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _repo);

            Assert.Equal(3, _repo.EntityCount);

            // Assert component values match serialized source data (§CGF1-S0302).
            var actualPositions = CollectPositions(_repo);
            Assert.Contains((1f, 2f, 3f), actualPositions);
            Assert.Contains((4f, 5f, 6f), actualPositions);
            Assert.Contains((7f, 8f, 9f), actualPositions);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Measures the wall-clock time of <see cref="EditLoadClusterOpHandler.Commit"/> with a
        /// 100-entity JSON file; asserts elapsed time is under 50 ms
        /// (CGF1-S0302 third success condition).
        /// </summary>
        [Fact]
        public async Task Commit_DoesNotBlockLongerThan50ms()
        {
            const string scenarioId = "edit_load_perf_01";
            var scenarioDir = Path.Combine(_tempDir, scenarioId);
            Directory.CreateDirectory(scenarioDir);

            // Spawn 100 entities and serialise to file.
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<EditLoadTestPos>();

            for (int i = 0; i < 100; i++)
            {
                var e = sourceRepo.CreateEntity();
                sourceRepo.SetComponent(e, new EditLoadTestPos { X = i, Y = 0f, Z = 0f });
            }

            var dom      = _serializer.Serialize(sourceRepo, new ScenarioHeader("Hrot.SimHost"));
            var filePath = Path.Combine(scenarioDir, "Hrot.SimHost.json");
            await File.WriteAllTextAsync(filePath, dom.ToJsonString());

            var handler = CreateHandler();
            var cmd = MakePrepareStateCmd(scenarioId);

            await handler.PrepareAsync(cmd, default);

            var sw = Stopwatch.StartNew();
            handler.Commit(cmd, _repo);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 50,
                $"Commit took {sw.ElapsedMilliseconds} ms — expected < 50 ms.");
        }
    }
}
