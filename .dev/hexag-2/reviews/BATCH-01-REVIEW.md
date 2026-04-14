# BATCH-01 Review

**Status:** APPROVED

**Reviewed by:** Dev Lead
**Date:** 2026-04-14

---

## Summary

BATCH-01 is approved. All three tasks (HEXAG2-S001, HEXAG2-S001b, HEXAG2-S002) were
implemented correctly. Bus unification is complete, the 4-phase Update loop is in place, and
all success conditions are met.

---

## Task-by-Task Review

### HEXAG2-S001 — Collapse Dual Buses in OrchestratorSubsystem

PASS. `OrchestratorSubsystem` now has exactly one `FdpEventBus? _bus` field. All secondary
bus fields deleted. `ClusterMaster`, `MasterSyncController`, `ClusterUiCache`,
`ClusterScenarioPanel` all receive the same instance. `TimeBusForTest` and `UiCacheForTest`
test hooks added. Tests `OrchestratorSubsystem_PauseUpdatesIsPaused` and
`OrchestratorSubsystem_ResumeClears_IsPaused` pass.

### HEXAG2-S001b — Collapse All Buses in ExConSubsystem

PASS. `ExConSubsystem` now has exactly one `FdpEventBus? _bus` field. All four secondary bus
fields (`_orchestrationBus`, `_uiCacheBus`, `_clusterOpEgressBus`, `_timeEventBus`) deleted.
Single `_bus?.SwapBuffers()` in Update(). Test
`ExConSubsystem_ClusterUiCache_UpdatesIsPaused_AfterSwitchTimeModeEvent` passes.

### HEXAG2-S002 — Strict 4-Phase Single-Swap Update Loop

PASS. `OrchestratorSubsystem.Update()` has exactly one `_bus?.SwapBuffers()` call. The
`_masterSync?.Update()` is now in Phase 3 (after SwapBuffers) as required. Integration tests
`ContinuousMode_AllNodes_SimTimesWithinTolerance` and
`PauseStepResume_SimTimeAdvancesByStepAmount` pass.

---

## Test Quality Assessment

Tests are **sound**. The developer correctly identified that calling `subsystem.Update(0f)`
would double-swap the bus (one explicit + one in Phase 2), and instead tests bus unification
by writing directly to the bus and calling `uiCache.Update()`. This is functionally correct
for proving the success condition. Assertions check actual state values (`IsPaused`), not just
compilation or string matching.

---

## Developer Insights Extracted

Issues recorded in DEBT-TRACKER:
- **Rogue `HrotEnvironment.CreateParticipant()` call in headless mode** (both
  `OrchestratorSubsystem` and `ExConSubsystem`) — P2, tracked in HEXAG2-DEBT-001 area,
  to be fixed in HEXAG2-S008 / HEXAG2-S012.
- **Pre-existing audit test hard-codes wrong path** (`ExConSubsystem_HasNoDirectClusterMasterReference`)
  — P3, added as HEXAG2-DEBT-005.
- **`OrchestrationObserverTranslator.Tick()` now runs after single swap** causing 1-frame UI
  delay — P3, acceptable per design, documented.

---

## Debt Tracker Updates

Added HEXAG2-DEBT-005 (audit test path bug) to DEBT-TRACKER.md.

---

## Suggested Git Commit Message

```
refactor(orchestration): unify event buses and enforce single-swap Update loop (hexag-2 Phase 1)

HEXAG2-S001: OrchestratorSubsystem — replace _orchestrationBus + _eventBus with single _bus;
  wire ClusterMaster, MasterSyncController, ClusterUiCache, ClusterScenarioPanel to same instance;
  add TimeBusForTest and UiCacheForTest hooks

HEXAG2-S001b: ExConSubsystem — replace four isolated buses with single _bus;
  single SwapBuffers() call in Update(); add BusForTest and UiCacheForTest hooks

HEXAG2-S002: OrchestratorSubsystem.Update() rewritten to strict 4-phase sequence:
  Phase 1 network boundary, Phase 2 single swap, Phase 3 core logic (masterSync moved here),
  Phase 4 UI observation, Phase 5 NTP ingress

Intent stubs added: PauseTimeIntent, ResumeTimeIntent, StepTimeIntent, SetTimeScaleIntent
  in Fdp.Toolkits.Time.Domain (consumer wiring in HEXAG2-S011)

Tests: OrchestratorSubsystemBusTests (2), ExConSubsystemBusTests (1)

Fixes: IsPaused UI bug caused by split-bus anti-pattern
```
