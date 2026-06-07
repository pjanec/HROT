# BATCH-03 Review

**Batch:** BATCH-03  
**Reviewer:** Development Lead  
**Date:** 2026-05-31  
**Status:** APPROVED

---

## Summary

7 tasks completed (2 corrective + 5 HSM host fixes). 854 blueprint tests + 264 HSM editor tests passing. 14 new tests added. 2 pre-existing `Fhsm.Tests` failures are unrelated to this batch and were failing before BATCH-03.

---

## Issues Found

No issues found.

---

## Test Quality Assessment

All tests verify actual behavior with concrete values:

- **CORR-02-1**: Uses `unsafe` byte manipulation on `Blackboard1024` to set known structure hash and float Speed value; asserts `FieldValues["Speed"] == 3.14f`. Hash-mismatch test verifies empty `FieldValues`. Fully exercises the `MemoryMarshal` path.
- **CORR-02-2**: After E2 hits same-frame dedup, asserts `HitCount == 2` -- confirms accumulation even when no new pause is triggered.
- **BPF-017**: Extracts actual `OnEntryActionId` from the blob's `StateDef`, then asserts `ActionNames[actionHashId] == "AttackAction"` -- verifies the exact hash-to-name mapping that the overlay uses. Multi-action test verifies `GetActionName` returns real names, not fallback `Action_<id>` strings.
- **BPF-023**: Sets `ActiveLeafIds[0]=1, ActiveLeafIds[1]=2` in `BrainHsm64`, provides `StateStableIds[1]=stableA, [2]=stableB`, calls `Update`, asserts snapshot has both StableId Guids. Sentinel 0xFFFF exclusion tested separately.
- **BPF-024**: StepOut tested in Entry phase (no pause) and Activity phase (pause). StepOver tested with MicroStep change (pause). Distinct predicates verified by two separate behavioral scenarios.
- **BPF-025**: States created with pre-known Guids; projected states asserted to have those exact `StableId`s. Layout-by-StableId tested with known positions.

---

## Verdict

**Status: APPROVED**

All requirements met. Ready to merge.

---

## 📝 Commit Message

```
fix: HSM host critical/high defects + corrective tests (BATCH-03)

Completes CORR-02-1, CORR-02-2, BPF-017, BPF-022, BPF-023, BPF-024, BPF-025

Corrective tests from BATCH-02 review:
- CORR-02-1: AiPrimitive field-value reading tested with Blackboard1024 unsafe byte setup
- CORR-02-2: Same-frame HitCount accumulation implemented and tested

HSM host fixes:
- BPF-017: ActionNames keyed by hash IDs from BuildActionTable/BuildGuardTable (not positional)
- BPF-022: HsmFluentEmitter emits DeferEvent() calls in ascending ID order
- BPF-023: HsmDebugSession.Update decodes ActiveLeafIds from BrainHsm64/128 via StateStableIds
- BPF-024: StepOut uses Phase==Activity predicate; StepOver uses MicroStep-change predicate
- BPF-025: StableId assigned from metadata.StateStableIds (content-based, not positional sort)

Tests: 854 blueprint + 264 HSM editor passing. 14 new tests.
```

---

**Next Batch:** BATCH-04 (BTree Host Fixes + NodeEditor Fixes)
