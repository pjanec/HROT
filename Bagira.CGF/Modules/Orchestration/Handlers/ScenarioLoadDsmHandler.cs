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
        public bool CanHandle(NodeOpType op) => op == NodeOpType.PrepareLive;

        /// <inheritdoc />
        /// <remarks>
        /// Peeks <c>Header.SubsystemType</c> in the scenario file.  Always returns
        /// <see langword="null"/> (success): a mismatch means "not our file" and a match means
        /// "acknowledged — no entities to spawn in CGF".
        /// </remarks>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            var scenarioId = ParseScenarioId(cmd.PayloadJson);
            if (string.IsNullOrWhiteSpace(scenarioId))
                return Task.FromResult<string?>(null);

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
    }
}
