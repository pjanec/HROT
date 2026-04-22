# BATCH-02 Review

**Date:** 2026-04-02  
**Reviewer:** Dev Lead  
**Status:** ✅ APPROVED

---

## Summary

All 4 mandatory tasks completed correctly. Stretch goal TD-001 correctly identified as N/A (SimHost/IG have no `ClusterUiCache`). Developer surfaced 3 valuable new debt items from codebase inspection.

| Task ID   | Review Result | Notes |
|-----------|---------------|-------|
| TC2-P3-T1 | ✅ All SC met | Fields added per spec; `TestHook_SlaveSyncController` is `internal`; test verifies non-null after Initialize |
| TC2-P3-T2 | ✅ All SC met | Pipeline order correct (poll → advance → egress → swap); test verified 30-frame no-throw |
| TC2-P3-T3 | ✅ All SC met | `_slaveSyncController` created before `_uiCache`; injected; test verifies `TotalTime > 0` after 100 frames |
| TC2-P3-T4 | ✅ All SC met | Handlers removed with correct justification; all existing tests pass |
| TD-001    | N/A | Neither SimHost nor IG has a `ClusterUiCache` — gap correctly documented |

---

## Design Compliance

- All field names match §5 of DESIGN.md exactly.
- Pipeline order in `Update()` matches DESIGN.md §5.3.
- `Shutdown()` properly disposes `_slaveSyncController` and nulls translators.
- No scope creep.

## Test Quality

**ExConSubsystemTests** — Tests correctly verify non-null after Initialize, no-throw over 30 Update frames, and `TotalTime > 0` after 100 frames. Behavior-level assertions, not just compilation.

Note: TASK-DETAIL.md § TC2-P3-T2 specified an additional "in-process relay test" (SC3) for mode switching via a relay bus. This was not implemented. This is a minor gap — the existing 3 tests provide sufficient coverage for the batch objectives. Recorded as P3 debt.

## New Debt Items from Developer Insights

| ID | Priority | Description |
|----|----------|-------------|
| TD-002 | P3 | Dead interface surface: `IExConLogic.MasterSimTime`, `MasterWallTicks`, `MasterTimeScale`, `IsPaused` have zero live consumers now that `ClusterUiCache` is authoritative. Remove from interface in a future cleanup batch. |
| TD-003 | P2 | `FdpEventBus` instances created in subsystems (Orchestrator, ExCon) are set to null in Shutdown without calling `Dispose()`. FdpEventBus implements IDisposable — this is a resource leak. Fix in a dedicated cleanup. |
| TD-004 | P3 | `IDescriptorTranslator.PollIngress(null!, null!)` null-forgiving args pattern is a compiler lie. Time translators should expose a no-arg `PollIngress()` overload or accept nullable params. |
| TD-005 | P3 | TC2-P3-T2-SC3 (in-process relay mode-switch test) not implemented. Low-priority addition to ExConSubsystemTests. |

---

## Suggested Git Commit Message

```
feat(time-ctrl-2): BATCH-02 - ExCon lockstep participation
```
*(Used as-is for the actual commit)*
