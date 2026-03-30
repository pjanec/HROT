using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="ReferenceStoryLoadHandler"/> — CGF1-S0308 / BATCH-21 Part A.2
    /// success conditions: every Prepare→Commit path emits an ACK or throws; no silent
    /// no-op (<c>_pendingTransactionId</c> mismatch → swallowed Commit).
    /// </summary>
    public sealed class StoryLoadDsmHandlerTests : IDisposable
    {
        private readonly string           _tempRoot;
        private readonly EntityRepository _repo;
        private readonly ScenarioSerializer _serializer;

        public StoryLoadDsmHandlerTests()
        {
            _tempRoot   = Path.Combine(Path.GetTempPath(), "story_handler_" + Guid.NewGuid().ToString("N"));
            _repo       = new EntityRepository();
            _repo.RegisterComponent<StoryTagForTest>();
            _repo.RegisterComponent<StoryTag>();
            _serializer = new ScenarioSerializerBuilder("Bagira.SimHost").Build();
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }

        // ── A.2 fix 1: invalid StoryId in StartStory ─────────────────────────

        /// <summary>
        /// A <c>StartStory</c> (operationId=20) command with no StoryId in the
        /// payload must call <see cref="IDsmHandler.Commit"/> (because
        /// <c>_pendingTransactionId</c> is now set) and publish a
        /// non-participating ACK rather than silently swallowing the Commit.
        /// </summary>
        [Fact]
        public async Task StartStory_MissingStoryId_SetsTransactionId_SoCommitFires()
        {
            var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot), _repo);
            var cmd = new OrchestrationCommand(
                Guid.NewGuid(), 0, ReferenceStoryLoadHandler.StartStoryOperationId,
                "{\"ScenarioId\":\"s1\"}");   // no StoryId

            await handler.PrepareAsync(cmd, CancellationToken.None);

            // IsParticipatingForTest must be false (invalid payload → not participating).
            Assert.False(handler.IsParticipatingForTest,
                "Handler must mark itself non-participating when StoryId is missing.");

            // Commit must not throw — the non-participating ACK path requires no repo.
            var ex = Record.Exception(() => handler.Commit(cmd, null));
            Assert.Null(ex);
        }

        // ── A.2 fix 2: invalid ScenarioId in StartStory ──────────────────────

        [Fact]
        public async Task StartStory_MissingScenarioId_SetsTransactionId_SoCommitFires()
        {
            var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot), _repo);
            var cmd = new OrchestrationCommand(
                Guid.NewGuid(), 0, ReferenceStoryLoadHandler.StartStoryOperationId,
                $"{{\"StoryId\":\"{Guid.NewGuid()}\"}}"  // no ScenarioId
            );

            await handler.PrepareAsync(cmd, CancellationToken.None);

            Assert.False(handler.IsParticipatingForTest);
            var ex = Record.Exception(() => handler.Commit(cmd, null));
            Assert.Null(ex);
        }

        // ── A.2 fix 3: null repo when CommitStartStory runs for a participant ─

        [Fact]
        public async Task StartStory_NullRepo_WhenParticipating_Throws()
        {
            // Create a scenario directory + matching JSON file so PrepareAsync participates.
            var scenarioId = "s_nullrepo_" + Guid.NewGuid().ToString("N");
            var dir = Path.Combine(_tempRoot, scenarioId);
            Directory.CreateDirectory(dir);

            // Minimal scenario JSON that passes subsystem-match check.
            var buildRepo = new EntityRepository();
            buildRepo.RegisterComponent<StoryTag>();
            var dom = _serializer.Serialize(buildRepo, new ScenarioHeader("Bagira.SimHost"));
            File.WriteAllText(Path.Combine(dir, "Bagira.SimHost.json"), dom.ToJsonString());
            buildRepo.Dispose();

            // Handler constructed without _world so repo must come from Commit param.
            var handlerNoWorld = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot));
            var storyId = Guid.NewGuid();
            var cmd = new OrchestrationCommand(
                Guid.NewGuid(), 0, ReferenceStoryLoadHandler.StartStoryOperationId,
                JsonSerializer.Serialize(new { StoryId = storyId, ScenarioId = scenarioId }));

            await handlerNoWorld.PrepareAsync(cmd, CancellationToken.None);
            Assert.True(handlerNoWorld.IsParticipatingForTest,
                "Handler must be participating when a matching scenario file exists.");

            // Commit with repo=null must throw (fail loud, not silent no-op).
            Assert.Throws<InvalidOperationException>(() => handlerNoWorld.Commit(cmd, repo: null));
        }

        // ── A.2 fix 4: invalid StoryId in StopStory ──────────────────────────

        [Fact]
        public async Task StopStory_MissingStoryId_SetsTransactionId_SoCommitFires()
        {
            var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot), _repo);
            var cmd = new OrchestrationCommand(
                Guid.NewGuid(), 0, ReferenceStoryLoadHandler.StopStoryOperationId,
                "{}");  // no StoryId

            await handler.PrepareAsync(cmd, CancellationToken.None);

            Assert.False(handler.IsParticipatingForTest);
            // Commit must not throw and must handle the non-participating path.
            var ex = Record.Exception(() => handler.Commit(cmd, null));
            Assert.Null(ex);
        }

        // ── A.2 fix 5: null repo when CommitStopStory runs for a participant ──

        [Fact]
        public async Task StopStory_NullRepo_WhenParticipating_Throws()
        {
            // Handler constructed without _world so repo must come from Commit param.
            var handlerNoWorld = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot));
            var storyId = Guid.NewGuid();
            var cmd = new OrchestrationCommand(
                Guid.NewGuid(), 0, ReferenceStoryLoadHandler.StopStoryOperationId,
                JsonSerializer.Serialize(new { StoryId = storyId }));

            await handlerNoWorld.PrepareAsync(cmd, CancellationToken.None);
            Assert.True(handlerNoWorld.IsParticipatingForTest,
                "Handler must be participating for a valid StopStory payload.");

            // Commit with repo=null must throw (fail loud).
            Assert.Throws<InvalidOperationException>(() => handlerNoWorld.Commit(cmd, repo: null));
        }
    }

    [ComponentId(220)]
    internal struct StoryTagForTest
    {
        public float X;
    }
}
