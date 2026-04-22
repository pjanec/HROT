# CGF-1 Design Addendum 3 — Orchestrator UI Overhaul & CQRS Network Architecture

> **Source:** `design-review-3.md`  
> **Scope:** Phase 5 — Operational UI, Real Network Dispatch, Time Control, Archive
> Pipeline, and CQRS-Decoupled Cluster Panel.  
> **Author:** Design review conversation, 2026-03-30.  
> **Tasks:** [CGF-1-TASK-DETAIL.md §Phase 5](./CGF-1-TASK-DETAIL.md#phase-5--operational-ui-real-network-dispatch--cqrs-architecture)

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Orchestrator ImGui Window & 2PC History Overhaul](#2-orchestrator-imgui-window--2pc-history-overhaul)
   - 2.1 [Beige Title Bar + Window Wrapper](#21-beige-title-bar--window-wrapper)
   - 2.2 [Source→Target Transition Indicator](#22-sourcetarget-transition-indicator)
   - 2.3 [2PC History Table Overhaul](#23-2pc-history-table-overhaul)
3. [Real Network Dispatch Fix](#3-real-network-dispatch-fix)
   - 3.1 [DdsWriter in OrchestratorSubsystem](#31-ddswriter-in-orchestratorsubsystem)
   - 3.2 [OrchestratorScenarioPanel Uses SysOpWriter](#32-orchestratorscenariopanel-uses-sysopwriter)
   - 3.3 [ClusterMaster Fan-out Loop (Critical Fix)](#33-clustmaster-fan-out-loop-critical-fix)
4. [Time Control Section](#4-time-control-section)
   - 4.1 [New ClusterOpType Entries](#41-new-sysoptype-entries)
   - 4.2 [ClusterMaster.TimeControlRequested Event](#42-clustmastertimecontrolrequested-event)
   - 4.3 [Replay Seek Debounce](#43-replay-seek-debounce)
5. [Asset Combo Selection (Local Scan)](#5-asset-combo-selection-local-scan)
6. [Archive Export/Import Pipeline](#6-archive-exportimport-pipeline)
   - 6.1 [ClusterOpType.CancelOperation](#61-sysoptypecanceloperation)
   - 6.2 [Cancellation Threading in StorageGatewayModule](#62-cancellation-threading-in-storagegatewaymodule)
   - 6.3 [ReferenceArchiveHandler (Toolkit, Node-Side)](#63-referencearchivehandler-toolkit-node-side)
   - 6.4 [ClusterMaster Orchestration Branches](#64-clustmaster-orchestration-branches)
   - 6.5 [Archive Management UI Section](#65-archive-management-ui-section)
7. [CQRS Decoupling: AssetInventoryTopic + ClusterUiCache](#7-cqrs-decoupling-assetinventorytopic--clusteruicache)
   - 7.1 [AssetInventoryTopic DDS Message](#71-assetinventorytopic-dds-message)
   - 7.2 [ClusterMaster Publishes Inventory](#72-clustmaster-publishes-inventory)
   - 7.3 [ClusterUiCache — Network Projection](#73-clusteruicache--network-projection)
   - 7.4 [ClusterScenarioPanel — Shared UI Component](#74-clusterscenariopanel--shared-ui-component)
   - 7.5 [OrchestratorSubsystem Refactored](#75-orchestratorsubsystem-refactored)
8. [IOS Remote Cluster Control Panel](#8-ios-remote-cluster-control-panel)
   - 8.1 [Time Ingress Handlers](#81-time-ingress-handlers)
   - 8.2 [IIosLogic Time State and Commands](#82-iioslogic-time-state-and-commands)
   - 8.3 [IosSubsystem Wiring](#83-iossubsystem-wiring)
9. [Storage Layout Reference](#9-storage-layout-reference)
10. [Phase 5 Task Summary](#10-phase-5-task-summary)

---

## 1. Motivation

Phase 5 is driven by a set of interrelated problems surfaced in design-review-3.md:

| Problem | Symptom | Fix |
|---------|---------|-----|
| **Missing ImGui window wrapper** | All Orchestrator UI stacks directly inside `DrawUI()` with no `ImGui.Begin` block; beige colour bleeds into child backgrounds instead of the panel title bar | §2.1 |
| **Buttons do nothing on the network** | Clicking "Standby → LoadingLive" triggers `ClusterMaster.HandleClusterOpRequest` locally but never fans out `PrepareState`/`CommitState` DDS commands, so all nodes remain in Standby | §3.3 |
| **UI talks directly to ClusterMaster** | Tightly coupled; IOS/CGF cannot independently render the same control panel without a local `ClusterMaster` reference | §7, §8 |
| **No time control in UI** | Pause/Resume/Step/Speed are not currently exposed in the Orchestrator panel | §4 |
| **Archive pipeline not implemented** | `ClusterOpType.ExportArchive`/`ImportArchive` are defined but never handled; drill recordings stay permanently on local SSD | §6 |
| **Scenario/Drill/Story are text inputs** | Operators must type GUIDs manually; combo-box selection from local filesystem scan is the right UX | §5 |
| **IOS cannot see cluster state or control the drill** | IOS has no orchestrator UI; it should be functionally equivalent to the Orchestrator panel via pure DDS messaging | §8 |

The solution is organized around a **CQRS** (Command–Query Responsibility Segregation)
principle: all UI components publish `ClusterOpRequest` commands and observe
`SystemStateTopic`, `NodeHeartbeat`, `AssetInventoryTopic`, and time topics — never
reaching into local C# service instances.

---

## 2. Orchestrator ImGui Window & 2PC History Overhaul

### 2.1 Beige Title Bar + Window Wrapper

**`OrchestratorSubsystem.cs`** currently exposes:
```csharp
public System.Numerics.Vector4 TitleBarColor => new(0.12f, 0.18f, 0.42f, 1f);
```
Change to the beige vector used consistently with the Orchestrator brand:
```csharp
public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);
```

`DrawUI()` currently draws everything as loose ImGui calls without an enclosing window.
Add:
```csharp
public void DrawUI()
{
    if (_uiCache == null) return;
    if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }
    // ... all existing content ...
    ImGui.End();
}
```

**`OrchestratorScenarioPanel.cs`** currently applies `BeigeChildBg` via
`ImGui.PushStyleColor(ImGuiCol.ChildBg, ...)` before every `BeginChild` call and
`PopStyleColor` after every `EndChild`. This must be **deleted** — the beige is already
applied to the whole window via `SubsystemOrchestrator`'s title-bar push; nested child
backgrounds should remain at the default dark theme colour.

Remove `private static readonly Vector4 BeigeChildBg` and all `PushStyleColor` /
`PopStyleColor` calls wrapping child regions.

### 2.2 Source→Target Transition Indicator

Add `SourceClusterState` to `DistributedTransaction`:
```csharp
public ClusterState SourceClusterState { get; set; }
```

In `ClusterMaster.ProcessSingleClusterOpRequest`, capture `_currentClusterState` before any
optimistic advance and assign it to the new transaction:
```csharp
ClusterState sourceState = _currentClusterState;
// ... resolve trajectory, optimistic advance ...
var tx = new DistributedTransaction
{
    TransactionId  = txId,
    SourceClusterState = sourceState,   // NEW
    TargetClusterState = resolvedTarget,
    // ...
};
```

In `RenderStatusBanner` inside `OrchestratorScenarioPanel` / `ClusterScenarioPanel`:
```csharp
if (hasInFlight && activeTx != null &&
    activeTx.SourceClusterState != activeTx.TargetClusterState)
{
    ImGui.Text($"State: {activeTx.SourceClusterState} → {activeTx.TargetClusterState}");
}
else
{
    ImGui.Text($"State: {currentState}");
}
```

### 2.3 2PC History Table Overhaul

Add to `DistributedTransaction`:
```csharp
/// <summary>The ClusterOpRequest JSON payload that initiated this transaction.</summary>
public string PayloadJson { get; set; } = string.Empty;

/// <summary>Per-node ResultJson from the final NodeOpStatus ACK, keyed by node ID.</summary>
public Dictionary<int, string> NodeResponses { get; } = new();
```

Populate in `ClusterMaster`:
- `PayloadJson = req.PayloadJson ?? string.Empty` when creating the transaction.
- `tx.NodeResponses[status.NodeId] = status.ResultJson` in `ConsumeNodeOpStatuses`.

The 2PC History table in `DrawUI()` is replaced with:
- **5 columns:** `TransactionId`, `Target State`, `Result`, `ACK Latency`, `Payload`
- **Scrollable, 10-row max height:** `ImGuiTableFlags.ScrollY` + `ImGui.TableSetupScrollFreeze(0,1)`
- **Full GUID in column 1** via `ImGui.TreeNodeEx(tx.TransactionId.ToString(), ImGuiTreeNodeFlags.SpanFullWidth)`
- **Payload snippet** in column 5 (first 25 chars + `"..."`); on `ImGui.IsItemHovered()` open `ImGui.BeginTooltip()` showing `FormatPrettyJson(payloadStr)`
- **Context menu** on right-click: single `MenuItem("Copy line to clipboard")` that calls `ImGui.SetClipboardText()`
- **Expandable rows:** when `TreeNodeEx` is open, render one child row per entry in `tx.NodeResponses`; columns 1 indent `↳ Node {id}`, column 4 shows per-node latency, column 5 shows the node's `ResultJson`

```csharp
private static string FormatPrettyJson(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return string.Empty;
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return System.Text.Json.JsonSerializer.Serialize(doc,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    catch { return json; }
}
```

---

## 3. Real Network Dispatch Fix

### 3.1 DdsWriter in OrchestratorSubsystem

```csharp
private DdsWriter<ClusterOpRequest>? _sysOpWriter;
```

In `Initialize`: `_sysOpWriter = new DdsWriter<ClusterOpRequest>(_participant);`  
In `Shutdown`: `_sysOpWriter?.Dispose(); _sysOpWriter = null;`  
Pass to panel: `_scenarioPanel = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter);`
(later renamed `ClusterScenarioPanel` with `ClusterUiCache` in S0506).

### 3.2 OrchestratorScenarioPanel Uses SysOpWriter

Constructor updated to:
```csharp
public OrchestratorScenarioPanel(ClusterMaster drillMaster, DdsWriter<ClusterOpRequest> sysOpWriter)
```

Every button that was calling `_drillMaster.HandleClusterOpRequest(...)` is replaced with
`_sysOpWriter.Write(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ...,
PayloadJson = ... })`. This applies to all six render helpers:
`RenderDrillControl`, `RenderCheckpointSection`, `RenderScenarioSection`,
`RenderReplaySection`, `RenderStoriesSection`, and the "Initialize Live / Pause / Resume"
buttons that were left as TODO comments in `DrawUI()`.

### 3.3 ClusterMaster Fan-out Loop (Critical Fix)

**Root cause:** `ClusterMaster.ProcessSingleClusterOpRequest` plans the trajectory and
optimistically advances `_currentClusterState`, but never fans out `NodeOpCommand` DDS
messages. Nodes never receive a `PrepareState` or `CommitState` packet.

After the trajectory is resolved and the `DistributedTransaction` is created, iterate
the planned steps and fan out:

```csharp
if (req.OperationType == ClusterOpType.TransitionState && activeNodeIds.Count > 0)
{
    foreach (var step in trajectory)
    {
        if (step is TransitionStep tStep)
        {
            // Map TargetState to lifecycle prepare operation
            NodeOpType prepareOp = tStep.TargetState switch
            {
                ClusterState.LoadingLive     => NodeOpType.PrepareLive,
                ClusterState.UnloadingLive   => NodeOpType.FinalizeLive,
                ClusterState.LoadingReplay   => NodeOpType.PrepareReplay,
                ClusterState.UnloadingReplay => NodeOpType.FinalizeReplay,
                ClusterState.LoadingEdit     => NodeOpType.PrepareEdit,
                ClusterState.UnloadingEdit   => NodeOpType.FinalizeEdit,
                _                       => NodeOpType.PrepareState,
            };

            FanOutNodeOp(new NodeOpCommand
            {
                TransactionId = tx.TransactionId,
                Operation     = prepareOp,
                PayloadJson   = req.PayloadJson ?? string.Empty,
            }, activeNodeIds);

            FanOutNodeOp(new NodeOpCommand
            {
                TransactionId = tx.TransactionId,
                Operation     = NodeOpType.CommitState,
                PayloadJson   = ((int)tStep.TargetState).ToString(),
            }, activeNodeIds);
        }
        else if (step is OperationStep opStep &&
                 opStep.Operation == ClusterOpType.ReplaySeek)
        {
            FanOutNodeOp(new NodeOpCommand
            {
                TransactionId = tx.TransactionId,
                Operation     = NodeOpType.NodeReplaySeek,
                PayloadJson   = opStep.PayloadJson,
            }, activeNodeIds);
        }
    }
}
```

**Why correct:** The `PrepareXxx` operation routes to each node's registered
`IDsmHandler` (e.g. `ReferenceLiveLoadHandler`) with the full payload, allowing async
file staging. `CommitState` forces the `ClusterSlave` to update `_localStateId`. Node
heartbeats then reflect the new state within one 1 Hz cycle, updating the Node Health
table.

---

## 4. Time Control Section

### 4.1 New ClusterOpType Entries

Extend `Hrot.NED/Orchestration/OrchestrationMessages.cs`:
```csharp
public enum ClusterOpType : int
{
    // ... existing 0–12 ...
    CancelOperation = 13,   // S0505 — force-cancel an in-flight archive op
    StepTime        = 14,   // advance exactly one deterministic frame (~16 ms)
    SetTimeScale    = 15,   // payload: float scale in range [0.1, 10.0]
}
```

### 4.2 ClusterMaster.TimeControlRequested Event

Time manipulation does not require 2PC across simulation nodes (only the
`DistributedTimeCoordinator` needs to act). Intercept these requests in
`ClusterMaster.ProcessSingleClusterOpRequest` before the main switch:

```csharp
if (req.OperationType is ClusterOpType.PauseTime or ClusterOpType.ResumeTime
                      or ClusterOpType.StepTime  or ClusterOpType.SetTimeScale)
{
    TimeControlRequested?.Invoke(req.OperationType, req.PayloadJson ?? string.Empty);
    return;
}

public event Action<ClusterOpType, string>? TimeControlRequested;
```

`OrchestratorSubsystem.Initialize` subscribes:
```csharp
_drillMaster.TimeControlRequested += (op, payload) =>
{
    switch (op)
    {
        case ClusterOpType.PauseTime:
            var ids = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
            _timeCoordinator?.SwitchToDeterministic(ids);
            break;
        case ClusterOpType.ResumeTime:
            _timeCoordinator?.SwitchToContinuous();
            break;
        case ClusterOpType.StepTime:
            _timeKernel?.StepFrame(1f / 60f);
            break;
        case ClusterOpType.SetTimeScale:
            if (float.TryParse(payload, out float s))
                _timeKernel?.GetTimeController()?.SetTimeScale(s);
            break;
    }
};
```

**UI rendering** (new `CollapsingHeader("Time Control")` in `DrawUI()` / `ClusterScenarioPanel`):
- Wall time: `new DateTime(wallTicks, DateTimeKind.Utc).ToString("HH:mm:ss.fff")`
- Drill time: `drillTime.ToString("F2") + " s"`
- `Button(isPaused ? "Resume" : "Pause")` → dispatches `PauseTime`/`ResumeTime` via `_sysOpWriter`
- `Button("Step")` (disabled when not paused) → dispatches `StepTime`
- `SliderFloat("Speed", ref timeScale, 0.1f, 10.0f)` → dispatches `SetTimeScale` on change

`isPaused` is inferred from the `SwitchTimeModeWireDto` network topic consumed by
`ClusterUiCache` (see §7.3), ensuring the toggle is consistent even when paused by
another node.

### 4.3 Replay Seek Debounce

`OrchestratorScenarioPanel` gains an `Update(float dt)` method called from
`OrchestratorSubsystem.Update`:

```csharp
private float _seekDebounceTimer = 0f;
private bool  _seekPending       = false;

public void Update(float dt)
{
    if (!_seekPending) return;
    _seekDebounceTimer -= dt;
    if (_seekDebounceTimer > 0f) return;

    _seekPending = false;
    long wallTicks = (long)(_seekSliderValue * 10_000_000L);
    _sysOpWriter.Write(new ClusterOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = ClusterOpType.ReplaySeek,
        PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
    });
    _requestPause?.Invoke();   // enter deterministic once seek is dispatched
}
```

When the slider is dragged: `_seekPending = true; _seekDebounceTimer = 0.5f;`.  
When the slider is **not** being dragged: `_seekSliderValue = currentDrillTime;` (tracks
playback position passively).  
`_replayDuration` is loaded from the selected drill's `*.meta.json` file at load time.

---

## 5. Asset Combo Selection (Local Scan)

`OrchestratorScenarioPanel` (and later `ClusterScenarioPanel`) replaces
`_loadScenarioId`, `_replayExerciseId`, and `_injectScenarioId` / `_injectStoryId` text
inputs with combo-box selections backed by `RefreshLocalAssets()`.

```csharp
private void RefreshLocalAssets()
{
    string root = @"C:\FDP_Temp";
    var scenarios = new List<string>();
    var drills    = new List<string>();

    if (Directory.Exists(root))
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir)!;
            if (Directory.GetFiles(dir, "*.fdp").Length > 0)  drills.Add(name);
            else if (Directory.GetFiles(dir, "*.json").Length > 0) scenarios.Add(name);
        }
    }

    _availableScenarios = scenarios.ToArray();
    _availableStories   = scenarios.ToArray(); // stories share the same packages
    _availableDrills    = drills.ToArray();

    // Clamp selection indices
    if (_selectedLoadScenarioIdx >= _availableScenarios.Length) _selectedLoadScenarioIdx = -1;
    if (_selectedStoryIdx        >= _availableStories.Length)   _selectedStoryIdx        = -1;
    if (_selectedExerciseIdx        >= _availableDrills.Length)    _selectedExerciseIdx        = -1;
}
```

A `"⟳"` refresh button sits `ImGui.SameLine()` next to each combo.

**Story injection** no longer requires a manual `StoryId` text input — a new
`Guid.NewGuid().ToString()` is generated automatically on button click. The combo
selects the `ScenarioId` (asset package folder), and the StoryId is an auto-generated
runtime identifier.

> **Note:** In S0506 (CQRS decoupling), `RefreshLocalAssets()` is superseded by the
> `AssetInventoryTopic` DDS feed from ClusterMaster, which queries the actual NAS
> rather than the Orchestrator's local SSD. The combo array-filling logic is moved
> into `ClusterUiCache.Update()`.

---

## 6. Archive Export/Import Pipeline

This phase implements the previously-deferred `ExportArchive` and `ImportArchive`
operations (§9 of CGF-1-DESIGN.md).

### 6.1 ClusterOpType.CancelOperation

`CancelOperation = 13` carries `PayloadJson = "<target-operation-request-guid>"`.  
On receipt, the Orchestrator kills the local `CancellationTokenSource` for that GUID
and fans out `NodeOpType.AbortTransaction` to all active nodes.

### 6.2 Cancellation Threading in StorageGatewayModule

Every bulk-copy method (`PullToNasAsync`, `PushToNodesAsync`, and the new
`PrefetchArchiveAsync`) gains a `CancellationToken ct` parameter:

```csharp
public async Task<GatewayResult> PullToNasAsync(
    IReadOnlyList<FileManifestEntry> manifests,
    string nasBasePath,
    CancellationToken ct = default)
{
    var opts = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies,
                                     CancellationToken = ct };
    var partial = new ConcurrentBag<string>();
    try
    {
        await Task.Run(() => Parallel.ForEach(manifests, opts, entry =>
        {
            var dest = Path.Combine(nasBasePath, entry.RelativeDest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            partial.Add(dest);
            File.Copy(entry.SourceUnc, dest, overwrite: true);
            partial.TryTake(out _);   // only remove on success
        }), ct);
    }
    catch (OperationCanceledException)
    {
        // Delete partially-written NAS files to keep storage consistent
        foreach (var f in partial) try { File.Delete(f); } catch { }
        throw;
    }
    return new GatewayResult { SuccessCount = manifests.Count, FailureCount = 0 };
}
```

A new `PrefetchArchiveAsync` method works symmetrically: instead of broadcasting a
scenario file to every node, it pushes `node_{nodeId}.fdp` only to the specific node
that owns it:
```csharp
public async Task<GatewayResult> PrefetchArchiveAsync(
    string drillId,
    IReadOnlyList<NodeDistributionTarget> targets,
    string nasBasePath,
    CancellationToken ct = default)
```

### 6.3 ReferenceArchiveHandler (Toolkit, Node-Side)

New handler added to `FDP.Toolkit.Orchestration`:

```csharp
public sealed class ReferenceArchiveHandler : IDsmHandler
{
    private readonly string _localTempRoot;
    private readonly int    _nodeId;

    public ReferenceArchiveHandler(string localTempRoot, int nodeId) { ... }

    public bool CanHandle(int operationId)
        => operationId == (int)NodeOpType.SerializeLocal;

    public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        => Task.FromResult<string?>(null);   // no async preparation needed

    public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
    {
        var drillId = ParseExerciseId(cmd.PayloadJson);   // looks for "ExerciseId" key
        if (drillId is null) return;                    // not an archive request; skip

        var file = Path.Combine(_localTempRoot, drillId, $"node_{_nodeId}.fdp");
        if (!File.Exists(file)) return;

        // Report the file manifest so the Orchestrator's gateway can pull it
        var manifest = new[] { new FileManifestEntry
        {
            SourceUnc    = file,
            RelativeDest = Path.Combine(drillId, $"node_{_nodeId}.fdp"),
        }};
        // ResultJson carries serialised manifest for ClusterMaster.ConsumeNodeOpStatuses
    }

    public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
    {
        // Delete any partially-written local file so the cluster remains consistent
        var drillId = ParseExerciseId(cmd.PayloadJson);
        if (drillId is null) return;
        var file = Path.Combine(_localTempRoot, drillId, $"node_{_nodeId}.fdp");
        try { if (File.Exists(file)) File.Delete(file); } catch { /* best-effort */ }
    }
}
```

Registered by `NodeBootstrapper.BuildOrchestration()` alongside the existing handlers.

### 6.4 ClusterMaster Orchestration Branches

`ClusterMaster` gains:
```csharp
private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();
```

New branches in `ProcessSingleClusterOpRequest`:

```
ClusterOpType.ExportArchive:
  1. Create CancellationTokenSource, store in _activeCancellations[req.RequestId].
  2. FanOutSerializeLocal(txId, activeNodeIds, req.PayloadJson)   // payload contains ExerciseId
  // ConsumeNodeOpStatuses already aggregates FileManifestEntry lists and calls
  // _gateway.PullToNasAsync when all ACKs arrive — pass the CTS token.

ClusterOpType.ImportArchive:
  1. Create CancellationTokenSource.
  2. _ = _gateway.PrefetchArchiveAsync(drillId, targets, _nasBasePath, cts.Token)
         .ContinueWith(t => { /* publish ClusterOpStatus Success or Timeout */ });

ClusterOpType.CancelOperation:
  1. Parse target op Guid from PayloadJson.
  2. If _activeCancellations.Remove(targetId, out var cts): cts.Cancel().
  3. FanOutNodeOp AbortTransaction to all active nodes with targetId.
```

### 6.5 Archive Management UI Section

New `RenderArchiveSection(ClusterState, bool)` in `ClusterScenarioPanel`:

| Control | Action |
|---------|--------|
| Combo "Unarchived Local" | Lists drills present locally but absent from NAS |
| Button "Export to NAS ▶" | Writes `ClusterOpRequest { ExportArchive, ExerciseId }` |
| Combo "Archived Drills" | Lists drills present on NAS |
| Button "Import from NAS ◀" | Writes `ClusterOpRequest { ImportArchive, ExerciseId }` |
| `ProgressBar` + yellow label | Shown only while `_activeArchiveOpId != Guid.Empty` |
| Red Button "CANCEL OPERATION" | Always active while archiving; writes `ClusterOpType.CancelOperation`; optimistically clears `_activeArchiveOpId` |

The combo lists are populated from `ClusterUiCache` (which receives `AssetInventoryTopic`
carrying both local and NAS drill lists), so they reflect NAS reality, not local SSD.

---

## 7. CQRS Decoupling: AssetInventoryTopic + ClusterUiCache

**Principle:** The Orchestrator UI and any remote UI (IOS, future CGF console) must never
hold a reference to `ClusterMaster`, `StorageGatewayModule`, or any local C# service. They
observe network state and emit commands. This makes the same `ClusterScenarioPanel`
instantiable on any node.

### 7.1 AssetInventoryTopic DDS Message

Add to `Hrot.NED/Orchestration/OrchestrationMessages.cs`:

```csharp
[DdsTopic("AssetInventory")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct AssetInventoryTopic
{
    [DdsKey] public int    NodeId;                   // 0 = singleton cluster orchestrator
    [DdsManaged] public string LocalScenariosJson;   // JSON string[]
    [DdsManaged] public string LocalDrillsJson;      // JSON string[] (local .fdp dirs)
    [DdsManaged] public string ArchivedDrillsJson;   // JSON string[] (NAS .fdp dirs)
    [DdsManaged] public string UnarchivedLocalDrillsJson; // localDrills minus archived
}
```

`TransientLocal` QoS ensures late-joining subscribers (IOS boots after Orchestrator)
receive the latest inventory sample immediately.

### 7.2 ClusterMaster Publishes Inventory

```csharp
private DdsWriter<AssetInventoryTopic>? _inventoryWriter;
private DateTime _lastInventoryScan = DateTime.MinValue;

public void Tick()
{
    // ... existing tick logic ...
    if ((DateTime.UtcNow - _lastInventoryScan).TotalSeconds >= 5)
    {
        PublishAssetInventory();
        _lastInventoryScan = DateTime.UtcNow;
    }
}

private void PublishAssetInventory()
{
    var localScenarios  = _gateway.ScanLocalScenarios(_nasBasePath);
    var localDrills     = _gateway.ScanLocalDrills(_nasBasePath);
    var archivedDrills  = _gateway.ScanNasDrills(_nasBasePath);
    var unarchived      = localDrills.Except(archivedDrills).ToList();

    _inventoryWriter?.Write(new AssetInventoryTopic
    {
        NodeId                  = 0,
        LocalScenariosJson      = JsonSerializer.Serialize(localScenarios),
        LocalDrillsJson         = JsonSerializer.Serialize(localDrills),
        ArchivedDrillsJson      = JsonSerializer.Serialize(archivedDrills),
        UnarchivedLocalDrillsJson = JsonSerializer.Serialize(unarchived),
    });
}
```

Expose `public string NasBasePath => _nasBasePath;` so `RefreshLocalAssets` (if still
used for fallback) can reach it.

`StorageGatewayModule` gains three pure scan helpers:
`ScanLocalScenarios(string root)`, `ScanLocalDrills(string root)`,
`ScanNasDrills(string nasRoot)` — parameterized, no side effects, no DDS.

### 7.3 ClusterUiCache — Network Projection

New class `Hrot.ClusterRunner.Services.ClusterUiCache` (also usable from `IosSubsystem`):

```csharp
public sealed class ClusterUiCache : IDisposable
{
    // ── Published state ──────────────────────────────────────────────────
    public ClusterState       CurrentState          { get; private set; }
    public bool           IsBootstrapped        { get; private set; }
    public bool           HasInFlightTransaction { get; private set; }

    public string[]       AvailableScenarios    { get; private set; } = [];
    public string[]       AvailableDrills       { get; private set; } = [];
    public string[]       ArchivedDrills        { get; private set; } = [];
    public string[]       UnarchivedLocalDrills { get; private set; } = [];

    public double         MasterSimTime         { get; private set; }
    public long           MasterWallTicks       { get; private set; }
    public bool           IsPaused              { get; private set; }

    public IReadOnlyDictionary<int, NodeHeartbeat> ActiveNodes => _activeNodes;
    public IReadOnlyList<DistributedTransaction>   TxHistory   => _txHistory;

    // ── DDS Readers ──────────────────────────────────────────────────────
    private readonly DdsReader<SystemStateTopic>        _stateReader;
    private readonly DdsReader<AssetInventoryTopic>     _inventoryReader;
    private readonly DdsReader<NodeHeartbeat>           _heartbeatReader;
    private readonly DdsReader<ClusterOpStatus>             _sysOpStatusReader;
    private readonly DdsReader<NodeOpCommand>           _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus>            _nodeOpStatusReader;
    private readonly DdsReader<TimePulseDescriptor>     _timePulseReader;
    private readonly DdsReader<SwitchTimeModeWireDto>   _timeModeReader;

    public ClusterUiCache(DdsParticipant participant) { /* construct all readers */ }

    public void Update()
    {
        // Drain all readers; update published state properties.
        // Process2PcNetworkTraffic() sniffs NodeOpCommand/NodeOpStatus to build TxHistory
        // without requiring direct ClusterMaster access.
        // IsPaused is inferred from SwitchTimeModeWireDto.Mode == Deterministic.
    }

    public void Dispose() { /* dispose all readers */ }
}
```

`Process2PcNetworkTraffic()` inserts a new `DistributedTransaction` entry when a
`NodeOpType.PrepareState` command is observed, caps `TxHistory` at 10 entries, and
appends `NodeResponses` as `NodeOpStatus` ACKs arrive.

### 7.4 ClusterScenarioPanel — Shared UI Component

`OrchestratorScenarioPanel.cs` is **renamed** to
`Hrot.ClusterRunner.Services.ClusterScenarioPanel.cs`.

Constructor changes:
```csharp
// Old: (ClusterMaster drillMaster, DdsWriter<ClusterOpRequest> sysOpWriter)
// New:
public ClusterScenarioPanel(DdsWriter<ClusterOpRequest> sysOpWriter,
                             ClusterUiCache uiCache,
                             Action? requestPause = null)
```

The `_drillMaster` field is removed entirely. All data (current state, active
transaction, active stories list, node roster) is read from `uiCache`. The seven render
sections — Status Banner, Drill Control, Checkpoint, Scenario, Replay, Stories, Archive
Management — remain structurally identical; their data source switches from ClusterMaster
properties to `ClusterUiCache` properties.

`ClusterScenarioPanel.Render(ClusterUiCache cache, bool disableAll)` is the public entry
point. Both `OrchestratorSubsystem.DrawUI()` and `IosSubsystem.DrawUI()` call it with
their independently-constructed `ClusterUiCache` instances.

### 7.5 OrchestratorSubsystem Refactored

After S0506:

```csharp
// Data plane (headless)
private ClusterMaster?                 _drillMaster;
private ModuleHostKernel?            _timeKernel;
private DistributedTimeCoordinator?  _timeCoordinator;

// Control plane (UI clients — no direct _drillMaster references in DrawUI)
private DdsWriter<ClusterOpRequest>?     _sysOpWriter;
private ClusterUiCache?              _uiCache;
private ClusterScenarioPanel?        _scenarioPanel;
```

`DrawUI()` reads only from `_uiCache` and `_scenarioPanel`. The `TestHook_ClusterMaster`
internal property is kept for E2E test access.

---

## 8. IOS Remote Cluster Control Panel

### 8.1 Time Ingress Handlers

Two lightweight `IIngressHandler` implementations added to `Hrot.ExCon`:

```csharp
public sealed class TimePulseIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<TimePulseDescriptor> _reader;
    private readonly Action<TimePulseDescriptor>    _onPulse;
    public void Poll() { using var l = _reader.Take(); foreach (var s in l) if (s.IsValid) _onPulse(s.Data); }
}

public sealed class TimeModeIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SwitchTimeModeWireDto> _reader;
    private readonly Action<SwitchTimeModeWireDto>    _onMode;
    public void Poll() { using var l = _reader.Take(); foreach (var s in l) if (s.IsValid) _onMode(s.Data); }
}
```

### 8.2 IIosLogic Time State and Commands

New members added to `IIosLogic` and implemented in `IosLogic`:

```csharp
// ── Observed from network ────────────────────────────────────────────
double MasterSimTime   { get; }
long   MasterWallTicks { get; }
float  MasterTimeScale { get; }
bool   IsPaused        { get; }

// ── Commands dispatched to Orchestrator ─────────────────────────────
void RequestPause();
void RequestResume();
void RequestStep();
void SetTimeScale(float scale);
```

`IosLogic` implements each method by writing a `ClusterOpRequest` via its
existing `_sysOpWriter`. The observed properties are updated by
`TimePulseIngressHandler` and `TimeModeIngressHandler`.

### 8.3 IosSubsystem Wiring

`IosSubsystem.Initialize`:
1. Construct `ClusterUiCache(_participant)`.
2. Construct `ClusterScenarioPanel(_sysOpWriter, _uiCache)`.
3. Register `TimePulseIngressHandler` and `TimeModeIngressHandler` in the IOS
   ingress handler list.

`IosSubsystem.DrawUI`:
```csharp
if (!ImGui.Begin("Cluster Control")) { ImGui.End(); return; }
_scenarioPanel?.Render(_uiCache!, disableAll: /* derived from uiCache */);
ImGui.End();
```

The IOS panel renders an **identical** cluster control UI to the Orchestrator panel:
Time Control, Status Banner, Drill Control, Checkpoint, Scenario, Replay, Stories,
and Archive Management — all dispatching commands over DDS, with no direct access to
any local service.

---

## 9. Storage Layout Reference

For implementers of the Archive pipeline (S0505):

```
NAS base path (e.g. \\NAS01\FDP)
├── scenarios/
│   └── <scenarioId>/          ← flat folder; all .json files broadcast to all nodes on Prefetch
│       ├── entities.json
│       ├── terrain.json
│       └── ...
└── recordings/
    └── <drillId>/             ← one file per node
        ├── node_100.fdp
        ├── node_200.fdp
        └── node_300.fdp

Local SSD per node (C:\FDP_Temp)
├── <scenarioId>/              ← mirrors NAS scenarios/<scenarioId>/
└── <drillId>/                 ← written by RecordingModule during RunningLive
    └── node_<nodeId>.fdp
```

Key invariants:
- **Scenarios:** NAS is authoritative. Prefetch = NAS → every node's SSD.
- **Recordings:** Local SSD is authoritative during RunningLive. ExportArchive = all nodes' SSDs → NAS recordings/<drillId>/. ImportArchive = NAS → each node's own SSD only.
- **Connection limit:** Orchestrator's SMB Pull Gateway is the only process that touches the NAS. Simulation nodes never open NAS connections.
- **Cancellation:** Any partial NAS write on export, or partial SSD write on import, is cleaned up by the catch-block in the gateway methods and the `Abort()` hook in `ReferenceArchiveHandler`.

---

## 10. Phase 5 Task Summary

| Task | Title | Key deliverables |
|------|-------|-----------------|
| **CGF1-S0501** | Orchestrator ImGui Window & 2PC History Overhaul | Beige title bar; `ImGui.Begin` wrapper; `DistributedTransaction.{PayloadJson,NodeResponses,SourceClusterState}`; 5-col scrollable 2PC table with full GUID, JSON tooltip, context menu, expandable node rows; `FormatPrettyJson`; "Old→New" banner |
| **CGF1-S0502** | Real Network Dispatch + ClusterMaster Fan-out | `DdsWriter<ClusterOpRequest>` in `OrchestratorSubsystem`; panel constructor updated; all direct `HandleClusterOpRequest` calls replaced; `ClusterMaster` fan-out loop (PrepareXxx + CommitState per step) |
| **CGF1-S0503** | Time Control Section + Remote Time Commands | `ClusterOpType.{StepTime,SetTimeScale}`; `ClusterMaster.TimeControlRequested` event; `OrchestratorSubsystem` event→`_timeCoordinator`; "Time Control" collapsing header; replay seek debounce + `.meta.json` duration cap |
| **CGF1-S0504** | Asset Combo Selection | `RefreshLocalAssets()` scanning `C:\FDP_Temp`; scenario/drill/story combos; auto-generated `StoryId`; refresh buttons |
| **CGF1-S0505** | Archive Export/Import Pipeline | `ClusterOpType.CancelOperation`; `_activeCancellations` dict in `ClusterMaster`; `PrefetchArchiveAsync` + cancellation in `StorageGatewayModule`; `ReferenceArchiveHandler`; Archive Management UI section with progress bar and Cancel button |
| **CGF1-S0506** | CQRS Decoupling: AssetInventoryTopic + ClusterUiCache | `AssetInventoryTopic` DDS struct; `ClusterMaster` publishes inventory every 5 s; `ClusterUiCache`; `OrchestratorScenarioPanel` → `ClusterScenarioPanel`; `OrchestratorSubsystem` uses cache |
| **CGF1-S0507** | IOS Remote Cluster Control Panel | `TimePulse`/`TimeMode` ingress handlers; `IIosLogic` time API; `IosSubsystem` wires `ClusterScenarioPanel`; IOS renders identical cluster control UI over DDS |

**Dependency order:** S0501 → S0502 → S0503 → S0504 → S0505 → S0506 → S0507.
S0505 depends on S0502 (fan-out fix must be in place before archive pipeline uses
the same mechanism). S0507 depends on S0506 (ClusterScenarioPanel must be
refactored before IOS can adopt it).
