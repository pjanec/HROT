namespace FDP.Toolkit.Orchestration.Handlers
{
	/// <summary>
	/// Reference implementation of the scenario-load Cluster handler.
	/// </summary>
	public sealed class ReferenceScenarioLoadHandler : IClusterStateHandler
	{
		private readonly FDP.Toolkit.Scenario.ScenarioSerializer _serializer;
		private readonly IScenarioLoader _scenarioLoader;
		private readonly Fdp.Kernel.EntityRepository? _world;

		private string? _pendingJson;
		private System.Guid? _pendingTransactionId;
		private int _prepareCallCount;

		/// <summary>
		/// Number of times <see cref="PrepareAsync"/> has been invoked.
		/// For integration-test assertions only.
		/// </summary>
		public int PrepareCallCountForTest => _prepareCallCount;

		public ReferenceScenarioLoadHandler(
			FDP.Toolkit.Scenario.ScenarioSerializer serializer,
			IScenarioLoader scenarioLoader,
			Fdp.Kernel.EntityRepository? world = null)
		{
			_serializer = serializer ?? throw new System.ArgumentNullException(nameof(serializer));
			_scenarioLoader = scenarioLoader ?? throw new System.ArgumentNullException(nameof(scenarioLoader));
			_world = world;
		}

		/// <inheritdoc />
		public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareLive;

		/// <inheritdoc />
		public System.Threading.Tasks.Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, System.Threading.CancellationToken ct)
		{
			_prepareCallCount++;
			_pendingJson = null;
			_pendingTransactionId = null;

			var scenarioId = intent.DomainPayload as string;
			if (string.IsNullOrWhiteSpace(scenarioId))
				return System.Threading.Tasks.Task.FromResult<object?>(null);

			_pendingJson = _scenarioLoader.TryLoadScenarioJson(scenarioId);
			if (_pendingJson != null)
				_pendingTransactionId = intent.TransactionId;

			return System.Threading.Tasks.Task.FromResult<object?>(null);
		}

		/// <inheritdoc />
		public void Commit(ExecuteNodeOpIntent intent, Fdp.Kernel.EntityRepository? repo)
		{
			if (_pendingJson == null || _pendingTransactionId != intent.TransactionId) return;

			var targetRepo = repo ?? _world;
			if (targetRepo == null)
			{
				_pendingJson = null;
				_pendingTransactionId = null;
				return;
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
		}
	}
}
