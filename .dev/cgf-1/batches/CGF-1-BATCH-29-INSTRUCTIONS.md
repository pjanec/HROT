# CGF-1-BATCH-29 — CQRS Decoupling: AssetInventoryTopic + ClusterUiCache (S0506)

**Batch Number:** CGF-1-BATCH-29  
**Tasks:** CGF1-S0506 (CQRS Decoupling: AssetInventoryTopic + ClusterUiCache)  
**Phase:** Phase 5 — Operational UI, Real Network Dispatch & CQRS Architecture  
**Estimated Effort:** 14–18 hours  
**Design authority:** [CGF-1-ADDENDUM-3.md](../CGF-1-ADDENDUM-3.md) §7  
**Report target:** `.dev/cgf-1/reports/CGF-1-BATCH-29-REPORT.md`

---

## 1. Onboarding

### 1.1 Project Context
You are working on **Bagira** — a distributed military simulation system. The system runs
four node types: **Orchestrator** (DrillMaster), **SimHost** (muscle), **CGF** (brain),
and **IOS** (commander UI). All cluster state is published over CycloneDDS.

The guiding principle for this batch is **CQRS** (Command-Query Responsibility Segregation):
the Orchestrator UI and any remote UI (IOS, future nodes) must never hold a reference to
`DrillMaster` or other local C# services. They observe network state (`ClusterUiCache`)
and emit commands (`SysOpRequest`). This makes the same `ClusterScenarioPanel`
instantiable on any node.

### 1.2 Relevant Design Documents
- **[CGF-1-ADDENDUM-3.md](../CGF-1-ADDENDUM-3.md) §7** — primary authority for S0506
- **[CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0506** — work list and success conditions
- **[CGF-1-BATCH-28-REVIEW.md](../reviews/CGF-1-BATCH-28-REVIEW.md)** — last approved batch
- **[CGF-1-BATCH-28-REPORT.md](../reports/CGF-1-BATCH-28-REPORT.md)** — developer notes

### 1.3 Key Files You Will Touch

| File | Purpose |
|------|---------|
| `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs` | Add `AssetInventoryTopic` struct |
| `Bagira.Orchestrator/DrillMaster.cs` | Add inventory writer, `PublishAssetInventory()`, `NasBasePath` property |
| `Bagira.Runner/Services/ClusterUiCache.cs` | **New file** — 8-reader network projection |
| `Bagira.Runner/Services/OrchestratorScenarioPanel.cs` | Rename to `ClusterScenarioPanel.cs`; remove `_drillMaster`; switch to `ClusterUiCache` |
| `Bagira.Runner/Services/ClusterScenarioPanel.cs` | **New file** (renamed panel) |
| `Bagira.Runner/Services/OrchestratorSubsystem.cs` | Use `ClusterUiCache` + `ClusterScenarioPanel`; `DrawUI()` reads only cache |
| `Bagira.Runner.Tests/ClusterUiCacheTests.cs` | **New file** — cache unit tests |
| `Bagira.Runner.Tests/OrchestratorSubsystemTests.cs` | Update references from OrchestratorScenarioPanel → ClusterScenarioPanel |
| `Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs` | Rename to `ClusterScenarioPanelTests.cs` |

### 1.4 Current Test Baseline
- `Bagira.DDS.DataModel.Tests`: 45
- `Bagira.Orchestrator.Tests`: 60
- `Bagira.Runner.Tests`: 161

All 266 tests were passing after BATCH-28. Build the solution first:
`dotnet build IOS-IG-SimHost.sln -c Debug`

---

## 2. Task A — `AssetInventoryTopic` DDS Struct

**File:** `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs`

Add the following struct after `SystemStateTopic` (keep `[DdsIdlFile("bdc-sst-orchestration")]`
consistent with the existing topics):

```csharp
/// <summary>
/// Published by the Orchestrator every 5 seconds. Carries the NAS/local asset lists
/// so that any subscriber — including IOS — can populate asset combo-boxes purely over DDS,
/// with no direct reference to <see cref="Bagira.Orchestrator.DrillMaster"/>
/// or <see cref="Bagira.Orchestrator.StorageGatewayModule"/>.
/// </summary>
[DdsTopic("AssetInventory")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct AssetInventoryTopic
{
    /// <summary>Key: 0 = singleton cluster orchestrator.</summary>
    [DdsKey] public int NodeId;

    /// <summary>JSON-serialised <c>string[]</c> of locally available scenario directory names.</summary>
    [DdsManaged] public string LocalScenariosJson;

    /// <summary>JSON-serialised <c>string[]</c> of locally recorded drill directory names.</summary>
    [DdsManaged] public string LocalDrillsJson;

    /// <summary>JSON-serialised <c>string[]</c> of drill directory names archived on NAS.</summary>
    [DdsManaged] public string ArchivedDrillsJson;

    /// <summary>JSON-serialised <c>string[]</c> of local drills that are NOT yet on NAS.</summary>
    [DdsManaged] public string UnarchivedLocalDrillsJson;
}
```

`TransientLocal` QoS ensures late-joining subscribers immediately receive the latest
inventory sample.

After adding the struct, rebuild the DataModel project:
`dotnet build Bagira.DDS.DataModel/Bagira.DDS.DataModel.csproj -c Debug`
The source generator will emit the required partial class.

Add a schema pin test in `Bagira.DDS.DataModel.Tests/OrchestrationSchemaTests.cs` to
verify `AssetInventoryTopic` is present in the generated IDL:

```csharp
[Fact]
public void AssetInventoryTopic_IsRegisteredInIdl()
{
    // Verify AssetInventoryTopic is a codegen type in the orchestration IDL
    var types = typeof(AssetInventoryTopic).Assembly.GetTypes()
        .Where(t => t.Namespace == "Bagira.BDC.SSTD.Orchestration")
        .Select(t => t.Name)
        .ToArray();
    Assert.Contains("AssetInventoryTopic", types);
}
```

---

## 3. Task B — DrillMaster Publishes Asset Inventory

**File:** `Bagira.Orchestrator/DrillMaster.cs`

### B.1 — `NasBasePath` Property and Inventory Writer

Add to DrillMaster:
```csharp
public string NasBasePath => _nasBasePath;

private DdsWriter<AssetInventoryTopic>? _inventoryWriter;
private DateTime _lastInventoryScan = DateTime.MinValue;
```

Initialize in constructor:
```csharp
_inventoryWriter = new DdsWriter<AssetInventoryTopic>(_participant);
```

Dispose in `Dispose()`:
```csharp
_inventoryWriter?.Dispose();
_inventoryWriter = null;
```

### B.2 — `PublishAssetInventory()` + Tick Throttle

Add to existing `Tick()` method (after the existing drain logic):
```csharp
if ((DateTime.UtcNow - _lastInventoryScan).TotalSeconds >= 5.0)
{
    PublishAssetInventory();
    _lastInventoryScan = DateTime.UtcNow;
}
```

Add private method:
```csharp
private void PublishAssetInventory()
{
    var localScenarios = _gateway.ScanLocalScenarios(_nasBasePath);
    var localDrills    = _gateway.ScanLocalDrills(_nasBasePath);
    var archivedDrills = _gateway.ScanNasDrills(_nasBasePath);
    var unarchived     = localDrills.Except(archivedDrills).ToList();

    _inventoryWriter?.Write(new AssetInventoryTopic
    {
        NodeId                    = 0,
        LocalScenariosJson        = JsonSerializer.Serialize(localScenarios),
        LocalDrillsJson           = JsonSerializer.Serialize(localDrills),
        ArchivedDrillsJson        = JsonSerializer.Serialize(archivedDrills),
        UnarchivedLocalDrillsJson = JsonSerializer.Serialize(unarchived),
    });
}
```

Add required `using System.Text.Json;` and `using System.Linq;` if not present.

---

## 4. Task C — `ClusterUiCache`

**File:** `Bagira.Runner/Services/ClusterUiCache.cs` *(new)*

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;

namespace Bagira.Runner.Services;

/// <summary>
/// Network projection of cluster state — the CQRS read-model (CGF1-S0506).
///
/// <para>Constructs 8 DDS readers and maintains all published properties by draining
/// them on every <see cref="Update"/> call. No direct reference to
/// <see cref="DrillMaster"/> or any local service. Thread-unsafe; must be updated
/// from a single thread.</para>
/// </summary>
public sealed class ClusterUiCache : IDisposable
{
    // ── Published state ────────────────────────────────────────────────────────
    public DSMState    CurrentState           { get; private set; }
    public bool        IsBootstrapped         { get; private set; }
    public bool        HasInFlightTransaction  { get; private set; }

    public string[]    AvailableScenarios     { get; private set; } = Array.Empty<string>();
    public string[]    AvailableDrills        { get; private set; } = Array.Empty<string>();
    public string[]    ArchivedDrills         { get; private set; } = Array.Empty<string>();
    public string[]    UnarchivedLocalDrills  { get; private set; } = Array.Empty<string>();

    public double      MasterSimTime          { get; private set; }
    public long        MasterWallTicks        { get; private set; }
    public bool        IsPaused               { get; private set; }

    public IReadOnlyDictionary<int, NodeHeartbeat> ActiveNodes => _activeNodes;
    public IReadOnlyList<DistributedTransaction>   TxHistory   => _txHistory;

    // ── DDS Readers ────────────────────────────────────────────────────────────
    private readonly DdsReader<SystemStateTopic>      _stateReader;
    private readonly DdsReader<AssetInventoryTopic>   _inventoryReader;
    private readonly DdsReader<NodeHeartbeat>         _heartbeatReader;
    private readonly DdsReader<SysOpStatus>           _sysOpStatusReader;
    private readonly DdsReader<NodeOpCommand>         _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus>          _nodeOpStatusReader;
    private readonly DdsReader<TimePulseDescriptor>   _timePulseReader;
    private readonly DdsReader<SwitchTimeModeWireDto> _timeModeReader;

    // ── Internal state ─────────────────────────────────────────────────────────
    private readonly Dictionary<int, NodeHeartbeat>      _activeNodes = new();
    private readonly List<DistributedTransaction>         _txHistory   = new();
    private readonly Dictionary<Guid, DistributedTransaction> _inFlight = new();

    public ClusterUiCache(DdsParticipant participant)
    {
        _stateReader       = new DdsReader<SystemStateTopic>(participant);
        _inventoryReader   = new DdsReader<AssetInventoryTopic>(participant);
        _heartbeatReader   = new DdsReader<NodeHeartbeat>(participant);
        _sysOpStatusReader = new DdsReader<SysOpStatus>(participant);
        _nodeOpCmdReader   = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader= new DdsReader<NodeOpStatus>(participant);
        _timePulseReader   = new DdsReader<TimePulseDescriptor>(participant);
        _timeModeReader    = new DdsReader<SwitchTimeModeWireDto>(participant);
    }

    /// <summary>Drains all readers and updates the published state. Call once per frame.</summary>
    public void Update()
    {
        DrainSystemState();
        DrainInventory();
        DrainHeartbeats();
        DrainTimePulse();
        DrainTimeMode();
        Process2PcNetworkTraffic();
        DrainSysOpStatus();
    }

    public void Dispose()
    {
        _stateReader.Dispose();
        _inventoryReader.Dispose();
        _heartbeatReader.Dispose();
        _sysOpStatusReader.Dispose();
        _nodeOpCmdReader.Dispose();
        _nodeOpStatusReader.Dispose();
        _timePulseReader.Dispose();
        _timeModeReader.Dispose();
    }

    // ── Private drain methods ──────────────────────────────────────────────────

    private void DrainSystemState()
    {
        using var l = _stateReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            CurrentState    = s.Data.CurrentState;
            IsBootstrapped  = s.Data.CurrentState != DSMState.Standby;
        }
    }

    private void DrainInventory()
    {
        using var l = _inventoryReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            AvailableScenarios    = DeserializeStringArray(s.Data.LocalScenariosJson);
            AvailableDrills       = DeserializeStringArray(s.Data.LocalDrillsJson);
            ArchivedDrills        = DeserializeStringArray(s.Data.ArchivedDrillsJson);
            UnarchivedLocalDrills = DeserializeStringArray(s.Data.UnarchivedLocalDrillsJson);
        }
    }

    private void DrainHeartbeats()
    {
        using var l = _heartbeatReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            _activeNodes[s.Data.NodeId] = s.Data;
        }
    }

    private void DrainTimePulse()
    {
        using var l = _timePulseReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            MasterSimTime   = s.Data.SimTimeSnapshot;
            MasterWallTicks = s.Data.WallTicksUtc;
        }
    }

    private void DrainTimeMode()
    {
        using var l = _timeModeReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            IsPaused = s.Data.Mode == TimeSyncMode.Deterministic;
        }
    }

    private void Process2PcNetworkTraffic()
    {
        // Insert new transactions when PrepareState NodeOpCommand arrives
        using var cmdList = _nodeOpCmdReader.Take();
        foreach (var s in cmdList)
        {
            if (!s.IsValid) continue;
            if (s.Data.Operation != NodeOpType.PrepareState) continue;
            var txId = s.Data.TransactionId;
            if (!_inFlight.ContainsKey(txId))
            {
                var tx = new DistributedTransaction
                {
                    TransactionId  = txId,
                    TargetDsmState = s.Data.TargetState,
                };
                _inFlight[txId] = tx;
                _txHistory.Insert(0, tx);
                while (_txHistory.Count > 10) _txHistory.RemoveAt(_txHistory.Count - 1);
            }
            HasInFlightTransaction = _inFlight.Count > 0;
        }

        // Append NodeOpStatus ACKs to in-flight transactions
        using var statusList = _nodeOpStatusReader.Take();
        foreach (var s in statusList)
        {
            if (!s.IsValid) continue;
            if (_inFlight.TryGetValue(s.Data.TransactionId, out var tx))
                tx.NodeResponses[s.Data.NodeId] = s.Data.ResultJson ?? string.Empty;
        }
    }

    private void DrainSysOpStatus()
    {
        using var l = _sysOpStatusReader.Take();
        foreach (var s in l)
        {
            if (!s.IsValid) continue;
            // If the transaction that produced this SysOpStatus is in-flight, close it
            if (_inFlight.Remove(s.Data.RequestId, out var tx))
            {
                tx.Completed = s.Data.StatusCode == SysOpStatusCode.Completed;
            }
            HasInFlightTransaction = _inFlight.Count > 0;
        }
    }

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
```

**Notes:**
1. Check the existing `DdsReader<T>` API (e.g. `_stateReader.Take()`) by examining how it
   is used in other subsystems. The pattern used throughout the codebase is
   `using var lease = reader.Take(); foreach (var sample in lease) { if (!sample.IsValid) continue; ... }`.
2. `TimePulseDescriptor`, `SwitchTimeModeWireDto`, and `TimeSyncMode` may be in different
   namespaces — grep the codebase to find them.
3. `DistributedTransaction` already exists in `Bagira.Orchestrator`. Check if it has the
   `NodeResponses` dictionary, `SourceDsmState`, `PayloadJson`, and `Completed` fields
   added in earlier batches, or whether you need to add `Completed` as a new property.
4. `SysOpStatus`, `SysOpStatusCode`, `NodeOpCommand`, `NodeOpStatus` — find their exact
   field names in OrchestrationMessages.cs.
5. If `NodeOpCommand.TargetState` does not exist as a property, check the actual field name.

---

## 5. Task D — Rename OrchestratorScenarioPanel → ClusterScenarioPanel

**Old file:** `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`  
**New file:** `Bagira.Runner/Services/ClusterScenarioPanel.cs`

**Steps:**
1. Create `ClusterScenarioPanel.cs` by **copying** the contents of `OrchestratorScenarioPanel.cs`.
2. Rename the class from `OrchestratorScenarioPanel` to `ClusterScenarioPanel`.
3. Replace constructor signature:
   ```csharp
   // Old:
   public OrchestratorScenarioPanel(DrillMaster drillMaster,
                                     DdsWriter<SysOpRequest> sysOpWriter,
                                     StorageGatewayModule? gateway = null,
                                     Action? requestPause = null)
   // New:
   public ClusterScenarioPanel(DdsWriter<SysOpRequest> sysOpWriter,
                                ClusterUiCache uiCache,
                                Action? requestPause = null)
   ```
4. Remove `private readonly DrillMaster _drillMaster` field entirely.
5. Add `private readonly ClusterUiCache _uiCache` field.
6. Replace all data reads from `_drillMaster.*` with equivalent `_uiCache.*` reads:

   | Old (`_drillMaster.*`) | New (`_uiCache.*`) |
   |------------------------|-------------------|
   | `_drillMaster.BootstrapComplete` | `_uiCache.IsBootstrapped` |
   | `_drillMaster.HasInFlightTransaction` | `_uiCache.HasInFlightTransaction` |
   | `_drillMaster.CurrentDsmState` | `_uiCache.CurrentState` |
   | `_drillMaster.TransactionHistory` | `_uiCache.TxHistory` |
   | `_drillMaster.NodeRoster.ActiveNodes` | `_uiCache.ActiveNodes` |
   | `_drillMaster.ActiveStories` | from `_uiCache` (see note below) |

   **Active stories:** `DrillMaster.ActiveStories` is a set of story IDs. Check what
   `ClusterUiCache` should expose for this. If `SystemStateTopic` carries an active-stories
   list, read from there. Otherwise, add `IReadOnlySet<Guid> ActiveStories` to
   `ClusterUiCache` that is populated from a `ManageStory` ACK sniffer in
   `Process2PcNetworkTraffic`. For now, if not available, the Stories section can fall back
   to an empty set from the cache (the UI degrades gracefully to showing "no active stories").
   **Do not add a `_drillMaster` fallback** — keep the CQRS separation strict.

7. Update `Render(...)` signature:
   ```csharp
   // Old:
   public void Render(bool isPaused, float drillTime)
   // New:
   public void Render(ClusterUiCache cache, bool disableAll)
   ```
   Inside `Render`, read `cache.IsPaused`, `cache.MasterSimTime`, `cache.IsBootstrapped`,
   `cache.HasInFlightTransaction`, `cache.CurrentState`, `cache.TxHistory`,
   `cache.ActiveNodes`, and the asset arrays. The `disableAll` parameter replaces the
   derived `!bootstrapped || hasInFlight` logic.

8. Remove the `RefreshLocalAssets` fallback call that used to call `_gateway.ScanNasDrills`
   and replace asset arrays with reads from `cache.AvailableScenarios`, `cache.AvailableDrills`,
   `cache.ArchivedDrills`, `cache.UnarchivedLocalDrills`. The combos are now populated
   from the DDS feed.

9. Keep `GetReplayDuration` static helper (still used for local meta.json read on Load Replay).

10. **Delete** `OrchestratorScenarioPanel.cs` after all references are updated.

---

## 6. Task E — Refactor OrchestratorSubsystem

**File:** `Bagira.Runner/Services/OrchestratorSubsystem.cs`

### E.1 — Field Changes

Replace:
```csharp
// Old:
private OrchestratorScenarioPanel? _scenarioPanel;
```
With:
```csharp
private ClusterUiCache?         _uiCache;
private ClusterScenarioPanel?   _scenarioPanel;
```

### E.2 — Initialize

After constructing `_drillMaster` and `_sysOpWriter`, add:
```csharp
_uiCache       = new ClusterUiCache(_participant);
_scenarioPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache, requestPause: null);
```

Remove the old `OrchestratorScenarioPanel` construction which takes `_drillMaster`.

### E.3 — Update()

Add `_uiCache?.Update();` to the `Update()` method (alongside the existing `_drillMaster?.Tick()` call).

### E.4 — DrawUI()

**Goal:** `DrawUI()` must read only from `_uiCache` and `_scenarioPanel`. No direct
`_drillMaster.*` property access is allowed inside `DrawUI()`.

The `internal DrillMaster? TestHook_DrillMaster { get; }` property is kept for tests.

Rewrite `DrawUI()` to:
```csharp
public void DrawUI()
{
    if (_uiCache == null) return;
    if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }

    bool disableAll = !_uiCache.IsBootstrapped || _uiCache.HasInFlightTransaction;

    _scenarioPanel?.Render(_uiCache, disableAll);

    ImGui.End();
}
```

The existing inline node table, time control, 2PC history, alert overlay — all of these
are now rendered inside `ClusterScenarioPanel.Render()` reading from `_uiCache`. The
subsystem's `DrawUI()` becomes a thin wrapper that opens the ImGui window and delegates.

**If some sections cannot be moved to the panel yet** (e.g. if the node table reads
`_drillMaster.MandatoryNodes` which is not in `ClusterUiCache`), add the needed
properties to `ClusterUiCache` rather than keeping direct `_drillMaster` access in
`DrawUI()`. The constraint is strict: **zero `_drillMaster.*` reads in `DrawUI()`**.

### E.5 — Shutdown

Add to `Shutdown()`:
```csharp
_uiCache?.Dispose();
_uiCache = null;
```

---

## 7. Update Test Files

### 7.1 — Rename and Update Panel Tests

**File:** `Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs`  
→ rename to `Bagira.Runner.Tests/ClusterScenarioPanelTests.cs`

Update:
- Class name: `OrchestratorScenarioPanelTests` → `ClusterScenarioPanelTests`
- Constructor calls: replace `new OrchestratorScenarioPanel(drillMaster, writer, ...)` with
  `new ClusterScenarioPanel(writer, uiCache, ...)` where `uiCache` is a stub or real
  `ClusterUiCache` — see §7.3 for guidance.
- Any tests that set state via `drillMaster.*` fields must now either set it on the
  `ClusterUiCache` stub or use a real DDS participant to publish state.

For the initial rename, if a test is hard to rewrite, document it as P3 in the report.
Ensure all existing passing tests still compile and pass.

### 7.2 — Update OrchestratorSubsystemTests

**File:** `Bagira.Runner.Tests/OrchestratorSubsystemTests.cs`

Update any instantiation of `OrchestratorScenarioPanel` to `ClusterScenarioPanel`.
Verify that tests using `TestHook_DrillMaster` still compile.

### 7.3 — New ClusterUiCacheTests

**File:** `Bagira.Runner.Tests/ClusterUiCacheTests.cs` *(new)*

Four facts per success conditions:

**`Fact: ClusterUiCache_ReflectsSystemStateTopic`**
- Write a `SystemStateTopic` sample with `CurrentState = DSMState.LoadingLive`.
- Call `uiCache.Update()`.
- Assert `uiCache.CurrentState == DSMState.LoadingLive`.
- Assert `uiCache.IsBootstrapped == true`.

**`Fact: ClusterUiCache_Sniffs2PcTraffic`**
- Write a `NodeOpCommand` with `Operation = NodeOpType.PrepareState`.
- Call `uiCache.Update()`.
- Assert `uiCache.TxHistory.Count == 1`.
- Assert `uiCache.HasInFlightTransaction == true`.

**`Fact: ClusterUiCache_UpdatesInventoryFromTopic`**
- Write an `AssetInventoryTopic` with `LocalScenariosJson = "[\"scene1\"]"`.
- Call `uiCache.Update()`.
- Assert `uiCache.AvailableScenarios.Length == 1`.
- Assert `uiCache.AvailableScenarios[0] == "scene1"`.

**`Fact: ClusterUiCache_UpdatesIsPausedFromTimeMode`**
- Write a `SwitchTimeModeWireDto` with `Mode = TimeSyncMode.Deterministic`.
- Call `uiCache.Update()`.
- Assert `uiCache.IsPaused == true`.

---

## 8. Success Conditions Verification

All five success conditions from CGF-1-TASK-DETAIL.md §CGF1-S0506 must be covered:

1. **`Fact: AssetInventoryTopic published by DrillMaster`** — unit test: tick DrillMaster
   for 6 seconds (advance internal time mock or use `Thread.Sleep(6000)` + `Tick()`);
   read `AssetInventoryTopic` via a `DdsReader`; assert at least one sample received.

2. **`Fact: ClusterUiCache reflects SystemStateTopic`** — see §7.3.

3. **`Fact: ClusterUiCache sniffs 2PC traffic`** — see §7.3.

4. **`Fact: OrchestratorSubsystem.DrawUI has no _drillMaster reads`** — use
   `grep_search` on `OrchestratorSubsystem.cs` to confirm the `DrawUI()` method body
   contains no `_drillMaster.` (except `TestHook_DrillMaster`). Document the result in
   your report. This is a static-analysis success condition.

5. **`Fact: ClusterScenarioPanel compiles with ClusterUiCache`** — the solution must
   build with zero errors after the rename/refactoring. Confirm with
   `dotnet build IOS-IG-SimHost.sln -c Debug`.

6. **`Fact: No regression in E2E DSM test suite`** — all existing `DsmE2eScriptTests`
   still pass (they are in `Bagira.Runner.Tests`).

---

## 9. Mandatory Workflow: Test-Driven Task Progression

> **COPY THIS VERBATIM — do not summarise or paraphrase:**
>
> For each task:
> 1. Write the test first (or write test structure).
> 2. Implement code until the test passes.
> 3. Run `dotnet test <project>.Tests.csproj -c Debug --no-build --logger "console;verbosity=quiet"` and confirm green before moving to the next task.
> 4. If a test was already passing before your code change, verify it still passes after.
> 5. Never leave a failing test and move on.
> 6. All success conditions in §8 must be covered by at least one `[Fact]`.

---

## 10. Developer Insights — Required Questions

In your report, explicitly answer:

1. **What issues were encountered?** (API mismatches, missing fields, compilation errors)
2. **What weak points were spotted in the codebase?** (patterns that break encapsulation, missing abstractions)
3. **What design decisions were made beyond the spec?** (e.g. how did you handle ActiveStories in the panel?)
4. **How did you handle fields not yet in `ClusterUiCache` (e.g. MandatoryNodes, ActiveStories)?** Describe the strategy.
5. **DdsReader API:** What exact API did you find for reading DDS samples in this codebase?

---

## 11. Report Format

Write your completion report to `.dev/cgf-1/reports/CGF-1-BATCH-29-REPORT.md` with these sections:

```markdown
# CGF-1-BATCH-29 Report

## Tasks Completed
- [ ] A: AssetInventoryTopic DDS struct
- [ ] B: DrillMaster publishes inventory (NasBasePath, _inventoryWriter, Tick throttle)
- [ ] C: ClusterUiCache (8 readers, Update(), Dispose())
- [ ] D: OrchestratorScenarioPanel → ClusterScenarioPanel refactor
- [ ] E: OrchestratorSubsystem uses ClusterUiCache + ClusterScenarioPanel
- [ ] Tests: all 6 success conditions covered

## Test Counts (before → after)
- Bagira.DDS.DataModel.Tests: 45 →
- Bagira.Orchestrator.Tests:  60 →
- Bagira.Runner.Tests:        161 →

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions
### ActiveStories / Missing ClusterUiCache Fields Strategy
### DdsReader API

## Open Items / Risks
```

---

## 12. Important Notes

- **Do not break S0507 preconditions.** S0507 will require that `ClusterScenarioPanel`
  and `ClusterUiCache` exist in `Bagira.Runner.Services` with the exact constructor
  signatures defined here. Do not deviate.

- **`Bagira.Runner.Tests` collection isolation:** The existing `[Collection("RunnerTests")]`
  or equivalent isolation attributes must be preserved for DDS-based tests.

- **TimePulseDescriptor:** Find the exact field name for sim time (likely `SimTimeSnapshot`)
  and wall time (`WallTicksUtc`) by searching the codebase. Do not guess.

- **SwitchTimeModeWireDto / TimeSyncMode:** Find these types by grepping. They are used in
  `OrchestratorSubsystem.cs` already — look at line ~130 for the existing reference.

---

*Good luck. Do not stop for questions unless there is a breaking architectural conflict.
Implement, test, and report.*
