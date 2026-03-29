using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace Bagira.SimHost.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler that processes <see cref="NodeOpType.PrefetchFiles"/> commands on
    /// simulation nodes (CGF1-S0302 / A.2).
    ///
    /// <para>
    /// When the orchestrator's <see cref="Bagira.Orchestrator.DrillMaster"/> pushes
    /// scenario files to each node's local staging directory via the SMB gateway, it
    /// also fans out a <see cref="NodeOpType.PrefetchFiles"/> command.  This handler
    /// ensures the local staging directory exists and acknowledges completion with a
    /// <see cref="NodeOpStatus"/> ACK back to the orchestrator.
    /// </para>
    ///
    /// <para>
    /// <b>Prepare path:</b> Parses <c>ScenarioId</c> from the command payload;
    /// creates <c>localTempRoot\scenarioId\</c> if it does not already exist.
    /// </para>
    ///
    /// <para>
    /// <b>Commit path:</b> Publishes <see cref="NodeOpStatus"/>(<see cref="OpStatus.Success"/>)
    /// to the orchestrator via the injected <see cref="DdsWriter{T}"/>.
    /// </para>
    /// </summary>
    public sealed class PrefetchFilesDsmHandler : IDsmHandler
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";

        private readonly DdsWriter<NodeOpStatus>? _statusWriter;
        private readonly int    _nodeId;
        private readonly string _localTempRoot;

        private string? _pendingScenarioId;
        private Guid?   _pendingTransactionId;

        /// <param name="statusWriter">
        /// DDS writer used to publish <see cref="NodeOpStatus"/> ACKs back to the
        /// orchestrator.  Pass <c>null</c> in unit tests that verify staging behaviour
        /// without a live DDS stack.
        /// </param>
        /// <param name="nodeId">Local node identifier embedded in ACK messages.</param>
        /// <param name="localTempRoot">
        /// Root staging directory on this node.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public PrefetchFilesDsmHandler(
            DdsWriter<NodeOpStatus>? statusWriter,
            int    nodeId,
            string localTempRoot = DefaultLocalTempRoot)
        {
            _statusWriter  = statusWriter;
            _nodeId        = nodeId;
            _localTempRoot = string.IsNullOrWhiteSpace(localTempRoot) ? DefaultLocalTempRoot : localTempRoot;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType op) => op == NodeOpType.PrefetchFiles;

        /// <inheritdoc />
        /// <remarks>
        /// Parses <c>ScenarioId</c> from the command payload and ensures the local
        /// staging directory (<c>localTempRoot\scenarioId\</c>) exists, creating it
        /// when absent.
        /// </remarks>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            _pendingScenarioId    = null;
            _pendingTransactionId = null;

            var scenarioId = ParseScenarioId(cmd.PayloadJson);
            if (!string.IsNullOrWhiteSpace(scenarioId))
            {
                var stagingDir = Path.Combine(_localTempRoot, scenarioId);
                Directory.CreateDirectory(stagingDir);
                FdpLog<PrefetchFilesDsmHandler>.Info(
                    "[SimHost] PrefetchFiles: staging directory ready at '{0}'.", stagingDir);
                _pendingScenarioId    = scenarioId;
                _pendingTransactionId = cmd.TransactionId;
            }

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Publishes a <see cref="NodeOpStatus"/>(<see cref="OpStatus.Success"/>) ACK
        /// to the orchestrator for the transaction that was prepared.
        /// </remarks>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            try
            {
                _statusWriter?.Write(new NodeOpStatus
                {
                    TransactionId    = cmd.TransactionId,
                    NodeId           = _nodeId,
                    Status           = OpStatus.Success,
                    IsParticipating  = true,
                    ErrorCode        = 0,
                    ResultJson       = string.Empty,
                });
                FdpLog<PrefetchFilesDsmHandler>.Info(
                    "[SimHost] PrefetchFiles ACK sent for scenario '{0}'.", _pendingScenarioId);
            }
            finally
            {
                _pendingScenarioId    = null;
                _pendingTransactionId = null;
            }
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
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
                if (doc.RootElement.TryGetProperty("ScenarioId", out var prop))
                    return prop.GetString();
            }
            catch { /* malformed payload — treated as no ScenarioId */ }
            return null;
        }
    }
}
