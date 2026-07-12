using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.Core.Network;

namespace Hrot.CGF.Orchestration.Handlers
{
    /// <summary>
    /// CGF-authoritative scenario load handler (TASK-C006 / BATCH-04).
    ///
    /// <para>
    /// On <c>PrepareLive</c>, loads the scenario JSON and extracts
    /// <see cref="EntityCreationRequest"/> objects via <see cref="StagingEntityExtractor"/>.
    /// Also starts the ECS recording when an exercise ID is present, mirroring the
    /// SimHost <c>HrotScenarioLoadHandler</c> pattern so <c>node_400.fdp</c> is written.
    /// On <c>Commit</c>, enqueues the requests into the
    /// <see cref="ScenarioEntityCreationRequestSource"/> so they are processed on the next
    /// ECS tick.  On <c>Abort</c>, clears the pending request list.
    /// </para>
    /// <para>
    /// Implements <see cref="ITickableClusterStateHandler"/> to intercept the
    /// <c>PrepareState(OperatingLive)</c> transition and hold the cluster in
    /// <c>LoadingLive</c> until the genesis pipeline is fully resolved: all requests
    /// dequeued, all <c>Constructing</c> entities promoted, and all transient Intent
    /// DTO managed components removed by <c>GenesisMaterializationSystem</c>.
    /// </para>
    /// </summary>
    public sealed class CgfScenarioLoadHandler : ITickableClusterStateHandler
    {
        private readonly ScenarioSerializer _serializer;
        private readonly IScenarioLoader _scenarioLoader;
        private readonly StagingEntityExtractor _extractor;
        private readonly ScenarioEntityCreationRequestSource _source;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly EntityRepository? _world;
        private readonly ScenarioBehaviorRemapper? _remapper;
        private readonly IRecordReplayController? _controller;
        private readonly string _storageDirectory;

        private IReadOnlyList<EntityCreationRequest>? _pendingRequests;
        private Guid? _pendingTransactionId;
        private TaskCompletionSource<object?>? _operatingLiveTcs;
        private Guid _pendingExerciseId;

        public CgfScenarioLoadHandler(
            ScenarioSerializer serializer,
            IScenarioLoader scenarioLoader,
            StagingEntityExtractor extractor,
            ScenarioEntityCreationRequestSource source,
            INetworkIdAllocator idAllocator,
            ScenarioBehaviorRemapper? remapper = null)
            : this(serializer, scenarioLoader, extractor, source, idAllocator, world: null, remapper: remapper)
        {
        }

        public CgfScenarioLoadHandler(
            ScenarioSerializer serializer,
            IScenarioLoader scenarioLoader,
            StagingEntityExtractor extractor,
            ScenarioEntityCreationRequestSource source,
            INetworkIdAllocator idAllocator,
            EntityRepository? world,
            ScenarioBehaviorRemapper? remapper = null,
            IRecordReplayController? controller = null,
            string? storageDirectory = null)
        {
            _serializer        = serializer     ?? throw new ArgumentNullException(nameof(serializer));
            _scenarioLoader    = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
            _extractor         = extractor      ?? throw new ArgumentNullException(nameof(extractor));
            _source            = source         ?? throw new ArgumentNullException(nameof(source));
            _idAllocator       = idAllocator    ?? throw new ArgumentNullException(nameof(idAllocator));
            _world             = world;
            _remapper          = remapper;
            _controller        = controller;
            _storageDirectory  = storageDirectory ?? OrchestrationConstants.ResolveStagingRoot();
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.PrepareLive ||
            operation == NodeOpType.PrepareState;

        /// <inheritdoc />
        public bool CanHandle(ExecuteNodeOpIntent intent)
        {
            if (intent.Operation == NodeOpType.PrepareLive) return true;
            return intent.Operation == NodeOpType.PrepareState &&
                   intent.DomainPayload is EditLoadHandlerPayload p &&
                   p.TargetState == ClusterState.OperatingLive;
        }

        /// <inheritdoc />
        public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            // Intercept PrepareState targeting OperatingLive: hold the cluster in LoadingLive
            // until DrainDeferredAcks confirms that genesis is fully resolved.
            if (intent.Operation == NodeOpType.PrepareState)
            {
                if (intent.DomainPayload is EditLoadHandlerPayload elp &&
                    elp.TargetState == ClusterState.OperatingLive)
                {
                    _operatingLiveTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await _operatingLiveTcs.Task.ConfigureAwait(false);

                    if (_controller != null && _pendingExerciseId != Guid.Empty)
                        await _controller.PrepareRecordingAsync(_pendingExerciseId, _storageDirectory)
                            .ConfigureAwait(false);
                    return null;
                }
                return null;
            }

            _pendingRequests      = null;
            _pendingTransactionId = null;
            _pendingExerciseId    = ResolveExerciseId(intent.DomainPayload);

            var scenarioId = intent.DomainPayload is EditLoadHandlerPayload payload
                ? payload.ScenarioId
                : intent.DomainPayload as string;

            if (string.IsNullOrWhiteSpace(scenarioId))
                return null;

            string? json = null;
            int retries = 0;
            while (json == null && retries < 100)
            {
                json = _scenarioLoader.TryLoadScenarioJson(scenarioId);
                if (json == null)
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                    retries++;
                }
            }

            if (json == null)
            {
                Fdp.Core.Logging.FdpLog<CgfScenarioLoadHandler>.Error(
                    "[CgfScenarioLoadHandler] Scenario file '{0}' not found after waiting for prefetch.", scenarioId);
                return null;
            }

            _pendingRequests      = _extractor.Extract(_serializer, json, _idAllocator, episodeId: null, behaviorRemapper: _remapper);
            _pendingTransactionId = intent.TransactionId;

            return null;
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
            _operatingLiveTcs?.TrySetCanceled();
            _operatingLiveTcs = null;
            _pendingExerciseId = Guid.Empty;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Called from the main ECS thread each frame.  Checks whether the genesis
        /// pipeline is complete and, if so, signals the deferred
        /// <c>PrepareState(OperatingLive)</c> task so the cluster may commit the
        /// state transition.
        /// </remarks>
        public void DrainDeferredAcks()
        {
            if (_operatingLiveTcs == null) return;

            // Condition 1: all extraction requests have been consumed by CreateEntityRequestSystem.
            if (!_source.IsEmpty) return;

            if (_world != null)
            {
                // Condition 2: ELM handshakes are complete — no entities awaiting peer ACKs.
                foreach (var _ in _world.Query().WithLifecycle(EntityLifecycle.Constructing).Build())
                    return;

                // Condition 3: GenesisMaterializationSystem has resolved all cross-entity
                // references and removed the transient Intent DTO managed components.
                foreach (var _ in _world.Query().WithManaged<InitialPassengersIntent>().Build()) return;
                foreach (var _ in _world.Query().WithManaged<InitialVehicleIntent>().Build()) return;
                foreach (var _ in _world.Query().WithManaged<InitialHierarchyIntent>().Build()) return;
                foreach (var _ in _world.Query().WithManaged<InitialTargetsIntent>().Build()) return;
                foreach (var _ in _world.Query().WithManaged<InitialRouteIntent>().Build()) return;
                foreach (var _ in _world.Query().WithManaged<InitialUnitSubordinateIntent>().Build()) return;
            }

            _operatingLiveTcs.TrySetResult(null);
            _operatingLiveTcs = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Guid ResolveExerciseId(object? domainPayload) =>
            domainPayload switch
            {
                Guid g => g,
                EditLoadHandlerPayload p => p.ExerciseId,
                _ => Guid.Empty,
            };
    }
}
