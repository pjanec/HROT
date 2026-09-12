# BATCH-12 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P4-T4/T5: extended `AssetBrowserPanel` with an incremental case-insensitive name filter
(per-kind tree prune + All-tab flat list), All-tab kind chips, initial-path auto-expand/select, and
per-kind last-opened persist/restore. Completes Phase 4.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New + existing tests run by lead: `AssetBrowserPanelTests` → **10 passed, 0 failed** (5 BATCH-11 +
  5 BATCH-12). Suites green: AiShared 914, Fdp.Toolkits 1856, SimHost 585.
- Seams read: `Filter`, `FilteredTreeFor(kind)` (prune to matching leaves + ancestors),
  `FilteredFlatList()` (All tab), `IsKindChipEnabled`/`SetKindChip`/`ToggleKindChip`,
  `ExpandedFolders(kind)` (ancestor set), `LastOpenedByKind` + `RestoreLastOpened`, `GetAncestorPaths`.
  BATCH-11 API/behavior intact (additive); panel still side-effect-free.
- A `Passengers_DeferredWhenReferencedEntityNotInMap` ordering flake appeared once and cleared on
  re-run — same nondeterministic test-isolation family as PRE-3/PRE-4, unrelated to this batch.

## Test Quality
Strong. Filter test asserts tree pruned to the matching folder (+ IsLeaf) and empty result for a
non-match. Chip test toggles Blueprint visibility in/out of the flat list. Initial-reveal test asserts
`ExpandedFolders` contains `combat` and `combat/patrol` but NOT the leaf path, and `Selection`==Guard.
Last-opened test asserts empty→updated per-kind, BTree activation doesn't disturb Blueprint, and a
second panel restoring the map reveals/selects the remembered path. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P4-T4, MTB-P4-T5 → `[x]`. **Phase 4 complete.**

## Commit Message
```
feat(main-toolbar): asset browser All-tab + filter + chips + auto-expand/last-opened (MTB-P4-T4, T5)

Extend AssetBrowserPanel: case-insensitive Filter (FilteredTreeFor prunes to matching leaves +
ancestors; FilteredFlatList for the All tab), All-tab kind chips (IsKindChipEnabled/ToggleKindChip),
initial-path reveal (ExpandedFolders ancestors + Selection leaf), and per-kind last-opened memory
(LastOpenedByKind + RestoreLastOpened). Additive over BATCH-11; panel stays side-effect-free.
Tests: 5 new (10 total in AssetBrowserPanelTests). Completes Phase 4.
```
