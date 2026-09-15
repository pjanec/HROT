# BATCH-29 Review

**Batch:** BATCH-29 (MTB-P8-T3 — wire Open-Asset picker through NodeEdit; retire AssetPickerModal path)
**Reviewer:** Development Lead
**Date:** 2026-06-12
**Status:** ✅ APPROVED

## Summary
Added `AssetPickerLauncher` (testable glue: builds a Tree `PickerRequest` from `AssetPickerSource.BuildEntries`,
opens via an injected seam, routes the picked `Tag` → `AssetPickActionRouter.Route` / `onPicked`). Rewired all
four Open-Asset entry points in `EditorSubsystem` to it through a **dedicated `_shellPickers` registry**
(`DrawFrame`-ed once at top-level DrawUI), and removed the `AssetPickerModal` production wiring.

## Verification (independent)
- Read `AssetPickerLauncher.cs`: matches spec — Tree/Single request, `ItemsProvider = source.BuildEntries`,
  result handler routes `result.First.Tag as IEditableAsset` via `onPicked ?? router.Route`; cancel → noop;
  null-safe ctor.
- Read the `EditorSubsystem.cs` diff: 5 edits exactly as specified — dedicated `_shellPickers`
  (`SetServices(IconProvider, EditorTheme)`), `DrawModal → _shellPickers.DrawFrame`, launcher built only when
  `_assetPickRouter != null` (bare-ctor safe), scenario `openPicker` → `launcher.Open(kinds, callback)`
  (ScenarioMenuCommands signature unchanged), `shell.openAsset` → `launcher.Open(All)`. Toolbar/menu/Ctrl+O
  registration preserved. Docked browser + in-canvas `adapterBundle.PickerRegistry` untouched.
- `grep` confirms **no dangling `_assetPickerModal` references**. `AssetPickerModal.cs`, `AssetBrowserPanel`,
  docked browser, and their tests remain (no deletions — ORCH §5 respected).
- Double-`DrawFrame` correctly avoided: the global picker uses its own registry, separate from the one canvas
  windows already `DrawFrame`.
- Tests assert **actual values**: captured request Layout/SelectionMode/Title, `ItemsProvider()` Tag identity
  + non-null IconKey, router delegate fired on confirm, nothing on cancel, `onPicked` path bypasses router,
  scenario filter yields only scenarios.

## Test results (independently run)
- `Hrot.Editor.Tests`: **183/183 pass** (AssetPickerLauncherTests ×5 + AssetPickActionRouterTests ×9 +
  EditorSubsystem guardrails incl. `…_PopulatesMainToolbar`). Build 0 warnings.
- `Hrot.Blueprints.Tests`: **1868 passed / 7 failed / 8 skipped** (34s). All 7 failures are PRE-1 family
  (AiPrimitive golden ×2, Stage8 ×2, alloc ×2, MoveToAndFire snapshot). **Zero new failures** — and 2
  fewer than the prior 9 (the CF breakpoint-drift tests now pass, fixed by recent blueprint-debug commits
  on this branch). T3 touches no Blueprints code; confirmed no regression.

## Issues Found
None. (No in-review fixes required.)

## 📝 Commit Message
```
feat(main-toolbar): wire Open-Asset picker through NodeEdit Tree picker; retire AssetPickerModal path (MTB-P8-T3)

Completes MTB-P8-T3 (Phase 8 BATCH-29) — Phase 8 complete.

- AssetPickerLauncher (Hrot.Editor/Browser): testable glue — builds a Tree-layout
  PickerRequest from AssetPickerSource.BuildEntries, opens via an injected openPicker
  seam, routes the picked PickerEntry.Tag → AssetPickActionRouter.Route (or onPicked).
- EditorSubsystem: all four Open-Asset entry points (toolbar / File→Open Asset… / Ctrl+O
  = All; Scenario→Load = Scenario) now open the NodeEdit entry-driven picker
  (PickerRegistry.OpenPicker, Tree layout) via the launcher. Dedicated _shellPickers
  registry DrawFrame()-ed once at top-level DrawUI (separate from the canvas-window
  registry to avoid double-DrawFrame). [DEC-15]
- Retired the AssetPickerModal production path (field + construction + DrawModal call);
  the class, AssetBrowserPanel, docked browser, and all tests remain (no deletions).

Tests: Hrot.Editor.Tests 183/183 (5 new AssetPickerLauncherTests); Blueprints.Tests
stability unchanged (PRE-1 only, zero new). Build 0 warnings.

Related: ASSET-PICKER-UX-DESIGN.md, TASK-DETAIL MTB-P8-T3, DEC-15

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
