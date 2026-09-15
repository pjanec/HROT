using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager (Saga) that handles NAS storage pulls for SerializeLocal operations.
/// Reacts to <see cref="ClusterOpCompletedEvent"/> carrying aggregated file manifests
/// and coordinates the pull to NAS via <see cref="StorageGatewayModule"/>.
/// Handles the ExportArchive NAS pull path, distinguished via
/// <see cref="ExportArchiveBegunEvent"/> published by <see cref="ClusterMaster"/>,
/// and the SaveScenarioJson pull+merge path.
/// </summary>
public sealed class StorageProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;

    // Archive export contexts keyed by the SerializeLocal transaction ID.
    // Set via ExportArchiveBegunEvent; consumed when the matching ClusterOpCompletedEvent arrives.
    private readonly Dictionary<Guid, (Guid ArchiveRequestId, CancellationTokenSource Cts)>
        _pendingArchiveExports = new();

    // CE-277(c2): SerializeLocal tx id → scenario name, for the SaveScenarioJson pull+merge path.
    private readonly Dictionary<Guid, string> _pendingSaveScenarioJson = new();

    /// <summary>The <c>$meta.docType</c> our hosts stamp on a scenario slice — matches
    /// <c>HrotScenarioSaveHandler.ScenarioDocType</c>. A slice with any other tag is foreign (e.g. ExCon).</summary>
    private const string ScenarioDocType = "Hrot.Scenario";

    /// <param name="bus">Shared event bus.</param>
    /// <param name="gateway">Storage gateway for NAS pull operations.</param>
    /// <param name="nasBasePath">Root directory on the NAS for scenario files.</param>
    public StorageProcessManager(
        FdpEventBus bus,
        StorageGatewayModule gateway,
        string nasBasePath)
    {
        _bus         = bus         ?? throw new ArgumentNullException(nameof(bus));
        _gateway     = gateway     ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath = nasBasePath ?? throw new ArgumentNullException(nameof(nasBasePath));
    }

    /// <summary>
    /// Checks for <see cref="ClusterOpCompletedEvent"/> with aggregated manifest payloads
    /// and initiates NAS pulls. Call once per frame in Phase 3, after
    /// <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        // Capture archive export contexts so we can route ClusterOpCompletedEvent correctly.
        foreach (var aev in _bus.ReadManaged<ExportArchiveBegunEvent>())
            _pendingArchiveExports[aev.TransactionId] = (aev.ArchiveRequestId, aev.Cts);

        // CE-277(c2): map SaveScenarioJson fan-out tx → scenario name for the pull+merge path.
        foreach (var jev in _bus.ReadManaged<SaveScenarioJsonBegunEvent>())
            _pendingSaveScenarioJson[jev.TransactionId] = jev.ScenarioName;

        // ImportArchive: prefetch files from NAS to per-node staging directories.
        foreach (var iev in _bus.ReadManaged<ImportArchiveBegunEvent>())
        {
            var importRequestId = iev.RequestId;
            _ = _gateway.PrefetchArchiveAsync(iev.ExerciseId, iev.Targets, _nasBasePath, iev.Cts.Token)
                .ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = importRequestId,
                            StatusCode = OrchestrationStatusCode.Rejected,
                        });
                    }
                    else if (t.IsFaulted)
                    {
                        FdpLog<StorageProcessManager>.Error(
                            "[StorageProcessManager] ImportArchive NAS prefetch failed: {0}",
                            t.Exception?.GetBaseException().Message ?? "unknown error");
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = importRequestId,
                            StatusCode = OrchestrationStatusCode.Rejected,
                        });
                    }
                    else
                    {
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = importRequestId,
                            StatusCode = OrchestrationStatusCode.Success,
                        });
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
        }

        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.StatusCode != OrchestrationStatusCode.Success) continue;
            if (ev.ResultPayload is not List<FileManifestEntry> manifest) continue;
            if (manifest.Count == 0) continue;

            // ExportArchive path: use CTS and publish final status for the archive request ID.
            if (_pendingArchiveExports.TryGetValue(ev.RequestId, out var archCtx))
            {
                _pendingArchiveExports.Remove(ev.RequestId);
                var archRequestId = archCtx.ArchiveRequestId;
                var archCts       = archCtx.Cts;

                _ = _gateway.PullToNasAsync(manifest, _nasBasePath, archCts.Token)
                    .ContinueWith(pullTask =>
                    {
                        if (pullTask.IsCanceled)
                        {
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Rejected,
                            });
                        }
                        else if (pullTask.IsFaulted)
                        {
                            FdpLog<StorageProcessManager>.Error(
                                "[StorageProcessManager] ExportArchive NAS pull failed: {0}",
                                pullTask.Exception?.GetBaseException().Message ?? "unknown error");
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Rejected,
                            });
                        }
                        else if (pullTask.Result.IsFullSuccess)
                        {
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Success,
                            });
                        }
                        else
                        {
                            FdpLog<StorageProcessManager>.Error(
                                "[StorageProcessManager] ExportArchive NAS pull partial failure: {0} file(s) failed",
                                pullTask.Result.FailureCount);
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Rejected,
                            });
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
                continue;
            }

            // CE-277(c2) SaveScenarioJson path: pull the per-node slices to NAS, then merge the
            // format-compatible ones into the one canonical scenario.json and route the foreign ones.
            if (_pendingSaveScenarioJson.TryGetValue(ev.RequestId, out var scenarioName))
            {
                _pendingSaveScenarioJson.Remove(ev.RequestId);
                var scnManifest = new List<FileManifestEntry>(manifest);
                _ = _gateway.PullToNasAsync(scnManifest, _nasBasePath)
                    .ContinueWith(pullTask =>
                    {
                        if (pullTask.IsCompletedSuccessfully && pullTask.Result.IsFullSuccess)
                        {
                            try { MergeScenarioSlices(scenarioName, scnManifest); }
                            catch (Exception ex)
                            {
                                FdpLog<StorageProcessManager>.Error(
                                    "[StorageProcessManager] scenario merge '{0}' failed: {1}",
                                    scenarioName, ex.Message);
                            }
                        }
                        else if (pullTask.IsFaulted)
                        {
                            FdpLog<StorageProcessManager>.Error(
                                "[StorageProcessManager] SaveScenario (JSON) NAS pull failed: {0}",
                                pullTask.Exception?.GetBaseException().Message ?? "unknown error");
                        }
                        else if (pullTask.IsCompletedSuccessfully)
                        {
                            FdpLog<StorageProcessManager>.Error(
                                "[StorageProcessManager] SaveScenario (JSON) NAS pull partial failure: {0} file(s) failed",
                                pullTask.Result.FailureCount);
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
                continue;
            }

            // CE-278: the SaveScenario=2 pull path (prepend the orchestrator Orchestrator.json entry, pull
            // to NAS, then WriteScenarioManifestAsync) is retired. Export and SaveScenarioJson are the only
            // manifest-carrying completions handled here now; a non-matching completion is ignored.
        }
    }

    /// <summary>
    /// CE-277(c2) — after the pull, combine the format-compatible per-node slices into the one canonical
    /// <c>scenarios/&lt;name&gt;/scenario.json</c> via <see cref="ScenarioMergeCore"/>, keep the foreign slices
    /// under <c>foreign/</c> for load-side push-back, and clean up the staging <c>.slices/</c>.
    /// </summary>
    private void MergeScenarioSlices(string scenarioName, List<FileManifestEntry> manifest)
    {
        var scenarioDir = Path.Combine(_nasBasePath, OrchestrationConstants.ScenariosDirectoryName, scenarioName);
        var slicesDir   = Path.Combine(scenarioDir, ".slices");
        var foreignDir  = Path.Combine(scenarioDir, "foreign");

        var slices       = new List<ScenarioSlice>();
        var foreignFiles = new Dictionary<int, string>();   // nodeId → pulled foreign file path

        foreach (var entry in manifest)
        {
            if (string.IsNullOrEmpty(entry.DocType)) continue;  // non-scenario (e.g. .fdp) — not this merge
            var pulledPath = Path.Combine(_nasBasePath, entry.RelativeDest);
            int nodeId     = ParseNodeId(entry.RelativeDest);

            if (string.Equals(entry.DocType, ScenarioDocType, StringComparison.Ordinal))
            {
                if (!File.Exists(pulledPath)) continue;
                if (JsonNode.Parse(File.ReadAllText(pulledPath)) is JsonObject dom)
                    slices.Add(new ScenarioSlice(nodeId, entry.DocType, dom));
            }
            else
            {
                // ⛔ Foreign: never parsed — routed by tag (§4c).
                slices.Add(new ScenarioSlice(nodeId, entry.DocType, null));
                foreignFiles[nodeId] = pulledPath;
            }
        }

        if (slices.Count == 0) return;

        var result = ScenarioMergeCore.Merge(slices, ScenarioDocType);

        if (result.CanonicalDom != null)
        {
            var canonicalFile = Path.Combine(scenarioDir, "scenario.json");
            File.WriteAllText(canonicalFile,
                result.CanonicalDom.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            FdpLog<StorageProcessManager>.Info(
                "[StorageProcessManager] merged {0} slice(s) → '{1}/scenario.json'.", slices.Count, scenarioName);
        }

        if (result.Foreign.Count > 0)
        {
            Directory.CreateDirectory(foreignDir);
            var index = new JsonArray();
            foreach (var f in result.Foreign)
            {
                if (foreignFiles.TryGetValue(f.OriginNodeId, out var src) && File.Exists(src))
                    File.Copy(src, Path.Combine(foreignDir, $"node_{f.OriginNodeId}.json"), overwrite: true);
                index.Add(new JsonObject { ["OriginNodeId"] = f.OriginNodeId, ["DocType"] = f.DocType });
            }
            File.WriteAllText(Path.Combine(foreignDir, "index.json"), index.ToJsonString());
            FdpLog<StorageProcessManager>.Info(
                "[StorageProcessManager] kept {0} foreign slice(s) under '{1}/foreign'.", result.Foreign.Count, scenarioName);
        }

        // The canonical + foreign files are the durable output; the staging slices are transient.
        try { if (Directory.Exists(slicesDir)) Directory.Delete(slicesDir, recursive: true); }
        catch (Exception ex)
        {
            FdpLog<StorageProcessManager>.Warn(
                "[StorageProcessManager] could not clean staging slices for '{0}': {1}", scenarioName, ex.Message);
        }
    }

    /// <summary>Parses the node id from a per-node slice RelativeDest (<c>.../.slices/node_&lt;id&gt;.json</c>).</summary>
    private static int ParseNodeId(string relativeDest)
    {
        var name = Path.GetFileNameWithoutExtension(relativeDest);   // node_<id>
        var us   = name.LastIndexOf('_');
        return us >= 0 && int.TryParse(name.AsSpan(us + 1), out var id) ? id : -1;
    }
}
