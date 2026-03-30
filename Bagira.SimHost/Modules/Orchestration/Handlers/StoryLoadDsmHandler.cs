using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Scenario;

namespace Bagira.SimHost.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler that injects and removes scenario-derived story entities during a running
    /// drill (<see cref="NodeOpType.StartStory"/> and <see cref="NodeOpType.StopStory"/>).
    ///
    /// <para>
    /// <b>StartStory prepare path:</b> Locates the scenario file for the story's
    /// <c>ScenarioId</c> from <c>C:\FDP_Temp\&lt;scenarioId&gt;\{SubsystemType}.json</c>,
    /// peeks <c>Header.SubsystemType</c>; if the type does not match this subsystem,
    /// sets <see cref="IsParticipatingForTest"/> to <c>false</c> and no-ops.
    /// If the type matches, reads and stores the full JSON DOM for <see cref="Commit"/>.
    /// </para>
    ///
    /// <para>
    /// <b>StartStory commit path:</b> Calls
    /// <see cref="ScenarioSerializer.Deserialize(EntityRepository, JsonObject, bool, Guid?)"/>
    /// with <c>asStory: true</c> and the parsed <c>storyId</c>, so every spawned entity
    /// receives a <see cref="Fdp.Kernel.StoryTag"/> stamped with that GUID.
    /// </para>
    ///
    /// <para>
    /// <b>StopStory prepare path:</b> Queries the live <see cref="EntityRepository"/> for
    /// all entities whose <see cref="Fdp.Kernel.StoryTag.StoryId"/> matches the payload
    /// <c>StoryId</c> and caches their handles.
    /// </para>
    ///
    /// <para>
    /// <b>StopStory commit path:</b> Destroys all cached story entities.
    /// </para>
    /// </summary>
    public sealed class StoryLoadDsmHandler : IDsmHandler
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";

        private readonly ScenarioSerializer _serializer;
        private readonly string             _localTempRoot;
        private readonly EntityRepository?  _world;

        // ── Pending state between PrepareAsync and Commit ────────────────────
        private JsonObject?      _pendingDom;
        private Guid             _pendingStoryId;
        private Guid?            _pendingTransactionId;
        private List<Entity>?    _pendingStopEntities;
        private bool             _pendingIsParticipating;

        /// <param name="serializer">
        /// Pre-built serializer scoped to this subsystem; used for SubsystemType matching and
        /// story entity deserialization.
        /// </param>
        /// <param name="localTempRoot">
        /// Root of the local staging area where pre-fetched scenario files land.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        /// <param name="world">
        /// Optional live entity repository.  When provided it is used when
        /// <see cref="DrillSlave"/> passes <c>repo: null</c> from its dispatch loop.
        /// Pass <c>null</c> in tests that supply the repository directly via the
        /// <see cref="Commit"/> parameter.
        /// </param>
        public StoryLoadDsmHandler(
            ScenarioSerializer serializer,
            string             localTempRoot = DefaultLocalTempRoot,
            EntityRepository?  world         = null)
        {
            _serializer    = serializer   ?? throw new ArgumentNullException(nameof(serializer));
            _localTempRoot = string.IsNullOrWhiteSpace(localTempRoot) ? DefaultLocalTempRoot : localTempRoot;
            _world         = world;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType op) =>
            op == NodeOpType.StartStory ||
            op == NodeOpType.StopStory;

        /// <summary>
        /// <c>true</c> after a <see cref="NodeOpType.StartStory"/> <see cref="PrepareAsync"/>
        /// in which the subsystem type matched (this node is a participant for the story).
        /// <c>false</c> when there is no matching file (non-participant).
        /// Exposed for integration-test assertions.
        /// </summary>
        internal bool IsParticipatingForTest => _pendingIsParticipating;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            // Reset per-transaction state.
            _pendingDom              = null;
            _pendingStoryId          = Guid.Empty;
            _pendingTransactionId    = null;
            _pendingStopEntities     = null;
            _pendingIsParticipating  = false;

            if (cmd.Operation == NodeOpType.StartStory)
                return PrepareStartStory(cmd);

            if (cmd.Operation == NodeOpType.StopStory)
                return PrepareStopStory(cmd);

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            if (cmd.Operation == NodeOpType.StartStory)
                CommitStartStory(cmd, repo);
            else if (cmd.Operation == NodeOpType.StopStory)
                CommitStopStory(cmd, repo);
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            _pendingDom             = null;
            _pendingStoryId         = Guid.Empty;
            _pendingTransactionId   = null;
            _pendingStopEntities    = null;
            _pendingIsParticipating = false;
        }

        // ── StartStory ────────────────────────────────────────────────────────

        private Task<string?> PrepareStartStory(NodeOpCommand cmd)
        {
            var (storyId, scenarioId) = ParseStartStoryPayload(cmd.PayloadJson);
            if (storyId == Guid.Empty)
            {
                FdpLog<StoryLoadDsmHandler>.Warn(
                    "[SimHost] StoryLoadDsmHandler: StartStory payload missing valid StoryId " +
                    "(transactionId={0}).", cmd.TransactionId);
                return Task.FromResult<string?>(null);
            }
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<StoryLoadDsmHandler>.Warn(
                    "[SimHost] StoryLoadDsmHandler: StartStory payload missing ScenarioId " +
                    "(transactionId={0}).", cmd.TransactionId);
                return Task.FromResult<string?>(null);
            }

            var scenarioDir = Path.Combine(_localTempRoot, scenarioId);
            if (!Directory.Exists(scenarioDir))
            {
                FdpLog<StoryLoadDsmHandler>.Info(
                    "[SimHost] StoryLoadDsmHandler.PrepareStartStory: directory '{0}' not found — " +
                    "not participating.", scenarioDir);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }

            foreach (var filePath in Directory.GetFiles(scenarioDir, "*.json"))
            {
                try
                {
                    var text = File.ReadAllText(filePath);
                    var dom  = JsonNode.Parse(text)?.AsObject();
                    if (dom == null) continue;

                    var subsysType = dom["Header"]?.AsObject()?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    // Matching subsystem: queue for Commit.
                    _pendingDom             = dom;
                    _pendingStoryId         = storyId;
                    _pendingTransactionId   = cmd.TransactionId;
                    _pendingIsParticipating = true;
                    FdpLog<StoryLoadDsmHandler>.Info(
                        "[SimHost] StoryLoadDsmHandler.PrepareStartStory: story {0} queued from '{1}'.",
                        storyId, filePath);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<StoryLoadDsmHandler>.Warn(
                        "[SimHost] StoryLoadDsmHandler.PrepareStartStory: failed to peek '{0}': {1}",
                        filePath, ex.Message);
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

        private void CommitStartStory(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (_pendingDom == null)
            {
                // Not participating — nothing to do.
                _pendingTransactionId = null;
                return;
            }

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                FdpLog<StoryLoadDsmHandler>.Warn(
                    "[SimHost] StoryLoadDsmHandler.CommitStartStory: EntityRepository is null — " +
                    "cannot deserialize story entities.");
                _pendingDom             = null;
                _pendingTransactionId   = null;
                _pendingIsParticipating = false;
                return;
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom, asStory: true, storyId: _pendingStoryId);
                FdpLog<StoryLoadDsmHandler>.Info(
                    "[SimHost] StoryLoadDsmHandler.CommitStartStory: story {0} entities injected.",
                    _pendingStoryId);
            }
            catch (Exception ex)
            {
                FdpLog<StoryLoadDsmHandler>.Error(
                    "[SimHost] StoryLoadDsmHandler.CommitStartStory: Deserialize failed: {0}", ex.Message);
                throw;
            }
            finally
            {
                _pendingDom           = null;
                _pendingTransactionId = null;
            }
        }

        // ── StopStory ─────────────────────────────────────────────────────────

        private Task<string?> PrepareStopStory(NodeOpCommand cmd)
        {
            var storyId = ParseStopStoryPayload(cmd.PayloadJson);
            if (storyId == Guid.Empty)
            {
                FdpLog<StoryLoadDsmHandler>.Warn(
                    "[SimHost] StoryLoadDsmHandler: StopStory payload missing valid StoryId " +
                    "(transactionId={0}).", cmd.TransactionId);
                return Task.FromResult<string?>(null);
            }

            // The live repo is needed to query story entities.
            // If _world is null we rely on the caller supplying a repo in Commit;
            // defer entity list build to Commit (no-op here).
            _pendingStoryId         = storyId;
            _pendingTransactionId   = cmd.TransactionId;
            _pendingIsParticipating = true;

            if (_world != null)
                _pendingStopEntities = CollectStoryEntities(_world, storyId);

            return Task.FromResult<string?>(null);
        }

        private void CommitStopStory(NodeOpCommand cmd, EntityRepository? repo)
        {
            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                FdpLog<StoryLoadDsmHandler>.Warn(
                    "[SimHost] StoryLoadDsmHandler.CommitStopStory: EntityRepository is null — " +
                    "cannot destroy story entities.");
                _pendingTransactionId = null;
                _pendingStopEntities  = null;
                return;
            }

            // Build entity list if not already collected in PrepareStopStory (e.g. _world was null then).
            var toDestroy = _pendingStopEntities ?? CollectStoryEntities(targetRepo, _pendingStoryId);
            foreach (var entity in toDestroy)
            {
                if (targetRepo.IsAlive(entity))
                    targetRepo.DestroyEntity(entity);
            }

            FdpLog<StoryLoadDsmHandler>.Info(
                "[SimHost] StoryLoadDsmHandler.CommitStopStory: story {0} — {1} entity/entities destroyed.",
                _pendingStoryId, toDestroy.Count);

            _pendingStopEntities    = null;
            _pendingTransactionId   = null;
            _pendingIsParticipating = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static (Guid storyId, string? scenarioId) ParseStartStoryPayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return (Guid.Empty, null);
            try
            {
                var doc = JsonDocument.Parse(payloadJson);
                using (doc)
                {
                    Guid storyId = Guid.Empty;
                    if (doc.RootElement.TryGetProperty("StoryId", out var storyProp))
                        Guid.TryParse(storyProp.GetString(), out storyId);

                    string? scenarioId = null;
                    if (doc.RootElement.TryGetProperty("ScenarioId", out var scenarioProp))
                        scenarioId = scenarioProp.GetString();

                    return (storyId, scenarioId);
                }
            }
            catch
            {
                return (Guid.Empty, null);
            }
        }

        private static Guid ParseStopStoryPayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return Guid.Empty;
            try
            {
                var doc = JsonDocument.Parse(payloadJson);
                using (doc)
                {
                    if (doc.RootElement.TryGetProperty("StoryId", out var storyProp))
                    {
                        Guid.TryParse(storyProp.GetString(), out var storyId);
                        return storyId;
                    }
                    return Guid.Empty;
                }
            }
            catch
            {
                return Guid.Empty;
            }
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
