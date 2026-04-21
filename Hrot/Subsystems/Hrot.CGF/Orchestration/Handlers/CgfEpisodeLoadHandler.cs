using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;

namespace Hrot.CGF.Orchestration.Handlers
{
    /// <summary>
    /// CGF-authoritative episode load handler (TASK-C007 / BATCH-04).
    ///
    /// <para>
    /// On <c>StartEpisode</c>, extracts episode entities via <see cref="StagingEntityExtractor"/>
    /// and enqueues them into <see cref="ScenarioEntityCreationRequestSource"/> at Commit time.
    /// On <c>StopEpisode</c>, collects the network IDs of live/constructing entities tagged with
    /// the episode and publishes a <see cref="DestroyEntityCommand"/> per entity at Commit time.
    /// Uses the event bus so proper ELM lifecycle teardown is triggered rather than
    /// calling <c>DestroyEntity</c> directly.
    /// </para>
    /// </summary>
    public sealed class CgfEpisodeLoadHandler : IClusterStateHandler
    {
        private readonly ScenarioSerializer _serializer;
        private readonly IScenarioLoader _scenarioLoader;
        private readonly StagingEntityExtractor _extractor;
        private readonly ScenarioEntityCreationRequestSource _source;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly EntityRepository _world;
        private readonly ScenarioBehaviorRemapper? _remapper;

        private IReadOnlyList<EntityCreationRequest>? _pendingRequests;
        private List<long>? _pendingDestroyNetworkIds;
        private Guid? _pendingTransactionId;
        private bool _pendingIsParticipating;

        public CgfEpisodeLoadHandler(
            ScenarioSerializer serializer,
            IScenarioLoader scenarioLoader,
            StagingEntityExtractor extractor,
            ScenarioEntityCreationRequestSource source,
            INetworkIdAllocator idAllocator,
            EntityRepository world,
            ScenarioBehaviorRemapper? remapper = null)
        {
            _serializer    = serializer    ?? throw new ArgumentNullException(nameof(serializer));
            _scenarioLoader = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
            _extractor     = extractor     ?? throw new ArgumentNullException(nameof(extractor));
            _source        = source        ?? throw new ArgumentNullException(nameof(source));
            _idAllocator   = idAllocator   ?? throw new ArgumentNullException(nameof(idAllocator));
            _world         = world         ?? throw new ArgumentNullException(nameof(world));
            _remapper      = remapper;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.StartEpisode ||
            operation == NodeOpType.StopEpisode;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            _pendingRequests          = null;
            _pendingDestroyNetworkIds = null;
            _pendingTransactionId     = null;
            _pendingIsParticipating   = false;

            if (intent.Operation == NodeOpType.StartEpisode)
                return PrepareStartEpisode(intent);

            if (intent.Operation == NodeOpType.StopEpisode)
                return PrepareStopEpisode(intent);

            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (_pendingTransactionId != intent.TransactionId) return;

            if (intent.Operation == NodeOpType.StartEpisode)
                CommitStartEpisode();
            else if (intent.Operation == NodeOpType.StopEpisode)
                CommitStopEpisode();
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _pendingRequests          = null;
            _pendingDestroyNetworkIds = null;
            _pendingTransactionId     = null;
            _pendingIsParticipating   = false;
        }

        // ── StartEpisode ──────────────────────────────────────────────────────────

        private Task<object?> PrepareStartEpisode(ExecuteNodeOpIntent intent)
        {
            var payload    = intent.DomainPayload is EpisodeHandlerPayload p ? p : default;
            var episodeId  = payload.EpisodeId;
            var scenarioId = payload.ScenarioId;

            if (episodeId == Guid.Empty || string.IsNullOrWhiteSpace(scenarioId))
            {
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<object?>(null);
            }

            var json = _scenarioLoader.TryLoadScenarioJson(scenarioId);
            if (json == null)
            {
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<object?>(null);
            }

            _pendingRequests        = _extractor.Extract(_serializer, json, _idAllocator, episodeId: episodeId, behaviorRemapper: _remapper);
            _pendingTransactionId   = intent.TransactionId;
            _pendingIsParticipating = true;
            return Task.FromResult<object?>(null);
        }

        private void CommitStartEpisode()
        {
            if (_pendingRequests == null)
            {
                _pendingTransactionId   = null;
                _pendingIsParticipating = false;
                return;
            }

            try
            {
                foreach (var request in _pendingRequests)
                    _source.Enqueue(request);
            }
            finally
            {
                _pendingRequests      = null;
                _pendingTransactionId = null;
            }
        }

        // ── StopEpisode ───────────────────────────────────────────────────────────

        private Task<object?> PrepareStopEpisode(ExecuteNodeOpIntent intent)
        {
            var payload   = intent.DomainPayload is EpisodeHandlerPayload p ? p : default;
            var episodeId = payload.EpisodeId;

            if (episodeId == Guid.Empty)
            {
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<object?>(null);
            }

            _pendingDestroyNetworkIds = CollectEpisodeNetworkIds(_world, episodeId);
            _pendingTransactionId     = intent.TransactionId;
            _pendingIsParticipating   = true;
            return Task.FromResult<object?>(null);
        }

        private void CommitStopEpisode()
        {
            if (!_pendingIsParticipating)
            {
                _pendingTransactionId     = null;
                _pendingDestroyNetworkIds = null;
                return;
            }

            try
            {
                var networkIds = _pendingDestroyNetworkIds ?? new List<long>();
                foreach (var networkId in networkIds)
                {
                    _world.Bus.PublishManaged(new DestroyEntityCommand
                    {
                        NetworkId = networkId,
                        Reason    = "CgfEpisodeStop",
                    });
                }
            }
            finally
            {
                _pendingDestroyNetworkIds = null;
                _pendingTransactionId     = null;
                _pendingIsParticipating   = false;
            }
        }

        /// <summary>
        /// Collects network IDs of all entities tagged with <paramref name="episodeId"/>.
        /// Uses <c>WithLifecycle(EntityLifecycle.All)</c> to also catch entities in the
        /// <c>Constructing</c> state.
        /// </summary>
        private static List<long> CollectEpisodeNetworkIds(EntityRepository repo, Guid episodeId)
        {
            var result = new List<long>();
            if (!repo.IsComponentTypeRegistered<EpisodeTag>()) return result;

            var query = repo.Query().With<EpisodeTag>().With<NetworkIdentity>().WithLifecycle(EntityLifecycle.All).Build();
            foreach (var entity in query)
            {
                ref readonly var tag = ref repo.GetComponentRO<EpisodeTag>(entity);
                if (tag.EpisodeId != episodeId) continue;

                ref readonly var netId = ref repo.GetComponentRO<NetworkIdentity>(entity);
                result.Add(netId.Value);
            }
            return result;
        }
    }
}
