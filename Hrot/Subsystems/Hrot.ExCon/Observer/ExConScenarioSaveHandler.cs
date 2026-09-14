using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.ExCon.Observer;

/// <summary>
/// ⭐⭐⭐ CE-277(c3) — ExCon's save handler for the distributed scenario save. ExCon is a fan-out node with no
/// ECS world, so instead of an entity slice it writes its <see cref="ExConObserverState"/> in an
/// <b>intentionally-incompatible</b> format (<c>$meta.docType = "ExCon.Observer"</c>). The orchestrator's merge
/// classifies it foreign by that tag and routes it to <c>foreign/</c> verbatim — never merging it into the ECS
/// <c>scenario.json</c> (§4c). Mirrors <c>HrotScenarioSaveHandler</c>: write to per-node staging, report a
/// <see cref="FileManifestResult"/> so the orchestrator pulls it.
/// </summary>
public sealed class ExConScenarioSaveHandler : IClusterStateHandler
{
    /// <summary>The deliberately-foreign format tag — NOT <c>Hrot.Scenario</c>.</summary>
    public const string ObserverDocType = "ExCon.Observer";

    private const string SliceFileName = "scenario.json";

    private readonly ExConObserverState _state;
    private readonly int                _nodeId;

    public ExConScenarioSaveHandler(ExConObserverState state, int nodeId)
    {
        _state  = state ?? throw new ArgumentNullException(nameof(state));
        _nodeId = nodeId;
    }

    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;

    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        if (intent.DomainPayload is not ScenarioSaveHandlerPayload payload)
            return Task.FromResult<object?>(null);
        if (string.IsNullOrWhiteSpace(payload.ScenarioName))
            return Task.FromResult<object?>(null);

        var dir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(_nodeId), payload.ScenarioName);
        Directory.CreateDirectory(dir);
        var localFile = Path.Combine(dir, SliceFileName);

        // A deliberately non-Hrot.Scenario document: our $meta envelope with a FOREIGN docType + fake state.
        var dom = new JsonObject
        {
            ["Observer"] = new JsonObject
            {
                ["CameraX"]        = _state.CameraX,
                ["CameraY"]        = _state.CameraY,
                ["CameraZ"]        = _state.CameraZ,
                ["InstanceMarker"] = _state.InstanceMarker,
            },
        };
        JsonEnvelope.Write(dom, new DocumentMeta(ObserverDocType, 1));
        File.WriteAllText(localFile, dom.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));

        var relativeDest = Path.Combine(
            OrchestrationConstants.ScenariosDirectoryName, payload.ScenarioName, ".slices", $"node_{_nodeId}.json");

        FdpLog<ExConScenarioSaveHandler>.Info(
            "[ExConScenarioSaveHandler] node {0} wrote FOREIGN observer slice '{1}' ({2}).",
            _nodeId, payload.ScenarioName, ObserverDocType);

        return Task.FromResult<object?>(
            new[] { new FileManifestResult(localFile, relativeDest, ObserverDocType) });
    }

    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
}
