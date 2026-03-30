using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Scenario;

namespace Bagira.CGF.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler that participates in scenario loading for the CGF subsystem.
    ///
    /// <para>
    /// The CGF subsystem does not own an <see cref="EntityRepository"/>, so scenario
    /// loading is a header-peek-only operation.  If the file's <c>SubsystemType</c>
    /// matches this subsystem, the handler acknowledges the load without spawning
    /// entities; a mismatch is a silent success (no-op).
    /// </para>
    /// </summary>
    public sealed class ScenarioLoadDsmHandler : IDsmHandler
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";

        private readonly ScenarioSerializer _serializer;
        private readonly string _localTempRoot;

        /// <param name="serializer">
        /// Pre-built serializer specifying the CGF subsystem type; used only for the
        /// <see cref="ScenarioSerializer.IsMatchingSubsystem"/> header-peek.
        /// </param>
        /// <param name="localTempRoot">
        /// Root staging directory for pre-fetched scenario files.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        public ScenarioLoadDsmHandler(ScenarioSerializer serializer, string localTempRoot = DefaultLocalTempRoot)
        {
            _serializer    = serializer   ?? throw new ArgumentNullException(nameof(serializer));
            _localTempRoot = string.IsNullOrWhiteSpace(localTempRoot) ? DefaultLocalTempRoot : localTempRoot;
        }

        /// <inheritdoc />
        /// <remarks>
        /// This handler is the sole <c>PrepareLive</c> handler on the CGF node (BATCH-19 A.1).
        /// <see cref="FailLoudRecordReplayStub"/> was narrowed to exclude <c>PrepareLive</c> so
        /// that both normal scenario payloads (with <c>ScenarioId</c>) and branch payloads
        /// (with <c>DrillId</c>, no <c>ScenarioId</c>) route here.  Branch payloads are
        /// detected in <see cref="PrepareAsync"/> via the <c>HasDrillId</c> guard.
        /// </remarks>
        public bool CanHandle(NodeOpType op) => op == NodeOpType.PrepareLive;

        /// <summary>
        /// Incremented each time <see cref="PrepareAsync"/> is invoked.  Exposed for unit/integration
        /// tests that need to assert the handler was reached via DrillSlave dispatch.
        /// </summary>
        internal int PrepareCallCountForTest { get; private set; }

        /// <inheritdoc />
        /// <remarks>
        /// Peeks <c>Header.SubsystemType</c> in the scenario file.  Always returns
        /// <see langword="null"/> (success): a mismatch means "not our file" and a match means
        /// "acknowledged — no entities to spawn in CGF".
        /// <para>
        /// <b>Branch guard (BATCH-19 A.1):</b> When the payload contains a <c>DrillId</c> field
        /// but <b>no</b> <c>ScenarioId</c> field the command is a Live-from-Replay branch
        /// <see cref="NodeOpType.PrepareLive"/> issued by the orchestrator.  CGF does not yet
        /// host a recordable kernel, so this handler logs an <c>Error</c> to surface the gap
        /// and returns without attempting a file read.
        /// </para>
        /// </remarks>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            PrepareCallCountForTest++;

            var scenarioId = ParseScenarioId(cmd.PayloadJson);
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                // BATCH-19 A.1: distinguish branch PrepareLive (has DrillId, no ScenarioId).
                // This handler is now the sole PrepareLive handler on CGF, so it must
                // fail loud for branch commands just as FailLoudRecordReplayStub used to.
                if (HasDrillId(cmd.PayloadJson))
                {
                    FdpLog<ScenarioLoadDsmHandler>.Error(
                        "[CGF] ScenarioLoadDsmHandler: branch PrepareLive received " +
                        "(DrillId-only payload, no ScenarioId — transactionId={0}).  " +
                        "CGF does not yet host a recordable kernel — brain-side persistence " +
                        "is incomplete.  Replace with a real handler when the CGF kernel is wired.",
                        cmd.TransactionId);
                }
                return Task.FromResult<string?>(null);
            }

            var scenarioDir = Path.Combine(_localTempRoot, scenarioId);
            if (!Directory.Exists(scenarioDir))
            {
                FdpLog<ScenarioLoadDsmHandler>.Info(
                    "[CGF] ScenarioLoadDsmHandler.PrepareAsync: directory '{0}' not found — skipping.", scenarioDir);
                return Task.FromResult<string?>(null);
            }

            foreach (var filePath in Directory.GetFiles(scenarioDir, "*.json"))
            {
                try
                {
                    var text   = File.ReadAllText(filePath);
                    var dom    = JsonNode.Parse(text)?.AsObject();
                    if (dom == null) continue;

                    var subsysType = dom["Header"]?.AsObject()?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    FdpLog<ScenarioLoadDsmHandler>.Info(
                        "[CGF] ScenarioLoadDsmHandler.PrepareAsync: matched '{0}' — acknowledged (no ECS).", filePath);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<ScenarioLoadDsmHandler>.Warn(
                        "[CGF] ScenarioLoadDsmHandler.PrepareAsync: failed to peek '{0}': {1}", filePath, ex.Message);
                }
            }

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        /// <remarks>CGF has no entity repository; commit is always a no-op.</remarks>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            // No ECS in CGF — nothing to commit for scenario loading.
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            // Nothing to roll back.
        }

        private static string? ParseScenarioId(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return null;
            try
            {
                var node = JsonNode.Parse(payloadJson);
                return node?["ScenarioId"]?.GetValue<string>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="payloadJson"/> contains a
        /// <c>DrillId</c> field, indicating a Live-from-Replay branch
        /// <see cref="NodeOpType.PrepareLive"/> command (no <c>ScenarioId</c> present).
        /// </summary>
        private static bool HasDrillId(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return false;
            try
            {
                var node = JsonNode.Parse(payloadJson);
                return node?["DrillId"] != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
