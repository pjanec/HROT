# BATCH-10 Review

**Batch:** BATCH-10
**Tasks:** DEBT-06, 1f-07, 1e-01, 1e-02
**Reviewer:** Development Lead
**Date:** 2026-05-27
**Status:** ⚠️ APPROVED WITH P2 CORRECTIONS IN NEXT BATCH

---

## Summary

All four tasks are implemented and build clean. Test count: BTree 239→265 (wait — batch-10 went to 239, batch-11 completed to 265). Actual BATCH-10 only: BTree 221→239, AiShared 365→372, HSM unchanged at 215. Solution: 0 errors, 0 warnings.

---

## Issues Found

### Issue 1: Missing `PruneStaleAliasBindings` unit tests (P2)

**File:** No file — tests absent entirely.

**Problem:** BATCH-10 instructions explicitly required:
> "One unit test per asset: `PruneStaleAliasBindings_RemovesBindings_ForUnknownAssets` — add two bindings from two different requiring-asset GUIDs, prune with a set containing only one of them, verify the other was removed."

Neither `Hrot.BTree.Editor.Tests/` nor `Hrot.Hsm.Editor.Tests/` contains any test for `PruneStaleAliasBindings`. The `BlackboardLoadStateTests.cs` tests only `ValidateSaveAllowed`.

**Fix (BATCH-12 corrective):** Add to `Hrot.BTree.Editor.Tests/Blackboard/BTreePruneStaleBindingsTests.cs`:
- `PruneStaleAliasBindings_RemovesBindings_ForUnknownRequiringAsset` — two aliases from different requiring GUIDs; prune with only one; verify single removal
- `PruneStaleAliasBindings_NoOp_WhenAllAssetIdsKnown` — prune with all IDs present; verify no removal and no `Changed` event
- `GetKnownSubAssetIds_ReturnsAllDistinctRequiringIds`

Add equivalent 3 tests to `Hrot.Hsm.Editor.Tests/`.

### Issue 2: Missing concrete LoadState computation tests (P2)

**File:** No file — tests absent.

**Problem:** BATCH-10 instructions required:
> "T6: Confirm that `BehaviorTreeAsset` sets `LoadState = Clean` when `BlackboardSourceTextParser.Parse` succeeds"
> "T7: `BehaviorTreeAsset` sets `LoadState = StructParseFailed` when `LocateResult.Found == false`"
> "Concrete `BehaviorTreeAsset` tests go in `Hrot.BTree.Editor.Tests/`. Concrete `HsmAsset` tests go in `Hrot.Hsm.Editor.Tests/`."

The `BlackboardLoadStateTests.cs` T6/T7 test only the `ValidateSaveAllowed` null guard and default interface value — not the actual load pipeline computation. Neither `Hrot.BTree.Editor.Tests/` nor `Hrot.Hsm.Editor.Tests/` contains any test exercising `SetLoadDiagnostic` from the projector path.

**Fix (BATCH-12 corrective):** Add concrete load-state tests in both test projects using `SetLoadDiagnostic` to simulate what the projector sets, then verify `LoadState` and `LoadDiagnosticMessage` are exposed correctly via the interface.

---

## Test Quality Assessment

The tests that were written are good quality:
- `BlackboardLoadStateTests.cs` — 7 tests covering all four `ValidateSaveAllowed` branches correctly; behavioral assertions
- `BTreeSubtreeSyncPanelTests.cs` — 11 tests; actual model behavior (upsert, round-trip, `Changed` fire)
- `BTreeBoundToDropdownTests.cs` — 8 tests; verifies CLR-vs-display-name distinction (T5/T6 correctly reject "Int32"/"Single")

---

## Verdict

**Status:** APPROVED — corrections carried as P2 into BATCH-12 corrective section.

---

## Commit Message

```
feat: DEBT-06 prune, load-state banners, inspector sync panel (BATCH-10)

Completes DEBT-06 (partial), TASK-BB-1f-07, TASK-BB-1e-01, TASK-BB-1e-02

DEBT-06: PruneStaleAliasBindings + GetKnownSubAssetIds on BehaviorTreeAsset
  and HsmAsset; window calls prune each frame before BuildViewModel.

1f-07 (BlackboardLoadState banners):
- BlackboardLoadState enum (Clean/SpanCaptureFailed/StructParseFailed/AssemblyFailed)
- LoadState + LoadDiagnosticMessage on IBlackboardManagedAsset (default interface impl)
- BehaviorTreeAsset + HsmAsset: SetLoadDiagnostic internal setter
- BlackboardDtoEmitter.ValidateSaveAllowed static guard (blocks C/D; gates B on allowLossySave)
- BlackboardAuthoringWindow: AssemblyFailed red banner + early return; SpanCaptureFailed
  yellow warning + lossy-save popup; StructParseFailed read-only display

1e-01 (Subtree Sync Panel):
- SubtreeNodeInfo, SubtreeSyncBinding, IBTreeSyncableAsset in Hrot.Editor.AiShared
- BehaviorTreeAsset implements IBTreeSyncableAsset (GetSubtreeNodeInfo,
  GetSyncBindings, SetSyncBinding, ClearSyncBindings)
- InspectorWindow: PARAMETER SYNCHRONIZATION section for Subtree nodes

1e-02 (Bound-to Dropdown):
- GetVariablesOfType on BehaviorTreeAsset (display-name exact match)
- DrawSyncBindingsTable 4-column table with type-filtered combo

Tests: BTree +18 (239 total), AiShared +7 (372 total), HSM unchanged (215)
Build: 0 errors, 0 warnings
```

---

**Next Batch:** BATCH-12 (corrective P2 + TASK-BB-1f-01 + TASK-BB-1f-02)
