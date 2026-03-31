using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Scenario;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the edit-load Cluster handler (CGF1-G0404).
    ///
    /// <para>
    /// Handles <c>PrepareState (operationId=1)</c> payloads that target
    /// <c>ClusterState.LoadingEdit (state 10)</c>.  All other <c>PrepareState</c>
    /// targets are passed through as no-ops.
    /// </para>
    ///
    /// <para>
    /// <b>Prepare path</b>: parses <c>IsNewScenario</c> / <c>ScenarioId</c> from
    /// the payload.  For new scenarios no file I/O is performed.  For existing
    /// scenarios, locates and caches the matching pre-fetched JSON DOM via the
    /// storage provider.  Throws <see cref="InvalidOperationException"/> when a
    /// scenario file is required but absent.
    /// </para>
    ///
    /// <para>
    /// <b>Commit path</b>: deserializes entities from the cached DOM into the
    /// repository.  For new scenarios the world is left blank.
    /// </para>
    /// </summary>
    public sealed class ReferenceEditLoadHandler : IClusterStateHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareState</c> (stable constant).</summary>
        public const int PrepareStateOperationId = 1;

        /// <summary>Integer value of <c>ClusterState.LoadingEdit</c>.</summary>
        private const int LoadingEditState = 10;

        private readonly ScenarioSerializer   _serializer;
        private readonly IScenarioStorageProvider _storageProvider;
        private readonly EntityRepository?    _world;

        private JsonObject? _pendingDom;
        private Guid?       _pendingTransactionId;
        private bool        _pendingIsNew;

        /// <param name="serializer">
        /// Pre-built <see cref="ScenarioSerializer"/> configured with the owning
        /// subsystem's component translators and subsystem type string.
        /// </param>
        /// <param name="storageProvider">
        /// Storage provider for locating pre-fetched scenario files.
        /// Use <c>LocalDiskStorageProvider</c> in production.
        /// </param>
        /// <param name="world">
        /// Optional entity repository used as the deserialization target when the
        /// dispatch loop passes <c>repo: null</c>.  Pass <c>null</c> in unit tests
        /// that supply the repository directly via <see cref="Commit"/>.
        /// </param>
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
        /// <remarks>
        /// Returns <see langword="true"/> for <c>PrepareState (1)</c>.  Only
        /// <c>LoadingEdit</c> commands produce entity I/O; other states are ignored
        /// inside <see cref="PrepareAsync"/>.
        /// </remarks>
        public bool CanHandle(int operationId) => operationId == PrepareStateOperationId;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            _pendingDom           = null;
            _pendingTransactionId = null;
            _pendingIsNew         = false;

            // Only act on PrepareState targeting LoadingEdit.
            if (ParseTargetState(cmd.PayloadJson) != LoadingEditState)
                return Task.FromResult<string?>(null);

            var isNew      = ParseIsNewScenario(cmd.PayloadJson);
            var scenarioId = ParseScenarioId(cmd.PayloadJson);

            _pendingTransactionId = cmd.TransactionId;
            _pendingIsNew         = isNew;

            if (isNew || string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<ReferenceEditLoadHandler>.Info(
                    "[ReferenceEditLoadHandler] PrepareAsync: new scenario, blank world.");
                return Task.FromResult<string?>(null);
            }

            // Existing scenario: locate and cache the pre-fetched file.
            foreach (var fileName in _storageProvider.EnumerateScenarioFiles(scenarioId))
            {
                try
                {
                    using var stream = _storageProvider.OpenScenarioFile(scenarioId, Path.GetFileName(fileName));
                    if (stream == null) continue;

                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    var dom  = JsonNode.Parse(text)?.AsObject();
                    if (dom == null) continue;

                    var subsysType = dom["Header"]?.AsObject()?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    _pendingDom = dom;
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

            if (_pendingDom == null)
                throw new InvalidOperationException(
                    $"[ReferenceEditLoadHandler] no matching scenario file found for ScenarioId='{scenarioId}'." +
                    " Ensure PrefetchFiles completed before LoadingEdit.");

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            if (_pendingIsNew || _pendingDom == null)
            {
                FdpLog<ReferenceEditLoadHandler>.Info(
                    "[ReferenceEditLoadHandler] Commit: blank-world scenario committed.");
                _pendingDom           = null;
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingDom           = null;
                _pendingTransactionId = null;
                throw new InvalidOperationException(
                    "[ReferenceEditLoadHandler] Commit: EntityRepository is null but scenario " +
                    "deserialization is required.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom);
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
                _pendingDom           = null;
                _pendingTransactionId = null;
            }
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _pendingDom           = null;
            _pendingTransactionId = null;
            _pendingIsNew         = false;
        }

        private static int ParseTargetState(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return 0;
            if (int.TryParse(payloadJson, out var n)) return n;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("TargetState", out var prop))
                    return prop.GetInt32();
            }
            catch { /* malformed payload */ }
            return 0;
        }

        private static bool ParseIsNewScenario(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("IsNewScenario", out var prop))
                    return prop.GetBoolean();
            }
            catch { /* malformed payload */ }
            return false;
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
            catch { /* malformed payload */ }
            return null;
        }
    }
}
