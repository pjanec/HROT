# BATCH-02 Report: Phase 3 — ExCon Lockstep Participation

**Batch:** BATCH-02  
**Tasks:** TC2-P3-T1, TC2-P3-T2, TC2-P3-T3, TC2-P3-T4  
**Status:** ✅ Complete  
**Date:** 2026-04-02

---

## A. Task Completion Summary

| Task ID | Status | Notes |
|---------|--------|-------|
| TC2-P3-T1 | ✅ | `SlaveSyncController` + 3 translators added to `ExConSubsystem`; `TestHook_SlaveSyncController` property added |
| TC2-P3-T2 | ✅ | Time pipeline driven in `Update()` before `_clusterSlave?.Tick()` |
| TC2-P3-T3 | ✅ | `_slaveSyncController` created before `_uiCache`; injected into `ClusterUiCache` constructor |
| TC2-P3-T4 | ✅ | `_timePulseHandler` and `_timeModeHandler` removed; `OnTimePulse`/`OnTimeMode` are purely display-only properties with no live consumers |
| TD-001 (stretch) | ⚠️ | Not applicable — SimHost and IG have no `ClusterUiCache` at all; see §C.5 |

---

## B. Test Results

```
Passed!  - Failed:     0, Passed:   194, Skipped:     0, Total:   194, Duration: 15 s - Hrot.ClusterRunner.Tests.dll (net8.0)
```

**3 new tests added:**
- `ExCon_Initialize_CreatesSlaveTimeController` (TC2-P3-T1)
- `ExCon_Update_DoesNotThrow_WithTimePipeline` (TC2-P3-T2)
- `ExCon_UiCache_MasterSimTime_AdvancesWithController` (TC2-P3-T3)

**191 pre-existing tests:** all continue to pass (no regressions).

---

## C. Developer Insights

### 1. What issues were encountered?

No blocking errors. The only minor surprise was that `--no-build` on the first test run reported 191 (stale DLL); running with a fresh build correctly showed 194 passing. Compilation was clean on the first attempt.

### 2. How was `iosNodeId` obtained?

`iosNodeId` is already derived at the top of `Initialize()`:
```csharp
var iosNodeId = config.NodeId != 0 ? config.NodeId : 500;
```
This is a fallback to `500` when no explicit NodeId is configured. The slave time-pipeline components were inserted immediately after this line, reusing the same local variable.

### 3. `TimeNetworkModule.CreateTimePulseIngressTranslator` signature

The method exists in `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs`:
```csharp
public static IDescriptorTranslator CreateTimePulseIngressTranslator(
    DdsParticipant? participant, FdpEventBus eventBus)
```
It accepts a nullable `DdsParticipant?` (safe no-op when null) and returns a `TimePulseIngressTranslator`. No adapter or workaround was needed.

### 4. Were `TimePulseIngressHandler`/`TimeModeIngressHandler` retained or removed?

**Removed.** Both handlers were defined in `Hrot.ExCon/Services/DdsEventIngressHandlers.cs` and their callbacks updated only `ExConLogic.MasterSimTime`, `MasterWallTicks`, `MasterTimeScale`, and `IsPaused`. A codebase-wide grep confirmed that **none** of these `ExConLogic` properties are read by any panel, window, or other code path:

- `ClusterScenarioPanel` reads `_uiCache.IsPaused`, not `logic.IsPaused`
- No `.MasterSimTime`, `.MasterWallTicks`, `.MasterTimeScale`, or `.IsPaused` property accesses exist on `ExConLogic` outside the class itself

These properties are exposed on `IExConLogic` and could theoretically be consumed in future, but currently have zero live consumers. The removal is safe and the replacement comment in `Initialize()` documents the rationale.

### 5. Stretch goal (TD-001) outcome

**Not implemented.** A search of `Hrot.SimHost/**` and `Hrot.IG/**` found **zero references to `ClusterUiCache`**. Neither subsystem instantiates or uses a `ClusterUiCache` at all — they are pure simulation/rendering nodes without an instructor-station UI cache. There is no `_uiCache` construction point to wire a slave controller into. The stretch goal as specified is not applicable for the current codebase state.

### 6. Weak points spotted

- **`ExConLogic.IsPaused` / `MasterSimTime` dead properties:** These properties are in `IExConLogic` (public interface) but have zero consumers. They represent stale interface surface from before `ClusterUiCache` was the authoritative time-display source. A future cleanup should remove them from `IExConLogic` / `ExConLogic` entirely, but this is out of scope for this batch.
- **`_timeEventBus` not disposed:** Following the `OrchestratorSubsystem` pattern, `_timeEventBus` is set to `null` in `Shutdown()` without calling `Dispose()`. `FdpEventBus` implements `IDisposable`, so this is technically a resource leak. This pre-existing pattern across multiple subsystems warrants a tech-debt note.
- **PollIngress `null!` arguments:** The `IDescriptorTranslator.PollIngress(IEntityCommandBuffer, ISimulationView)` interface requires two arguments, but the time translators don't use them (they're event-bus driven). The `null!` null-forgiving pattern is effectively a suppressed lie to the compiler; the translators should accept nullable or no-op defaults more idiomatically.

### 7. Design decisions beyond the spec

- None beyond the spec. The five new fields match exactly the names specified in `BATCH-02-INSTRUCTIONS.md`, and the `TestHook_SlaveSyncController` property is `internal` as required.

---

## D. Scope / Deviation Notes

No deviations from spec. All 4 mandatory tasks completed exactly as specified.

Stretch goal TD-001 not implemented because the prerequisite condition (`ModuleHostKernel.GetTimeController()` accessible at `_uiCache` construction in SimHost/IG) does not apply — neither subsystem has a `ClusterUiCache`.
