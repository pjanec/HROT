using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;

namespace Hrot.CGF.Orchestration.Handlers
{
    /// <summary>
    /// CGF-authoritative scenario load handler (TASK-C006 / BATCH-04).
    ///
    /// <para>
    /// On <c>PrepareLive</c>, loads the scenario JSON and extracts
    /// <see cref="EntityCreationRequest"/> objects via <see cref="StagingEntityExtractor"/>.
    /// On <c>Commit</c>, enqueues the requests into the
    /// <see cref="ScenarioEntityCreationRequestSource"/> so they are processed on the next
    /// ECS tick.  On <c>Abort</c>, clears the pending request list.
    /// </para>
    /// </summary>
    public sealed class CgfScenarioLoadHandler : IClusterStateHandler
    {
        private readonly ScenarioSerializer _serializer;
        private readonly IScenarioLoader _scenarioLoader;
        private readonly StagingEntityExtractor _extractor;
        private readonly ScenarioEntityCreationRequestSource _source;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly ScenarioBehaviorRemapper? _remapper;

        private IReadOnlyList<EntityCreationRequest>? _pendingRequests;
        private Guid? _pendingTransactionId;

        public CgfScenarioLoadHandler(
            ScenarioSerializer serializer,
            IScenarioLoader scenarioLoader,
            StagingEntityExtractor extractor,
            ScenarioEntityCreationRequestSource source,
            INetworkIdAllocator idAllocator,
            ScenarioBehaviorRemapper? remapper = null)
        {
            _serializer   = serializer   ?? throw new ArgumentNullException(nameof(serializer));
            _scenarioLoader = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
            _extractor    = extractor    ?? throw new ArgumentNullException(nameof(extractor));
            _source       = source       ?? throw new ArgumentNullException(nameof(source));
            _idAllocator  = idAllocator  ?? throw new ArgumentNullException(nameof(idAllocator));
            _remapper     = remapper;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareLive;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            _pendingRequests      = null;
            _pendingTransactionId = null;

            var scenarioId = intent.DomainPayload is EditLoadHandlerPayload elp
                ? elp.ScenarioId
                : intent.DomainPayload as string;

            if (string.IsNullOrWhiteSpace(scenarioId))
                return Task.FromResult<object?>(null);

            var json = _scenarioLoader.TryLoadScenarioJson(scenarioId);
            if (json == null)
                return Task.FromResult<object?>(null);

            _pendingRequests      = _extractor.Extract(_serializer, json, _idAllocator, episodeId: null, behaviorRemapper: _remapper);
            _pendingTransactionId = intent.TransactionId;

            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (_pendingRequests == null || _pendingTransactionId != intent.TransactionId)
                return;

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

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _pendingRequests      = null;
            _pendingTransactionId = null;
        }
    }
}
