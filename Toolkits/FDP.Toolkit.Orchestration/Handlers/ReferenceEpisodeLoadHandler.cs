using System;
using System.Collections.Generic;
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
    /// Reference implementation of the episode-load Cluster handler (CGF1-G0404).
    ///
    /// <para>
    /// Handles <c>StartEpisode (operationId=20)</c> and <c>StopEpisode (operationId=21)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>StartEpisode prepare path:</b> Locates the scenario file for the episode's
    /// <c>ScenarioId</c> via <see cref="IScenarioStorageProvider"/>, peeks
    /// <c>Header.SubsystemType</c>; caches the matching DOM for <see cref="Commit"/>.
    /// On mismatch, sets <see cref="IsParticipatingForTest"/> to <c>false</c>.
    /// </para>
    ///
    /// <para>
    /// <b>StartEpisode commit path:</b> Calls
    /// <c>ScenarioSerializer.Deserialize(repo, dom, asEpisode: true, episodeId)</c> so
    /// every spawned entity receives a <c>EpisodeTag</c> stamped with the episode GUID;
    /// then publishes a participating ACK via <see cref="IOrchestrationTransport"/>.
    /// </para>
    ///
    /// <para>
    /// <b>StopEpisode prepare path:</b> Queries the live repository for entities whose
    /// <c>EpisodeTag.EpisodeId</c> matches the payload <c>EpisodeId</c> and caches their
    /// handles.
    /// </para>
    ///
    /// <para>
    /// <b>StopEpisode commit path:</b> Destroys all cached episode entities and publishes
    /// a participating ACK.
    /// </para>
    /// </summary>
    public sealed class ReferenceEpisodeLoadHandler : IClusterStateHandler
    {
        /// <summary>Integer value of <c>NodeOpType.StartEpisode</c>.</summary>
        public const int StartEpisodeOperationId = 20;
        /// <summary>Integer value of <c>NodeOpType.StopEpisode</c>.</summary>
        public const int StopEpisodeOperationId  = 21;

        private readonly ScenarioSerializer       _serializer;
        private readonly IScenarioStorageProvider _storageProvider;
        private readonly EntityRepository?        _world;
        private readonly IOrchestrationTransport? _transport;
        private readonly int                      _nodeId;

        // ── Pending state between PrepareAsync and Commit ─────────────────────
        private JsonObject?   _pendingDom;
        private Guid          _pendingEpisodeId;
        private Guid?         _pendingTransactionId;
        private List<Entity>? _pendingStopEntities;
        private bool          _pendingIsParticipating;

        /// <param name="serializer">
        /// Pre-built serializer scoped to this subsystem; used for SubsystemType matching
        /// and episode entity deserialization.
        /// </param>
        /// <param name="storageProvider">
        /// Storage provider for locating pre-fetched scenario files.
        /// Use <c>LocalDiskStorageProvider</c> in production.
        /// </param>
        /// <param name="world">
        /// Optional live entity repository.  When provided it is used when the dispatch
        /// loop passes <c>repo: null</c>.  Pass <c>null</c> in tests that supply the
        /// repository directly via <see cref="Commit"/>.
        /// </param>
        /// <param name="transport">
        /// Optional transport used to publish <c>NodeOpStatus</c> ACKs back to the
        /// orchestrator.  Pass <c>null</c> in unit tests that do not require DDS.
        /// </param>
        /// <param name="nodeId">
        /// Local node identifier embedded in ACK messages.
        /// </param>
        public ReferenceEpisodeLoadHandler(
            ScenarioSerializer        serializer,
            IScenarioStorageProvider  storageProvider,
            EntityRepository?         world     = null,
            IOrchestrationTransport?  transport = null,
            int                       nodeId    = 0)
        {
            _serializer      = serializer      ?? throw new ArgumentNullException(nameof(serializer));
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            _world           = world;
            _transport       = transport;
            _nodeId          = nodeId;
        }

        /// <inheritdoc />
        public bool CanHandle(int operationId) =>
            operationId == StartEpisodeOperationId ||
            operationId == StopEpisodeOperationId;

        /// <summary>
        /// <c>true</c> after a <c>StartEpisode</c> <see cref="PrepareAsync"/> in which the
        /// subsystem type matched (this node is a participant for the episode).
        /// Exposed for integration-test assertions.
        /// </summary>
        internal bool IsParticipatingForTest => _pendingIsParticipating;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            _pendingDom             = null;
            _pendingEpisodeId         = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;

            if (cmd.OperationId == StartEpisodeOperationId)
                return PrepareStartEpisode(cmd);

            if (cmd.OperationId == StopEpisodeOperationId)
                return PrepareStopEpisode(cmd);

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            if (cmd.OperationId == StartEpisodeOperationId)
                CommitStartEpisode(cmd, repo);
            else if (cmd.OperationId == StopEpisodeOperationId)
                CommitStopEpisode(cmd, repo);
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _pendingDom             = null;
            _pendingEpisodeId         = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;
        }

        // ── StartEpisode ────────────────────────────────────────────────────────

        private Task<string?> PrepareStartEpisode(OrchestrationCommand cmd)
        {
            var (episodeId, scenarioId) = ParseStartEpisodePayload(cmd.PayloadJson);
            if (episodeId == Guid.Empty)
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] StartEpisode payload missing valid EpisodeId " +
                    "(transactionId={0}) — will ACK as non-participating.", cmd.TransactionId);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] StartEpisode payload missing ScenarioId " +
                    "(transactionId={0}) — will ACK as non-participating.", cmd.TransactionId);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }

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

                    _pendingDom             = dom;
                    _pendingEpisodeId         = episodeId;
                    _pendingTransactionId   = cmd.TransactionId;
                    _pendingIsParticipating = true;
                    FdpLog<ReferenceEpisodeLoadHandler>.Info(
                        "[ReferenceEpisodeLoadHandler] PrepareStartEpisode: episode {0} queued from '{1}'.",
                        episodeId, fileName);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<ReferenceEpisodeLoadHandler>.Warn(
                        "[ReferenceEpisodeLoadHandler] PrepareStartEpisode: failed to peek '{0}': {1}",
                        fileName, ex.Message);
                }
            }

            if (_pendingDom == null)
            {
                // No matching file — not participating.
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
            }

            return Task.FromResult<string?>(null);
        }

        private void CommitStartEpisode(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (_pendingDom == null)
            {
                PublishAck(cmd.TransactionId, isParticipating: false);
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingDom             = null;
                _pendingTransactionId   = null;
                _pendingIsParticipating = false;
                throw new InvalidOperationException(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: EntityRepository is null — " +
                    "cannot deserialize episode entities.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom, asEpisode: true, episodeId: _pendingEpisodeId);
                FdpLog<ReferenceEpisodeLoadHandler>.Info(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: episode {0} entities injected.",
                    _pendingEpisodeId);
                PublishAck(cmd.TransactionId, isParticipating: true);
            }
            catch (Exception ex)
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: Deserialize failed: {0}", ex.Message);
                throw;
            }
            finally
            {
                _pendingDom           = null;
                _pendingTransactionId = null;
            }
        }

        // ── StopEpisode ─────────────────────────────────────────────────────────

        private Task<string?> PrepareStopEpisode(OrchestrationCommand cmd)
        {
            var episodeId = ParseStopEpisodePayload(cmd.PayloadJson);
            if (episodeId == Guid.Empty)
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] StopEpisode payload missing valid EpisodeId " +
                    "(transactionId={0}) — will ACK as non-participating.", cmd.TransactionId);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }

            _pendingEpisodeId         = episodeId;
            _pendingTransactionId   = cmd.TransactionId;
            _pendingIsParticipating = true;

            if (_world != null)
                _pendingStopEntities = CollectEpisodeEntities(_world, episodeId);

            return Task.FromResult<string?>(null);
        }

        private void CommitStopEpisode(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (!_pendingIsParticipating)
            {
                PublishAck(cmd.TransactionId, isParticipating: false);
                _pendingTransactionId = null;
                _pendingStopEntities  = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingTransactionId = null;
                _pendingStopEntities  = null;
                throw new InvalidOperationException(
                    "[ReferenceEpisodeLoadHandler] CommitStopEpisode: EntityRepository is null — " +
                    "cannot destroy episode entities.");
            }

            var toDestroy = _pendingStopEntities ?? CollectEpisodeEntities(targetRepo, _pendingEpisodeId);
            foreach (var entity in toDestroy)
            {
                if (targetRepo.IsAlive(entity))
                    targetRepo.DestroyEntity(entity);
            }

            FdpLog<ReferenceEpisodeLoadHandler>.Info(
                "[ReferenceEpisodeLoadHandler] CommitStopEpisode: episode {0} — {1} entity/entities destroyed.",
                _pendingEpisodeId, toDestroy.Count);

            PublishAck(cmd.TransactionId, isParticipating: true);

            _pendingStopEntities    = null;
            _pendingTransactionId   = null;
            _pendingIsParticipating = false;
        }

        // ── ACK helper ────────────────────────────────────────────────────────

        private void PublishAck(Guid transactionId, bool isParticipating)
        {
            _transport?.PublishStatus(new OrchestrationStatus(
                TransactionId:   transactionId,
                NodeId:          _nodeId,
                StatusCode:      OrchestrationStatusCode.Success,
                IsParticipating: isParticipating,
                ResultJson:      string.Empty));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static (Guid episodeId, string? scenarioId) ParseStartEpisodePayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return (Guid.Empty, null);
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                Guid episodeId = Guid.Empty;
                if (doc.RootElement.TryGetProperty("EpisodeId", out var episodeProp))
                    Guid.TryParse(episodeProp.GetString(), out episodeId);

                string? scenarioId = null;
                if (doc.RootElement.TryGetProperty("ScenarioId", out var scenarioProp))
                    scenarioId = scenarioProp.GetString();

                return (episodeId, scenarioId);
            }
            catch { return (Guid.Empty, null); }
        }

        private static Guid ParseStopEpisodePayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return Guid.Empty;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("EpisodeId", out var episodeProp))
                {
                    Guid.TryParse(episodeProp.GetString(), out var episodeId);
                    return episodeId;
                }
                return Guid.Empty;
            }
            catch { return Guid.Empty; }
        }

        private static List<Entity> CollectEpisodeEntities(EntityRepository repo, Guid episodeId)
        {
            var result = new List<Entity>();
            if (!repo.IsComponentTypeRegistered<EpisodeTag>()) return result;

            var query = repo.Query().With<EpisodeTag>().Build();
            foreach (var e in query)
            {
                ref readonly var tag = ref repo.GetComponentRO<EpisodeTag>(e);
                if (tag.EpisodeId == episodeId)
                    result.Add(e);
            }
            return result;
        }
    }
}
