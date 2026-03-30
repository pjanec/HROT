using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Scenario;

namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the scenario-load DSM handler (CGF1-G0404).
    ///
    /// <para>
    /// Handles <c>PrepareLive (operationId=9)</c>: locates a matching scenario file from
    /// <see cref="IScenarioStorageProvider"/>, peeks <c>Header.SubsystemType</c>, and if
    /// the type matches this subsystem caches the JSON DOM for <see cref="Commit"/>.
    /// </para>
    ///
    /// <para>
    /// When <c>EntityRepository? world</c> is <c>null</c> the handler behaves as the
    /// CGF header-peek-only path — it participates and caches the DOM but skips entity
    /// injection on <see cref="Commit"/>. This eliminates the need for a separate
    /// CGF-specific copy of the handler.
    /// </para>
    /// </summary>
    public sealed class ReferenceScenarioLoadHandler : IDsmHandler
    {
        /// <summary>Integer value of <c>NodeOpType.PrepareLive</c> (stable constant).</summary>
        public const int PrepareLiveOperationId = 9;

        private readonly ScenarioSerializer  _serializer;
        private readonly IScenarioStorageProvider _storageProvider;
        private readonly EntityRepository?   _world;

        private JsonObject? _pendingDom;
        private Guid?       _pendingTransactionId;
        private int         _prepareCallCount;

        /// <summary>
        /// Number of times <see cref="PrepareAsync"/> has been invoked.
        /// For integration-test assertions only.
        /// </summary>
        internal int PrepareCallCountForTest => _prepareCallCount;

        /// <param name="serializer">
        /// Pre-built <see cref="ScenarioSerializer"/> configured with the owning subsystem's
        /// component translators and subsystem type string.
        /// </param>
        /// <param name="storageProvider">
        /// Storage provider for locating pre-fetched scenario files.
        /// Use <c>LocalDiskStorageProvider</c> in production.
        /// </param>
        /// <param name="world">
        /// Optional entity repository.  When provided it is used as the deserialization
        /// target when the dispatch loop passes <c>repo: null</c>.
        /// Pass <c>null</c> for CGF-style header-peek-only participation (no entity injection).
        /// </param>
        public ReferenceScenarioLoadHandler(
            ScenarioSerializer        serializer,
            IScenarioStorageProvider  storageProvider,
            EntityRepository?         world = null)
        {
            _serializer      = serializer      ?? throw new ArgumentNullException(nameof(serializer));
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            _world           = world;
        }

        /// <inheritdoc />
        public bool CanHandle(int operationId) => operationId == PrepareLiveOperationId;

        /// <inheritdoc />
        /// <remarks>
        /// Locates and parses the scenario file via the storage provider; caches the DOM
        /// if the subsystem type matches.  Returns <see langword="null"/> (success) on
        /// a SubsystemType mismatch — the subsystem has no file to load for this scenario.
        /// </remarks>
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            _prepareCallCount++;
            _pendingDom           = null;
            _pendingTransactionId = null;

            var scenarioId = ParseScenarioId(cmd.PayloadJson);
            if (string.IsNullOrWhiteSpace(scenarioId))
                return Task.FromResult<string?>(null);

            foreach (var fileName in _storageProvider.EnumerateScenarioFiles(scenarioId))
            {
                try
                {
                    using var stream = _storageProvider.OpenScenarioFile(scenarioId, Path.GetFileName(fileName));
                    if (stream == null) continue;

                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    var dom  = System.Text.Json.Nodes.JsonNode.Parse(text)?.AsObject();
                    if (dom == null) continue;

                    var headerNode = dom["Header"]?.AsObject();
                    var subsysType = headerNode?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    _pendingDom           = dom;
                    _pendingTransactionId = cmd.TransactionId;
                    FdpLog<ReferenceScenarioLoadHandler>.Info(
                        "[ReferenceScenarioLoadHandler] PrepareAsync: queued '{0}' for load.",
                        fileName);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<ReferenceScenarioLoadHandler>.Warn(
                        "[ReferenceScenarioLoadHandler] PrepareAsync: failed to peek '{0}': {1}",
                        fileName, ex.Message);
                }
            }

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Deserializes entities from the cached DOM into the repository when a match was
        /// found.  No-ops on subsystem mismatch or when <c>world</c> is <c>null</c> (CGF
        /// header-peek path).
        /// </remarks>
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (_pendingDom == null || _pendingTransactionId != cmd.TransactionId) return;

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                // CGF header-peek path: no entity repository — participate but skip injection.
                FdpLog<ReferenceScenarioLoadHandler>.Info(
                    "[ReferenceScenarioLoadHandler] Commit: no EntityRepository — header-peek only.");
                _pendingDom           = null;
                _pendingTransactionId = null;
                return;
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom);
                FdpLog<ReferenceScenarioLoadHandler>.Info(
                    "[ReferenceScenarioLoadHandler] Commit: entities deserialized successfully.");
            }
            catch (Exception ex)
            {
                FdpLog<ReferenceScenarioLoadHandler>.Error(
                    "[ReferenceScenarioLoadHandler] Commit: Deserialize failed: {0}", ex.Message);
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
        }

        private static string? ParseScenarioId(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return null;
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(payloadJson);
                return node?["ScenarioId"]?.GetValue<string>();
            }
            catch
            {
                return null;
            }
        }
    }
}
