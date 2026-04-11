namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="ReferenceEpisodeLoadHandler"/> episode operations.
    /// Used for both <c>StartEpisode</c> and <c>StopEpisode</c> intents.
    /// </summary>
    public record struct EpisodeHandlerPayload(System.Guid EpisodeId, string? ScenarioId, bool IsStart);

    /// <summary>
    /// Reference implementation of the episode-load Cluster handler.
    /// Handles <c>StartEpisode</c> and <c>StopEpisode</c> operations.
    /// </summary>
    public sealed class ReferenceEpisodeLoadHandler : IClusterStateHandler
    {
        private readonly FDP.Toolkit.Scenario.ScenarioSerializer _serializer;
        private readonly IScenarioLoader _scenarioLoader;
        private readonly Fdp.Kernel.EntityRepository? _world;

        private string? _pendingJson;
        private System.Guid _pendingEpisodeId;
        private System.Guid? _pendingTransactionId;
        private System.Collections.Generic.List<Fdp.Kernel.Entity>? _pendingStopEntities;
        private bool _pendingIsParticipating;

        public ReferenceEpisodeLoadHandler(
            FDP.Toolkit.Scenario.ScenarioSerializer serializer,
            IScenarioLoader scenarioLoader,
            Fdp.Kernel.EntityRepository? world = null)
        {
            _serializer = serializer ?? throw new System.ArgumentNullException(nameof(serializer));
            _scenarioLoader = scenarioLoader ?? throw new System.ArgumentNullException(nameof(scenarioLoader));
            _world = world;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.StartEpisode ||
            operation == NodeOpType.StopEpisode;

        /// <summary>
        /// <c>true</c> after a <c>StartEpisode</c> <see cref="PrepareAsync"/> in which this node participates.
        /// Exposed for integration-test assertions.
        /// </summary>
        public bool IsParticipatingForTest => _pendingIsParticipating;

        /// <inheritdoc />
        public System.Threading.Tasks.Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, System.Threading.CancellationToken ct)
        {
            _pendingJson = null;
            _pendingEpisodeId = System.Guid.Empty;
            _pendingTransactionId = null;
            _pendingStopEntities = null;
            _pendingIsParticipating = false;

            if (intent.Operation == NodeOpType.StartEpisode)
                return PrepareStartEpisode(intent);

            if (intent.Operation == NodeOpType.StopEpisode)
                return PrepareStopEpisode(intent);

            return System.Threading.Tasks.Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, Fdp.Kernel.EntityRepository? repo)
        {
            if (_pendingTransactionId != intent.TransactionId) return;

            if (intent.Operation == NodeOpType.StartEpisode)
                CommitStartEpisode(repo);
            else if (intent.Operation == NodeOpType.StopEpisode)
                CommitStopEpisode(repo);
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, Fdp.Kernel.EntityRepository? repo)
        {
            _pendingJson = null;
            _pendingEpisodeId = System.Guid.Empty;
            _pendingTransactionId = null;
            _pendingStopEntities = null;
            _pendingIsParticipating = false;
        }

        private System.Threading.Tasks.Task<object?> PrepareStartEpisode(ExecuteNodeOpIntent intent)
        {
            var payload = intent.DomainPayload is EpisodeHandlerPayload p ? p : default;
            var episodeId = payload.EpisodeId;
            var scenarioId = payload.ScenarioId;

            if (episodeId == System.Guid.Empty || string.IsNullOrWhiteSpace(scenarioId))
            {
                _pendingTransactionId = intent.TransactionId;
                _pendingIsParticipating = false;
                return System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            _pendingJson = _scenarioLoader.TryLoadScenarioJson(scenarioId);
            if (_pendingJson == null)
            {
                _pendingTransactionId = intent.TransactionId;
                _pendingIsParticipating = false;
                return System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            _pendingEpisodeId = episodeId;
            _pendingTransactionId = intent.TransactionId;
            _pendingIsParticipating = true;
            return System.Threading.Tasks.Task.FromResult<object?>(null);
        }

        private void CommitStartEpisode(Fdp.Kernel.EntityRepository? repo)
        {
            if (_pendingJson == null)
            {
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingJson = null;
                _pendingTransactionId = null;
                _pendingIsParticipating = false;
                throw new System.InvalidOperationException(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: EntityRepository is null - cannot deserialize episode entities.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingJson, asEpisode: true, episodeId: _pendingEpisodeId);
            }
            finally
            {
                _pendingJson = null;
                _pendingTransactionId = null;
            }
        }

        private System.Threading.Tasks.Task<object?> PrepareStopEpisode(ExecuteNodeOpIntent intent)
        {
            var payload = intent.DomainPayload is EpisodeHandlerPayload p ? p : default;
            var episodeId = payload.EpisodeId;

            if (episodeId == System.Guid.Empty)
            {
                _pendingTransactionId = intent.TransactionId;
                _pendingIsParticipating = false;
                return System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            _pendingEpisodeId = episodeId;
            _pendingTransactionId = intent.TransactionId;
            _pendingIsParticipating = true;

            if (_world != null)
                _pendingStopEntities = CollectEpisodeEntities(_world, episodeId);

            return System.Threading.Tasks.Task.FromResult<object?>(null);
        }

        private void CommitStopEpisode(Fdp.Kernel.EntityRepository? repo)
        {
            if (!_pendingIsParticipating)
            {
                _pendingTransactionId = null;
                _pendingStopEntities = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingTransactionId = null;
                _pendingStopEntities = null;
                throw new System.InvalidOperationException(
                    "[ReferenceEpisodeLoadHandler] CommitStopEpisode: EntityRepository is null - cannot destroy episode entities.");
            }

            var toDestroy = _pendingStopEntities ?? CollectEpisodeEntities(targetRepo, _pendingEpisodeId);
            foreach (var entity in toDestroy)
            {
                if (targetRepo.IsAlive(entity))
                    targetRepo.DestroyEntity(entity);
            }

            _pendingStopEntities = null;
            _pendingTransactionId = null;
            _pendingIsParticipating = false;
        }

        private static System.Collections.Generic.List<Fdp.Kernel.Entity> CollectEpisodeEntities(
            Fdp.Kernel.EntityRepository repo,
            System.Guid episodeId)
        {
            var result = new System.Collections.Generic.List<Fdp.Kernel.Entity>();
            if (!repo.IsComponentTypeRegistered<Fdp.Kernel.EpisodeTag>()) return result;

            var query = repo.Query().With<Fdp.Kernel.EpisodeTag>().Build();
            foreach (var e in query)
            {
                ref readonly var tag = ref repo.GetComponentRO<Fdp.Kernel.EpisodeTag>(e);
                if (tag.EpisodeId == episodeId)
                    result.Add(e);
            }
            return result;
        }
    }
}
