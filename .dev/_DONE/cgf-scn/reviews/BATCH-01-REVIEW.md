# BATCH-01 Review

**Batch:** BATCH-01
**Tasks:** TASK-C001, TASK-C002, TASK-C003
**Reviewer:** Dev Lead
**Date:** 2026-04-21
**Decision:** ✅ APPROVED

---

## Summary

All three Phase 1 tasks delivered correctly. Build is clean, all 99/99 Core.Tests pass.
The implementation is minimal, well-structured, and aligns with the design.

---

## Implementation Review

### TASK-C001 — ScenarioEntityCreationRequestSource ✅

- `ConcurrentQueue<T>` used correctly — no lock, no allocation in `ProcessRequests`
- Drain loop uses `for`-with-counter + `TryDequeue` early exit: correct, avoids
  `Count`-based race condition
- `ArgumentNullException` on null request: defensive, acceptable
- 4 tests, all meaningful including the concurrent safety test

### TASK-C002 — CompositeEntityCreationRequestSource ✅

- Sequential drain over `IReadOnlyList<T>` — correct for the expected 2-source case
- Empty list rejection at construction: correct per spec
- Exception propagation (no swallowing) tested explicitly: good
- 5 tests (exceeded minimum 4)

### TASK-C003 — CgfLogicPack Wiring ✅

- `ScenarioEntityCreationRequestSource` injected, not constructed internally: correct per spec
- `ArgumentNullException` guard on the new parameter: correct
- All downstream call sites updated (EditorSubsystem, OfflineKernelBootTests, EditorHarness, NetworkDemo): complete
- `CgfApplication` holds the shared instance for future load handlers: correct

---

## Test Quality Assessment

Tests are meaningful — checking FIFO order, cap boundaries, concurrent total count,
and null guards. No tests check mere compilation or string presence.

---

## Debt Items Identified

| ID | Priority | Description |
|----|----------|-------------|
| D-001 | P3 | `CgfLogicPack` accepts concrete `ScenarioEntityCreationRequestSource` instead of `IEntityCreationRequestSource`; widens the coupling unnecessarily. Consider accepting the interface in a future refactor. |
| D-002 | P2 | 3 pre-existing system-count assertions in `Hrot.SimHost.Tests` (`CgfLogicPack_EmptyWorld`, `SimHostCoreLogicPack_EmptyWorld`, `SimulationLogicModule_EmptyWorld`) are stale and should be updated to current counts. |

---

## Git Commit

```
feat(cgf-scn): Phase 1 - Entity creation source infrastructure (TASK-C001, C002, C003)
```
(Committed at 905eeb8)

---

## TASK-TRACKER Update

- [x] TASK-C001 — done
- [x] TASK-C002 — done
- [x] TASK-C003 — done
