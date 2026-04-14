using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Scenario;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="ReferenceEpisodeLoadHandler"/> — CGF1-S0308 / BATCH-21 Part A.2
    /// success conditions: every Prepare→Commit path emits an ACK or throws; no silent
    /// no-op (<c>_pendingTransactionId</c> mismatch → swallowed Commit).
    /// </summary>
    public sealed class EpisodeLoadClusterOpHandlerTests : IDisposable
    {
        private readonly string           _tempRoot;
        private readonly EntityRepository _repo;
        private readonly ScenarioSerializer _serializer;

        public EpisodeLoadClusterOpHandlerTests()
        {
            _tempRoot   = Path.Combine(Path.GetTempPath(), "episode_handler_" + Guid.NewGuid().ToString("N"));
            _repo       = new EntityRepository();
            _repo.RegisterComponent<EpisodeTagForTest>();
            _repo.RegisterComponent<EpisodeTag>();
            _serializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }

        // ── A.2 fix 1: invalid EpisodeId in StartEpisode ─────────────────────────

        /// <summary>
        /// A <c>StartEpisode</c> (operationId=20) command with no EpisodeId in the
        /// payload must call <see cref="IClusterOpHandler.Commit"/> (because
        /// <c>_pendingTransactionId</c> is now set) and publish a
        /// non-participating ACK rather than silently swallowing the Commit.
        /// </summary>
        [Fact]
        public async Task StartEpisode_MissingEpisodeId_SetsTransactionId_SoCommitFires()
        {
            var handler = new ReferenceEpisodeLoadHandler(
                _serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType),
                _repo);
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode,
                DomainPayload = new EpisodeHandlerPayload(Guid.Empty, "s1", IsStart: true),  // EpisodeId is Guid.Empty → not valid
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);

            // IsParticipatingForTest must be false (invalid payload → not participating).
            Assert.False(handler.IsParticipatingForTest,
                "Handler must mark itself non-participating when EpisodeId is missing.");

            // Commit must not throw — the non-participating ACK path requires no repo.
            var ex = await Record.ExceptionAsync(() => { handler.Commit(cmd, null); return Task.CompletedTask; });
            Assert.Null(ex);
        }

        // ── A.2 fix 2: invalid ScenarioId in StartEpisode ──────────────────────

        [Fact]
        public async Task StartEpisode_MissingScenarioId_SetsTransactionId_SoCommitFires()
        {
            var handler = new ReferenceEpisodeLoadHandler(
                _serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType),
                _repo);
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode,
                DomainPayload = new EpisodeHandlerPayload(Guid.NewGuid(), ScenarioId: null, IsStart: true),  // no ScenarioId
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);

            Assert.False(handler.IsParticipatingForTest);
            var ex = await Record.ExceptionAsync(() => { handler.Commit(cmd, null); return Task.CompletedTask; });
            Assert.Null(ex);
        }

        // ── A.2 fix 3: null repo when CommitStartEpisode runs for a participant ─

        [Fact]
        public async Task StartEpisode_NullRepo_WhenParticipating_Throws()
        {
            // Create a scenario directory + matching JSON file so PrepareAsync participates.
            var scenarioId = "s_nullrepo_" + Guid.NewGuid().ToString("N");
            var dir = Path.Combine(_tempRoot, scenarioId);
            Directory.CreateDirectory(dir);

            // Minimal scenario JSON that passes subsystem-match check.
            var buildRepo = new EntityRepository();
            buildRepo.RegisterComponent<EpisodeTag>();
            var dom = _serializer.Serialize(buildRepo, new ScenarioHeader("Hrot.SimHost"));
            File.WriteAllText(Path.Combine(dir, "Hrot.SimHost.json"), dom.ToJsonString());
            buildRepo.Dispose();

            // Handler constructed without _world so repo must come from Commit param.
            var handlerNoWorld = new ReferenceEpisodeLoadHandler(
                _serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType));
            var episodeId = Guid.NewGuid();
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode,
                DomainPayload = new EpisodeHandlerPayload(episodeId, scenarioId, IsStart: true),
            };

            await handlerNoWorld.PrepareAsync(cmd, CancellationToken.None);
            Assert.True(handlerNoWorld.IsParticipatingForTest,
                "Handler must be participating when a matching scenario file exists.");

            // Commit with repo=null must throw (fail loud, not silent no-op).
            await Assert.ThrowsAsync<InvalidOperationException>(() => { handlerNoWorld.Commit(cmd, repo: null); return Task.CompletedTask; });
        }

        // ── A.2 fix 4: invalid EpisodeId in StopEpisode ──────────────────────────

        [Fact]
        public async Task StopEpisode_MissingEpisodeId_SetsTransactionId_SoCommitFires()
        {
            var handler = new ReferenceEpisodeLoadHandler(
                _serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType),
                _repo);
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.StopEpisode,
                DomainPayload = new EpisodeHandlerPayload(Guid.Empty, ScenarioId: null, IsStart: false),  // empty EpisodeId
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);

            Assert.False(handler.IsParticipatingForTest);
            // Commit must not throw and must handle the non-participating path.
            var ex = await Record.ExceptionAsync(() => { handler.Commit(cmd, null); return Task.CompletedTask; });
            Assert.Null(ex);
        }

        // ── A.2 fix 5: null repo when CommitStopEpisode runs for a participant ──

        [Fact]
        public async Task StopEpisode_NullRepo_WhenParticipating_Throws()
        {
            // Handler constructed without _world so repo must come from Commit param.
            var handlerNoWorld = new ReferenceEpisodeLoadHandler(
                _serializer,
                new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType));
            var episodeId = Guid.NewGuid();
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.StopEpisode,
                DomainPayload = new EpisodeHandlerPayload(episodeId, ScenarioId: null, IsStart: false),
            };

            await handlerNoWorld.PrepareAsync(cmd, CancellationToken.None);
            Assert.True(handlerNoWorld.IsParticipatingForTest,
                "Handler must be participating for a valid StopEpisode payload.");

            // Commit with repo=null must throw (fail loud).
            await Assert.ThrowsAsync<InvalidOperationException>(() => { handlerNoWorld.Commit(cmd, repo: null); return Task.CompletedTask; });
        }
    }

    [ComponentId(220)]
    internal struct EpisodeTagForTest
    {
        public float X;
    }
}
