using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the prefetch-files Cluster handler.
    ///
    /// <para>Handles the <c>PrefetchFiles</c> operation (integer id 25).
    /// When the orchestrator's ClusterMaster copies scenario files to a node's staging
    /// directory via a storage gateway, it also fans out a <c>PrefetchFiles</c> command.
    /// This handler ensures the local staging directory exists and acknowledges completion
    /// via <see cref="IOrchestrationTransport"/>.</para>
    ///
    /// <para>
    /// <b>Prepare path:</b> parses <c>ScenarioId</c> from the command payload and calls
    /// <see cref="IScenarioStorageProvider.EnsureStagingDirectory"/>.
    /// </para>
    /// <para>
    /// <b>Commit path:</b> calls <see cref="IOrchestrationTransport.PublishStatus"/> with
    /// <see cref="OrchestrationStatusCode.Success"/>.
    /// </para>
    /// </summary>
    public sealed class ReferencePrefetchHandler : IClusterStateHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrefetchFiles</c> (stable constant).</summary>
        public const int PrefetchFilesOperationId = 25;

        private readonly IOrchestrationTransport? _transport;
        private readonly int                      _nodeId;
        private readonly IScenarioStorageProvider _storageProvider;

        private string? _pendingScenarioId;
        private Guid?   _pendingTransactionId;

        /// <param name="transport">
        /// Transport used to ACK the orchestrator.  Pass <c>null</c> in unit tests
        /// that do not require DDS.
        /// </param>
        /// <param name="nodeId">Node identifier embedded in ACK messages.</param>
        /// <param name="storageProvider">
        /// Storage provider used to ensure the scenario staging directory exists.
        /// Use <c>LocalDiskStorageProvider</c> in production.
        /// </param>
        public ReferencePrefetchHandler(
            IOrchestrationTransport?  transport,
            int                       nodeId,
            IScenarioStorageProvider  storageProvider)
        {
            _transport       = transport;
            _nodeId          = nodeId;
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        }

        /// <inheritdoc />
        public bool CanHandle(int operationId) => operationId == PrefetchFilesOperationId;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            _pendingScenarioId    = null;
            _pendingTransactionId = null;

            var scenarioId = ParseScenarioId(cmd.PayloadJson);
            if (!string.IsNullOrWhiteSpace(scenarioId))
            {
                var stagingDir = _storageProvider.EnsureStagingDirectory(scenarioId);
                FdpLog<ReferencePrefetchHandler>.Info(
                    "[ReferencePrefetchHandler] Staging directory ready at '{0}'.", stagingDir);
                _pendingScenarioId    = scenarioId;
                _pendingTransactionId = cmd.TransactionId;
            }

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            try
            {
                _transport?.PublishStatus(new OrchestrationStatus(
                    TransactionId:   cmd.TransactionId,
                    NodeId:          _nodeId,
                    StatusCode:      OrchestrationStatusCode.Success,
                    IsParticipating: true,
                    ResultJson:      string.Empty));

                FdpLog<ReferencePrefetchHandler>.Info(
                    "[ReferencePrefetchHandler] ACK sent for scenario '{0}'.", _pendingScenarioId ?? "(null)");
            }
            finally
            {
                _pendingScenarioId    = null;
                _pendingTransactionId = null;
            }
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _pendingScenarioId    = null;
            _pendingTransactionId = null;
        }

        private static string? ParseScenarioId(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                return doc.RootElement.TryGetProperty("ScenarioId", out var p)
                    ? p.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
