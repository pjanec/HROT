# BATCH-01: Phase 1 — Unify Event Buses (Fix IsPaused Bug)

**Batch Number:** BATCH-01
**Tasks:** HEXAG2-S001, HEXAG2-S001b, HEXAG2-S002
**Phase:** Phase 1 — Unify Event Buses
**Estimated Effort:** 8-12 hours

---

## Onboarding

You are implementing the first phase of the hexag-2 design. Read these documents before starting:
- `.dev/hexag-2/DESIGN.md` — full architecture and rationale (especially Sections 1, 2, and 3)
- `.dev/hexag-2/TASK-DETAIL.md` — per-task specifications with exact success conditions
- `.dev/hexag-2/ONBOARDING.md` — codebase overview

**Key principle:** This batch fixes an IsPaused UI bug caused by the split-bus anti-pattern. The fix is total: every component in OrchestratorSubsystem and ExConSubsystem that currently writes to an isolated bus must be collapsed into a single unified `_bus` instance.

**Development branch:** All changes go on the current working branch.

**Build command:** `dotnet build IOS-IG-SimHost.sln -v q`
**Test command:** `dotnet test IOS-IG-SimHost.sln --no-build -v q`

---

## Developer Insights Section

When writing your report, please answer these questions explicitly:
1. **What issues were encountered?** (compile errors, unexpected dependencies, constructor mismatches)
2. **What weak points did you spot in the codebase?** (fragile wiring, missing null guards, missing test hooks)
3. **What design decisions did you make beyond the spec?** (e.g., ordering of component construction, how to handle null buses during tests)

---

## Test-Driven Task Progression (MANDATORY — do not skip)

For every task:
1. **Read the success conditions** in TASK-DETAIL.md before touching any code.
2. **Write or verify the unit tests first.** The tests define the contract.
3. **Implement** until all tests pass.
4. **Run the full test suite** (`dotnet test IOS-IG-SimHost.sln --no-build -v q`) after each task.
5. **Do not move to the next task** until the current task's unit tests pass AND the full suite is not newly broken.

---

## Tasks

### HEXAG2-S001 — Collapse Dual Buses into Single `_bus` in `OrchestratorSubsystem`

**Files to change:**
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.Tests.cs` (or the test project)

**What to do:**
1. In `OrchestratorSubsystem.cs`, DELETE the fields `_orchestrationBus` and `_eventBus`.
2. ADD `private FdpEventBus? _bus;`
3. In `Initialize()`, replace the two bus constructions with a single `_bus = new FdpEventBus();`
4. Pass `_bus` to `ClusterMaster`, `MasterSyncController`, `ClusterUiCache`, and `ClusterScenarioPanel`.
5. Change `new ClusterScenarioPanel(_clusterMaster, _uiCache)` to `new ClusterScenarioPanel(_bus, _uiCache)`.
   - `ClusterScenarioPanel` accepts a `FdpEventBus` in first position (not `ClusterMaster`). Check its constructor — if it currently takes `ClusterMaster`, you must update `ClusterScenarioPanel` to accept `FdpEventBus` instead, so the panel publishes strongly-typed intents directly to the bus.
6. Update the `TimeBusForTest` test hook property to return `_bus`.
7. In `Initialize()`, do one `_bus.SwapBuffers()` after construction to promote the initial `SwitchTimeModeEvent{Continuous}` that `MasterSyncController`'s constructor publishes.

**Unit tests to write** (in the existing Hrot.Orchestrator test project or Hrot.ClusterRunner.Tests):
- `OrchestratorSubsystem_PauseUpdatesIsPaused`:
  - Construct `OrchestratorSubsystem()` (parameterless constructor).
  - Call `Initialize()` with a minimal `SubsystemConfig`.
  - Access the bus via `TimeBusForTest`.
  - Publish `PauseTimeIntent` to the bus write buffer.
  - Call `bus.SwapBuffers()`.
  - Call `subsystem.Update(0f)`.
  - Assert `_uiCache.IsPaused == true` (expose `_uiCache` via a test-internal property or use the existing accessor if present).
- `OrchestratorSubsystem_ResumeClears_IsPaused`:
  - Same setup. Pause first, then publish `ResumeTimeIntent`, swap, Update.
  - Assert `IsPaused == false` after the resume cycle.

**Note on `PauseTimeIntent` / `ResumeTimeIntent`:** These structs do not exist yet in the codebase (they will be added in HEXAG2-S010). For this batch, add them as minimal structs in `Fdp.Toolkits/Time/TimeLocalEvents.cs` in the `Fdp.Toolkits.Time.Domain` namespace:
```csharp
public struct PauseTimeIntent    { }
public struct ResumeTimeIntent   { }
public struct StepTimeIntent     { public float DeltaSeconds; }
public struct SetTimeScaleIntent { public float TimeScale; }
```
`MasterSyncController.Update()` does not need to drain them yet — that wiring is done in HEXAG2-S011 (a later batch). For now the tests can directly inspect `ClusterUiCache.IsPaused` using the existing `SwitchTimeModeEvent` path: if `ClusterUiCache` subscribes to `SwitchTimeModeEvent`, you can publish that event directly to test the bus unification. The intent structs are added here as stubs for future use.

**Success conditions (from TASK-DETAIL.md HEXAG2-S001):**
1. `OrchestratorSubsystem` has exactly one `FdpEventBus` field (`_bus`); ALL secondary bus fields deleted.
2. `ClusterMaster`, `MasterSyncController`, `ClusterUiCache`, `ClusterScenarioPanel` all receive the same bus instance.
3. `ClusterScenarioPanel` constructed with `_bus` (not `_clusterMaster`).
4. Unit test `OrchestratorSubsystem_PauseUpdatesIsPaused` passes.
5. Unit test `OrchestratorSubsystem_ResumeClears_IsPaused` passes.

---

### HEXAG2-S001b — Collapse All Buses in `ExConSubsystem` into Single `_bus`

**Files to change:**
- `Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs`

**What to do:**
1. DELETE fields: `_orchestrationBus`, `_uiCacheBus`, `_clusterOpEgressBus`, `_timeEventBus`.
2. ADD `private FdpEventBus? _bus;`
3. In `Initialize()`, replace all bus constructions with a single `_bus = new FdpEventBus()`.
4. Pass `_bus` to every component that previously received an isolated bus:
   - `new ClusterSlave(nodeId, SubsystemName, _bus)`
   - `new NodeOpSlaveTranslator(..., bus: _bus, ...)`
   - `new ClusterUiCache(_bus, _slaveSyncController)`
   - `new ClusterScenarioPanel(_bus, _uiCache)`
   - `new OrchestrationObserverTranslator(_participant, _bus)`
   - `new ClusterOpEgressTranslator(_bus, _participant)`
   - `new SlaveSyncController(_bus, nodeId, TimeConfig.Default)`
   - All time translators (pass `_bus`)
5. In `Update()`, replace the THREE `SwapBuffers()` calls (`_orchestrationBus?.SwapBuffers()`, `_uiCacheBus?.SwapBuffers()`, `_clusterOpEgressBus?.SwapBuffers()`) with exactly ONE: `_bus?.SwapBuffers()`.
   - Keep the single swap at the appropriate place in the Update() sequence to preserve phase discipline (after all ingress, before all logic).

**Unit test to write:**
- `ExConSubsystem_ClusterUiCache_UpdatesIsPaused_AfterSwitchTimeModeEvent`:
  - Construct `ExConSubsystem()` (parameterless, headless — no DDS participant).
  - Call `Initialize()` with minimal config.
  - Get the bus via a test hook (add one if needed: `internal FdpEventBus? BusForTest => _bus`).
  - Publish `SwitchTimeModeEvent { Mode = TimeMode.Deterministic }` to the bus write buffer.
  - Call `_bus.SwapBuffers()`.
  - Call `subsystem.Update(0f)`.
  - Assert `_uiCache.IsPaused == true` (use existing accessor or add test hook: `internal ClusterUiCache? UiCacheForTest => _uiCache`).

**Success conditions (from TASK-DETAIL.md HEXAG2-S001b):**
1. `ExConSubsystem` has exactly one `FdpEventBus` field (`_bus`); all four secondary bus fields deleted.
2. `ExConSubsystem.Update()` contains exactly one `_bus?.SwapBuffers()` call.
3. Unit test `ExConSubsystem_ClusterUiCache_UpdatesIsPaused_AfterSwitchTimeModeEvent` passes.

---

### HEXAG2-S002 — Strict 4-Phase Single-Swap Update Loop in `OrchestratorSubsystem`

**Files to change:**
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`

**What to do:**
Rewrite the body of `OrchestratorSubsystem.Update()` to follow the documented phase sequence exactly. Remove any old `_orchestrationBus` or `_eventBus` references. The exact sequence:

```
Phase 1: Network boundary
    _timeModeTranslator?.ScanAndPublish(null!)
    _timeModeTranslator?.PollIngress(null!, null!)
    _lockstepTranslator?.ScanAndPublish(null!)
    _lockstepTranslator?.PollIngress(null!, null!)
    // Heartbeat bridging loop (manual DDS bridge): keep as temporary shim until HEXAG2-S008

Phase 2: Single frame boundary swap
    _bus?.SwapBuffers()    // exactly once

Phase 3: Core logic
    _masterSync?.Update()  // MOVED HERE from top of Update()
    _clusterMaster?.Tick()

Phase 4: Local observation
    _uiCache?.Update()
    _scenarioPanel?.Update(deltaTime)

Phase 5: Time-sync NTP ingress
    _masterTimeSyncTranslator?.PollIngress(null!, null!)
```

**Key changes:**
- `_masterSync?.Update()` moves FROM the top of `Update()` TO Phase 3 (after SwapBuffers).
- Only ONE `_bus?.SwapBuffers()` call — no extra swaps.
- Remove the separate `_orchestrationBus?.SwapBuffers()` call entirely.
- The manual heartbeat bridging loop (DDS `_heartbeatReader.Take()` -> `PublishManaged`) stays in Phase 1 as a temporary shim.

**Success conditions (from TASK-DETAIL.md HEXAG2-S002):**
1. `Update()` contains exactly one `_bus?.SwapBuffers()` call.
2. Zero references to `_orchestrationBus` or `_eventBus` in `Update()`.
3. Phases 1-5 execute in documented order.
4. Integration test `ContinuousMode_AllNodes_SimTimesWithinTolerance` continues to pass.
5. Integration test `PauseStepResume_SimTimeAdvancesByStepAmount` continues to pass.

---

## Report Format

Submit your report to `.dev/hexag-2/reports/BATCH-01-REPORT.md` with the following structure:

```markdown
# BATCH-01 Report

## Tasks Completed
- [ ] HEXAG2-S001 — ...
- [ ] HEXAG2-S001b — ...
- [ ] HEXAG2-S002 — ...

## Tests Written
(list test names and their test project location)

## Test Results
(paste output from `dotnet test ... --no-build -v q`)

## Developer Insights
### Issues Encountered
...
### Weak Points Spotted
...
### Design Decisions Made Beyond Spec
...

## Files Changed
(list every file changed and a one-line summary)
```

---

## Verification Checklist (Dev Lead will verify these)

- [ ] `OrchestratorSubsystem` has ONLY `_bus` field (no `_orchestrationBus`, no `_eventBus`)
- [ ] `ExConSubsystem` has ONLY `_bus` field (no `_orchestrationBus`, `_uiCacheBus`, `_clusterOpEgressBus`, `_timeEventBus`)
- [ ] `OrchestratorSubsystem.Update()` has exactly ONE `SwapBuffers()` call
- [ ] `ExConSubsystem.Update()` has exactly ONE `SwapBuffers()` call
- [ ] Unit test `OrchestratorSubsystem_PauseUpdatesIsPaused` exists and passes
- [ ] Unit test `OrchestratorSubsystem_ResumeClears_IsPaused` exists and passes
- [ ] Unit test `ExConSubsystem_ClusterUiCache_UpdatesIsPaused_AfterSwitchTimeModeEvent` exists and passes
- [ ] `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`, `SetTimeScaleIntent` exist in `Fdp.Toolkits.Time.Domain`
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with zero errors
- [ ] All PREVIOUSLY PASSING tests still pass
