# BATCH-04 Instructions

**Batch:** BATCH-04  
**Tasks:** TASK-P001 (GlobalContextProcessManager) + TASK-P002 (AssetPrefetchProcessManager)  
**Phase:** 3 — Persistence and Prefetch Extractions  
**Author:** Dev Lead  

---

## Context

Phases 1 and 2 are complete. `ClusterMaster` no longer owns episode state, NAS-pull I/O,
branch time-freezing, or seek clock-snap. What remains is:
1. `_globalContextHandler` / `SetGlobalContextHandler` / `PrepareAsync+Commit` calls
2. `_pendingPrefetch` / `DrainPendingPrefetch` / `ExecutePrefetchScenario` polling loop

These two extractions form Phase 3 and are the final tasks in this refactoring.

---

## TASK-P001: GlobalContextProcessManager

### Goal

Create `GlobalContextProcessManager` that owns `GlobalContextClusterOpHandler`. Remove
`_globalContextHandler`, `SetGlobalContextHandler`, and all `_globalContextHandler.*` call sites
from `ClusterMaster`. Update `StorageProcessManager` to drop its transitional shim.

### New file

**`Hrot/Subsystems/Hrot.Orchestrator/GlobalContextProcessManager.cs`**

```
namespace Hrot.Orchestrator;

public sealed class GlobalContextProcessManager
```

Constructor: `(FdpEventBus bus, GlobalContextClusterOpHandler handler)`

Internal state:
- `_pendingScenarioId`: `string?` — captured from most recent `TransitionStateIntent`
- `_pendingExerciseId`: `Guid` — captured from most recent `TransitionStateIntent`

`Tick()` logic:
1. Read `TransitionStateIntent` events from the bus. For each event, store
   `_pendingScenarioId = intent.ScenarioId` and `_pendingExerciseId = intent.ExerciseId`.
   (This captures the context needed for the commit step regardless of ordering.)
2. Read `ExecuteStorageOpIntent` events from the bus. For each where
   `Operation == StorageOpType.SaveScenario`:
   - Build `localCmd = ClusterNodeOpBuilder.LocalContextCmd(NodeOpType.SerializeLocal, Guid.NewGuid(), exerciseIdJson)`
     where `exerciseIdJson = intent.ExerciseId != Guid.Empty ? JsonSerializer.Serialize(new { ExerciseId = intent.ExerciseId }) : string.Empty`
   - Call `handler.PrepareAsync(localCmd, CancellationToken.None).ContinueWith(t => { if (!t.IsFaulted) { handler.Commit(localCmd, null); PublishManifestReady(handler.CommitManifestEntry); } }, TaskScheduler.Default)`
3. Read `ClusterStateTransitionedEvent` events from the bus. For each where
   `NewStateId == ClusterState.LoadingLive || NewStateId == ClusterState.LoadingEdit`:
   - If `_pendingScenarioId` is null, log a warning and skip.
   - Build `localPayload = JsonSerializer.Serialize(new NodeTransitionPayloadDto(TargetState: (ClusterState)ev.NewStateId, ScenarioId: _pendingScenarioId, ExerciseId: _pendingExerciseId), OrchestrationJsonOptions.Default)`
   - Call `handler.Commit(ClusterNodeOpBuilder.LocalContextCmd(NodeOpType.CommitState, Guid.NewGuid(), localPayload), null)`
   - Clear `_pendingScenarioId = null`

Private helper: `PublishManifestReady(FileManifestEntry? entry)` — if entry is non-null, publishes
`GlobalContextManifestReadyEvent { Entry = entry }` to the bus.

**Note on `OnContextLoaded`:** `GlobalContextClusterOpHandler.OnContextLoaded` is a delegate.
`OrchestratorSubsystem` subscribes to it for seeding `MasterSyncController`. This subscription
stays in `OrchestratorSubsystem` — do NOT move it into the process manager.

### New event

Define **`GlobalContextManifestReadyEvent`** in
`Hrot/Subsystems/Hrot.Orchestrator/OrchestratorInternalEvents.cs` (create if absent):

```csharp
namespace Hrot.Orchestrator;

/// <summary>
/// Published by <see cref="GlobalContextProcessManager"/> after the local
/// Orchestrator.json has been serialized and committed.
/// Consumed by <see cref="StorageProcessManager"/> to prepend the orchestrator's
/// own manifest entry before the NAS pull.
/// </summary>
internal struct GlobalContextManifestReadyEvent
{
    public FileManifestEntry Entry;
}
```

No `[EventId]` needed — internal bus event.

### Changes to `StorageProcessManager`

1. Remove the `Func<FileManifestEntry?>? _getOrchestratorEntry` field and constructor parameter.
2. Add a `_pendingOrchestratorEntry: FileManifestEntry?` field.
3. In `Tick()`, BEFORE reading `ClusterOpCompletedEvent`, read
   `GlobalContextManifestReadyEvent` events from the bus. For each, set
   `_pendingOrchestratorEntry = ev.Entry`.
4. In the `ClusterOpCompletedEvent` handler, replace the shim call
   `_getOrchestratorEntry?.Invoke()` with `_pendingOrchestratorEntry`. After using it, clear
   `_pendingOrchestratorEntry = null`.
5. Update constructor signature (remove `getOrchestratorEntry` parameter).

### Changes to `ClusterMaster`

Remove:
- `private GlobalContextClusterOpHandler? _globalContextHandler;` field
- `public void SetGlobalContextHandler(GlobalContextClusterOpHandler handler)` method
- The entire `if (_globalContextHandler != null) { ... PrepareAsync ... Commit ... }` block in
  `ProcessStorageOpIntent` (SaveScenario case)
- The entire `if (_globalContextHandler != null && (LoadingLive || LoadingEdit)) { Commit(...) }`
  block in `ProcessTransitionStateIntent`

Also remove the `SetMasterSync` Obsolete no-op stub (DEBT-04).

### Changes to `OrchestratorSubsystem`

1. Construct `GlobalContextProcessManager` in `Initialize()`:
   ```csharp
   var ctxManager = new GlobalContextProcessManager(_bus, contextHandler);
   ```
2. Register its `Tick()` in `Update()` — call it BEFORE `_master.Tick()`.
3. Remove the lambda shim passed to `StorageProcessManager`:
   Old: `new StorageProcessManager(_bus, _gateway, _nasBasePath, () => contextHandler?.CommitManifestEntry)`
   New: `new StorageProcessManager(_bus, _gateway, _nasBasePath)`
4. Remove the `SetGlobalContextHandler` call.

### Changes to `StorageProcessManagerTests`

Update to not pass `getOrchestratorEntry`. Existing tests should still pass without modification
beyond constructor call.

### Changes to `ClusterMasterContextHandlerTests`

Replace `exercise.SetGlobalContextHandler(handler)` with constructing
`GlobalContextProcessManager(bus, handler)` and calling its `Tick()` in the test loop.
Test still verifies that `OnContextLoaded` fires with the correct wall ticks.

### Success Conditions

SC1. `StorageProcessManager` constructor has **no** `Func<FileManifestEntry?>` parameter.

SC2. `ClusterMaster` has zero references to `GlobalContextClusterOpHandler` (compiler
verification — the project builds without referencing the type at all from `ClusterMaster.cs`).

SC3. Test: publish `ExecuteStorageOpIntent(SaveScenario)` → tick `GlobalContextProcessManager` →
`GlobalContextManifestReadyEvent` is published → `StorageProcessManager` captures the entry →
when `ClusterOpCompletedEvent(manifest)` is later published, the orchestrator entry is included
in the combined manifest passed to `PullToNasAsync`. (Can be verified with a spy delegate or
simple stub gateway.)

SC4. Test: existing `ClusterMasterContextHandlerTests.TransitionState_LoadingLive_InvokesLocalContextHandler`
passes after being updated to use `GlobalContextProcessManager` instead of `SetGlobalContextHandler`.

SC5. Build passes. Run:
```
dotnet build Hrot/Subsystems/Hrot.Orchestrator/Hrot.Orchestrator.csproj
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build
```
Expect: all previously passing tests still pass. No new failures beyond the 3 pre-existing.

---

## TASK-P002: AssetPrefetchProcessManager

### Goal

Create `AssetPrefetchProcessManager` that owns the async `PrefetchScenarioAsync` call. Remove
`_pendingPrefetch`, `PendingPrefetchOp`, `DrainPendingPrefetch()`, and `ExecutePrefetchScenario()`
from `ClusterMaster`. Replace the polling `DrainPendingPrefetch()` in `ClusterMaster.Tick()` with
a `ProcessPrefetchStagingCompleted()` reader that reacts to a bus event.

### New events

Define the following in `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorInternalEvents.cs`:

```csharp
/// <summary>
/// Published by <see cref="ClusterMaster"/> when a PrefetchScenario operation step is
/// encountered in a trajectory or a standalone PrefetchScenario op request is received.
/// Consumed by <see cref="AssetPrefetchProcessManager"/>.
/// </summary>
internal struct ExecutePrefetchIntent
{
    /// <summary>Original cluster op request ID. Used to report failure.</summary>
    public Guid RequestId;
    /// <summary>Logical scenario identifier (sub-directory under NAS root).</summary>
    public string ScenarioId;
    /// <summary>Active node IDs captured at fan-out time (for PrefetchFiles fan-out).</summary>
    public List<int> ActiveNodeIds;
}

/// <summary>
/// Published by <see cref="AssetPrefetchProcessManager"/> when the gateway
/// <c>PrefetchScenarioAsync</c> task completes (success or failure).
/// Consumed by <see cref="ClusterMaster"/> to drive the PrefetchFiles fan-out or
/// report a timeout failure.
/// </summary>
internal struct PrefetchStagingCompletedEvent
{
    public Guid   RequestId;
    public string ScenarioId;
    public bool   IsSuccess;
    /// <summary>Active node IDs to fan out PrefetchFiles to on success.</summary>
    public List<int> ActiveNodeIds;
}
```

### New file

**`Hrot/Subsystems/Hrot.Orchestrator/AssetPrefetchProcessManager.cs`**

```
namespace Hrot.Orchestrator;

public sealed class AssetPrefetchProcessManager
```

Constructor: `(FdpEventBus bus, StorageGatewayModule gateway, string nasBasePath)`

`Tick()` logic:
1. Read `ExecutePrefetchIntent` events from the bus. For each:
   - If `gateway` is null or `nasBasePath` is blank, publish
     `PrefetchStagingCompletedEvent { RequestId, ScenarioId, IsSuccess = false, ActiveNodeIds = []}`
     and log a warning. (This matches the current "skipped" path that also calls
     `DrainPendingPrefetch` with null result — adjust to fail explicitly so the op resolves.)
   - Otherwise:
     - Capture `targets = BuildNodeDistributionTargets(intent.ActiveNodeIds, intent.ScenarioId)`
       — a helper that builds `NodeDistributionTarget` list from node IDs using the
       `C:\FDP_Temp\<scenarioId>\` convention (same logic as `ClusterMaster.BuildNodeDistributionTargets`)
     - Start `gateway.PrefetchScenarioAsync(intent.ScenarioId, targets, nasBasePath)`
     - Attach `.ContinueWith(task => { var success = !task.IsFaulted && !task.IsCanceled && task.Result.FailureCount == 0; PublishStagingCompleted(intent, success); }, TaskScheduler.Default)`
     - Log start of prefetch.

Private helper `BuildNodeDistributionTargets(List<int> nodeIds, string scenarioId)`:
- Returns `List<NodeDistributionTarget>` — one entry per node, with `DestinationPath = $@"C:\FDP_Temp\{scenarioId}\"` (same convention as the existing `ClusterMaster.BuildNodeDistributionTargets`).
- This is moved/copied from `ClusterMaster`.

Private helper `PublishStagingCompleted(ExecutePrefetchIntent intent, bool isSuccess)`:
- Publishes `PrefetchStagingCompletedEvent { RequestId = intent.RequestId, ScenarioId = intent.ScenarioId, IsSuccess = isSuccess, ActiveNodeIds = intent.ActiveNodeIds }` to the bus.

**IMPORTANT:** `Tick()` must NOT contain any `Task.IsCompleted` poll, `Task.Wait()`, or
`Task.Result` access outside of the `ContinueWith` callback. All completion handling is reactive
(via `ContinueWith`).

### Changes to `ClusterMaster`

**Remove:**
- `private sealed class PendingPrefetchOp { ... }` inner class
- `private PendingPrefetchOp? _pendingPrefetch;` field
- `private void DrainPendingPrefetch() { ... }` method
- `private void ExecutePrefetchScenario(string scenarioId, Guid requestId) { ... }` method
- `private List<NodeDistributionTarget> BuildNodeDistributionTargets(string scenarioId) { ... }` method
- The call to `DrainPendingPrefetch();` in `Tick()`
- The call to `ExecutePrefetchScenario(...)` in `ProcessTransitionStateIntent`
- The call to `ExecutePrefetchScenario(...)` in `ProcessClusterOpRequests`

**Replace `ExecutePrefetchScenario(scenarioId, requestId)` call sites with:**
```csharp
_bus.PublishManaged(new ExecutePrefetchIntent
{
    RequestId    = requestId,
    ScenarioId   = scenarioId,
    ActiveNodeIds = new List<int>(_roster.ActiveNodes.Keys),
});
```

This applies in two places:
1. The `PrefetchScenario` OperationStep loop in `ProcessTransitionStateIntent`
2. The `ClusterOpType.PrefetchScenario` case in `ProcessClusterOpRequests`

**Add `ProcessPrefetchStagingCompleted()` in `ClusterMaster.Tick()` in place of `DrainPendingPrefetch()`:**

In `Tick()`, replace `DrainPendingPrefetch();` with a call to a new private method
`ProcessPrefetchStagingCompleted()`:

```csharp
private void ProcessPrefetchStagingCompleted()
{
    foreach (var ev in _bus.ReadManaged<PrefetchStagingCompletedEvent>())
    {
        if (!ev.IsSuccess)
        {
            FdpLog<ClusterMaster>.Error(
                "[Orchestrator] PrefetchScenario for '{0}' failed — publishing Timeout for request {1}.",
                ev.ScenarioId, ev.RequestId);
            PublishOpStatus(ev.RequestId, OrchestrationStatusCode.Timeout);
            continue;
        }

        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] PrefetchScenario for '{0}' succeeded — fanning out PrefetchFiles to {1} node(s).",
            ev.ScenarioId, ev.ActiveNodeIds.Count);
        FanOutNodeOp(NodeOpType.PrefetchFiles, Guid.NewGuid(),
            new PrefetchHandlerPayload(ev.ScenarioId), ev.ActiveNodeIds);
    }
}
```

### Changes to `OrchestratorSubsystem`

1. Construct `AssetPrefetchProcessManager` in `Initialize()`.
2. Register its `Tick()` in `Update()` — call it BEFORE `_master.Tick()`.
3. Remove construction of `StorageGatewayModule` from `ClusterMaster.SetStorageGateway` path
   (if applicable; the `_gateway` field stays in ClusterMaster for asset inventory scanning only).

### Changes to `ClusterMasterPrefetchTests`

The pre-existing failure `PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion`
needs to be rewritten for the new event-driven flow:
1. Setup: create `ClusterMaster` + `AssetPrefetchProcessManager` sharing the same bus.
2. Publish `ExecutePrefetchIntent` to the bus.
3. Tick `AssetPrefetchProcessManager` (picks up intent, fires gateway, ContinueWith fires
   synchronously for fake immediate-complete task).
4. Tick `ClusterMaster` (reads `PrefetchStagingCompletedEvent`, fans out `PrefetchFiles`).
5. Assert `PrefetchFiles` fan-out happened.

### Success Conditions

SC1. Test: see above rewrite for `PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion`.
Assert `ExecuteNodeOpIntent(PrefetchFiles)` fan-out fired after staging completes.

SC2. Test: faulted gateway → `PrefetchStagingCompletedEvent(IsSuccess=false)` →
`ClusterOpCompletedEvent(Timeout)` published. No `PrefetchFiles` fan-out.

SC3. Compiler verification: `ClusterMaster` does not contain `_pendingPrefetch`,
`PendingPrefetchOp`, `DrainPendingPrefetch`, or `ExecutePrefetchScenario` after this task.
Build passes.

SC4. `ClusterMaster.Tick()` does not call `DrainPendingPrefetch`. Compiler verification.

SC5. Run full test suite:
```
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build
```
Expect: all previously passing tests still pass. The previously failing
`PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion` now passes.
Total: at most 2 pre-existing failures (Archive + FanOut).

---

## Invariants

- Preserve all existing comments exactly unless they are wrong.
- Do not normalize Unicode or change encoding.
- Only change lines required for the functional fix.
- Do not add docstrings or comments beyond what is specified here.

## Build Verification

After completing both tasks, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP
dotnet build Hrot/Subsystems/Hrot.Orchestrator/Hrot.Orchestrator.csproj
dotnet build Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build
```

Include the build and test output in your BATCH-04-REPORT.md.

## Report

Write `.dev/cluster-master-refact/reports/BATCH-04-REPORT.md` documenting:
- Files created, files modified
- Each test written and its result
- Any deviations from these instructions with justification
- Build + test output
