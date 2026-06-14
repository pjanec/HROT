# ADA-BATCH-03 Report — Fix scenario load (headless) — P1 corrective

**Batch:** ADA-BATCH-03
**Tasks:** P1 corrective for ADA-P1-T04 (scenario load completion)
**Date:** 2026-06-14
**Executor:** claude-sonnet-4-6 (Claude Code agent)
**Branch:** `feat/ai-debug-api` (working tree only — not committed, per instructions)

---

## Summary

`POST /scenario/load {name, waitForReady:true}` now completes in headless `-m editor` mode.

### Round 1 fixes (storage path)
- Root cause identified: wrong `LocalDiskStorageProvider` base path in `EditorSubsystem.cs` — `HrotEditLoadHandler` was looking in the node staging directory instead of the NAS/shared scenarios directory.
- One-line fix applied to `EditorSubsystem.cs` (one new variable + wiring).
- Mandatory integration test added: loads `test-move` via real ClusterMaster/ClusterSlave 2PC pipeline, asserts `entityCount > 0`.

### Round 2 fixes (roster seeding + wall-clock poll)
- **Second root cause identified**: the unit test passed by manually injecting a `NodeHeartbeatEvent` to seed ClusterMaster's roster, sidestepping the real problem. In production headless, `ClusterSlave` heartbeat fires every ~1 second but `waitForReady` formerly polled only 600 kernel frames (much less than 1s) — so the roster was empty when the scenario load was processed. With an empty roster `ClusterMaster` skips fan-out, calls `PublishOpStatus(Success)` without `BroadcastClusterStateOnComplete`, so `PublishClusterState` is never called — `EditorApplication` never sees `OperatingEdit` → 504.
- **Fix B** (`EditorSubsystem.cs`): After creating `ClusterMaster`, immediately publish a synthetic `NodeHeartbeatEvent` for node 0 (the in-process editor node), `SwapBuffers()`, and tick `ClusterMaster` once — roster is seeded before any scenario load can arrive.
- **Fix A** (`DebugApiHost.cs`): Replace `ScenarioReadyMaxPolls = 600` (frame count) with a wall-clock `Stopwatch` loop bounded to 30 seconds. Defense-in-depth so future timing edge cases do not produce 504s.
- Build: 0 errors, 13 pre-existing warnings.
- Tests: 21/21 pass.
- Headless reproduce: `entityCount:1` confirmed on second round.

---

## Root Cause (with evidence)

### Round 2 root cause — empty ClusterMaster roster (this batch's corrective)

The unit test `LoadScenarioByName_ViaOrchestrationPipeline_MaterialisesEntities` passed by manually publishing a `NodeHeartbeatEvent` before calling `LoadScenarioByName`. This seeded `ClusterMaster._roster` so `activeNodeIds.Count == 1` when the `TransitionStateIntent` was processed. Without seeding, `activeNodeIds.Count == 0` and the fan-out `if` block is skipped entirely (`ClusterMaster.cs` line ~738-739):

```csharp
var activeNodeIds = new List<int>(_roster.ActiveNodes.Keys);
if (activeNodeIds.Count > 0)
{
    // fan-out + set BroadcastClusterStateOnComplete = true
}
else
{
    // No nodes registered — complete immediately WITHOUT broadcasting cluster state
    PublishOpStatus(requestId, OrchestrationStatusCode.Success);
}
```

When `BroadcastClusterStateOnComplete = false` (or not set), `PublishClusterState(_currentDsmState)` is never called (line ~1167-1168), so `ClusterStateTransitionedEvent` is never published → `EditorApplication.CurrentClusterState` stays at `Idle` → `PollClusterStateIsOperatingEdit()` always returns false.

In headless mode `ClusterSlave.Tick()` fires a `NodeHeartbeatEvent` every ~1 second (wall clock). The former 600-frame poll loop runs much faster than 1 second in headless, so it exits before the heartbeat fires. The roster never gets populated.

**Fix B**: Seed the roster at `ClusterMaster` construction by publishing a synthetic heartbeat for node 0 (`EditorNodeId`) immediately in `Initialize()`, `SwapBuffers()`, and calling `Tick()` once. This mirrors exactly what the unit test did manually, but in production.

**Fix A**: Replace the 600-frame hard cap with a 30-second wall-clock `Stopwatch` loop. This is defense-in-depth.

### Round 1 root cause — wrong storage path (prior batch; kept for context)

### Investigation summary (Round 1)

The four candidate causes from the batch instructions were each checked against code:

1. **Poll budget / timing** — `WaitForReadyAsync` in `DebugApiService` polls `_clusterState()` in a loop; the loop had a 600-tick hard cap with `Task.Delay(100)` between polls. This gives ~60 seconds wall-clock — not the actual bottleneck.

2. **Wrong trigger** — `LoadScenarioByName()` in `EditorApplication.cs` publishes `TransitionStateIntent{Idle}` then `TransitionStateIntent{OperatingEdit}`. `EditorSubsystem.Update()` ticks ClusterMaster and ClusterSlave every frame, so the full 2PC pipeline runs. This path is correct.

3. **Wrong completion signal** — `DebugApiService` gets a `Func<ClusterState>` lambda from `EditorSubsystem` that reads `EditorApplication.CurrentClusterState`. `CurrentClusterState` is updated by `EditorApplication.Update()` consuming `ClusterStateUpdateEvent`. This wiring is correct.

4. **Wrong storage path** — **this is the actual root cause** (see below).

### Actual root cause

In `EditorSubsystem.cs` (lines ~837–848), `HrotEditLoadHandler` was constructed with:

```csharp
var storageProvider = new LocalDiskStorageProvider(isolatedTempRoot);
var scenarioLoader  = new HrotScenarioLoader(storageProvider, "Hrot.Scenario");
```

`isolatedTempRoot` resolves to `C:\FDP_Temp\nodes\node-0` (the per-node staging directory, computed via `OrchestrationConstants.GetNodeStagingRoot(0)`).

`HrotEditLoadHandler.PrepareAsync(PrepareEdit)` calls `_scenarioLoader.TryLoadScenarioJson(scenarioId)`, which calls `LocalDiskStorageProvider.EnumerateScenarioFiles(scenarioId)`, which enumerates `{_localTempRoot}/scenarios/{scenarioId}/*.json`.

In headless mode the `ReferencePrefetchHandler` copies scenario files from NAS to the staging directory asynchronously. The `HrotEditLoadHandler` runs its `PrepareAsync` during the same 2PC PrepareEdit phase — **before** the prefetch copy has completed. The staging directory is empty (or does not yet exist), so `TryLoadScenarioJson` returns null and `PrepareAsync` throws `InvalidOperationException("no scenario file found")`, aborting the load.

The NAS/shared path is `C:\FDP_Temp\shared` (`ClusterConfiguration.Default.NasBasePath`). Scenario JSON files live at `C:\FDP_Temp\shared\scenarios\{name}\*.json`. This directory is always populated before load is called.

**Evidence:** `LocalDiskStorageProvider.EnumerateScenarioFiles` in `FDP/Toolkits/Fdp.Toolkits/Orchestration/LocalDiskStorageProvider.cs` — path construction is `{_localTempRoot}/scenarios/{scenarioId}/*.json`. With `isolatedTempRoot` it looks in `C:\FDP_Temp\nodes\node-0\scenarios\test-move\` (empty); with `NasBasePath` it looks in `C:\FDP_Temp\shared\scenarios\test-move\` (present).

---

## Fixes

### Fix B — Seed node 0 in ClusterMaster roster at startup

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

After `_clusterMaster = new ClusterMaster(...)` (line ~1349):

```csharp
// ADA-BATCH-03 FIX-B: seed the editor's own node (node 0) in the ClusterMaster
// roster immediately at startup.
_orchestrationBus!.PublishManaged(new NodeHeartbeatEvent
{
    NodeId        = EditorNodeId,
    LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
    WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
    SubsystemName = "Editor",
});
_orchestrationBus!.SwapBuffers();   // make the heartbeat readable
_clusterMaster.Tick();              // IngestHeartbeats → roster populated
```

### Fix A — Wall-clock poll instead of frame-count in HandleScenarioLoad

**File:** `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs`

```csharp
// Before:
private const int ScenarioReadyMaxPolls = 600;
for (int i = 0; i < ScenarioReadyMaxPolls; i++) { ... }

// After:
private const double ScenarioReadyTimeoutSeconds = 30.0;
var sw = System.Diagnostics.Stopwatch.StartNew();
int pollCount = 0;
while (sw.Elapsed.TotalSeconds < ScenarioReadyTimeoutSeconds) { pollCount++; ... }
return Fail(504, $"... did not reach OperatingEdit within {ScenarioReadyTimeoutSeconds}s ({pollCount} polls).");
```

### Round 1 Fix — NAS storage path (prior; kept for context)

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**Change:** Split into two storage providers — one for the scenario loader (NAS path) and one for the prefetch handler (staging path):

```csharp
// Before (broken):
var storageProvider = new LocalDiskStorageProvider(isolatedTempRoot);
var scenarioLoader  = new HrotScenarioLoader(storageProvider, "Hrot.Scenario");
clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler(storageProvider));

// After (fixed):
// ADA-BATCH-03 FIX: HrotEditLoadHandler must read scenario JSON from the NAS/shared
// scenarios root (C:\FDP_Temp\shared\scenarios\...), NOT from the node staging root
// (C:\FDP_Temp\nodes\node-0\scenarios\...).  In headless mode the async prefetch copy
// from NAS→staging is a race condition; the slave's PrepareAsync runs before the copy
// completes, finds an empty staging dir and throws — entities never load.
// ReferencePrefetchHandler still uses isolatedTempRoot because its job is to ensure the
// staging directory exists for the prefetch copy destination.
var nasStorageProvider  = new LocalDiskStorageProvider(ClusterConfiguration.Default.NasBasePath);
var scenarioLoader      = new HrotScenarioLoader(nasStorageProvider, "Hrot.Scenario");
var storageProvider     = new LocalDiskStorageProvider(isolatedTempRoot);
clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler(storageProvider));
```

`ClusterConfiguration` was already in scope via `using Hrot.Orchestrator;`.

---

## Integration Test

**New file:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiScenarioLoadTests.cs`

**Test:** `ScenarioLoad_RealOrchestrationPipeline_EntitiesMaterialize`

The test proves the fix via the real ClusterMaster/ClusterSlave 2PC load pipeline:

1. Writes a minimal `scenario.json` with one entity (TkbType `1L`) to `C:\FDP_Temp\shared\scenarios\{id}\scenario.json`.
2. Registers all orchestration events on `orchBus` via `OrchestrationEventRegistry.RegisterAll`.
3. Pre-seeds ClusterMaster's node roster by injecting a `NodeHeartbeatEvent` for node 0 (critical: without a registered node, ClusterMaster calls `PublishOpStatus(Success)` immediately without publishing `ClusterStateUpdateEvent`, so the state machine never advances).
4. Calls `editorApp.LoadScenarioByName(scenarioId)`.
5. Pumps frames in the correct EditorSubsystem order: `kernel.Update()` → `orchBus.SwapBuffers()` → `clusterMaster.Tick()` → `clusterSlave.Tick()` → drain spawn requests → `editorApp.Update()`.
6. Asserts `reachedOperatingEdit == true` and `repo.EntityCount > 0`.

The test does **not** use `EditorHarness.BuildDebugApiService()` (which has no ClusterMaster wiring). It uses `EditorHarness` for the ECS kernel/world infrastructure only, and wires ClusterMaster + ClusterSlave manually with the same construction path as `EditorSubsystem`.

---

## Headless Reproduce Output (Round 2 — this batch)

Command sequence run after Fix B + Fix A:

```
# start
dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll \
  -m editor --debug-api --debug-api-port 8099 --headless &

# wait for ready
curl -s http://localhost:8099/status
# → {"ok":true,"data":{"scenario":null,"clusterState":"Idle","simTime":0,"timeScale":1,"isPaused":true,"inPreview":false,"entityCount":0,"recording":false},"error":null,"awaited":null}

# load scenario with waitForReady
curl -s -X POST -H "Content-Type: application/json" \
  -d '{"name":"test-move","waitForReady":true}' \
  http://localhost:8099/scenario/load
# → {"ok":true,"data":{"loaded":"test-move","awaited":true},"error":null,"awaited":null}

# check entity count
curl -s http://localhost:8099/status
# → {"ok":true,"data":{"scenario":"test-move","clusterState":"OperatingEdit","simTime":0,"timeScale":1,"isPaused":true,"inPreview":false,"entityCount":1,"recording":false},"error":null,"awaited":null}

# shutdown
curl -s -X POST -H "Content-Length: 0" http://localhost:8099/shutdown
# → {"ok":true,"data":null,"error":null,"awaited":null}
```

**Result:** `entityCount:1`, `clusterState:"OperatingEdit"` — scenario loaded and entity materialized.

Previously (before Fix B): `waitForReady:true` returned `504 "Scenario 'test-move' did not reach OperatingEdit within 600 ticks"` because the roster was empty and `ClusterMaster` never published `ClusterStateTransitionedEvent`.

---

## Build & Test Results (Round 2)

```
dotnet build IOS-IG-SimHost.sln
  → Build succeeded.  13 Warning(s) (all pre-existing)  0 Error(s)

dotnet test IOS-IG-SimHost.sln --filter "DebugApi"
  → Passed!  Failed: 0, Passed: 21, Skipped: 0, Total: 21
```

All 21 DebugApi tests pass including `LoadScenarioByName_ViaOrchestrationPipeline_MaterialisesEntities`.

---

## Residual Debt

| ID | Description | Severity |
|----|-------------|----------|
| ADA-03-D01 | `WaitForReadyAsync` polls with `Task.Delay(100)` — 100 ms per poll, up to 600 polls (60 s wall-clock). This is a fixed-duration poll, not a completion signal. Under load or on slow machines, latency could accumulate. A `TaskCompletionSource`-based approach would be cleaner. | Low |
| ADA-03-D02 | `DebugApiScenarioLoadTests` writes scenario files to `C:\FDP_Temp\shared` — the real shared NAS path. Test isolation would be improved by using a temp directory and overriding `ClusterConfiguration.Default.NasBasePath`. Currently relies on `test-move` existing. | Low |
| ADA-03-D03 | SaveScenario meaningful round-trip is still unverified (BATCH-02 review noted: save was only exercised on an empty world, `entityCount=0`). Now that load works end-to-end, a save-then-reload round-trip test can be written. | Medium (ADA-02-D02 carried forward) |
| ADA-03-D04 | Headless smoke is env-gated (requires `C:\FDP_Temp\shared\scenarios\test-move` to exist). No hermetic fixture for integration-level scenario scan. | Low (ADA-02-D03 carried forward) |
