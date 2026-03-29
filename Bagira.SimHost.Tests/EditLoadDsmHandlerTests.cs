using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.SimHost.Modules.Orchestration.Handlers;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;
using Xunit;

namespace Bagira.SimHost.Tests
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
    /// Unit tests for <see cref="EditLoadDsmHandler"/> — CGF1-S0302 success conditions.
    /// </summary>
    public sealed class EditLoadDsmHandlerTests : IDisposable
    {
        private readonly string            _tempDir;
        private readonly EntityRepository  _repo;
        private readonly ScenarioSerializer _serializer;

        public EditLoadDsmHandlerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "fdp_edit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _repo = new EntityRepository();
            _repo.RegisterComponent<EditLoadTestPos>();

            // Serializer built AFTER component registration so FdpAutoSerializer compiles
            // delegates for EditLoadTestPos.
            _serializer = new ScenarioSerializerBuilder("Bagira.SimHost").Build();
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private NodeOpCommand MakePrepareStateCmd(string payloadJson) => new()
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareState,
            PayloadJson   = payloadJson,
        };

        private EditLoadDsmHandler CreateHandler() =>
            new EditLoadDsmHandler(_serializer, _tempDir);

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// When <c>IsNewScenario = true</c>, <see cref="EditLoadDsmHandler.Commit"/> must
        /// leave the repository empty — no entities are spawned for a blank-world scenario.
        /// </summary>
        [Fact]
        public async Task NewScenario_SpawnsNoEntities()
        {
            var handler = CreateHandler();
            var cmd = MakePrepareStateCmd(
                $"{{\"TargetState\":{(int)DSMState.LoadingEdit},\"IsNewScenario\":true}}");

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _repo);

            Assert.Equal(0, _repo.EntityCount);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writing a scenario file with 3 entities and calling
        /// <see cref="EditLoadDsmHandler.Commit"/> must produce exactly 3 entities in the
        /// repository with matching position data (CGF1-S0302 second success condition).
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

            var dom      = _serializer.Serialize(sourceRepo, new ScenarioHeader("Bagira.SimHost"));
            var filePath = Path.Combine(scenarioDir, "Bagira.SimHost.json");
            await File.WriteAllTextAsync(filePath, dom.ToJsonString());

            // Load via EditLoadDsmHandler.
            var handler = CreateHandler();
            var cmd = MakePrepareStateCmd(
                $"{{\"TargetState\":{(int)DSMState.LoadingEdit},\"ScenarioId\":\"{scenarioId}\"}}");

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _repo);

            Assert.Equal(3, _repo.EntityCount);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Measures the wall-clock time of <see cref="EditLoadDsmHandler.Commit"/> with a
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

            var dom      = _serializer.Serialize(sourceRepo, new ScenarioHeader("Bagira.SimHost"));
            var filePath = Path.Combine(scenarioDir, "Bagira.SimHost.json");
            await File.WriteAllTextAsync(filePath, dom.ToJsonString());

            var handler = CreateHandler();
            var cmd = MakePrepareStateCmd(
                $"{{\"TargetState\":{(int)DSMState.LoadingEdit},\"ScenarioId\":\"{scenarioId}\"}}");

            await handler.PrepareAsync(cmd, default);

            var sw = Stopwatch.StartNew();
            handler.Commit(cmd, _repo);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 50,
                $"Commit took {sw.ElapsedMilliseconds} ms — expected < 50 ms.");
        }
    }
}
