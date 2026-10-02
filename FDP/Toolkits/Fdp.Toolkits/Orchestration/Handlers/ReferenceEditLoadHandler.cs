namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="ReferenceEditLoadHandler"/> commands.
    /// <c>TargetState</c> must equal <c>ClusterState.LoadingEdit</c> for the
    /// handler to perform any I/O; other target states are no-ops.
    ///
    /// <para>⭐⭐⭐ <c>L1</c> — <b><c>TkbName</c> and <c>TerrainName</c> are the SHARED CONTENT NAMES</b>,
    /// extracted by the orchestrator from the MASTER scenario on the NAS and carried to every node on the
    /// message that opens the <c>Loading*</c> phase.
    /// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.2, <c>L1</c>.</para>
    ///
    /// <para>⭐⭐ <b>Why they ride the MESSAGE and not the scenario file.</b> 📐 Measured: four of the five
    /// roles never open the scenario file at all — only <c>Brain</c> reads it, because only <c>Brain</c>
    /// also edits and saves it. A muscle, navigation or map node therefore cannot read these names out of
    /// the scenario, yet every ECS node needs the knowledge base (it composes the full genesis pipeline —
    /// <c>Q65-A′</c>) and the movement roles need the terrain.</para>
    ///
    /// <para>⛔ <b>They previously travelled as a staged sidecar file</b>
    /// (<c>{node staging}/TKB/ScenarioHeader.json</c>), written during the file copy — a channel that
    /// RACES the step consuming it. 📐 Measured in one load: the content step was dispatched 6 ms after the
    /// copy STARTED and 49 ms before the files were fanned out. The scenario loader survived only on a
    /// two-second retry; the knowledge-base and terrain loaders do not retry and silently concluded
    /// <i>"this scenario names none"</i> — indistinguishable from the truth.</para>
    ///
    /// <para>⚠ Both are <see langword="null"/>-tolerant: a scenario naming neither is legal and silent
    /// (an absent <b>artifact</b> for a name that IS given is the loud case, and belongs to the loader).
    /// ⭐ The pair is additive on the wire — the payload crosses as JSON, so an older reader ignores them.</para>
    /// </summary>
    public record struct EditLoadHandlerPayload(
        string? ScenarioId,
        bool IsNewScenario = false,
        ClusterState TargetState = ClusterState.LoadingEdit,
        System.Guid ExerciseId = default,
        string? TkbName = null,
        string? TerrainName = null);

    /// <summary>
    /// Reference implementation of the edit-load Cluster handler.
    /// Handles <c>PrepareState</c> intents targeting <c>ClusterState.LoadingEdit</c>.
    /// </summary>
    public sealed class ReferenceEditLoadHandler : IClusterStateHandler
    {
        private readonly Fdp.Toolkit.Scenario.ScenarioSerializer _serializer;
        private readonly IScenarioLoader _scenarioLoader;
        private readonly Fdp.Core.EntityRepository? _world;

        private string? _pendingJson;
        private System.Guid? _pendingTransactionId;
        private bool _pendingIsNew;

        public ReferenceEditLoadHandler(
            Fdp.Toolkit.Scenario.ScenarioSerializer serializer,
            IScenarioLoader scenarioLoader,
            Fdp.Core.EntityRepository? world = null)
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

            if (payload.TargetState != ClusterState.LoadingEdit)
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
        public void Commit(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo)
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
        public void Abort(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo)
        {
            _pendingJson = null;
            _pendingTransactionId = null;
            _pendingIsNew = false;
        }
    }
}
