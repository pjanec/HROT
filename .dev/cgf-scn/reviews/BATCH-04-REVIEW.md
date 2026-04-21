# BATCH-04 Review

**Batch:** BATCH-04
**Tasks:** TASK-C006, TASK-C007, TASK-C012
**Reviewer:** Dev Lead
**Date:** 2026-04-21
**Decision:** ✅ APPROVED

---

## Summary

All three tasks delivered correctly. Critical constraint met: C007 and C012 shipped
together. `world: null` confirmed in `NodeBootstrapper.cs` — no second live-world
registration found. Build clean, 400/400 tests pass.

---

## Implementation Review

### TASK-C006 — CgfScenarioLoadHandler ✅

- `PrepareAsync` stores JSON without side effects
- `Commit` is a no-op when `_pendingJson == null`
- `Abort` clears state correctly

### TASK-C007 — CgfEpisodeLoadHandler ✅

- `StartEpisode.Commit` passes `episodeId` to extractor (so `EpisodeTag` is appended)
- `StopEpisode.Commit` uses `EntityLifecycle.All` and publishes `DestroyEntityCommand`
  via event bus (no direct `DestroyEntity` calls) — correct per design constraint
- `episodeId` stored across `PrepareAsync`→`Commit` lifetime

### TASK-C012 — SimHost Passive Demotion ✅

Single argument change confirmed. No second live-world `ReferenceEpisodeLoadHandler`
registration found on SimHost.

### CgfApplication Wiring ✅

Single `SequentialIdAllocator` shared between scenario and episode handlers — correct,
prevents ID collision between scenario load and episode loads.

---

## Test Quality Assessment

All success conditions are tested. `StopEpisode` test verifies event bus path NOT
`DestroyEntity` — correct approach.

---

## Debt Items Identified

No new P1/P2 items. One P3 observation:

| ID | Priority | Description |
|----|----------|-------------|
| D-005 | P3 | `CgfApplication` constructs `StagingEntityExtractor` via `new StagingEntityExtractor()` but does not inject `ScenarioBehaviorRemapper`. Once TASK-C011 (composition root) adds the remapper, this wiring should be updated. Tracked for BATCH-05. |

---

## Git Commit

```
feat(cgf-scn): Phases 3+4+6 - CGF load handlers + SimHost passive demotion (TASK-C006, C007, C012)
```
(Committed at 433ebeb)

---

## TASK-TRACKER Update

- [x] TASK-C006 — done
- [x] TASK-C007 — done
- [x] TASK-C012 — done
