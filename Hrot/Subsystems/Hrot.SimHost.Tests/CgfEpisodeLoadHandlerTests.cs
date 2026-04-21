using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.CGF.Orchestration.Handlers;
using Hrot.Core.Network;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CgfEpisodeLoadHandler"/> — TASK-C007 / BATCH-04.
    /// </summary>
    public sealed class CgfEpisodeLoadHandlerTests : IDisposable
    {
        private const string SubsystemType = "Test.Scenario";

        private readonly EntityRepository _goldRepo;
        private readonly EntityRepository _world;
        private readonly ScenarioSerializer _serializer;

        public CgfEpisodeLoadHandlerTests()
        {
            _goldRepo  = new EntityRepository();
            _goldRepo.RegisterComponent<SimTransform>();
            _goldRepo.RegisterComponent<NetworkIdentity>();
            _goldRepo.RegisterComponent<TkbIdentity>();
            _goldRepo.RegisterComponent<EpisodeTag>();

            _world    = new EntityRepository();
            _world.RegisterComponent<NetworkIdentity>();
            _world.RegisterComponent<EpisodeTag>();
            _world.RegisterComponent<SimTransform>();

            _serializer = new ScenarioSerializerBuilder(SubsystemType).Build();
        }

        public void Dispose()
        {
            _goldRepo.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private CgfEpisodeLoadHandler MakeHandler(
            string? json,
            ScenarioEntityCreationRequestSource source,
            out StubIdAllocator allocator)
        {
            allocator = new StubIdAllocator(200);
            return new CgfEpisodeLoadHandler(
                new ScenarioSerializerBuilder(SubsystemType).Build(),
                new LambdaScenarioLoader(_ => json),
                new StagingEntityExtractor(),
                source,
                allocator,
                _world);
        }

        private static ExecuteNodeOpIntent MakeStartIntent(Guid txId, Guid episodeId, string? scenarioId = "scn1")
            => new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = 0,
                Operation     = NodeOpType.StartEpisode,
                DomainPayload = new EpisodeHandlerPayload(episodeId, scenarioId, IsStart: true),
            };

        private static ExecuteNodeOpIntent MakeStopIntent(Guid txId, Guid episodeId)
            => new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = 0,
                Operation     = NodeOpType.StopEpisode,
                DomainPayload = new EpisodeHandlerPayload(episodeId, null, IsStart: false),
            };

        private string SerializeGold()
            => _serializer.Serialize(_goldRepo, new ScenarioHeader(SubsystemType)).ToJsonString();

        // ── Test 1: StartEpisode enqueues requests with EpisodeTag ────────────

        /// <summary>
        /// When PrepareAsync+Commit for StartEpisode is called with valid JSON containing
        /// 3 root entities, 3 EntityCreationRequests are enqueued.
        /// </summary>
        [Fact]
        public async Task StartEpisode_ValidJson_EnqueuesRequestsWithEpisodeTag()
        {
            _goldRepo.CreateEntity();
            _goldRepo.CreateEntity();
            _goldRepo.CreateEntity();
            var json   = SerializeGold();
            var source = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(json, source, out _);
            var txId    = Guid.NewGuid();
            var episodeId = Guid.NewGuid();
            var intent = MakeStartIntent(txId, episodeId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Commit(intent, null);

            var collected = new List<EntityCreationRequest>();
            source.ProcessRequests(r => collected.Add(r));
            Assert.Equal(3, collected.Count);

            // Every request must include an EpisodeTag with the correct episode ID.
            foreach (var req in collected)
            {
                var tag = req.InitialComponents?
                    .OfType<EpisodeTag>()
                    .FirstOrDefault();
                Assert.NotNull(tag);
                Assert.Equal(episodeId, tag!.Value.EpisodeId);
            }
        }

        // ── Test 2: StopEpisode publishes DestroyEntityCommand per entity ─────

        /// <summary>
        /// World contains 5 entities: 2 belong to the episode (have matching EpisodeTag +
        /// NetworkIdentity) and 3 do not.  PrepareAsync+Commit for StopEpisode must
        /// publish exactly 2 DestroyEntityCommand events on the world bus.
        /// </summary>
        [Fact]
        public async Task StopEpisode_PublishesDestroyCommandPerEpisodeEntity()
        {
            var episodeId = Guid.NewGuid();

            // Create 2 episode entities in the world with NetworkIdentity.
            var e1 = _world.CreateEntity();
            _world.SetComponent(e1, new EpisodeTag { EpisodeId = episodeId });
            _world.SetComponent(e1, new NetworkIdentity(101L));

            var e2 = _world.CreateEntity();
            _world.SetComponent(e2, new EpisodeTag { EpisodeId = episodeId });
            _world.SetComponent(e2, new NetworkIdentity(102L));

            // Create 3 entities NOT belonging to the episode.
            for (int i = 0; i < 3; i++)
            {
                var e = _world.CreateEntity();
                _world.SetComponent(e, new EpisodeTag { EpisodeId = Guid.NewGuid() });
                _world.SetComponent(e, new NetworkIdentity(999L + i));
            }

            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(null, source, out _);
            var txId    = Guid.NewGuid();
            var intent  = MakeStopIntent(txId, episodeId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Commit(intent, null);

            // Swap buffers so PublishManaged events become readable.
            _world.Bus.SwapBuffers();
            var commands = _world.Bus.ReadManaged<DestroyEntityCommand>().ToList();

            Assert.Equal(2, commands.Count);
            var publishedIds = commands.Select(c => c.NetworkId).OrderBy(x => x).ToList();
            Assert.Equal(new List<long> { 101L, 102L }, publishedIds);
        }

        // ── Test 3: CanHandle ─────────────────────────────────────────────────

        /// <summary>
        /// <see cref="CgfEpisodeLoadHandler.CanHandle"/> returns <c>true</c> for
        /// <see cref="NodeOpType.StartEpisode"/> and <see cref="NodeOpType.StopEpisode"/>,
        /// and <c>false</c> for <see cref="NodeOpType.PrepareLive"/>.
        /// </summary>
        [Fact]
        public void CanHandle_ReturnsTrue_ForStartAndStopEpisode()
        {
            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(null, source, out _);

            Assert.True(handler.CanHandle(NodeOpType.StartEpisode));
            Assert.True(handler.CanHandle(NodeOpType.StopEpisode));
            Assert.False(handler.CanHandle(NodeOpType.PrepareLive));
            Assert.False(handler.CanHandle(NodeOpType.FinalizeLive));
        }

        // ── Test 4: Abort before Commit leaves queue empty ────────────────────

        /// <summary>
        /// After PrepareAsync succeeds for StartEpisode, calling Abort then
        /// Commit must leave the source queue empty.
        /// </summary>
        [Fact]
        public async Task StartEpisode_Abort_ThenCommit_LeavesQueueEmpty()
        {
            _goldRepo.CreateEntity();
            var json    = SerializeGold();
            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(json, source, out _);
            var txId    = Guid.NewGuid();
            var episodeId = Guid.NewGuid();
            var intent  = MakeStartIntent(txId, episodeId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Abort(intent, null);
            handler.Commit(intent, null);  // must be a no-op

            var collected = new List<EntityCreationRequest>();
            source.ProcessRequests(r => collected.Add(r));
            Assert.Empty(collected);
        }

        // ── Test 5: Missing episode JSON leaves queue empty ───────────────────

        /// <summary>
        /// When the scenario loader returns null (scenario JSON not found),
        /// StartEpisode Commit enqueues no requests.
        /// </summary>
        [Fact]
        public async Task StartEpisode_MissingJson_NoRequestsEnqueued()
        {
            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(null, source, out _);  // loader always returns null
            var txId    = Guid.NewGuid();
            var episodeId = Guid.NewGuid();
            var intent  = MakeStartIntent(txId, episodeId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Commit(intent, null);

            var collected = new List<EntityCreationRequest>();
            source.ProcessRequests(r => collected.Add(r));
            Assert.Empty(collected);
        }

        // ── Private stub ──────────────────────────────────────────────────────

        private sealed class LambdaScenarioLoader : IScenarioLoader
        {
            private readonly Func<string, string?> _fn;
            public LambdaScenarioLoader(Func<string, string?> fn) => _fn = fn;
            public string? TryLoadScenarioJson(string scenarioId) => _fn(scenarioId);
        }
    }
}
