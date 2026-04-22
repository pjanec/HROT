# BATCH-04 Review

**Batch:** BATCH-04 — Role-Split Lockstep Translators + Factory Updates  
**Tasks:** TCU-TR001, TCU-TR002, TCU-TR003, TCU-T003, TCU-T004  
**Reviewer:** Dev Lead  
**Date:** 2026-04-01

---

## ✅ Verdict: APPROVED

All tasks complete and correct. 124 tests pass (0 failed, 1 pre-existing skip). No rework required.

---

## Review Findings

### Scope Check

| Task | Expected | Result |
|------|----------|--------|
| TCU-TR001 — MasterLockstepTranslator | Ordinal 202; egress+ingress; stateless; null-safe | ✅ Done |
| TCU-TR002 — SlaveLockstepTranslator | Ordinal 203; ingress+egress; stateless; null-safe | ✅ Done |
| TCU-TR003 — TimeNetworkModule updates | 2 new factory methods; old one marked `[Obsolete]` | ✅ Done |
| TCU-W005 — TimeControllerFactory updated | Master+Continuous→MasterSync; Slave+Any→SlaveSync | ✅ Done |
| TCU-T003 — Translator tests | 9 tests (required 8) | ✅ Done |
| TCU-T004 — Factory tests | 4 tests added + 3 existing updated | ✅ Done |

### Design Alignment

- `MasterLockstepTranslator` ordinal = 202 — ✅
- `SlaveLockstepTranslator` ordinal = 203 — ✅
- No echo-prevention tracking state in either translator — ✅
- `null participant` = safe no-op everywhere — ✅
- `AdvanceFrameIntent` events drained from bus even when participant is null — ✅ **Correct** (prevents bus accumulation)
- `CreateLockstepTranslator` preserved as `[Obsolete]` — ✅
- Standalone factory path unchanged — ✅
- Three pre-existing factory tests updated to reflect new return types — ✅ **Correct** (old assertions were stale)

### Design Issue Noted: `SequenceID` not mapped

`FrameOrderDescriptor.SequenceID` is set to default 0 in `MasterLockstepTranslator`. This field is not mentioned in the spec and not used by the new controllers. However it was used by the old `SteppedMasterController` — worth monitoring during Phase 5 integration.

---

## Debt Tracker Updates

- **DT-006 (P3):** `FrameOrderDescriptor.SequenceID` is not mapped in `MasterLockstepTranslator` (defaults to 0). Old `SteppedMasterController` populated this. New controllers do not use it but its purpose (ordering/deduplication) is unclear. Track for Phase 5 integration testing. Target: BATCH-05.

---

## Suggested Git Commit Message

```
feat(time-ctrl-unif,TCU-TR001/TR002/TR003/W005/T003/T004): role-split lockstep translators

- MasterLockstepTranslator (ordinal 202): stateless egress/ingress, null-safe
- SlaveLockstepTranslator (ordinal 203): stateless ingress/egress, null-safe
- TimeNetworkModule: CreateMasterLockstepTranslator + CreateSlaveLockstepTranslator added;
  CreateLockstepTranslator marked [Obsolete]
- TimeControllerFactory: Master+Continuous -> MasterSyncController; Slave+Any -> SlaveSyncController
- 13 new tests: 9 translator + 4 factory
```

---

## Next Batch

**BATCH-05** — Application Wiring + Deletion + E2E integration test:
- TCU-W001 — Wire MasterSyncController in Orchestrator
- TCU-W002 — Wire SlaveSyncController in SimHost  
- TCU-W003 — Wire SlaveSyncController in CGF
- TCU-W004 — Wire SlaveSyncController in IG
- TCU-W006 — Delete obsolete classes
- TCU-T006 — Integration Test: Full Pause/Step/Resume Cycle

Note: TCU-W005 (TimeControllerFactory) was completed in BATCH-04; mark it done in TASK-TRACKER.

Estimated effort: 8–10 hours.
