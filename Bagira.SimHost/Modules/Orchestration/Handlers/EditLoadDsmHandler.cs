using System;
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
    /// DSM handler that loads a scenario into the SimHost subsystem when the cluster
    /// transitions to <see cref="DSMState.LoadingEdit"/> (CGF1-S0302).
    ///
    /// <para>
    /// <b>Prepare path</b> (<see cref="NodeOpType.PrepareState"/> targeting
    /// <see cref="DSMState.LoadingEdit"/>):
    /// <list type="bullet">
    ///   <item>
    ///     When <c>IsNewScenario = true</c> (or no <c>ScenarioId</c> is present):
    ///     no file I/O — blank world is used on <see cref="Commit"/>.
    ///   </item>
    ///   <item>
    ///     When <c>ScenarioId != null</c>: verifies the pre-fetched scenario directory
    ///     exists under <c>localTempRoot\scenarioId\</c> and caches the matching JSON
    ///     DOM for <see cref="Commit"/>.  Throws <see cref="InvalidOperationException"/>
    ///     when the directory or a matching file is absent.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Commit path:</b> Spawns entities from the cached DOM via
    /// <see cref="ScenarioSerializer.Deserialize"/> into the live
    /// <see cref="EntityRepository"/>.  For new scenarios the world is left blank.
    /// </para>
    ///
    /// <para>
    /// <b>Schema</b>: Uses the <see cref="ScenarioSerializer"/> DOM format (same as
    /// <see cref="ScenarioLoadDsmHandler"/> for <c>PrepareLive</c>).  Files follow the
    /// naming convention <c>&lt;subsystemType&gt;.json</c> within the scenario directory.
    /// </para>
    ///
    /// <para>
    /// I/O in <see cref="PrepareAsync"/> is performed synchronously to ensure the DOM
    /// is immediately available when <see cref="DrillSlave"/> calls <see cref="Commit"/>
    /// right after (see <c>DrillSlave.DispatchCommand</c> fire-and-forget pattern).
    /// </para>
    /// </summary>
    public sealed class EditLoadDsmHandler : IDsmHandler
    {
        private const string DefaultLocalTempRoot = @"C:\FDP_Temp";

        private readonly ScenarioSerializer  _serializer;
        private readonly string              _localTempRoot;
        private readonly EntityRepository?   _world;

        // Cached between PrepareAsync and Commit for the current transaction.
        private JsonObject? _pendingDom;
        private Guid?       _pendingTransactionId;
        private bool        _pendingIsNew;

        /// <param name="serializer">
        /// Pre-built <see cref="ScenarioSerializer"/> configured with the owning
        /// subsystem's component translators and subsystem type string.
        /// </param>
        /// <param name="localTempRoot">
        /// Root of the local staging area where pre-fetched scenario directories land.
        /// Defaults to <c>C:\FDP_Temp</c>.
        /// </param>
        /// <param name="world">
        /// Optional entity repository used as the deserialization target when
        /// <see cref="DrillSlave"/> passes <c>repo: null</c> from its dispatch loop.
        /// Pass <c>null</c> in unit tests that supply the repository directly via the
        /// <see cref="Commit"/> <paramref name="repo"/> parameter.
        /// </param>
        public EditLoadDsmHandler(
            ScenarioSerializer serializer,
            string             localTempRoot = DefaultLocalTempRoot,
            EntityRepository?  world         = null)
        {
            _serializer    = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _localTempRoot = string.IsNullOrWhiteSpace(localTempRoot) ? DefaultLocalTempRoot : localTempRoot;
            _world         = world;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Returns <see langword="true"/> for <see cref="NodeOpType.PrepareState"/>.
        /// Within <see cref="PrepareAsync"/> and <see cref="Commit"/> the handler acts
        /// only when the payload encodes <see cref="DSMState.LoadingEdit"/>; all other
        /// <c>PrepareState</c> payloads are no-ops.
        /// </remarks>
        public bool CanHandle(NodeOpType op) => op == NodeOpType.PrepareState;

        /// <inheritdoc />
        /// <remarks>
        /// Parses <c>IsNewScenario</c> / <c>ScenarioId</c> from the payload.  For
        /// existing scenarios, locates and caches the matching pre-fetched JSON DOM.
        /// Throws <see cref="InvalidOperationException"/> when a scenario file is
        /// required but absent.
        /// </remarks>
        public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        {
            _pendingDom           = null;
            _pendingTransactionId = null;
            _pendingIsNew         = false;

            // Only act on PrepareState targeting LoadingEdit.
            var targetState = ParseTargetState(cmd.PayloadJson);
            if (targetState != DSMState.LoadingEdit)
                return Task.FromResult<string?>(null);

            var isNew      = ParseIsNewScenario(cmd.PayloadJson);
            var scenarioId = ParseScenarioId(cmd.PayloadJson);

            _pendingTransactionId = cmd.TransactionId;
            _pendingIsNew         = isNew;

            if (isNew || string.IsNullOrWhiteSpace(scenarioId))
            {
                // New scenario: blank world — no file I/O needed.
                FdpLog<EditLoadDsmHandler>.Info(
                    "[SimHost] EditLoadDsmHandler.PrepareAsync: new scenario, blank world.");
                return Task.FromResult<string?>(null);
            }

            // Existing scenario: locate and cache the pre-fetched file.
            var scenarioDir = Path.Combine(_localTempRoot, scenarioId);
            if (!Directory.Exists(scenarioDir))
                throw new InvalidOperationException(
                    $"[SimHost] EditLoadDsmHandler: scenario directory '{scenarioDir}' " +
                    $"not found. Ensure PrefetchScenario completed before LoadingEdit.");

            foreach (var filePath in Directory.GetFiles(scenarioDir, "*.json"))
            {
                try
                {
                    var text = File.ReadAllText(filePath);
                    var dom  = JsonNode.Parse(text)?.AsObject();
                    if (dom == null) continue;

                    var headerNode = dom["Header"]?.AsObject();
                    var subsysType = headerNode?["SubsystemType"]?.GetValue<string>();
                    if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

                    _pendingDom = dom;
                    FdpLog<EditLoadDsmHandler>.Info(
                        "[SimHost] EditLoadDsmHandler.PrepareAsync: queued '{0}' for edit-load.",
                        filePath);
                    break;
                }
                catch (Exception ex)
                {
                    FdpLog<EditLoadDsmHandler>.Warn(
                        "[SimHost] EditLoadDsmHandler.PrepareAsync: failed to peek '{0}': {1}",
                        filePath, ex.Message);
                }
            }

            if (_pendingDom == null)
                throw new InvalidOperationException(
                    $"[SimHost] EditLoadDsmHandler: no matching scenario file found in " +
                    $"'{scenarioDir}' for this subsystem. ScenarioId='{scenarioId}'.");

            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Deserializes entities from the cached DOM into the repository
        /// (<paramref name="repo"/> if non-null, otherwise the injected
        /// <see cref="_world"/>).  No-ops for unrelated <c>PrepareState</c> targets or
        /// when the transaction ID does not match the prepared transaction.
        /// </remarks>
        public void Commit(NodeOpCommand cmd, EntityRepository? repo)
        {
            if (_pendingTransactionId != cmd.TransactionId) return;

            if (_pendingIsNew || _pendingDom == null)
            {
                // New scenario — leave world blank.
                FdpLog<EditLoadDsmHandler>.Info(
                    "[SimHost] EditLoadDsmHandler.Commit: blank-world scenario committed.");
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
                    "[SimHost] EditLoadDsmHandler.Commit: EntityRepository is null but scenario " +
                    "deserialization is required. Ensure a valid world is injected via the " +
                    "constructor or passed through the repo parameter.");
            }

            try
            {
                _serializer.Deserialize(targetRepo, _pendingDom);
                FdpLog<EditLoadDsmHandler>.Info(
                    "[SimHost] EditLoadDsmHandler.Commit: entities deserialized successfully.");
            }
            catch (Exception ex)
            {
                FdpLog<EditLoadDsmHandler>.Error(
                    "[SimHost] EditLoadDsmHandler.Commit: deserialization failed: {0}", ex.Message);
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
            _pendingIsNew         = false;
        }

        private static DSMState ParseTargetState(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return DSMState.Standby;
            if (int.TryParse(payloadJson, out var n)) return (DSMState)n;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("TargetState", out var prop))
                    return (DSMState)prop.GetInt32();
            }
            catch { /* malformed payload — treat as Standby */ }
            return DSMState.Standby;
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
            catch { /* malformed payload — default false */ }
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
