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
    private readonly Dictionary<Guid, (Guid ArchiveRequestId, CancellationTokenSource Cts, StorageTransferProgress? Progress)>
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
            _pendingArchiveExports[aev.TransactionId] = (aev.ArchiveRequestId, aev.Cts, aev.Progress);

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
            var importProgress  = iev.Progress;
            importProgress?.SetTotals(iev.Targets.Count, 0);
            _ = _gateway.PrefetchArchiveAsync(iev.ExerciseId, iev.Targets, _nasBasePath, iev.Cts.Token, importProgress)
                .ContinueWith(t =>
                {
                    if (t.IsCanceled)
                        Answer(importRequestId, OrchestrationStatusCode.Rejected, "cancelled");
                    else if (t.IsFaulted)
                    {
                        var why = t.Exception?.GetBaseException().Message ?? "unknown error";
                        FdpLog<StorageProcessManager>.Error("[StorageProcessManager] ImportArchive NAS prefetch failed: {0}", why);
                        Answer(importRequestId, OrchestrationStatusCode.Rejected, "the import failed: " + why);
                    }
                    else if (t.Result.Placement is { Refused: true } refused)
                    {
                        // ⭐ CE-4331 ② (§9a) — no recording fits a node of this cluster: refused, with where it CAN be reviewed
                        FdpLog<StorageProcessManager>.Warn("[StorageProcessManager] ImportArchive: {0}.", refused.Refusal ?? "");
                        Answer(importRequestId, OrchestrationStatusCode.Rejected, refused.Refusal);
                    }
                    else if (t.Result.FailureCount > 0)
                    {
                        // ⭐ CE-4330 / CE-4331 ④ (Recording Storage §9) — the failure count is READ: an import that left a node without its file is not a success
                        var why = $"{t.Result.FailureCount} of {t.Result.SuccessCount + t.Result.FailureCount} node(s) did not get their file";
                        FdpLog<StorageProcessManager>.Error("[StorageProcessManager] ImportArchive: {0}.", why);
                        Answer(importRequestId, OrchestrationStatusCode.Rejected, why);
                    }
                    else
                        Answer(importRequestId, OrchestrationStatusCode.Success, t.Result.Placement);   // ⭐ CE-4331 ③ — what plays where, what does not, who listens
                }, System.Threading.Tasks.TaskScheduler.Default);
        }

        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            // ⭐ CE-4330 ② — an archive whose node round failed, or found no recording, is ANSWERED (it used to wait for ever: its job would hold the queue)
            if (_pendingArchiveExports.TryGetValue(ev.RequestId, out var roundCtx)
                && ev.StatusCode is not (OrchestrationStatusCode.InProgress or OrchestrationStatusCode.Pending)
                && (ev.StatusCode != OrchestrationStatusCode.Success || ev.ResultPayload is not List<FileManifestEntry> { Count: > 0 }))
            {
                _pendingArchiveExports.Remove(ev.RequestId);
                Answer(roundCtx.ArchiveRequestId, OrchestrationStatusCode.Rejected,
                    ev.StatusCode == OrchestrationStatusCode.Success ? "no node holds a recording of this exercise" : "the nodes could not hand over their recordings");
                continue;
            }
            if (ev.StatusCode != OrchestrationStatusCode.Success) continue;
            if (ev.ResultPayload is not List<FileManifestEntry> manifest) continue;
            if (manifest.Count == 0) continue;

            // ExportArchive path: use CTS and publish final status for the archive request ID.
            if (_pendingArchiveExports.TryGetValue(ev.RequestId, out var archCtx))
            {
                _pendingArchiveExports.Remove(ev.RequestId);
                var archRequestId = archCtx.ArchiveRequestId;
                var archCts       = archCtx.Cts;

                _ = _gateway.PullToNasAsync(manifest, _nasBasePath, archCts.Token, archCtx.Progress)
                    .ContinueWith(pullTask =>
                    {
                        if (pullTask.IsCanceled)
                            Answer(archRequestId, OrchestrationStatusCode.Rejected, "cancelled");
                        else if (pullTask.IsFaulted)
                        {
                            var why = pullTask.Exception?.GetBaseException().Message ?? "unknown error";
                            FdpLog<StorageProcessManager>.Error("[StorageProcessManager] ExportArchive NAS pull failed: {0}", why);
                            Answer(archRequestId, OrchestrationStatusCode.Rejected, "the archive failed: " + why);
                        }
                        else if (pullTask.Result.IsFullSuccess)
                            Answer(archRequestId, OrchestrationStatusCode.Success, PulledFiles(manifest));   // ⭐ CE-4330 ① — the manifest written LAST lists these (AssetInventoryProcessManager)
                        else
                        {
                            var why = $"{pullTask.Result.FailureCount} of {manifest.Count} file(s) could not be copied";
                            FdpLog<StorageProcessManager>.Error("[StorageProcessManager] ExportArchive NAS pull partial failure: {0}", why);
                            Answer(archRequestId, OrchestrationStatusCode.Rejected, why);
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
    /// ⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18c) — right after the canonical scenario is written: compile its terrain blocks / features into
    /// <c>&lt;scenario&gt;/terrain-overlay/</c> (the ONE <see cref="Fdp.Toolkit.Terrain.OverlayCompiler"/>; no blocks ⇒ a stale overlay is removed). ⚠ A failure is LOGGED, never thrown:
    /// the scenario save itself succeeded (the merge returns no result to carry it); the next prefetch would then stage no overlay for a scenario that has blocks, which the node's load
    /// reports as a missing overlay.
    /// </summary>
    private void CompileTerrainOverlay(JsonObject canonical, string scenarioDir, string scenarioName)
    {
        try
        {
            var terrains = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(localStagingRoot: null, nasRoot: _nasBasePath);
            var r = Fdp.Toolkit.Terrain.OverlayCompiler.Compile(canonical, scenarioDir, terrains.ResolveDefinition, unique: new Hrot.Terrain.Random.SynthUniqueBuildings());   // ⭐ T-3 part 2 — the same generator the editor's live overlay uses
            if (r.Wrote && r.Folder != null && r.ContentHash != null)
            {
                // ⭐ O-8 (CE-4420) — also kept BY HASH beside the terrains on the NAS ({nas}/assets/terrain/overlays/<hash>), so a recording that names the overlay can load it later
                //   (the Replay Browser's catalog reads the NAS root), long after the scenario was edited again
                new Fdp.Toolkit.Orchestration.Assets.AssetTreeSync().Sync(r.Folder, Path.Combine(ContentLayout.TerrainAssets(_nasBasePath),
                    Fdp.Toolkit.Terrain.TerrainCatalog.OverlaysDirectoryName, r.ContentHash));
            }
            if (r.Wrote)
                FdpLog<StorageProcessManager>.Info(
                    $"[StorageProcessManager] compiled the terrain overlay of '{scenarioName}': {r.Blocks} block(s), {r.Houses} house(s), {r.Woods} wood(s), hash {r.ContentHash}.");
        }
        catch (Exception ex)
        {
            FdpLog<StorageProcessManager>.Error("[StorageProcessManager] could not compile the terrain overlay of '{0}' (the scenario itself was saved): {1}", scenarioName, ex.Message);
            // ⭐ CE-4427 R-1 — the OLD overlay must not outlive a failed compile: it was built from the scenario BEFORE this edit and the next prefetch would distribute (and every node merge) blocks that are no longer
            //   there. No overlay at all is the honest state (the load of a scenario whose blocks have no overlay says so); a stale one is silently wrong.
            DeleteOverlayFolders(scenarioDir, scenarioName);
        }
    }

    private static void DeleteOverlayFolders(string scenarioDir, string scenarioName)
    {
        string final = Path.Combine(scenarioDir, Fdp.Toolkit.Terrain.OverlayCompiler.FolderName);
        foreach (var dir in new[] { final, final + ".part" })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception ex) { FdpLog<StorageProcessManager>.Error("[StorageProcessManager] could not remove the stale terrain overlay folder '{0}' of '{1}': {2}", dir, scenarioName, ex.Message); }
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
            ["entities"] = new JsonObject(),   // ⭐ CE-4363: an OBJECT keyed by entity id — ScenarioSerializer refuses an array (the blank scenario never loaded)
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
            CompileTerrainOverlay(result.CanonicalDom, scenarioDir, scenarioName);
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

    private void Answer(Guid requestId, OrchestrationStatusCode code, object? payload)
        => _bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = requestId, StatusCode = code, ResultPayload = payload });

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
