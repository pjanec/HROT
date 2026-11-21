using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager responsible for broadcasting
/// <see cref="AssetInventoryUpdateEvent"/> on the event bus.
///
/// <para>Extracted from <see cref="ClusterMaster"/> so the 2PC orchestration engine has
/// zero knowledge of file systems, NAS paths, or storage gateways (SRP / CGF1-S0506).</para>
/// </summary>
public sealed class AssetInventoryProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;
    private readonly string _ledgerDirectory;
    private readonly string _stagingRoot;
    private DateTime _lastInventoryScan = DateTime.MinValue;
    private readonly TimeSpan _scanInterval;
    private ClusterState _lastState = ClusterState.Idle;
    private string? _pendingScenarioId;
    private RecordingLedgerEntry? _currentRecording;
    // ⭐ CE-4097 (AQ108 D5) — the branch the nodes reported (LiveBranchResult) before the live run's ledger entry exists; consumed by it.
    private (Guid Parent, long Ticks)? _pendingBranch;
    private readonly Dictionary<Guid, Guid> _pendingExports = new();
    private readonly Dictionary<Guid, RecordingLedgerEntry> _unarchivedLedger = new();

    public AssetInventoryProcessManager(
        FdpEventBus bus,
        StorageGatewayModule gateway,
        string nasBasePath,
        string localStagingRoot,
        int nodeId,
        TimeSpan? scanInterval = null)   // default 5 s; a test passes TimeSpan.Zero to scan on every tick
    {
        _scanInterval    = scanInterval ?? TimeSpan.FromSeconds(5);
        _bus              = bus              ?? throw new ArgumentNullException(nameof(bus));
        _gateway          = gateway          ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath      = nasBasePath      ?? throw new ArgumentNullException(nameof(nasBasePath));
        _stagingRoot     = localStagingRoot ?? throw new ArgumentNullException(nameof(localStagingRoot));
        _ledgerDirectory = Path.Combine(
            OrchestrationConstants.GetNodeStagingRoot(localStagingRoot, nodeId),
            OrchestrationConstants.RecordingLedgerDirectoryName);
        HydrateLedger();
    }

    /// <summary>
    /// Tracks local recording state and publishes
    /// <see cref="AssetInventoryUpdateEvent"/> when the interval elapses.
    /// Call once per frame from the Update loop.
    /// </summary>
    public void Tick()
    {
        // ⭐ CE-3410 (AQ108 §0 D28 / D33) — did the live run's clock ever advance? Sampled every frame the cluster was Live (the previous frame's state, so the last Live frame counts).
        //   ⚠ Read as the sim clock RISING frame to frame — ⛔ not GlobalTime.IsAdvancing: the master's time reads (MasterSyncController.GetCurrentState) always report DeltaTime 0,
        //   so every run looked unrun and was dropped (ui's editor rails, NOTE_Ui_To_Backend_CE3410_Drops_Runs_That_Ran). A backward jump (a load's reset, a restore) is not a run.
        if (_lastState == ClusterState.OperatingLive && _currentRecording != null && SimSeconds?.Invoke() is { } sim)
        {
            if (!double.IsNaN(_lastLiveSim) && sim > _lastLiveSim) _liveRan = true;
            _lastLiveSim = sim;
        }

        foreach (var intent in _bus.ReadManaged<TransitionStateIntent>())
        {
            if (intent.TargetState == ClusterState.OperatingLive)
                _pendingScenarioId = intent.ScenarioId;
        }

        foreach (var ev in _bus.ReadManaged<ClusterStateUpdateEvent>())
        {
            if (_lastState != ClusterState.OperatingLive && ev.CurrentState == ClusterState.OperatingLive && ev.RunNotRecorded)
            {
                // ⭐⭐ CE-4090 (AQ108 D14, §5a) — THE ONE GATE AT LEDGER CREATION. A live run started with "Record live runs" off writes no
                //   ledger entry and no scenario copy, so the inventory never offers "AAR the last exercise" / keep / export for a recording
                //   that does not exist (and no empty archive is written). ⛔ Not gated again downstream: every consumer reads this ledger.
                FdpLog<AssetInventoryProcessManager>.Info("[AssetInventory] CE-4090: live run {0} is not recorded — no ledger entry.", ev.ExerciseId);
                _currentRecording = null;
                _pendingBranch = null;
            }
            else if (_lastState != ClusterState.OperatingLive && ev.CurrentState == ClusterState.OperatingLive)
            {
                var parent = _pendingBranch?.Parent ?? Guid.Empty;
                _currentRecording = new RecordingLedgerEntry(ev.ExerciseId, _pendingScenarioId, DateTime.UtcNow, TimeSpan.Zero,
                    (Fdp.Core.ExerciseId)(parent), _pendingBranch?.Ticks ?? 0);
                _pendingBranch = null;
                // ⭐ CE-3410 — a run RESUMED (back from DAR) already holds a ledger entry: it ran, and is never dropped
                _liveRan = _unarchivedLedger.ContainsKey(ev.ExerciseId);
                _lastLiveSim = double.NaN;
                SnapshotScenario(ev.ExerciseId, _pendingScenarioId, parent, _currentRecording.StartTimeUtc);
            }
            else if (_lastState == ClusterState.OperatingLive && ev.CurrentState != ClusterState.OperatingLive)
            {
                if (_currentRecording != null && _currentRecording.ExerciseId != Guid.Empty && !_liveRan && SimSeconds != null
                    && _currentRecording.ParentExerciseId == Guid.Empty)   // ⛔ never a BRANCH: it holds the parent's history up to the branch point (RecordingModule.StartBranch) — not empty (CE-4104: every branch keeps its recording)
                {
                    // ⭐⭐ CE-3410 (AQ108 §0 D28 / D33) — a Live left before its clock ever ran (a cancelled briefing, a switch away from Live) leaves NO recording: the master minted the
                    //   exercise at the load (CE-3310) and its first keyframe was written then (CE-3313), so it would list an empty exercise. ⛔ No ledger entry ⇒ never listed (the inventory
                    //   reads only the ledger); its scenario copy and staging directories go through the one deleter (CE-3218), as a discard does (CE-4090 D15).
                    FdpLog<AssetInventoryProcessManager>.Info("[AssetInventory] CE-3410: live run {0} never ran — dropped, not listed.", _currentRecording.ExerciseId);
                    Discard(_currentRecording.ExerciseId);
                }
                else if (_currentRecording != null && _currentRecording.ExerciseId != Guid.Empty)
                {
                    var duration = DateTime.UtcNow - _currentRecording.StartTimeUtc;
                    _currentRecording = _currentRecording with { Duration = duration };
                    _unarchivedLedger[_currentRecording.ExerciseId] = _currentRecording;
                    SaveLedgerEntry(_currentRecording);
                }

                _currentRecording = null;
                _pendingScenarioId = null;
            }

            // ⭐ CE-4097 — a branch that never reached OperatingLive (back to a replay / Idle) must not label the next load
            if (ev.CurrentState is ClusterState.Idle or ClusterState.OperatingReplay) _pendingBranch = null;

            _lastState = ev.CurrentState;
        }

        // ⭐ CE-4097 (AQ108 D5) — a replay → live branch: the nodes answer PrepareLive with the parent and branch point of the recording they
        //   start (the SAME RecordingBranch their sidecar is written from); the master's aggregator hands the earliest on ClusterOpCompletedEvent.
        //   The result may land before or after the cluster state reaches OperatingLive, so it labels the run's entry if that exists, else waits.
        foreach (var done in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (done.StatusCode != OrchestrationStatusCode.Success
                || done.ResultPayload is not LiveBranchResult { ParentExerciseId: var parent } branch || parent == Guid.Empty) continue;
            if (_currentRecording is { } run)
            {
                _currentRecording = run with { ParentExerciseId = parent, BranchWallTicks = branch.BranchWallTicks };
                // ⭐ CE-4091 (D7) — the result landed AFTER the entry: the copy made at go-live was of the scenario file; a branch keeps its parent's.
                InheritParentCopy(run.ExerciseId, parent);
            }
            else
                _pendingBranch = (parent, branch.BranchWallTicks);
        }

        foreach (var intent in _bus.ReadManaged<ExecuteStorageOpIntent>())
        {
            if (intent.Operation == StorageOpType.Export)
                _pendingExports[intent.RequestId] = intent.ExerciseId;
        }

        // CE-278 §5: on export success, persist the archived-exercise metadata sidecar next to the pulled
        // .fdp files on NAS BEFORE dropping the local ledger entry, then evict. This re-homes the
        // Orchestrator.json sidecar onto the Export path (previously written only by the retired
        // SaveScenario=2 op); StorageGatewayModule.ScanNasExercises reads it back for the Archived
        // Exercises list. ⚠ Keyed on ClusterOpCompletedEvent — the real production completion signal
        // (PublishOpStatus / StorageProcessManager). StorageOpCompletedEvent has NO production publisher
        // (only the test-only EventDrivenStorageGateway), so the previous StorageOpCompletedEvent branch
        // never fired outside tests and the ledger was never evicted in production. See CE-278 design §5.
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.StatusCode == OrchestrationStatusCode.Success &&
                _pendingExports.TryGetValue(ev.RequestId, out var exerciseId))
            {
                _pendingExports.Remove(ev.RequestId);
                _unarchivedLedger.TryGetValue(exerciseId, out var ledgerEntry);
                // ⭐ CE-4330 ① (§3 P4) — the scenario copy FIRST, the manifest (the sidecar, listing every pulled file) LAST: an archive cut anywhere
                //   before it stays INCOMPLETE on the NAS. ⛔ The ledger entry goes only once the manifest is written — else the exercise stays
                //   "on the nodes" and its archive can be resumed (⑧).
                if (ledgerEntry != null) ArchiveScenario(exerciseId, ledgerEntry);
                if (!WriteExerciseSidecar(exerciseId, ledgerEntry, (ev.ResultPayload as ArchivePullResult)?.Files)) continue;

                _unarchivedLedger.Remove(exerciseId);
                DeleteLedgerEntry(exerciseId);
            }
        }

        if (DateTime.UtcNow - _lastInventoryScan >= _scanInterval)
        {
            var scenariosNasPath  = Path.Combine(_nasBasePath, OrchestrationConstants.ScenariosDirectoryName);
            var exercisesNasPath  = Path.Combine(_nasBasePath, OrchestrationConstants.ExercisesDirectoryName);
            var localScenarios    = _gateway.ScanLocalScenarios(scenariosNasPath);
            var localExercises = _unarchivedLedger.Values
                .Select(e => NasStated(new ExerciseInventoryItem(e.ExerciseId, e.StartTimeUtc, e.Duration, e.ScenarioId, e.ParentExerciseId, e.BranchWallTicks), exercisesNasPath))
                .OrderByDescending(e => e.StartTimeUtc)
                .ToArray();
            var archivedExercises = _gateway.ScanNasExercises(exercisesNasPath)
                .Select(e => NasStated(e, exercisesNasPath))
                .OrderByDescending(e => e.StartTimeUtc)
                .ToArray();
            var unarchived = localExercises
                .ExceptBy(archivedExercises.Select(a => a.ExerciseId), e => e.ExerciseId)
                .ToArray();
            // ⭐ CE-4054 — the run in progress is replayable (ClusterMaster.ResolveReplayExercise picks it), so the inventory of "what can be
            //   replayed" lists it too, its length so far. 🔴 Measured: GET /exercises answered {count: 0} through a whole live run while
            //   POST /replay found "the run in progress". ⛔ Not among the UNARCHIVED: a run still recording is not offered for export.
            var replayable = _currentRecording is { } live && live.ExerciseId != Guid.Empty
                ? localExercises.Where(e => e.ExerciseId != live.ExerciseId)
                      .Prepend(new ExerciseInventoryItem(live.ExerciseId, live.StartTimeUtc, DateTime.UtcNow - live.StartTimeUtc, live.ScenarioId, live.ParentExerciseId, live.BranchWallTicks))
                      .ToArray()
                : localExercises;

            _bus.PublishManaged(new AssetInventoryUpdateEvent
            {
                LocalScenarios           = localScenarios.ToArray(),
                LocalExercises           = replayable,
                ArchivedExercises        = archivedExercises,
                UnarchivedLocalExercises = unarchived,
            });
            _lastInventoryScan = DateTime.UtcNow;
        }
    }

    /// <summary>⭐ CE-4330 ③ fact ② — <paramref name="item"/> with its NAS state (<see cref="ArchiveManifest.StateOf"/>, the one rule). Off the frame's hot path: the 5 s inventory scan.</summary>
    private static ExerciseInventoryItem NasStated(ExerciseInventoryItem item, string exercisesNasPath)
    {
        var (state, files, total) = ArchiveManifest.StateOf(Path.Combine(exercisesNasPath, ((Guid)item.ExerciseId).ToString()));
        return item with { NasState = state, NasFiles = files, NasFilesTotal = total };
    }

    /// <summary>
    /// ⭐ CE-4090 (AQ108 D15) — discards ONE unarchived recording: its ledger entry (memory and file), its scenario copy and the exercise's
    /// staging directories (<see cref="ExerciseRetention.Delete"/>, CE-3218 — the one deleter of recordings). Returns false for an id the
    /// ledger does not hold (a kept / archived exercise, the run still recording, a stranger) — ⛔ nothing is touched then.
    /// </summary>
    public bool DeleteExercise(Guid exerciseId)
    {
        if (!_unarchivedLedger.Remove(exerciseId)) return false;
        Discard(exerciseId);
        return true;
    }

    /// <summary>The files of a discarded exercise: its ledger file, its scenario copy and its staging directories (<see cref="ExerciseRetention.Delete"/>, CE-3218).</summary>
    /// <remarks>⚠ A node still flushing its recorder after the unload may leave a file behind; it is never LISTED (the inventory reads the ledger) and retention sweeps it.</remarks>
    private void Discard(Guid exerciseId)
    {
        DeleteLedgerEntry(exerciseId);
        ExerciseRetention.Delete(_stagingRoot, exerciseId, msg => FdpLog<AssetInventoryProcessManager>.Info("{0}", msg));
        _lastInventoryScan = DateTime.MinValue;   // the next Tick republishes: the inventory stops offering it at once
    }

    /// <summary>
    /// ⭐ CE-3410 (AQ108 §0 D28) — the cluster's sim clock in seconds (<c>GlobalTime.TotalTime</c>). Set by the host that owns the time controller (<c>OrchestratorCore</c>, like the
    /// master's <c>SimSeconds</c>); a live run RAN when it rose between two Live frames. <see langword="null"/> ⇒ every live run counts as run (nothing is dropped).
    /// </summary>
    public Func<double>? SimSeconds { get; set; }
    private bool _liveRan;
    private double _lastLiveSim = double.NaN;

    private void HydrateLedger()
    {
        if (!Directory.Exists(_ledgerDirectory))
        {
            Directory.CreateDirectory(_ledgerDirectory);
            return;
        }

        foreach (var file in Directory.GetFiles(_ledgerDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<RecordingLedgerEntry>(json, FdpJsonOptionsRegistry.DefaultRelaxed);
                if (entry != null && entry.ExerciseId != Guid.Empty)
                    _unarchivedLedger[entry.ExerciseId] = entry;
            }
            catch
            {
            }
        }
    }

    private void SaveLedgerEntry(RecordingLedgerEntry entry)
    {
        try
        {
            if (!Directory.Exists(_ledgerDirectory))
                Directory.CreateDirectory(_ledgerDirectory);

            var path = Path.Combine(_ledgerDirectory, $"{entry.ExerciseId}.json");
            var json = JsonSerializer.Serialize(entry, FdpJsonOptionsRegistry.Indented);
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private void DeleteLedgerEntry(Guid exerciseId)
    {
        try
        {
            var path = Path.Combine(_ledgerDirectory, $"{exerciseId}.json");
            if (File.Exists(path))
                File.Delete(path);

            var snapshot = SnapshotDirectory(exerciseId);
            if (Directory.Exists(snapshot))
                Directory.Delete(snapshot, recursive: true);
        }
        catch
        {
        }
    }

    /// <summary>The name of the archive's copy of the scenario, beside the recordings (CE-3119).</summary>
    public const string ArchivedScenarioDirectoryName = OrchestrationConstants.ArchivedScenarioDirectoryName;

    /// <summary>Where the start-time copy of an exercise's scenario waits for the export — beside its ledger entry.</summary>
    private string SnapshotDirectory(Guid exerciseId) => Path.Combine(_ledgerDirectory, OrchestrationConstants.GetScenarioSnapshotName(exerciseId));

    /// <summary>
    /// ⭐ CE-3119 — copies the scenario the exercise starts from into the ledger, once per exercise, so the archive
    /// files what RAN even if the scenario is saved over later (<c>docs/designs/mgmt-1/DESIGN.md</c> §13.3a).
    /// Best-effort, like the sidecar: a failure must not stop the exercise.
    /// <para>⭐ <c>CE-4091</c> (AQ108 D7): the copy is the WHOLE folder (<c>foreign/</c>, <c>takes/</c>, …) and is STAMPED at this go-live — the
    /// name, the hash of the SAVED <c>scenario.json</c> just copied, and <paramref name="recordedAtUtc"/> (<see cref="RecordedScenarioStamp"/>,
    /// the one source for "which version ran"). A BRANCH (<paramref name="parentExerciseId"/> set) inherits its parent's copy, stamp included,
    /// whatever scenario the load names.</para>
    /// </summary>
    private void SnapshotScenario(Guid exerciseId, string? scenarioId, Guid parentExerciseId, DateTime recordedAtUtc)
    {
        if (exerciseId == Guid.Empty) return;
        var snapshot = SnapshotDirectory(exerciseId);
        if (Directory.Exists(snapshot)) return; // a resumed exercise keeps the copy it started with

        if (parentExerciseId != Guid.Empty && InheritParentCopy(exerciseId, parentExerciseId)) return;
        if (string.IsNullOrWhiteSpace(scenarioId)) return;

        try
        {
            var copied = StorageGatewayModule.CopyScenarioFiles(
                ContentLayout.ResolveScenarioDirectory(_nasBasePath, scenarioId!), snapshot);
            if (copied == 0)
                FdpLog<AssetInventoryProcessManager>.Warn(
                    "[AssetInventory] scenario '{0}' has no files on the NAS; exercise {1} archives without a scenario copy.",
                    scenarioId, exerciseId);
            else
                RecordedScenarioStamp.Write(snapshot, scenarioId!, recordedAtUtc);
        }
        catch (Exception ex)
        {
            FdpLog<AssetInventoryProcessManager>.Warn(
                "[AssetInventory] could not snapshot scenario '{0}' for exercise {1}: {2}", scenarioId, exerciseId, ex.Message);
        }
    }

    /// <summary>
    /// ⭐ CE-4091 (D7) — the scenario copy of a recording, wherever it is now: the ledger's snapshot while the recording is unarchived, else the
    /// archive's <c>exercises/&lt;id&gt;/scenario/</c> on the NAS; null when it has none.
    /// </summary>
    private string? FindRecordedCopy(Guid exerciseId)
    {
        var snapshot = SnapshotDirectory(exerciseId);
        if (Directory.Exists(snapshot)) return snapshot;
        var archived = OrchestrationConstants.GetArchivedScenarioDirectory(_nasBasePath, exerciseId);
        return Directory.Exists(archived) ? archived : null;
    }

    /// <summary>
    /// ⭐ CE-4091 (D7) — makes <paramref name="exerciseId"/>'s scenario copy EQUAL its parent's (files, subfolders and the stamp): a branch ran the
    /// parent's scenario, not whatever the file says now. Replaces a copy already made from the file; false (nothing touched) when the parent has none.
    /// </summary>
    private bool InheritParentCopy(Guid exerciseId, Guid parentExerciseId)
    {
        try
        {
            if (FindRecordedCopy(parentExerciseId) is not { } parentCopy) return false;
            var snapshot = SnapshotDirectory(exerciseId);
            if (Directory.Exists(snapshot)) Directory.Delete(snapshot, recursive: true);
            return StorageGatewayModule.CopyScenarioFiles(parentCopy, snapshot) > 0;
        }
        catch (Exception ex)
        {
            FdpLog<AssetInventoryProcessManager>.Warn(
                "[AssetInventory] could not inherit the scenario copy of {0} for branch {1}: {2}", parentExerciseId, exerciseId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4091 (AQ108 D7, D22) — OPENS A RECORDED COPY AS A NEW SCENARIO: when <paramref name="scenarioName"/> is
    /// <see cref="RecordedScenarioNames.ForExercise"/>'s name, puts the recording's scenario copy (without its stamp) at
    /// <c>&lt;nas&gt;/opened/recorded/&lt;id&gt;/</c> — the place every node's load of that name resolves — and returns true; false for any
    /// other name. ⛔ It reads the copy and writes ONLY that new folder: never the original scenario's folder, never the recording or its copy.
    /// Run by the prefetch BEFORE it stages the name to the nodes (so it may run off the frame thread: it touches only the file system).
    /// A recording with no copy throws — the load is then refused like any scenario that is not there.
    /// </summary>
    public bool StageRecordedScenario(string scenarioName)
    {
        if (!RecordedScenarioNames.TryParse(scenarioName, out var exerciseId)) return false;
        var source = FindRecordedCopy(exerciseId)
            ?? throw new DirectoryNotFoundException($"[AssetInventory] exercise {exerciseId} has no scenario copy to open (recorded before CE-3119, recording off, or discarded).");
        var dest = RecordedScenarioNames.DirectoryFor(_nasBasePath, exerciseId);
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);   // always the recording's copy as it is, never a half-edited earlier open
        if (StorageGatewayModule.CopyScenarioFiles(source, dest, exceptFileName: RecordedScenarioStamp.FileName) == 0)
            throw new InvalidOperationException($"[AssetInventory] the scenario copy of exercise {exerciseId} is empty.");
        FdpLog<AssetInventoryProcessManager>.Info("[AssetInventory] CE-4091: exercise {0}'s scenario copy staged as '{1}' for editing.", exerciseId, scenarioName);
        return true;
    }

    /// <summary>
    /// ⭐ CE-3119 — files the scenario copy into <c>&lt;nas&gt;/exercises/&lt;exerciseId&gt;/scenario/</c>: the start-time
    /// snapshot when there is one, else (a ledger entry from before CE-3119) the scenario as it is on the NAS now.
    /// </summary>
    private void ArchiveScenario(Guid exerciseId, RecordingLedgerEntry entry)
    {
        var dest = OrchestrationConstants.GetArchivedScenarioDirectory(_nasBasePath, exerciseId);
        try
        {
            var snapshot = SnapshotDirectory(exerciseId);
            if (StorageGatewayModule.CopyScenarioFiles(snapshot, dest) > 0) return;

            if (string.IsNullOrWhiteSpace(entry.ScenarioId)) return;
            var copied = StorageGatewayModule.CopyScenarioFiles(
                ContentLayout.ResolveScenarioDirectory(_nasBasePath, entry.ScenarioId!), dest);
            FdpLog<AssetInventoryProcessManager>.Warn(
                "[AssetInventory] exercise {0} had no start-time scenario snapshot; archived the CURRENT '{1}' ({2} file(s)).",
                exerciseId, entry.ScenarioId, copied);
        }
        catch (Exception ex)
        {
            FdpLog<AssetInventoryProcessManager>.Warn(
                "[AssetInventory] failed to archive the scenario of exercise {0}: {1}", exerciseId, ex.Message);
        }
    }

    /// <summary>
    /// CE-278 §5 — writes the archived-exercise metadata sidecar
    /// (<c>&lt;nas&gt;/exercises/&lt;exerciseId&gt;/Orchestrator.json</c>) beside the pulled <c>.fdp</c>
    /// files, in the <see cref="GlobalContextDto"/> shape <see cref="StorageGatewayModule.ScanNasExercises"/>
    /// reads (start wall ticks + scenario id + duration). Best-effort: a failure must not abort the export
    /// lifecycle. The exercise dir uses <c>exerciseId.ToString()</c> to match <c>ReferenceArchiveHandler</c>'s
    /// NAS layout, so the sidecar lands in the same directory as the archived recordings.
    /// </summary>
    /// <remarks>
    /// ⭐ CE-4330 ① — the sidecar IS the archive's manifest: it lists <paramref name="files"/> (<see cref="ArchiveManifest.SetFiles"/>) and is written
    /// whole (<see cref="ArchiveManifest.WriteWhole"/>), LAST. With no ledger entry (an exercise archived again) an existing sidecar keeps its facts and
    /// only gains the list. <paramref name="files"/> null (a completion that carries no pull — older callers) writes the pre-CE-4330 shape.
    /// Returns false when nothing could be written (the archive stays INCOMPLETE).
    /// </remarks>
    private bool WriteExerciseSidecar(Guid exerciseId, RecordingLedgerEntry? entry, IReadOnlyList<ArchivedFile>? files)
    {
        try
        {
            var dir = Path.Combine(
                _nasBasePath, OrchestrationConstants.ExercisesDirectoryName, exerciseId.ToString());
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, ArchiveManifest.SidecarFileName);

            var opts = FdpJsonOptionsRegistry.Indented;
            JsonObject dom;
            if (entry == null && File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
                dom = existing;
            else
            {
                var dto = new GlobalContextDto
                {
                    StartWallTicks      = entry?.StartTimeUtc.Ticks ?? 0,
                    SceneId             = string.Empty,
                    ScenarioId          = entry?.ScenarioId ?? string.Empty,
                    ScenarioTimeSeconds = entry?.Duration.TotalSeconds ?? 0,
                };
                dom = JsonSerializer.SerializeToNode(dto, opts)!.AsObject();
                JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.OrchestratorContext, 2));
            }
            if (files != null) ArchiveManifest.SetFiles(dom, files);
            ArchiveManifest.WriteWhole(path, dom.ToJsonString(opts));

            FdpLog<AssetInventoryProcessManager>.Info(
                "[AssetInventory] wrote exercise manifest for {0} ({1} file(s), scenarioId='{2}').",
                exerciseId, files?.Count ?? -1, entry?.ScenarioId ?? "");
            return true;
        }
        catch (Exception ex)
        {
            FdpLog<AssetInventoryProcessManager>.Warn(
                "[AssetInventory] failed to write exercise manifest for {0}: {1} — the archive stays incomplete.", exerciseId, ex.Message);
            return false;
        }
    }
}
