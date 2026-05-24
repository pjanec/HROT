# BATCH-02: StorageProcessManager Unit Tests + Episode Extraction

**Batch Number:** BATCH-02  
**Tasks:** Corrective Task 0 (DEBT-01), TASK-S003  
**Phase:** Phase 1 completion  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 committed (already done)

---

## Onboarding & Workflow

### Developer Instructions

This batch has two parts:
- **Corrective Task 0**: Add the missing `StorageProcessManager` unit tests (DEBT-01 from BATCH-01 review). Also includes a minimal refactor to make `StorageProcessManager` unit-testable.
- **Task 1 (TASK-S003)**: Extract `EpisodeConsensusAggregator` + `EpisodeProcessManager`. This is the largest task of the batch.

### Required Reading (IN ORDER)

1. **Batch report template:** `.github/skills/developer/SKILL.md`
2. **Task definitions:** `.dev/cluster-master-refact/TASK-DETAIL.md` -- See TASK-S003
3. **Design document:** `.dev/cluster-master-refact/DESIGN.md` -- See §1.3 and §1.4
4. **Previous review:** `.dev/cluster-master-refact/reviews/BATCH-01-REVIEW.md`
5. **Previous report:** `.dev/cluster-master-refact/reports/BATCH-01-REPORT.md`
6. **Debt tracker:** `.dev/cluster-master-refact/DEBT-TRACKER.md`

### Source Code Locations

- **Primary code:** `Hrot/Subsystems/Hrot.Orchestrator/`
- **Unit tests:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/`
- **Shared CQRS events:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterCqrsEvents.cs`
- **Aggregator interface:** `Hrot/Subsystems/Hrot.Orchestrator/INodeResponseAggregator.cs`
- **Existing aggregator example:** `Hrot/Subsystems/Hrot.Orchestrator/StorageConsensusAggregator.cs`

### Build and Test Commands

```
dotnet build Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build
```

For Task 1 integration test:
```
dotnet test Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj --no-build --filter "EpisodeInjectionTests"
```

### Known Pre-Existing Test Failures (NOT your responsibility)

The following 3 tests fail on `HEAD` before your changes and are NOT in scope:
- `ClusterMasterArchiveTests.CancelOperation_CancelsActiveCts`
- `ClusterMasterFanOutTests.PayloadJson_PopulatedFromClusterOpRequest`
- `ClusterMasterPrefetchTests.PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion`

The 4 `ClusterMasterEpisodeTests` failures ARE in scope -- your Task 1 rewrites those tests.

### Report Submission

**When done, create:** `.dev/cluster-master-refact/reports/BATCH-02-REPORT.md`

**If you have questions, create:** `.dev/cluster-master-refact/questions/BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 implemented `StorageConsensusAggregator` (TASK-S001) and `StorageProcessManager` (TASK-S002). BATCH-01 review found:
- TASK-S001: fully approved
- TASK-S002: code correct but unit tests were missing (DEBT-01)

This batch fixes DEBT-01 and completes TASK-S003 (episode extraction).

---

## Corrective Task 0: StorageProcessManager Unit Tests (DEBT-01)

### 0a. Minimal Refactor -- StorageProcessManager testability

`GlobalContextClusterOpHandler` is `sealed` and requires a `DdsParticipant`. It cannot be used as a test double. To make `StorageProcessManager` properly unit-testable without changing the external DDS contract, replace the `GlobalContextClusterOpHandler?` constructor parameter with `Func<FileManifestEntry?>?`.

**File:** `Hrot/Subsystems/Hrot.Orchestrator/StorageProcessManager.cs` (MODIFY)

Change the constructor:
```csharp
// BEFORE:
public StorageProcessManager(
    FdpEventBus bus,
    StorageGatewayModule gateway,
    string nasBasePath,
    GlobalContextClusterOpHandler? contextHandler)

// AFTER:
public StorageProcessManager(
    FdpEventBus bus,
    StorageGatewayModule gateway,
    string nasBasePath,
    // TODO(TASK-P001): remove shim when GlobalContextProcessManager publishes manifest entry via bus
    Func<FileManifestEntry?>? getOrchestratorEntry)
```

Change the field:
```csharp
// BEFORE:
private readonly GlobalContextClusterOpHandler? _contextHandler;

// AFTER:
// TODO(TASK-P001): remove shim when GlobalContextProcessManager publishes manifest entry via bus
private readonly Func<FileManifestEntry?>? _getOrchestratorEntry;
```

Change the shim usage in `Tick()`:
```csharp
// BEFORE:
if (_contextHandler?.CommitManifestEntry != null)
{
    fullManifest.Insert(0, _contextHandler.CommitManifestEntry);
}

// AFTER:
var ownEntry = _getOrchestratorEntry?.Invoke();
if (ownEntry != null)
{
    fullManifest.Insert(0, ownEntry);
}
```

**File:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` (MODIFY)

Update the `StorageProcessManager` construction to pass a lambda shim:
```csharp
// BEFORE (approximate):
_storageProcessManager = new StorageProcessManager(bus, gateway, nasBasePath, contextHandler);

// AFTER:
_storageProcessManager = new StorageProcessManager(bus, gateway, nasBasePath,
    () => _globalContextHandler?.CommitManifestEntry);
```

Where `_globalContextHandler` is the `GlobalContextClusterOpHandler` field in `OrchestratorSubsystem`.

### 0b. Unit Tests

**File:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/StorageProcessManagerTests.cs` (NEW FILE)

Write 3 unit tests:

**SC1 -- Shim manifest entry is prepended (verifies transitional shim)**

Setup:
- Create a real temp source file (the "node file") at a known path.
- Create a real temp source file (the "orchestrator file", e.g. `Orchestrator.json`).
- Create a real temp NAS dir.
- Create a `FdpEventBus`, `StorageGatewayModule`.
- Create `StorageProcessManager` with `getOrchestratorEntry = () => new FileManifestEntry { SourceUnc = orchestratorFile, RelativeDest = "Orchestrator.json" }`.
- Publish `ClusterOpCompletedEvent { StatusCode = Success, ResultPayload = new List<FileManifestEntry> { nodeEntry } }` to the bus.
- `bus.SwapBuffers()`. Tick the manager. Wait for async pull: use `Task.Delay(2000)` or a spin-wait checking for file existence up to 3 seconds.

Assert: Both `nasDir/nodeFile.bin` AND `nasDir/Orchestrator.json` exist (both the node file AND the orchestrator shim entry were pulled to NAS).

Cleanup: delete temp dirs in `finally`.

**SC2 -- Null payload: no NAS pull**

Setup:
- Create a `FdpEventBus`, `StorageGatewayModule`, temp NAS dir.
- `getOrchestratorEntry = () => null`.
- Publish `ClusterOpCompletedEvent { StatusCode = Success, ResultPayload = null }`.
- `bus.SwapBuffers()`. Tick the manager.

Assert: NAS dir is empty (no files created). `PullToNasAsync` was not triggered (inferred by absence of any file in NAS dir after a short synchronous check).

**SC3 -- Empty manifest: no NAS pull**

Setup: same as SC2 but `ResultPayload = new List<FileManifestEntry>()` (empty list).

Assert: NAS dir is empty.

**Test class structure:**
```csharp
[Collection("OrchestratorTests")]
public sealed class StorageProcessManagerTests
{
    // Use Path.GetTempPath() + Path.GetRandomFileName() for temp dirs.
    // Always clean up in finally blocks.
}
```

SC4 (grep check) and SC5 (integration test) are already satisfied and do not require new test code.

---

## Task 1: TASK-S003 -- EpisodeConsensusAggregator and EpisodeProcessManager

Read `TASK-DETAIL.md#task-s003` carefully before starting. Full constraints and success conditions are defined there.

### 1a. Add EpisodeStateChangedEvent

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterCqrsEvents.cs` (MODIFY)

Add after `AssetInventoryUpdateEvent` (EventId 9017):
```csharp
/// <summary>
/// Published by <c>EpisodeProcessManager</c> after the active episode set changes.
/// Consumers (e.g. <c>ClusterUiCache</c>, tests) subscribe to this event instead of
/// reading internal state from any process manager.
/// </summary>
[EventId(9018)]
[DataPolicy(DataPolicy.NoRecord)]
public struct EpisodeStateChangedEvent
{
    /// <summary>Snapshot of all currently active episode IDs at time of publication.</summary>
    public HashSet<Guid> ActiveEpisodeIds;
}
```

### 1b. Add EpisodeConsensusPayload

**File:** `Hrot/Subsystems/Hrot.Orchestrator/EpisodeConsensusAggregator.cs` (NEW FILE)

Define `EpisodeConsensusPayload` and `EpisodeConsensusAggregator` in the same file:

```csharp
/// <summary>
/// Payload produced by <see cref="EpisodeConsensusAggregator"/> and carried by
/// <see cref="Fdp.Toolkit.Orchestration.ClusterOpCompletedEvent.ResultPayload"/>.
/// Consumed by <see cref="EpisodeProcessManager"/> to update active episode state.
/// </summary>
public sealed class EpisodeConsensusPayload
{
    public Guid EpisodeId { get; init; }
    public bool IsStart   { get; init; }
}
```

### 1c. Implement EpisodeConsensusAggregator

In the same file as `EpisodeConsensusPayload`:

```csharp
public sealed class EpisodeConsensusAggregator : INodeResponseAggregator
{
    public NodeOpType TargetOp { get; }

    public EpisodeConsensusAggregator(NodeOpType targetOp)
    {
        TargetOp = targetOp;
    }

    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        // Episode node responses carry a synthetic JSON with EpisodeConsensusPayload
        // (stored by ClusterMaster when collecting ACKs -- see ClusterMaster episode ACK path).
        foreach (var nodeDict in nodeResponses.Values)
        {
            if (nodeDict.TryGetValue(TargetOp, out var json) && !string.IsNullOrEmpty(json))
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<EpisodeConsensusPayload>(
                        json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }
            }
        }
        return null;
    }
}
```

### 1d. Implement EpisodeProcessManager

**File:** `Hrot/Subsystems/Hrot.Orchestrator/EpisodeProcessManager.cs` (NEW FILE)

```
/// Reads ClusterOpCompletedEvent, filters for EpisodeConsensusPayload results,
/// updates _activeEpisodes, publishes EpisodeStateChangedEvent.
/// Must NOT expose a public ActiveEpisodes property.
```

Key rules:
- Constructor: `EpisodeProcessManager(FdpEventBus bus)`
- `private readonly HashSet<Guid> _activeEpisodes = new();`
- On NAK (`ev.StatusCode.IsError()`): skip -- do NOT update `_activeEpisodes` and do NOT publish `EpisodeStateChangedEvent`
- On Success with `ResultPayload is EpisodeConsensusPayload p`:
  - If `p.IsStart`: `_activeEpisodes.Add(p.EpisodeId)`
  - Else: `_activeEpisodes.Remove(p.EpisodeId)`
  - Publish `EpisodeStateChangedEvent { ActiveEpisodeIds = new HashSet<Guid>(_activeEpisodes) }`
- No public `ActiveEpisodes` property. No other state-inspection surface.

### 1e. Extend ClusterMaster episode ACK path

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (MODIFY)

**Step 1: Extend `ManageEpisodeTask` to include `NodeResponses`**

Add a `NodeResponses` field to the existing `ManageEpisodeTask` inner class:
```csharp
private sealed class ManageEpisodeTask
{
    public Guid         RequestId;
    public bool         IsStart;
    public Guid         EpisodeId;
    public HashSet<int> RemainingNodeIds = new();
    // Synthetic per-node responses carrying episode context for the aggregator pipeline.
    public Dictionary<int, Dictionary<NodeOpType, string>> NodeResponses = new();
}
```

**Step 2: Store synthetic responses + call aggregator in ConsumeNodeOpStatuses**

In `ConsumeNodeOpStatuses`, find the `_pendingManageEpisodeTasks` block (around line 1359). Replace the entire success path so it:
1. Stores a synthetic JSON (serialized `EpisodeConsensusPayload`) as the node's response
2. After all ACKs, calls the registered aggregator (if any), then `PublishOpStatus`
3. Removes `_activeEpisodes.Add/Remove` calls entirely

```csharp
if (_pendingManageEpisodeTasks.TryGetValue(ev.TransactionId, out var episodeTask))
{
    if (ev.StatusCode.IsError())
    {
        _pendingManageEpisodeTasks.Remove(ev.TransactionId);
        FdpLog<ClusterMaster>.Warn(
            "[Orchestrator] ManageEpisode 2PC aborted for episode {0}: node {1} returned error {2}.",
            episodeTask.EpisodeId, ev.NodeId, ev.StatusCode);
        PublishOpStatus(episodeTask.RequestId, OrchestrationStatusCode.Rejected);
        continue;
    }

    // Store synthetic node response so the aggregator can reconstruct episode context.
    var syntheticJson = System.Text.Json.JsonSerializer.Serialize(
        new EpisodeConsensusPayload { EpisodeId = episodeTask.EpisodeId, IsStart = episodeTask.IsStart },
        new System.Text.Json.JsonSerializerOptions { IncludeFields = false });
    if (!episodeTask.NodeResponses.TryGetValue(ev.NodeId, out var nodeOpDict))
    {
        nodeOpDict = new Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>();
        episodeTask.NodeResponses[ev.NodeId] = nodeOpDict;
    }
    nodeOpDict[ev.Operation] = syntheticJson;

    episodeTask.RemainingNodeIds.Remove(ev.NodeId);
    if (episodeTask.RemainingNodeIds.Count == 0)
    {
        _pendingManageEpisodeTasks.Remove(ev.TransactionId);

        object? aggregated = null;
        if (_aggregators.TryGetValue(ev.Operation, out var agg))
            aggregated = agg.Aggregate(episodeTask.NodeResponses);

        PublishOpStatus(episodeTask.RequestId, OrchestrationStatusCode.Success, aggregated);
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] ManageEpisode 2PC complete for episode {0}: all node ACKs received.",
            episodeTask.EpisodeId);
    }
    continue;
}
```

**Step 3: Fix the zero-node-roster case in ProcessManageEpisodeIntent**

Find the `else` branch where `nodeIds.Count == 0` (around line 950):
```csharp
// REMOVE:
if (intent.IsStart) _activeEpisodes.Add(intent.EpisodeId);
else                _activeEpisodes.Remove(intent.EpisodeId);

// ADD (publish event so EpisodeProcessManager still fires):
var nodeOp = intent.IsStart ? Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode
                             : Fdp.Toolkit.Orchestration.NodeOpType.StopEpisode;
PublishOpStatus(requestId, OrchestrationStatusCode.Success,
    new EpisodeConsensusPayload { EpisodeId = intent.EpisodeId, IsStart = intent.IsStart });
```

**Step 4: Remove `_activeEpisodes` and `ActiveEpisodes`**

Remove:
- `private readonly HashSet<Guid> _activeEpisodes = new();` (around line 244)
- `public IReadOnlyCollection<Guid> ActiveEpisodes => _activeEpisodes;` (around line 251)

The compiler will show you the remaining references to fix.

### 1f. Update ClusterScenarioPanel

**File:** `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterScenarioPanel.cs` (MODIFY)

Find:
```csharp
private IReadOnlyCollection<Guid> EffectiveEpisodes
    => _master?.ActiveEpisodes ?? _uiCache.ActiveEpisodes;
```

Replace with:
```csharp
private IReadOnlyCollection<Guid> EffectiveEpisodes
    => _uiCache.ActiveEpisodes;
```

The `ClusterUiCache` already tracks episode state independently from bus events and is the correct read model for the UI. `ClusterMaster.ActiveEpisodes` no longer exists after this task.

### 1g. Wire in OrchestratorSubsystem

**File:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` (MODIFY)

In `Initialize()`, after registering `StorageConsensusAggregator`, add:
```csharp
_clusterMaster.RegisterAggregator(new EpisodeConsensusAggregator(Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode));
_clusterMaster.RegisterAggregator(new EpisodeConsensusAggregator(Fdp.Toolkit.Orchestration.NodeOpType.StopEpisode));
```

Add `_episodeProcessManager` field and instantiate:
```csharp
private EpisodeProcessManager? _episodeProcessManager;

// In Initialize():
_episodeProcessManager = new EpisodeProcessManager(_bus);
```

In `Update()`, after `_storageProcessManager?.Tick()`:
```csharp
_episodeProcessManager?.Tick();
```

### 1h. Rewrite ClusterMasterEpisodeTests

**File:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterEpisodeTests.cs` (MODIFY)

The 4 episode tests currently use `exercise.ActiveEpisodes` which no longer exists. Rewrite them to:
1. Set up both `ClusterMaster` AND `EpisodeProcessManager` ticking together
2. Register both `EpisodeConsensusAggregator` instances with the `ClusterMaster`
3. After ticking, read `EpisodeStateChangedEvent` from the bus instead of `exercise.ActiveEpisodes`

The `BootstrapToOperatingLive` helper must be updated to also return an `EpisodeProcessManager`.

**Test rewrite pattern:**

```csharp
private static (ClusterMaster master, EpisodeProcessManager episodeMgr, FdpEventBus bus) BootstrapToOperatingLive(int nodeId = 1)
{
    // ... existing bootstrap code ...
    var episodeMgr = new EpisodeProcessManager(bus);
    master.RegisterAggregator(new EpisodeConsensusAggregator(FdpNodeOpType.StartEpisode));
    master.RegisterAggregator(new EpisodeConsensusAggregator(FdpNodeOpType.StopEpisode));
    return (master, episodeMgr, bus);
}
```

Each test that previously asserted `Assert.Contains(episodeId, exercise.ActiveEpisodes)` must instead:
1. Call `episodeMgr.Tick()` after the final `exercise.Tick()`  
2. `bus.SwapBuffers()`
3. Read `bus.ReadManaged<EpisodeStateChangedEvent>().ToList()`
4. Assert that the list contains an event whose `ActiveEpisodeIds` contains the expected episode ID

For NAK tests (previously asserted `Assert.Empty(exercise.ActiveEpisodes)`), assert that NO `EpisodeStateChangedEvent` was published.

**IMPORTANT**: The tests also check for `Assert.True(intents.Any(), "ClusterMaster must fan out a StartEpisode ExecuteNodeOpIntent after ManageEpisode.")`. After the SwapBuffers pattern fix, the `ExecuteNodeOpIntent` must be read from the bus BEFORE the second SwapBuffers. Read it immediately after the first `exercise.Tick()` call (while the bus read buffer still contains the fan-out intent). See the existing test flow and fix the double-SwapBuffers timing issue.

The correct pattern for capturing the fan-out intent:
```csharp
exercise.HandleClusterOpRequest(new ClusterOpRequest { ... ManageEpisode ... });
bus.SwapBuffers();
exercise.Tick();
// Read the fan-out BEFORE the next SwapBuffers swaps away the read buffer
var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
    .Where(i => i.Operation == FdpNodeOpType.StartEpisode).ToList();
Assert.True(intents.Any(), "ClusterMaster must fan out a StartEpisode ...");
bus.SwapBuffers();
```

---

## Testing Requirements

**Minimum test counts for this batch:**
- `StorageProcessManagerTests`: 3 new tests (SC1-SC3)
- `ClusterMasterEpisodeTests`: 4 rewritten tests (all must PASS)
- New file `EpisodeAggregatorTests.cs` (or similar name) with unit tests for TASK-S003:
  - SC1: StartEpisode fan-out → node ACK → `EpisodeStateChangedEvent` contains episode ID
  - SC2: StopEpisode → `EpisodeStateChangedEvent` does NOT contain episode ID
  - SC3: NAK → no `EpisodeStateChangedEvent` published

**Tests must NOT:**
- Assert on `EpisodeProcessManager.ActiveEpisodes` (it must not exist)
- Assert on `ClusterMaster.ActiveEpisodes` (it must not exist)

**Tests that must still pass (not touched):**
- All 4 `StorageConsensusAggregatorTests`
- Integration: `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad`

---

## Success Criteria Checklist

Before submitting the report, verify:

- [ ] `StorageProcessManagerTests.cs` exists with 3 passing tests
- [ ] `EpisodeConsensusAggregator.cs` exists and implements `INodeResponseAggregator`
- [ ] `EpisodeProcessManager.cs` exists; no public `ActiveEpisodes` property
- [ ] `EpisodeStateChangedEvent` added to `ClusterCqrsEvents.cs` with `[EventId(9018)]`
- [ ] `ClusterMaster` has no `_activeEpisodes` field or `ActiveEpisodes` property
- [ ] `ClusterMaster` has no `_pendingManageEpisodeTasks` or `ManageEpisodeTask` (CHECK: the field `_pendingManageEpisodeTasks` must be removed; `ManageEpisodeTask` inner class must be removed if `NodeResponses` was added, OR kept/renamed if you used a different tracking approach)
- [ ] `ClusterScenarioPanel.EffectiveEpisodes` no longer references `_master?.ActiveEpisodes`
- [ ] `OrchestratorSubsystem` registers both `EpisodeConsensusAggregator` instances
- [ ] `OrchestratorSubsystem.Update()` ticks `EpisodeProcessManager` after `ClusterMaster`
- [ ] `dotnet test Hrot.Orchestrator.Tests.csproj --no-build`: 0 failures among the TASK-S003 and DEBT-01 tests (the 3 pre-existing non-episode failures are allowed to remain)
- [ ] `EpisodeInjectionTests` passes (if you can run `Hrot.SimHost.Integration.Tests`)

---

## Report Requirements

Submit `.dev/cluster-master-refact/reports/BATCH-02-REPORT.md` with:

1. **Summary**: what was done, any deviations from instructions
2. **Decisions made**: implementation choices not specified (e.g. naming, struct vs class for payload)
3. **Test results**: exact pass/fail counts from `dotnet test` output
4. **Known issues**: any tests still failing and why (pre-existing vs new)
5. **Files changed**: list of files modified/created
