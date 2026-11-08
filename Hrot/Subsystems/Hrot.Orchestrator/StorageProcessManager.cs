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
using Fdp.Core.Serialization;

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

    // ⭐ CE-4099 — the subset of those rounds that are AUTOSAVES.
    private readonly HashSet<Guid> _pendingAutosaves = new();


    /// <summary>The wall clock an autosave's meta stamps (<c>at</c>); a rail sets it.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

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
        {
            _pendingSaveScenarioJson[jev.TransactionId] = jev.ScenarioName;
            if (jev.Autosave) _pendingAutosaves.Add(jev.TransactionId);
        }

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
                                RequestId     = archRequestId,
                                StatusCode    = OrchestrationStatusCode.Success,
                                ResultPayload = PulledFiles(manifest),   // ⭐ CE-4330 ① — the manifest written LAST lists these (AssetInventoryProcessManager)
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
                var saveTxId = ev.RequestId;
                var scnManifest = new List<FileManifestEntry>(manifest);
                if (_pendingAutosaves.Remove(saveTxId))
                {
                    // ⭐ CE-4099 — an AUTOSAVE: merged into .autosave/, a failure only logged (A3: never a 409, never a toast, the master's unsaved fact untouched)
                    _ = _gateway.PullToNasAsync(scnManifest, _nasBasePath)
                        .ContinueWith(pullTask =>
                        {
                            if (pullTask.IsCompletedSuccessfully && pullTask.Result.IsFullSuccess)
                            {
                                try { MergeAutosaveSlices(scenarioName, scnManifest); }
                                catch (Exception ex) { FdpLog<StorageProcessManager>.Warn("[StorageProcessManager] autosave merge '{0}' failed: {1}", scenarioName, ex.Message); }
                            }
                            else FdpLog<StorageProcessManager>.Warn("[StorageProcessManager] autosave pull '{0}' failed — skipped; the next interval takes it.", scenarioName);
                        }, System.Threading.Tasks.TaskScheduler.Default);
                    continue;
                }
                _ = _gateway.PullToNasAsync(scnManifest, _nasBasePath)
                    .ContinueWith(pullTask =>
                    {
                        bool saved = false;   // ⭐ CE-4093 — the master learns whether the file on disk is current
                        if (pullTask.IsCompletedSuccessfully && pullTask.Result.IsFullSuccess)
                        {
                            try { MergeScenarioSlices(scenarioName, scnManifest); saved = true; DeleteAutosave(scenarioName); }   // ⭐ CE-4099 A6 — a completed save makes the autosave obsolete
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
                        _bus.PublishManaged(new ScenarioSaveFinishedEvent { TransactionId = saveTxId, Succeeded = saved });
                    }, System.Threading.Tasks.TaskScheduler.Default);
                continue;
            }

            // CE-278: the SaveScenario=2 pull path (prepend the orchestrator Orchestrator.json entry, pull
            // to NAS, then WriteScenarioManifestAsync) is retired. Export and SaveScenarioJson are the only
            // manifest-carrying completions handled here now; a non-matching completion is ignored.
        }
    }

    /// <summary>
    /// ⭐ New scenario N2 (📄 docs/UX/DESIGN_New_Scenario_And_Recipe.md §2.2, R-4135) — writes a BLANK scenario: a header naming
    /// <paramref name="terrainName"/>, no entities (the shape of the shipped <c>basic-desert</c> recipe), at the folder every load of
    /// <paramref name="scenarioName"/> resolves (<see cref="ContentLayout.ResolveScenarioDirectory"/> under this master's NAS — the same place a save
    /// merges into). Returns <see langword="null"/> when written, else the refusal: ⛔ a scenario that exists already (never overwritten — the
    /// picker's Save-As asks Replace first), ⛔ a terrain this master's catalog does not list (named with the ones it does).
    /// </summary>
    public string? WriteBlankScenario(string scenarioName, string terrainName)
    {
        string dir;
        try { dir = ContentLayout.ResolveScenarioDirectory(_nasBasePath, scenarioName); }
        catch (ArgumentException ex) { return ex.Message; }
        var file = Path.Combine(dir, ContentLayout.ScenarioFileName);
        if (File.Exists(file)) return $"a scenario named '{scenarioName}' already exists";

        var terrains = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(localStagingRoot: null, nasRoot: _nasBasePath);   // ⭐ the catalog the master's load reads the origin from
        string? found;
        try { found = terrains.ResolveDefinition(terrainName); }
        catch (ArgumentException ex) { return ex.Message; }
        if (found == null)
        {
            var known = terrains.List();
            return $"unknown terrain '{terrainName}' — the terrains are: {(known.Count == 0 ? "(none)" : string.Join(", ", known))}";
        }

        var doc = new JsonObject
        {
            ["$meta"] = new JsonObject { ["docType"] = ScenarioDocType, ["schemaVersion"] = 1 },
            ["header"] = new JsonObject
            {
                ["subsystemType"] = ScenarioDocType,
                ["schemaVersion"] = "1.0",
                ["terrainName"] = terrainName.Replace('\\', '/').Trim('/'),
            },
            ["entities"] = new JsonArray(),
        };
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException ex) { return $"the scenario could not be written: {ex.Message}"; }
        catch (UnauthorizedAccessException ex) { return $"the scenario could not be written: {ex.Message}"; }
        FdpLog<StorageProcessManager>.Info("[StorageProcessManager] N2: blank scenario '{0}' on '{1}' → '{2}'.", scenarioName, terrainName, file);
        return null;
    }

    /// <summary>
    /// CE-277(c2) — after the pull, combine the format-compatible per-node slices into the one canonical
    /// <c>scenarios/&lt;name&gt;/scenario.json</c> via <see cref="ScenarioMergeCore"/>, keep the foreign slices
    /// under <c>foreign/</c> for load-side push-back, and clean up the staging <c>.slices/</c>.
    /// </summary>
    internal void MergeScenarioSlices(string scenarioName, List<FileManifestEntry> manifest)   // ⭐ CE-4210 MA-4 — internal so a rail drives the merge without the bus
    {
        var scenarioDir = Fdp.Toolkit.Orchestration.ContentLayout.ResolveScenarioDirectory(_nasBasePath, scenarioName);
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
                    slices.Add(new ScenarioSlice((Fdp.Core.NodeId)(nodeId), entry.DocType, dom));
            }
            else
            {
                // ⛔ Foreign: never parsed — routed by tag (§4c).
                slices.Add(new ScenarioSlice((Fdp.Core.NodeId)(nodeId), entry.DocType, null));
                foreignFiles[nodeId] = pulledPath;
            }
        }

        if (slices.Count == 0) return;

        var result = ScenarioMergeCore.Merge(slices, ScenarioDocType);

        if (result.CanonicalDom != null)
        {
            var canonicalFile = Path.Combine(scenarioDir, ContentLayout.ScenarioFileName);
            File.WriteAllText(canonicalFile,
                result.CanonicalDom.ToJsonString(FdpJsonOptionsRegistry.DefaultRelaxed));
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
                index.Add(new JsonObject { ["OriginNodeId"] = f.OriginNodeId.Value, ["DocType"] = f.DocType });
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

    /// <summary>
    /// ⭐ CE-4099 (AQ108 §4c A5) — an autosave's slices merged by the SAME core into <c>&lt;scenario&gt;/.autosave/autosave.json</c>, with
    /// <c>autosave.meta.json {at, savedHash}</c>: <c>savedHash</c> = the hash of the <c>scenario.json</c> it was based on (A7: a recovery is offered only
    /// while that file is unchanged — saved elsewhere since ⇒ stale, reported, not offered). Foreign slices are not kept for an autosave.
    /// </summary>
    internal void MergeAutosaveSlices(string scenarioName, List<FileManifestEntry> manifest)
    {
        var scenarioDir = ContentLayout.ResolveScenarioDirectory(_nasBasePath, scenarioName);
        var autosaveDir = Path.Combine(scenarioDir, AutosaveFiles.Folder);
        var slices = new List<ScenarioSlice>();
        foreach (var entry in manifest)
        {
            if (!string.Equals(entry.DocType, ScenarioDocType, StringComparison.Ordinal)) continue;
            var pulledPath = Path.Combine(_nasBasePath, entry.RelativeDest);
            if (File.Exists(pulledPath) && JsonNode.Parse(File.ReadAllText(pulledPath)) is JsonObject dom)
                slices.Add(new ScenarioSlice((Fdp.Core.NodeId)(ParseNodeId(entry.RelativeDest)), ScenarioDocType, dom));
        }
        if (slices.Count == 0) return;
        var result = ScenarioMergeCore.Merge(slices, ScenarioDocType);
        if (result.CanonicalDom == null) return;
        Directory.CreateDirectory(autosaveDir);
        File.WriteAllText(Path.Combine(autosaveDir, AutosaveFiles.DataFile), result.CanonicalDom.ToJsonString(FdpJsonOptionsRegistry.DefaultRelaxed));
        var saved = Path.Combine(scenarioDir, ContentLayout.ScenarioFileName);
        var meta = new JsonObject
        {
            ["at"] = UtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["savedHash"] = File.Exists(saved) ? Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(File.ReadAllText(saved)) : "",
        };
        File.WriteAllText(Path.Combine(autosaveDir, AutosaveFiles.MetaFile), meta.ToJsonString());
        try { Directory.Delete(Path.Combine(autosaveDir, ".slices"), recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        FdpLog<StorageProcessManager>.Info("[StorageProcessManager] autosaved '{0}' ({1} slice(s)).", scenarioName, slices.Count);
    }

    /// <summary>⭐ CE-4099 (AQ108 §4c A6) — a real save completed: the scenario's autosave is obsolete.</summary>
    internal void DeleteAutosave(string scenarioName)
    {
        var dir = AutosaveFiles.DirectoryOf(_nasBasePath, scenarioName);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { FdpLog<StorageProcessManager>.Warn("[StorageProcessManager] could not delete the autosave of '{0}': {1}", scenarioName, ex.Message); }
    }

    /// <summary>
    /// ⭐ CE-4330 ① — the files a full archive pull put on the NAS, by name inside the exercise folder, each with the size it has THERE (the bytes the
    /// manifest vouches for).
    /// </summary>
    internal ArchivePullResult PulledFiles(List<FileManifestEntry> manifest)
    {
        var files = new List<ArchivedFile>(manifest.Count);
        foreach (var entry in manifest)
        {
            var path = Path.Combine(_nasBasePath, entry.RelativeDest);
            if (File.Exists(path)) files.Add(new ArchivedFile(Path.GetFileName(path), new FileInfo(path).Length));
        }
        return new ArchivePullResult(files);
    }

    /// <summary>Parses the node id from a per-node slice RelativeDest (<c>.../.slices/node_&lt;id&gt;.json</c>).</summary>
    private static int ParseNodeId(string relativeDest)
    {
        var name = Path.GetFileNameWithoutExtension(relativeDest);   // node_<id>
        var us   = name.LastIndexOf('_');
        return us >= 0 && int.TryParse(name.AsSpan(us + 1), out var id) ? id : -1;
    }
}
