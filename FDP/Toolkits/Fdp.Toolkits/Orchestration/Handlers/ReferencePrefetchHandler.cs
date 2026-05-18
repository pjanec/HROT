using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="ReferencePrefetchHandler"/> commands.
    /// </summary>
    public record struct PrefetchHandlerPayload(string? ScenarioId);

    /// <summary>
    /// Reference implementation of the prefetch-files Cluster handler.
    /// Handles the <c>PrefetchFiles</c> operation.
    /// </summary>
    public sealed class ReferencePrefetchHandler : IClusterStateHandler
    {
        private readonly IScenarioStorageProvider _storageProvider;

        private string? _pendingScenarioId;
        private Guid?   _pendingTransactionId;

        public ReferencePrefetchHandler(
            IScenarioStorageProvider  storageProvider)
        {
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrefetchFiles;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            _pendingScenarioId    = null;
            _pendingTransactionId = null;

            var scenarioId = intent.DomainPayload is PrefetchHandlerPayload p ? p.ScenarioId : null;
            if (!string.IsNullOrWhiteSpace(scenarioId))
            {
                var stagingDir = _storageProvider.EnsureStagingDirectory(scenarioId);
                FdpLog<ReferencePrefetchHandler>.Info(
                    "[ReferencePrefetchHandler] Staging directory ready at '{0}'.", stagingDir);
                _pendingScenarioId    = scenarioId;
                _pendingTransactionId = intent.TransactionId;
            }

            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (_pendingTransactionId != intent.TransactionId) return;

            FdpLog<ReferencePrefetchHandler>.Info(
                "[ReferencePrefetchHandler] ACK for scenario '{0}'.", _pendingScenarioId ?? "(null)");

            _pendingScenarioId    = null;
            _pendingTransactionId = null;
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _pendingScenarioId    = null;
            _pendingTransactionId = null;
        }
    }
}
