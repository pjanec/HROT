using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Scenario;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="ReferenceEditLoadHandler"/> commands.
    /// <c>TargetState</c> must equal <c>ClusterState.LoadingEdit (10)</c> for the
    /// handler to perform any I/O; other target states are no-ops.
    /// </summary>
    public record struct EditLoadHandlerPayload(string? ScenarioId, bool IsNewScenario = false, int TargetState = 10);

    /// <summary>
    /// Reference implementation of the edit-load Cluster handler (CGF1-G0404).
    /// Handles <c>PrepareState</c> intents targeting <c>ClusterState.LoadingEdit (state 10)</c>.
    /// </summary>
    public sealed class ReferenceEditLoadHandler : IClusterStateHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareState</c> (stable constant).</summary>
        public const int PrepareStateOperationId = 1;

        /// <summary>Integer value of <c>ClusterState.LoadingEdit</c>.</summary>
        private const int LoadingEditState = 10;

        private readonly ScenarioSerializer       _serializer;
        private readonly IScenarioStorageProvider _storageProvider;
        private readonly EntityRepository?        _world;

        private string? _pendingJson;
        private Guid?   _pendingTransactionId;
        private bool    _pendingIsNew;

        public ReferenceEditLoadHandler(
            ScenarioSerializer        serializer,
            IScenarioStorageProvider  storageProvider,
            EntityRepository?         world = null)
        {
            _serializer      = serializer      ?? throw new ArgumentNullException(nameof(serializer));
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            _world           = world;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareState;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            _pendingJson          = null;
            _pendingTransactionId = null;
            _pendingIsNew         = false;

            // If no typed payload, skip (DDS bridge — Phase 5 will provide translation).
            if (intent.DomainPayload is not EditLoadHandlerPayload payload)
                return Task.FromResult<object?>(null);

            // Only act on PrepareState targeting LoadingEdit.
            if (payload.TargetState != LoadingEditState)
                return Task.FromResult<object?>(null);

            var isNew      = payload.IsNewScenario;
            var scenarioId = payload.ScenarioId;

            _pendingTransactionId = intent.TransactionId;
            _pendingIsNew         = isNew;

            if (isNew || string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<ReferenceEditLoadHandler>.Info(
                    "[ReferenceEditLoadHandler] PrepareAsync: new scenario, blank world.");
                return Task.FromResult<object?>(null);
            }

            // Existing scenario: locate and cache the pre-fetched file.
            foreach (var fileName in _storageProvider.EnumerateScenarioFiles(scenarioId))
            {
                try
                {
                    using var stream = _storageProvider.OpenScenarioFile(scenarioId, Path.GetFileName(fileName));
                    if (stream == null) continue;

                    using var reader = new StreamReader(stream);
                    var text       = reader.ReadToEnd();
                    var subsysType = _serializer.PeekSubsystemType(text);
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    _pendingJson = text;
                    FdpLog<ReferenceEditLoadHandler>.Info(
                        "[ReferenceEditLoadHandler] PrepareAsync: queued '{0}' for edit-load.", fileName);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<ReferenceEditLoadHandler>.Warn(
                        "[ReferenceEditLoadHandler] PrepareAsync: failed to peek '{0}': {1}",
                        fileName, ex.Message);
                }
            }

            if (_pendingJson == null)
                throw new InvalidOperationException(
                    $"[ReferenceEditLoadHandler] no matching scenario file found for ScenarioId='{scenarioId}'." +
                    " Ensure PrefetchFiles completed before LoadingEdit.");

            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (_pendingTransactionId != intent.TransactionId) return;

            if (_pendingIsNew || _pendingJson == null)
            {
                FdpLog<ReferenceEditLoadHandler>.Info(
                    "[ReferenceEditLoadHandler] Commit: blank-world scenario committed.");
                _pendingJson          = null;
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingJson          = null;
                _pendingTransactionId = null;
                throw new InvalidOperationException(
                    "[ReferenceEditLoadHandler] Commit: EntityRepository is null but scenario " +
                    "deserialization is required.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingJson);
                FdpLog<ReferenceEditLoadHandler>.Info(
                    "[ReferenceEditLoadHandler] Commit: entities deserialized successfully.");
            }
            catch (Exception ex)
            {
                FdpLog<ReferenceEditLoadHandler>.Error(
                    "[ReferenceEditLoadHandler] Commit: deserialization failed: {0}", ex.Message);
                throw;
            }
            finally
            {
                _pendingJson          = null;
                _pendingTransactionId = null;
            }
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _pendingJson          = null;
            _pendingTransactionId = null;
            _pendingIsNew         = false;
        }
    }
}
