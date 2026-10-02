using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.ExCon.Observer;

/// <summary>
/// ⭐⭐⭐ CE-280 — ExCon's LOAD-side counterpart to <see cref="ExConScenarioSaveHandler"/>. On a scenario
/// load the orchestrator routes ExCon's format-incompatible <c>foreign/</c> slice (docType
/// <c>ExCon.Observer</c>) back to ExCon's per-node staging (see
/// <c>StorageGatewayModule.PrefetchScenarioAsync</c>'s foreign-routing arm), then fans out
/// <see cref="NodeOpType.PrefetchFiles"/>. This handler is ExCon's <b>only</b> <c>PrefetchFiles</c> handler
/// (it SUBSUMES <see cref="ReferencePrefetchHandler"/> — ClusterSlave dispatches first-match-wins), so it does
/// both jobs:
/// <list type="number">
///   <item>ensure the staging directory exists and ACK, exactly like the reference prefetch handler; and</item>
///   <item>if this scenario carried a foreign observer slice for THIS node, restore the camera + marker into
///     the shared <see cref="ExConObserverState"/> and flip <see cref="ExConObserverState.RestoredFromScenario"/>
///     true — the value- and flag-level proof of the round-trip that <c>GET /panels/excon_observer</c> exposes
///     (§4c/§6b T-C).</item>
/// </list>
/// <para>ExCon has no ECS world, so there is nothing entity-shaped to load; the observer state IS ExCon's
/// entire persistable scenario surface, and the prefetch phase is where its file arrives — the natural,
/// lowest-blast-radius hook (no change to the 2PC load sequence).</para>
/// </summary>
public sealed class ExConScenarioLoadHandler : IClusterStateHandler
{
    private readonly ExConObserverState _state;
    private readonly int                _nodeId;
    private readonly string             _stagingRoot;

    private string? _pendingScenarioId;
    private Guid?   _pendingTransactionId;

    /// <param name="state">The shared observer state the panel dumps; restore targets this instance.</param>
    /// <param name="nodeId">ExCon's node id — selects the <c>foreign/node_&lt;id&gt;.json</c> slice for THIS node.</param>
    /// <param name="stagingRoot">Local staging root; defaults to <see cref="OrchestrationConstants.ResolveStagingRoot"/>.</param>
    public ExConScenarioLoadHandler(ExConObserverState state, int nodeId, string? stagingRoot = null)
    {
        _state       = state ?? throw new ArgumentNullException(nameof(state));
        _nodeId      = nodeId;
        _stagingRoot = stagingRoot ?? OrchestrationConstants.ResolveStagingRoot();
    }

    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrefetchFiles;

    public bool CanHandle(ExecuteNodeOpIntent intent) => intent.Operation == NodeOpType.PrefetchFiles;

    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        _pendingScenarioId    = null;
        _pendingTransactionId = null;

        var scenarioId = intent.DomainPayload is PrefetchHandlerPayload p ? p.ScenarioId : null;
        if (string.IsNullOrWhiteSpace(scenarioId))
            return Task.FromResult<object?>(null);

        _pendingScenarioId    = scenarioId;
        _pendingTransactionId = intent.TransactionId;

        // Ensure the per-node staging directory exists (subsumes ReferencePrefetchHandler's job).
        var scenarioDir = Path.Combine(
            OrchestrationConstants.GetNodeScenariosRoot(_stagingRoot, _nodeId), scenarioId);
        Directory.CreateDirectory(scenarioDir);

        // If a foreign observer slice for THIS node was routed here on load, restore it.
        var foreignFile = Path.Combine(scenarioDir, "foreign", $"node_{_nodeId}.json");
        if (File.Exists(foreignFile))
            TryRestoreObserver(foreignFile, scenarioId);

        return Task.FromResult<object?>(null);
    }

    private void TryRestoreObserver(string foreignFile, string scenarioId)
    {
        try
        {
            var dom = JsonNode.Parse(File.ReadAllText(foreignFile)) as JsonObject;
            if (dom?["Observer"] is not JsonObject observer)
            {
                FdpLog<ExConScenarioLoadHandler>.Warn(
                    "[ExConScenarioLoadHandler] node {0} foreign slice for '{1}' has no Observer object — skipping restore.",
                    _nodeId, scenarioId);
                return;
            }

            _state.CameraX        = observer["CameraX"]?.GetValue<float>()  ?? _state.CameraX;
            _state.CameraY        = observer["CameraY"]?.GetValue<float>()  ?? _state.CameraY;
            _state.CameraZ        = observer["CameraZ"]?.GetValue<float>()  ?? _state.CameraZ;
            _state.InstanceMarker = observer["InstanceMarker"]?.GetValue<string>() ?? _state.InstanceMarker;
            _state.RestoredFromScenario = true;

            FdpLog<ExConScenarioLoadHandler>.Info(
                "[ExConScenarioLoadHandler] node {0} restored observer state from '{1}' foreign slice (camera=({2}), marker={3}).",
                _nodeId, scenarioId,
                $"{_state.CameraX},{_state.CameraY},{_state.CameraZ}", _state.InstanceMarker);
        }
        catch (Exception ex)
        {
            FdpLog<ExConScenarioLoadHandler>.Error(
                "[ExConScenarioLoadHandler] node {0} failed to restore observer state from '{1}': {2}",
                _nodeId, foreignFile, ex.Message);
        }
    }

    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        if (_pendingTransactionId != intent.TransactionId) return;
        FdpLog<ExConScenarioLoadHandler>.Info(
            "[ExConScenarioLoadHandler] ACK for scenario '{0}'.", _pendingScenarioId ?? "(null)");
        _pendingScenarioId    = null;
        _pendingTransactionId = null;
    }

    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        _pendingScenarioId    = null;
        _pendingTransactionId = null;
    }
}
