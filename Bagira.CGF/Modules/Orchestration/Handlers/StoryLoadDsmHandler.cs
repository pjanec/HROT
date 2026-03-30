using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Scenario;

namespace Bagira.CGF.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler that participates in story injection and deletion for the CGF subsystem
    /// (<see cref="NodeOpType.StartStory"/> and <see cref="NodeOpType.StopStory"/>).
    ///
    /// <para>
    /// The CGF subsystem does not own an <see cref="Fdp.Kernel.EntityRepository"/>.
    /// On <c>StartStory</c>, the handler peeks <c>Header.SubsystemType</c> in the story
    /// scenario file; if the type does not match, it acknowledges with
    /// <see cref="NodeOpStatus.IsParticipating"/> = <c>false</c> so the orchestrator
    /// excludes this node from the participation count.  On a type match, the handler
    /// acknowledges with <c>IsParticipating = true</c> but performs no entity work (CGF
    /// has no ECS kernel in Phase 1–2).
    /// </para>
    ///
    /// <para>
    /// On <c>StopStory</c>, participation is always <c>true</c> and the handler is a
    /// no-op (nothing to destroy without ECS).
    /// </para>
    ///
    /// <para>
    /// <b>ACK publishing:</b> When a <see cref="DdsWriter{NodeOpStatus}"/> is provided,
    /// the handler publishes a <see cref="NodeOpStatus"/> acknowledgement after
    /// <see cref="Commit"/> runs.  If the writer is <c>null</c> (DDS-less unit tests),
    /// the ACK is silently skipped.
    /// </para>
    /// </summary>
    public sealed class StoryLoadDsmHandler : Bagira.Common.Orchestration.IDsmHandler
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";

        private readonly ScenarioSerializer      _serializer;
        private readonly string                  _localTempRoot;
        private readonly DdsWriter<NodeOpStatus>? _statusWriter;
        private readonly int                     _nodeId;

        // ── Pending state between PrepareAsync and Commit ────────────────────
        private bool  _pendingIsParticipating;
        private Guid? _pendingTransactionId;

        /// <param name="serializer">
        /// Pre-built serializer specifying the CGF subsystem type; used for the
        /// <see cref="ScenarioSerializer.IsMatchingSubsystem"/> header-peek on
        /// <see cref="NodeOpType.StartStory"/>.
        /// </param>
        /// <param name="localTempRoot">
        /// Root staging directory for pre-fetched scenario files.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        /// <param name="statusWriter">
        /// Optional DDS writer used to publish <see cref="NodeOpStatus"/> ACKs back to
        /// the orchestrator.  Pass <c>null</c> in unit tests that verify dispatch without
        /// a live DDS stack.
        /// </param>
        /// <param name="nodeId">
        /// Local node identifier embedded in ACK messages.
        /// </param>
        public StoryLoadDsmHandler(
            ScenarioSerializer       serializer,
            string                   localTempRoot = DefaultLocalTempRoot,
            DdsWriter<NodeOpStatus>? statusWriter  = null,
            int                      nodeId        = 0)
        {
            _serializer    = serializer   ?? throw new ArgumentNullException(nameof(serializer));
            _localTempRoot = string.IsNullOrWhiteSpace(localTempRoot) ? DefaultLocalTempRoot : localTempRoot;
            _statusWriter  = statusWriter;
            _nodeId        = nodeId;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType op) =>
            op == NodeOpType.StartStory ||
            op == NodeOpType.StopStory;

        /// <summary>
        /// <c>true</c> after a <see cref="NodeOpType.StartStory"/> prepare in which the
        /// subsystem type matched (or after any <see cref="NodeOpType.StopStory"/> prepare).
        /// Exposed for unit/integration-test assertions.
        /// </summary>
        internal bool IsParticipatingForTest => _pendingIsParticipating;

        /// <inheritdoc />
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            _pendingIsParticipating = false;
            _pendingTransactionId   = null;

            if (cmd.Operation == NodeOpType.StartStory)
                return PrepareStartStory(cmd);

            if (cmd.Operation == NodeOpType.StopStory)
            {
                // CGF has nothing to destroy; always participating so orchestrator counts this node.
                _pendingIsParticipating = true;
                _pendingTransactionId   = cmd.TransactionId;
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public void Commit(NodeOpCommand cmd, Fdp.Kernel.EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            // CGF has no ECS: no entities to spawn or destroy.
            // Publish NodeOpStatus ACK so DrillMaster can track participation.
            PublishAck(cmd.TransactionId, _pendingIsParticipating);

            _pendingTransactionId   = null;
            _pendingIsParticipating = false;
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, Fdp.Kernel.EntityRepository? repo)
        {
            _pendingTransactionId   = null;
            _pendingIsParticipating = false;
        }

        // ── StartStory helpers ────────────────────────────────────────────────

        private Task<string?> PrepareStartStory(NodeOpCommand cmd)
        {
            var (_, scenarioId) = ParseStartStoryPayload(cmd.PayloadJson);
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                FdpLog<StoryLoadDsmHandler>.Warn(
                    "[CGF] StoryLoadDsmHandler: StartStory payload missing ScenarioId " +
                    "(transactionId={0}).", cmd.TransactionId);
                // Not participating — no scenario file to peek.
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }

            var scenarioDir = Path.Combine(_localTempRoot, scenarioId);
            if (!Directory.Exists(scenarioDir))
            {
                FdpLog<StoryLoadDsmHandler>.Info(
                    "[CGF] StoryLoadDsmHandler.PrepareStartStory: directory '{0}' not found — " +
                    "not participating.", scenarioDir);
                _pendingTransactionId   = cmd.TransactionId;
                _pendingIsParticipating = false;
                return Task.FromResult<string?>(null);
            }

            bool matched = false;
            foreach (var filePath in Directory.GetFiles(scenarioDir, "*.json"))
            {
                try
                {
                    var dom = System.Text.Json.Nodes.JsonNode.Parse(
                        File.ReadAllText(filePath))?.AsObject();
                    if (dom == null) continue;

                    var subsysType = dom["Header"]?.AsObject()?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    FdpLog<StoryLoadDsmHandler>.Info(
                        "[CGF] StoryLoadDsmHandler.PrepareStartStory: matched '{0}' — " +
                        "acknowledging (no ECS to populate).", filePath);
                    matched = true;
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<StoryLoadDsmHandler>.Warn(
                        "[CGF] StoryLoadDsmHandler.PrepareStartStory: failed to peek '{0}': {1}",
                        filePath, ex.Message);
                }
            }

            _pendingTransactionId   = cmd.TransactionId;
            _pendingIsParticipating = matched;

            if (!matched)
            {
                FdpLog<StoryLoadDsmHandler>.Info(
                    "[CGF] StoryLoadDsmHandler.PrepareStartStory: no matching scenario file in " +
                    "'{0}' for this subsystem — not participating.", scenarioDir);
            }

            return Task.FromResult<string?>(null);
        }

        // ── ACK helper ────────────────────────────────────────────────────────

        private void PublishAck(Guid transactionId, bool isParticipating)
        {
            if (_statusWriter == null) return;

            _statusWriter.Write(new NodeOpStatus
            {
                TransactionId  = transactionId,
                NodeId         = _nodeId,
                StatusCode     = OrchestrationStatusCode.Success,
                IsParticipating = isParticipating,
                ResultJson     = string.Empty,
            });
        }

        // ── Payload parsers ───────────────────────────────────────────────────

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
    }
}
