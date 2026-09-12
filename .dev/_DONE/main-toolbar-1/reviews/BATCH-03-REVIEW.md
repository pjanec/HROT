# BATCH-03 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
Phase-1 infra: `MainToolbarManager` (jitter-free declared height, perspective-filtered entries +
separators), `IconHandle` size overloads on `IconWidgets` (with disabled no-hit-area path), 15 §5.1
icon keys on `SilkIconProvider`, and an `AssetKind → IconKey` map. MTB-P1-T1/T2/T4 complete.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → **0 errors, 0 new warnings**.
- New tests run by lead: `MainToolbarManagerTests` + all `IconWidgetsTests` → **43 passed, 0 failed**
  (incl. the 8 toolbar + 13 new IconHandle tests). `IconKeysTests` (9) pass within the green
  `Hrot.Editor.AiShared.Tests` suite (885/0).
- **Pre-existing-failure audit:** worker claimed 19 `Fdp.Presentation.Tests` failures as pre-existing.
  Verified on the BATCH-02 baseline `bb68fb00`: `DebugPrimitiveRenderer2DTests`(7),
  `EntityInspectorPanelTests`(3), `EventBrowserPanelTests`(1) fail identically there. The remaining
  Vis2D `DebugGizmoLayer*`/`DebugPrimitiveRenderer2D*` NRE failures are the same renderer family,
  also pre-existing. **BATCH-03 introduced zero new failures.**
- `MainToolbarManager.cs` read line-by-line: `Height = max declaredHeight` over ALL items (jitter-free,
  §4.1.1 ✓); perspective filter `null=global` ✓; last-write-wins ✓; clean headless seam
  (`GetVisibleItemPlan`) with no ImGui in the logic path. Did NOT touch `WindowManager.cs`/`Program.cs`
  (correctly deferred to T3).
- Scope: only the 3 tasks' files + the documented `NodeEditor.Core` ProjectReference on
  `Fdp.Presentation` (in-scope for T2, required for `IconHandle`). No legacy deletions, no scope creep.

## Test Quality
Strong. T1 tests assert ordering via a recording delegate, height invariance across perspectives,
and last-write-wins by which delegate fires. T2 disabled test asserts never-true + no-hit-area + state
preserved. T4 asserts each key resolves, kinds map correctly, and prefix-only/unknown keys return false.
No tautological/skipped tests.

## Verdict
APPROVED. MTB-P1-T1, MTB-P1-T2, MTB-P1-T4 → `[x]`. Phase 1 still needs MTB-P1-T3 (next batch).

## Commit Message
```
feat(main-toolbar): MainToolbarManager + IconHandle widgets + icon keys (MTB-P1-T1, T2, T4)

New MainToolbarManager (Fdp.Presentation): jitter-free declared Height, perspective-filtered
entries + separators, headless GetVisibleItemPlan seam. IconWidgets IconHandle overloads
(IconButton/ToggleIcon/Tooltip) with disabled no-hit-area dimmed path; +NodeEditor.Core ref.
SilkIconProvider: 15 §5.1 keys. AssetKindIcons: AssetKind→IconKey + ScenarioIconKey (DEC-2).
Tests: 30 new (8 toolbar, 13 icon-widget, 9 icon-keys), all pass; 0 new warnings.
Pre-existing Fdp.Presentation.Tests failures verified present at baseline bb68fb00.
```
