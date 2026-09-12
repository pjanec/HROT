# BATCH-13 Review — TASK-BT-13 Palette offers only bindable actions

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent)
- `BTreeNodeCatalog` ctor gains `string? blackboardTypeName`; `BuildDynamicEntries` skips an entry when `!string.IsNullOrEmpty(blackboardTypeName) && entry.DtoType?.FullName != blackboardTypeName`. Static + generic entries never filtered. Null/empty → no filter (back-compat → BT-01 tests unchanged).
- `BTreeDocumentFactory` passes `btAsset.BlackboardTypeName`.
- 4 new tests (compatible action offered / mismatched filtered; condition variant; null → no filter; static always present). `Hrot.BTree.Editor.Tests` **505/0**.

## Issues
None. (UX-only; the build-break *guarantee* covering Inspector/hand-edit is BATCH-17.)

## Verdict
APPROVED. Palette now offers only blackboard-compatible actions/conditions.

## Commit message
```
feat(btree-editor): palette offers only blackboard-bindable actions/conditions (BATCH-13 / TASK-BT-13)

BTreeNodeCatalog filters dynamic action/condition entries to those whose
ActionSchemaEntry.DtoType matches the asset's BlackboardTypeName (the bindable
4-param shape); static + generic entries unchanged; null blackboard = no filter
(back-compat). Threaded from BTreeDocumentFactory. +4 tests. (Build-break
guarantee for Inspector/hand-edit bindings is BATCH-17.)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
