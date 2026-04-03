namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="ReferenceEditLoadHandler"/> commands.
    /// <c>TargetState</c> must equal <c>ClusterState.LoadingEdit (10)</c> for the
    /// handler to perform any I/O; other target states are no-ops.
    /// </summary>
    public record struct EditLoadHandlerPayload(string? ScenarioId, bool IsNewScenario = false, int TargetState = 10);

    /// <summary>
    /// Reference implementation of the edit-load Cluster handler.
    /// Handles <c>PrepareState</c> intents targeting <c>ClusterState.LoadingEdit (state 10)</c>.
    /// </summary>
    public sealed class ReferenceEditLoadHandler : IClusterStateHandler
    {
        /// <summary>Integer value of <c>ClusterState.LoadingEdit</c>.</summary>
        private const int LoadingEditState = 10;

        private readonly FDP.Toolkit.Scenario.ScenarioSerializer _serializer;
        private readonly IScenarioLoader _scenarioLoader;
        private readonly Fdp.Kernel.EntityRepository? _world;

        private string? _pendingJson;
        private System.Guid? _pendingTransactionId;
        private bool _pendingIsNew;

        public ReferenceEditLoadHandler(
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
            operation == NodeOpType.PrepareState ||
            operation == NodeOpType.PrepareEdit  ||
            operation == NodeOpType.FinalizeEdit;

        /// <inheritdoc />
        public System.Threading.Tasks.Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, System.Threading.CancellationToken ct)
        {
            _pendingJson = null;
            _pendingTransactionId = null;
            _pendingIsNew = false;

            if (intent.DomainPayload is not EditLoadHandlerPayload payload)
                return System.Threading.Tasks.Task.FromResult<object?>(null);

            if (payload.TargetState != LoadingEditState)
                return System.Threading.Tasks.Task.FromResult<object?>(null);

            var isNew = payload.IsNewScenario;
            var scenarioId = payload.ScenarioId;

            _pendingTransactionId = intent.TransactionId;
            _pendingIsNew = isNew;

            if (isNew || string.IsNullOrWhiteSpace(scenarioId))
                return System.Threading.Tasks.Task.FromResult<object?>(null);

            _pendingJson = _scenarioLoader.TryLoadScenarioJson(scenarioId);
            if (_pendingJson == null)
            {
                throw new System.InvalidOperationException(
                    $"[ReferenceEditLoadHandler] no scenario file found for ScenarioId='{scenarioId}'. " +
                    "Ensure PrefetchFiles completed before LoadingEdit.");
            }

            return System.Threading.Tasks.Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, Fdp.Kernel.EntityRepository? repo)
        {
            if (_pendingTransactionId != intent.TransactionId) return;

            if (_pendingIsNew || _pendingJson == null)
            {
                _pendingJson = null;
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingJson = null;
                _pendingTransactionId = null;
                throw new System.InvalidOperationException(
                    "[ReferenceEditLoadHandler] Commit: EntityRepository is null but scenario deserialization is required.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingJson);
            }
            finally
            {
                _pendingJson = null;
                _pendingTransactionId = null;
            }
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, Fdp.Kernel.EntityRepository? repo)
        {
            _pendingJson = null;
            _pendingTransactionId = null;
            _pendingIsNew = false;
        }
    }
}
