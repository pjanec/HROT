using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using Hrot.Common.Scenario;

namespace Hrot.Common.Orchestration.Handlers
{
    /// <summary>
    /// Reference implementation of the episode-load Cluster handler (CGF1-G0404).
    /// Handles <c>StartEpisode</c> and <c>StopEpisode</c> operations.
    /// </summary>
    public sealed class ReferenceEpisodeLoadHandler : IClusterStateHandler
    {
        private readonly ScenarioSerializer       _serializer;
        private readonly IScenarioStorageProvider _storageProvider;
        private readonly EntityRepository?        _world;

        private string?       _pendingJson;
        private Guid          _pendingEpisodeId;
        private Guid?         _pendingTransactionId;
        private List<Entity>? _pendingStopEntities;
        private bool          _pendingIsParticipating;

        public ReferenceEpisodeLoadHandler(
            ScenarioSerializer        serializer,
            IScenarioStorageProvider  storageProvider,
            EntityRepository?         world = null)
        {
            _serializer      = serializer      ?? throw new ArgumentNullException(nameof(serializer));
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            _world           = world;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation == NodeOpType.StartEpisode ||
            operation == NodeOpType.StopEpisode;

        /// <summary>
        /// <c>true</c> after a <c>StartEpisode</c> <see cref="PrepareAsync"/> in which the
        /// subsystem type matched.  Exposed for integration-test assertions.
        /// </summary>
        public bool IsParticipatingForTest => _pendingIsParticipating;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            _pendingJson            = null;
            _pendingEpisodeId       = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;

            if (intent.Operation == NodeOpType.StartEpisode)
                return PrepareStartEpisode(intent);

            if (intent.Operation == NodeOpType.StopEpisode)
                return PrepareStopEpisode(intent);

            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (_pendingTransactionId != intent.TransactionId) return;

            if (intent.Operation == NodeOpType.StartEpisode)
                CommitStartEpisode(intent, repo);
            else if (intent.Operation == NodeOpType.StopEpisode)
                CommitStopEpisode(intent, repo);
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            _pendingJson            = null;
            _pendingEpisodeId       = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;
        }

        // ── StartEpisode ─────────────────────────────────────────────────────

        private Task<object?> PrepareStartEpisode(ExecuteNodeOpIntent intent)
        {
            var payload    = intent.DomainPayload is EpisodeHandlerPayload p ? p : default;
            var episodeId  = payload.EpisodeId;
            var scenarioId = payload.ScenarioId;

            if (episodeId == Guid.Empty)
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] StartEpisode payload missing valid EpisodeId " +
                    "(transactionId={0}) — will ACK as non-participating.", intent.TransactionId);
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<object?>(null);
            }

            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] StartEpisode payload missing ScenarioId " +
                    "(transactionId={0}) — will ACK as non-participating.", intent.TransactionId);
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<object?>(null);
            }

            foreach (var fileName in _storageProvider.EnumerateScenarioFiles(scenarioId))
            {
                try
                {
                    using var stream = _storageProvider.OpenScenarioFile(scenarioId, Path.GetFileName(fileName));
                    if (stream == null) continue;

                    using var reader = new StreamReader(stream);
                    var text       = reader.ReadToEnd();
                    var subsysType = HrotScenarioEnvelope.PeekSubsystemType(text);
                    if (!HrotScenarioEnvelope.IsMatchingSubsystem(subsysType, _serializer.SubsystemType)) continue;

                    _pendingJson            = text;
                    _pendingEpisodeId       = episodeId;
                    _pendingTransactionId   = intent.TransactionId;
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

            if (_pendingJson == null)
            {
                // No matching file — not participating.
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
            }

            return Task.FromResult<object?>(null);
        }

        private void CommitStartEpisode(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (_pendingJson == null)
            {
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                _pendingJson            = null;
                _pendingTransactionId   = null;
                _pendingIsParticipating = false;
                throw new InvalidOperationException(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: EntityRepository is null — " +
                    "cannot deserialize episode entities.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingJson, asEpisode: true, episodeId: _pendingEpisodeId);
                FdpLog<ReferenceEpisodeLoadHandler>.Info(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: episode {0} entities injected.",
                    _pendingEpisodeId);
            }
            catch (Exception ex)
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] CommitStartEpisode: Deserialize failed: {0}", ex.Message);
                throw;
            }
            finally
            {
                _pendingJson          = null;
                _pendingTransactionId = null;
            }
        }

        // ── StopEpisode ──────────────────────────────────────────────────────

        private Task<object?> PrepareStopEpisode(ExecuteNodeOpIntent intent)
        {
            var payload   = intent.DomainPayload is EpisodeHandlerPayload p ? p : default;
            var episodeId = payload.EpisodeId;

            if (episodeId == Guid.Empty)
            {
                FdpLog<ReferenceEpisodeLoadHandler>.Error(
                    "[ReferenceEpisodeLoadHandler] StopEpisode payload missing valid EpisodeId " +
                    "(transactionId={0}) — will ACK as non-participating.", intent.TransactionId);
                _pendingTransactionId   = intent.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<object?>(null);
            }

            _pendingEpisodeId       = episodeId;
            _pendingTransactionId   = intent.TransactionId;
            _pendingIsParticipating = true;

            if (_world != null)
                _pendingStopEntities = CollectEpisodeEntities(_world, episodeId);

            return Task.FromResult<object?>(null);
        }

        private void CommitStopEpisode(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (!_pendingIsParticipating)
            {
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

            _pendingStopEntities    = null;
            _pendingTransactionId   = null;
            _pendingIsParticipating = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
