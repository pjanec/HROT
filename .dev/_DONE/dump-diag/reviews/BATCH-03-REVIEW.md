# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-05-03
**Status:** APPROVED

---

## Summary

All 7 tasks completed. Phase 3 multi-select and Phase 4 orchestration protocol are implemented.
257/260 presentation tests pass (3 pre-existing failures unrelated to this batch). 127/127
orchestrator tests pass. 10/10 new diagnostic tests pass.

---

## Issues Found

### Issue 1: NodeOpType uses CollectDiagnostics (not DumpDiagnostics) — P3 Documentation

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/NodeOpType.cs`
**Problem:** Task spec says `DumpDiagnostics = 28` but developer used `CollectDiagnostics = 28`
to avoid a CycloneDDS IDL module-scoped enum name collision.
**Status:** Correct design decision, acceptable. TASK-DETAIL.md has a wrong assumption about
IDL scoping. The value `28` is correct; the name difference is a valid workaround.
Note in DEBT-TRACKER as P3 documentation issue.

### Issue 2: StorageProcessManager bug (inherited) — P2 Debt

**File:** `Hrot/Subsystems/Hrot.Orchestrator/StorageProcessManager.cs`
**Problem:** `StorageProcessManager.ContinueWith` checks only `IsFaulted` and misses the case
where `PullToNasAsync` returns successfully with `FailureCount > 0`. `DiagnosticsDumpProcessManager`
has this fixed. `StorageProcessManager` does not.
**Fix:** In a follow-up batch, fix `StorageProcessManager` to also check `FailureCount == 0`.
Add to DEBT-TRACKER as P2.

---

## Test Quality Assessment

New tests verify actual behaviour:
- `Aggregate_ThreeNodesWithTwoEntriesEach_Returns6StrippedEntries`: checks merge count and SourceUnc stripping
- `BuildCopyJson` tests verify actual JSON structure and frame ordering
- Shift+Click test verifies exact index range and that `_lastClickedIndex` does not change
- All assertions on actual values, not just collection existence

---

## Verdict

**Status:** APPROVED

All requirements met. StorageProcessManager P2 debt recorded. Ready to merge.

---

## Commit Message

```
feat: multi-select copy-to-JSON + cluster dump orchestration protocol (BATCH-03)

Completes DD-P3-T01, DD-P3-T02, DD-P4-T01, DD-P4-T02, DD-P4-T03, DD-P4-T04, DD-P4-T05

Phase 3 - Multi-Select UI:
- EventBrowserPanel: Ctrl+Click toggle, Shift+Click range, JSON array export (DD-P3-T01)
- EntityInspectorPanel: same multi-select semantics, calls IEntityStateExtractionService (DD-P3-T02)
- IEntityContextMenuHandler: default interface method for multi-entity overload

Phase 4 - Cluster Dump Protocol:
- DumpDiagnostics=16 in ClusterOpType; CollectDiagnostics=28 in NodeOpType (IDL scoping fix)
- DiagnosticDumpPayloadDto record with JsonPropertyName attributes (DD-P4-T01)
- ExecuteDiagnosticDumpIntent struct [EventId(9058)] with PayloadJson field (DD-P4-T02)
- ClusterOpEgressTranslator + ClusterOpMasterTranslator DumpDiagnostics handling (DD-P4-T03)
- DiagnosticsConsensusAggregator: flattens per-node manifests, strips SourceUnc for DDS (DD-P4-T04)
- DiagnosticsDumpProcessManager: PullToNas on success, abort path, FailureCount check (DD-P4-T05)

Tests: 10 new orchestrator tests, 9 new presentation tests
```

---

**Next Batch:** BATCH-04 — Phase 5 + Phase 7 (Node-Side Handler + IFileDialogService)
