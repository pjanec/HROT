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

namespace Bagira.SimHost.Modules.Orchestration.Handlers
{
    /// <summary>
    /// DSM handler that loads a scenario JSON file for the SimHost subsystem during a
    /// <see cref="NodeOpType.PrepareLive"/> command.
    ///
    /// <para>
    /// <b>Prepare path:</b> Locates the scenario file from
    /// <c>C:\FDP_Temp\&lt;scenarioId&gt;\{SubsystemType}.json</c>, peeks
    /// <c>Header.SubsystemType</c>; if the type does not match this subsystem,
    /// returns <see langword="null"/> (success, no-op) so the subsystem skips gracefully.
    /// If the type matches, reads and stores the full JSON DOM for <see cref="Commit"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Commit path:</b> Calls <see cref="ScenarioSerializer.Deserialize"/> on the
    /// previously cached DOM, injecting entities into the live
    /// <see cref="EntityRepository"/>.
    /// </para>
    /// </summary>
    public sealed class ScenarioLoadDsmHandler : IDsmHandler
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";

        private readonly ScenarioSerializer   _serializer;
        private readonly string               _localTempRoot;
        private readonly EntityRepository?    _world;

        // Cached between PrepareAsync and Commit for the current transaction.
        private JsonObject? _pendingDom;
        private Guid?       _pendingTransactionId;

        /// <param name="serializer">
        /// A pre-built <see cref="ScenarioSerializer"/> configured with the owning subsystem's
        /// component translators and subsystem type string.
        /// </param>
        /// <param name="localTempRoot">
        /// Root of the local staging area where pre-fetched scenario directories land.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        /// <param name="world">
        /// Optional entity repository.  When provided it is used as the deserialization
        /// target when <see cref="DrillSlave"/> passes <c>repo: null</c> from its dispatch
        /// loop.  Pass <c>null</c> in unit/integration tests that supply the repository
        /// directly via the <see cref="Commit"/> <paramref name="repo"/> parameter.
        /// </param>
        public ScenarioLoadDsmHandler(
            ScenarioSerializer  serializer,
            string              localTempRoot = DefaultLocalTempRoot,
            EntityRepository?   world         = null)
        {
            _serializer    = serializer   ?? throw new ArgumentNullException(nameof(serializer));
            _localTempRoot = string.IsNullOrWhiteSpace(localTempRoot) ? DefaultLocalTempRoot : localTempRoot;
            _world         = world;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType op) => op == NodeOpType.PrepareLive;

        /// <inheritdoc />
        /// <remarks>
        /// Locates and parses the scenario file; caches the DOM if the subsystem type matches.
        /// Returns <see langword="null"/> (success) on SubsystemType mismatch — the subsystem
        /// simply has no file to load for this scenario.
        /// </remarks>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            _pendingDom           = null;
            _pendingTransactionId = null;

            var scenarioId = ParseScenarioId(cmd.PayloadJson);
            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                // No scenario context — nothing to load (e.g. warm start without scenario).
                return Task.FromResult<string?>(null);
            }

            // Convention: each subsystem writes its own file named after the subsystem type.
            // The ScenarioSerializer's subsystem type is internal; rely on the file existing by
            // the agreed pattern: <local_temp>/<scenarioId>/<SubsystemType>.json.
            // We discover the right file by peeking the Header of each candidate.

            var scenarioDir = Path.Combine(_localTempRoot, scenarioId);
            if (!Directory.Exists(scenarioDir))
            {
                FdpLog<ScenarioLoadDsmHandler>.Info(
                    "[SimHost] ScenarioLoadDsmHandler.PrepareAsync: directory '{0}' not found — skipping.",
                    scenarioDir);
                return Task.FromResult<string?>(null);
            }

            foreach (var filePath in Directory.GetFiles(scenarioDir, "*.json"))
            {
                try
                {
                    var text  = File.ReadAllText(filePath);
                    var dom   = JsonNode.Parse(text)?.AsObject();
                    if (dom == null) continue;

                    // Peek header — let the serializer decide on subsystem match.
                    // We detect match by calling Deserialize on a null repo (dry-run is not
                    // supported). Instead: manually peek SubsystemType vs serializer type.
                    var headerNode  = dom["Header"]?.AsObject();
                    var subsysType  = headerNode?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    // Match found — store the DOM for Commit.
                    _pendingDom           = dom;
                    _pendingTransactionId = cmd.TransactionId;
                    FdpLog<ScenarioLoadDsmHandler>.Info(
                        "[SimHost] ScenarioLoadDsmHandler.PrepareAsync: queued '{0}' for load.", filePath);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<ScenarioLoadDsmHandler>.Warn(
                        "[SimHost] ScenarioLoadDsmHandler.PrepareAsync: failed to peek '{0}': {1}",
                        filePath, ex.Message);
                }
            }

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Deserializes entities from the DOM cached by <see cref="PrepareAsync"/> into
        /// <paramref name="repo"/>.  No-ops when there is no pending DOM (SubsystemType
        /// mismatch or no scenario context).
        /// </remarks>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (_pendingDom == null || _pendingTransactionId != cmd.TransactionId) return;

            var targetRepo = repo ?? _world;
            if (targetRepo == null)
            {
                FdpLog<ScenarioLoadDsmHandler>.Warn(
                    "[SimHost] ScenarioLoadDsmHandler.Commit: EntityRepository is null — cannot deserialize entities.");
                _pendingDom           = null;
                _pendingTransactionId = null;
                return;
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom);
                FdpLog<ScenarioLoadDsmHandler>.Info(
                    "[SimHost] ScenarioLoadDsmHandler.Commit: entities deserialized successfully.");
            }
            catch (Exception ex)
            {
                FdpLog<ScenarioLoadDsmHandler>.Error(
                    "[SimHost] ScenarioLoadDsmHandler.Commit: Deserialize failed: {0}", ex.Message);
                throw;
            }
            finally
            {
                _pendingDom           = null;
                _pendingTransactionId = null;
            }
        }

        /// <inheritdoc />
        public void Abort(NodeOpCommand cmd, EntityRepository? repo)
        {
            _pendingDom           = null;
            _pendingTransactionId = null;
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
