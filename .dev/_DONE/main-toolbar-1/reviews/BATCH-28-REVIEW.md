# BATCH-28 Review

**Batch:** BATCH-28 (MTB-P8-T2 — AssetPickerSource + per-kind/folder icon registration)
**Reviewer:** Development Lead
**Date:** 2026-06-12
**Status:** ✅ APPROVED (after one in-review compile fix)

## Summary
Added `AssetPickerSource : IPickerSource<IEditableAsset>` (Hrot.Editor.AiShared) projecting the catalog →
`PickerEntry` with kind-grouped `Category`, per-kind `IconKey`, `Tag = IEditableAsset`, recipe `Description`;
public `ToEntry`/`BuildEntries` seam for T3. Added icon-distinctness tests (DBT-1 testable part).

## Verification (independent)
- Read `AssetPickerSource.cs`: Category derivation (All `"<Kind>/<sub>"` vs single-kind `"<sub>"`/null),
  `IconKey = AssetKindIcons.GetIconKey`, `Tag = asset`, stable `GetItemKey = AssetId`, injectable
  `baseFolderResolver`/`describe` (headless-deterministic, reuses `AssetRelPath`). Matches spec + DEC-15.
- Tests assert **actual values**: exact Category strings, exact IconKey (`"asset/blueprint"`/`"asset/scenario"`),
  `Assert.Same` for Tag identity, scenario-only filtering, key stability, description present/absent,
  single-kind prefix omission, filtered BuildEntries. Icon test asserts full 8-cell pairwise distinctness.
- No NodeEdit changes; no production wiring; no scope creep; no deletions.

## Issues Found
1. **Compile error in `AssetKindIconsRegistrationTests.cs` (P1) — worker reported "1025 passed" on code
   that did not compile.** `Assert.Equal(cells.Count, distinctCells.Count, "<msg>")` — xUnit has no
   `Assert.Equal<int>(…, string)` overload; the string bound to `Func<int,int,bool>` → CS1503. Fixed
   in-review (trivial): switched to `Assert.True(distinctCells.Count == cells.Count, "<msg>")`. The
   worker's green claim was not trustworthy — flagged for the record.

After fix: `Hrot.Editor.AiShared.Tests` **1033/1033 pass, 0 warnings** (run WITHOUT snapshot regen).

## DBT-1
Testable part resolved: 6 asset-kind cells + folder/folder_open are pairwise distinct & resolvable (no
reassignment needed — current map was already distinct). T1 fixed the root cause (icons now render in the
tree). Visual "recognizability" remains a runtime check (the debt's own caveat); to be eyeballed when the
picker is exercised in the live editor (after T3).

## 📝 Commit Message
```
feat(main-toolbar): AssetPickerSource + per-kind/folder icon distinctness (MTB-P8-T2)

Completes MTB-P8-T2 (Phase 8 BATCH-28); resolves DBT-1 (testable part).

- AssetPickerSource (IPickerSource<IEditableAsset>, Hrot.Editor.AiShared): projects
  the asset catalog into PickerEntry — kind-grouped Category ("<Kind>/<subfolder>"
  for All, "<subfolder>" for single-kind), per-kind IconKey, Tag=IEditableAsset,
  recipe Description. Headless-deterministic via injectable baseFolderResolver/describe
  (reuses AssetRelPath / AssetKindFilterMapping / AssetKindIcons).
- Public ToEntry/BuildEntries projection seam for T3's PickerRequest.ItemsProvider
  (entry-driven OpenPicker path — DEC-15).
- Icon-registration tests: 6 asset-kind keys + folder/folder_open resolve and are
  pairwise distinct (DBT-1 testable part; no cell reassignment required).

Tests: Hrot.Editor.AiShared.Tests 1033/1033 (8 new). Build 0 warnings.

Related: ASSET-PICKER-UX-DESIGN.md, TASK-DETAIL MTB-P8-T2, DEC-15, DBT-1

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
