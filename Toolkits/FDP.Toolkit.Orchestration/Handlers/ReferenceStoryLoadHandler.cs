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
    /// Reference implementation of the story-load DSM handler (CGF1-G0404).
    ///
    /// <para>
    /// Handles <c>StartStory (operationId=20)</c> and <c>StopStory (operationId=21)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>StartStory prepare path:</b> Locates the scenario file for the story's
    /// <c>ScenarioId</c> via <see cref="IScenarioStorageProvider"/>, peeks
    /// <c>Header.SubsystemType</c>; caches the matching DOM for <see cref="Commit"/>.
    /// On mismatch, sets <see cref="IsParticipatingForTest"/> to <c>false</c>.
    /// </para>
    ///
    /// <para>
    /// <b>StartStory commit path:</b> Calls
    /// <c>ScenarioSerializer.Deserialize(repo, dom, asStory: true, storyId)</c> so
    /// every spawned entity receives a <c>StoryTag</c> stamped with the story GUID;
    /// then publishes a participating ACK via <see cref="IOrchestrationTransport"/>.
    /// </para>
    ///
    /// <para>
    /// <b>StopStory prepare path:</b> Queries the live repository for entities whose
    /// <c>StoryTag.StoryId</c> matches the payload <c>StoryId</c> and caches their
    /// handles.
    /// </para>
    ///
    /// <para>
    /// <b>StopStory commit path:</b> Destroys all cached story entities and publishes
    /// a participating ACK.
    /// </para>
    /// </summary>
    public sealed class ReferenceStoryLoadHandler : IDsmHandler
    {
        /// <summary>Integer value of <c>NodeOpType.StartStory</c>.</summary>
        public const int StartStoryOperationId = 20;
        /// <summary>Integer value of <c>NodeOpType.StopStory</c>.</summary>
        public const int StopStoryOperationId  = 21;

        private readonly ScenarioSerializer       _serializer;
        private readonly IScenarioStorageProvider _storageProvider;
        private readonly EntityRepository?        _world;
        private readonly IOrchestrationTransport? _transport;
        private readonly int                      _nodeId;

        // ── Pending state between PrepareAsync and Commit ─────────────────────
        private JsonObject?   _pendingDom;
        private Guid          _pendingStoryId;
        private Guid?         _pendingTransactionId;
        private List<Entity>? _pendingStopEntities;
        private bool          _pendingIsParticipating;

        /// <param name="serializer">
        /// Pre-built serializer scoped to this subsystem; used for SubsystemType matching
        /// and story entity deserialization.
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
        public ReferenceStoryLoadHandler(
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
            operationId == StartStoryOperationId ||
            operationId == StopStoryOperationId;

        /// <summary>
        /// <c>true</c> after a <c>StartStory</c> <see cref="PrepareAsync"/> in which the
        /// subsystem type matched (this node is a participant for the story).
        /// Exposed for integration-test assertions.
        /// </summary>
        internal bool IsParticipatingForTest => _pendingIsParticipating;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            _pendingDom             = null;
            _pendingStoryId         = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;

            if (cmd.OperationId == StartStoryOperationId)
                return PrepareStartStory(cmd);

            if (cmd.OperationId == StopStoryOperationId)
                return PrepareStopStory(cmd);

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            if (cmd.OperationId == StartStoryOperationId)
                CommitStartStory(cmd, repo);
            else if (cmd.OperationId == StopStoryOperationId)
                CommitStopStory(cmd, repo);
        }

        /// <inheritdoc />
        public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
        {
            _pendingDom             = null;
            _pendingStoryId         = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;
        }

        // ── StartStory ────────────────────────────────────────────────────────

        private Task<string?> PrepareStartStory(OrchestrationCommand cmd)
        {
            var (storyId, scenarioId) = ParseStartStoryPayload(cmd.PayloadJson);
            if (storyId == Guid.Empty)
            {
                FdpLog<ReferenceStoryLoadHandler>.Error(
                    "[ReferenceStoryLoadHandler] StartStory payload missing valid StoryId " +
                    "(transactionId={0}) — will ACK as non-participating.", cmd.TransactionId);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<ReferenceStoryLoadHandler>.Error(
                    "[ReferenceStoryLoadHandler] StartStory payload missing ScenarioId " +
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
                    _pendingStoryId         = storyId;
                    _pendingTransactionId   = cmd.TransactionId;
                    _pendingIsParticipating = true;
                    FdpLog<ReferenceStoryLoadHandler>.Info(
                        "[ReferenceStoryLoadHandler] PrepareStartStory: story {0} queued from '{1}'.",
                        storyId, fileName);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<ReferenceStoryLoadHandler>.Warn(
                        "[ReferenceStoryLoadHandler] PrepareStartStory: failed to peek '{0}': {1}",
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

        private void CommitStartStory(OrchestrationCommand cmd, EntityRepository? repo)
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
                    "[ReferenceStoryLoadHandler] CommitStartStory: EntityRepository is null — " +
                    "cannot deserialize story entities.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom, asStory: true, storyId: _pendingStoryId);
                FdpLog<ReferenceStoryLoadHandler>.Info(
                    "[ReferenceStoryLoadHandler] CommitStartStory: story {0} entities injected.",
                    _pendingStoryId);
                PublishAck(cmd.TransactionId, isParticipating: true);
            }
            catch (Exception ex)
            {
                FdpLog<ReferenceStoryLoadHandler>.Error(
                    "[ReferenceStoryLoadHandler] CommitStartStory: Deserialize failed: {0}", ex.Message);
                throw;
            }
            finally
            {
                _pendingDom           = null;
                _pendingTransactionId = null;
            }
        }

        // ── StopStory ─────────────────────────────────────────────────────────

        private Task<string?> PrepareStopStory(OrchestrationCommand cmd)
        {
            var storyId = ParseStopStoryPayload(cmd.PayloadJson);
            if (storyId == Guid.Empty)
            {
                FdpLog<ReferenceStoryLoadHandler>.Error(
                    "[ReferenceStoryLoadHandler] StopStory payload missing valid StoryId " +
                    "(transactionId={0}) — will ACK as non-participating.", cmd.TransactionId);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }

            _pendingStoryId         = storyId;
            _pendingTransactionId   = cmd.TransactionId;
            _pendingIsParticipating = true;

            if (_world != null)
                _pendingStopEntities = CollectStoryEntities(_world, storyId);

            return Task.FromResult<string?>(null);
        }

        private void CommitStopStory(OrchestrationCommand cmd, EntityRepository? repo)
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
                    "[ReferenceStoryLoadHandler] CommitStopStory: EntityRepository is null — " +
                    "cannot destroy story entities.");
            }

            var toDestroy = _pendingStopEntities ?? CollectStoryEntities(targetRepo, _pendingStoryId);
            foreach (var entity in toDestroy)
            {
                if (targetRepo.IsAlive(entity))
                    targetRepo.DestroyEntity(entity);
            }

            FdpLog<ReferenceStoryLoadHandler>.Info(
                "[ReferenceStoryLoadHandler] CommitStopStory: story {0} — {1} entity/entities destroyed.",
                _pendingStoryId, toDestroy.Count);

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

        private static (Guid storyId, string? scenarioId) ParseStartStoryPayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return (Guid.Empty, null);
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                Guid storyId = Guid.Empty;
                if (doc.RootElement.TryGetProperty("StoryId", out var storyProp))
                    Guid.TryParse(storyProp.GetString(), out storyId);

                string? scenarioId = null;
                if (doc.RootElement.TryGetProperty("ScenarioId", out var scenarioProp))
                    scenarioId = scenarioProp.GetString();

                return (storyId, scenarioId);
            }
            catch { return (Guid.Empty, null); }
        }

        private static Guid ParseStopStoryPayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return Guid.Empty;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("StoryId", out var storyProp))
                {
                    Guid.TryParse(storyProp.GetString(), out var storyId);
                    return storyId;
                }
                return Guid.Empty;
            }
            catch { return Guid.Empty; }
        }

        private static List<Entity> CollectStoryEntities(EntityRepository repo, Guid storyId)
        {
            var result = new List<Entity>();
            if (!repo.IsComponentTypeRegistered<StoryTag>()) return result;

            var query = repo.Query().With<StoryTag>().Build();
            foreach (var e in query)
            {
                ref readonly var tag = ref repo.GetComponentRO<StoryTag>(e);
                if (tag.StoryId == storyId)
                    result.Add(e);
            }
            return result;
        }
    }
}
