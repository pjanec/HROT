using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
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
    /// Unit tests for <see cref="CgfScenarioLoadHandler"/> — TASK-C006 / BATCH-04.
    /// </summary>
    public sealed class CgfScenarioLoadHandlerTests : IDisposable
    {
        private const string SubsystemType = "Test.Scenario";

        private readonly EntityRepository _goldRepo;
        private readonly ScenarioSerializer _serializer;

        public CgfScenarioLoadHandlerTests()
        {
            _goldRepo = new EntityRepository();
            _goldRepo.RegisterComponent<SimTransform>();
            _goldRepo.RegisterComponent<NetworkIdentity>();
            _goldRepo.RegisterComponent<TkbIdentity>();
            _goldRepo.RegisterComponent<EpisodeTag>();
            _serializer = new ScenarioSerializerBuilder(SubsystemType).Build();
        }

        public void Dispose() => _goldRepo.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static CgfScenarioLoadHandler MakeHandler(
            string? json,
            ScenarioEntityCreationRequestSource source,
            out StubIdAllocator allocator)
        {
            allocator = new StubIdAllocator(100);
            return new CgfScenarioLoadHandler(
                new ScenarioSerializerBuilder(SubsystemType).Build(),
                new LambdaScenarioLoader(_ => json),
                new StagingEntityExtractor(),
                source,
                allocator);
        }

        private static ExecuteNodeOpIntent MakeIntent(Guid txId, string scenarioId = "scn1")
            => new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = 0,
                Operation     = NodeOpType.PrepareLive,
                DomainPayload = scenarioId,
            };

        private string SerializeGold()
            => _serializer.Serialize(_goldRepo, new ScenarioHeader(SubsystemType)).ToJsonString();

        // ── Test 1: Happy path ─────────────────────────────────────────────────

        /// <summary>
        /// When the scenario loader returns valid JSON with 2 root entities,
        /// Commit enqueues exactly 2 EntityCreationRequests into the source.
        /// </summary>
        [Fact]
        public async Task Commit_ValidJson_TwoRootEntities_EnqueuesTwoRequests()
        {
            _goldRepo.CreateEntity();
            _goldRepo.CreateEntity();
            var json   = SerializeGold();
            var source = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(json, source, out _);
            var txId   = Guid.NewGuid();
            var intent = MakeIntent(txId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Commit(intent, null);

            var collected = new List<EntityCreationRequest>();
            source.ProcessRequests(r => collected.Add(r));
            Assert.Equal(2, collected.Count);
        }

        // ── Test 2: Scenario not found ────────────────────────────────────────

        /// <summary>
        /// When the scenario loader returns null (scenario not found),
        /// Commit does not enqueue any requests.
        /// </summary>
        [Fact]
        public async Task Commit_ScenarioNotFound_NoRequestsEnqueued()
        {
            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(null, source, out _);
            var txId    = Guid.NewGuid();
            var intent  = MakeIntent(txId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Commit(intent, null);

            var collected = new List<EntityCreationRequest>();
            source.ProcessRequests(r => collected.Add(r));
            Assert.Empty(collected);
        }

        // ── Test 3: Abort clears pending requests ─────────────────────────────

        /// <summary>
        /// After PrepareAsync succeeds, Abort clears the pending state so a
        /// subsequent Commit with the same transaction ID is a no-op.
        /// </summary>
        [Fact]
        public async Task Abort_ClearsPendingRequests_SubsequentCommitIsNoOp()
        {
            _goldRepo.CreateEntity();
            var json    = SerializeGold();
            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(json, source, out _);
            var txId    = Guid.NewGuid();
            var intent  = MakeIntent(txId);

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Abort(intent, null);
            handler.Commit(intent, null);  // must be a no-op

            var collected = new List<EntityCreationRequest>();
            source.ProcessRequests(r => collected.Add(r));
            Assert.Empty(collected);
        }

        // ── Test 4: CanHandle ─────────────────────────────────────────────────

        /// <summary>
        /// <see cref="CgfScenarioLoadHandler.CanHandle"/> returns <c>true</c> only for
        /// <see cref="NodeOpType.PrepareLive"/> and <c>false</c> for all other operation types.
        /// </summary>
        [Fact]
        public void CanHandle_ReturnsTrue_OnlyForPrepareLive()
        {
            var source  = new ScenarioEntityCreationRequestSource();
            var handler = MakeHandler(null, source, out _);

            Assert.True(handler.CanHandle(NodeOpType.PrepareLive));
            Assert.False(handler.CanHandle(NodeOpType.StartEpisode));
            Assert.False(handler.CanHandle(NodeOpType.StopEpisode));
            Assert.False(handler.CanHandle(NodeOpType.FinalizeLive));
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
