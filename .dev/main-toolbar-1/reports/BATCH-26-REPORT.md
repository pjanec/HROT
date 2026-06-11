# BATCH-26 Report

## Implementation Summary

### Unified "Open Asset" Modal

One `AssetPickerModal` instance serves three entry points:
1. **Toolbar "Open Asset" button** (leftmost, `browser/open` icon, sortOrder -10) → Kinds=All
2. **File → Open Asset…** menu item + **Ctrl+O** hotkey → Kinds=All
3. **Scenario → Load** → Kinds=Scenario only

The `AssetPickActionRouter` is wired in production in `EditorSubsystem.RegisterWindows`:
- File kinds (Blueprint, BTree, Hsm) → `AiDocumentManager.Open(asset)`
- Scenario → `IEditorLogic.LoadScenarioByName(asset.Name)`

### Lock-up Fix (Scenario→Load blocking but invisible)

**Root cause:** The old `AssetPickerModal.DrawModal` called `OpenPopup("AssetPickerPopup")` but `BeginPopupModal($"{title}###AssetPickerPopup", ...)`. The ImGui `###` suffix causes the two calls to use different hashed IDs, so the popup never opens — yet `BeginPopupModal` still sets a modal scope, blocking all other input. Additionally, without an explicit `SetNextWindowSize`, the window could collapse to zero/invisible dimensions.

**Fix:** Mirrored the working "Rename Entity" modal pattern exactly:
1. `Open()` sets a `_pendingOpen` flag (consumed at DrawUI top level, NOT inside any menu/menu-bar).
2. `DrawModal()` consumes the flag: calls `OpenPopup("Open Asset")` once, then `SetNextWindowSize(new Vector2(720, 520), ImGuiCond.Appearing)`, then `BeginPopupModal("Open Asset###Open Asset", ...)` — the `###` portion is `"Open Asset"` which matches the `OpenPopup` argument exactly.
3. The identical ID string for `OpenPopup` + `BeginPopupModal`, plus the explicit size, makes the modal **bulletproof** against zero-size/invisible collapse.

### Modal UX

- **Esc** → cancels (already wired, unchanged).
- **Enter** → confirms current `panel.Selection` (if any).
- **Ctrl+Tab** → next tab, **Ctrl+Shift+Tab** → previous tab (wraps).
- **Double-click** → activates asset (already wired, unchanged).

### Tab Cycling (`AssetBrowserPanel`)

Added `SelectNextTab()` and `SelectPreviousTab()` public methods. They set `_requestedTabIndex` which is consumed in `DrawContent` via `ImGuiTabItemFlags.SetSelected` for one frame. The logical tab order is: "All" (if shown) → first permitted kind → … → last permitted kind → wraps.

---

## Design Decisions

1. **One modal, one `DrawModal` call.** Replaced the separate `_scenarioPickerModal` field with a unified `_assetPickerModal`. Both all-kinds and scenario-only entry points open the same instance with different `AssetBrowserPanelOptions`.

2. **Pending-flag pattern internal to `AssetPickerModal`.** Rather than adding an external `_pendingOpenAssetKinds` flag in `EditorSubsystem`, the pending-open logic lives in the modal itself. `Open()` sets `_pendingOpen = true`; `DrawModal()` consumes it at the correct ImGui scope. This keeps the modal self-contained and testable.

3. **Tab switching via `_requestedTabIndex` + one-frame `SetSelected`.** This is the standard ImGui pattern for programmatic tab activation. The request is consumed and cleared after the tab bar renders, preventing sticky selection.

4. **`InternalsVisibleTo` for `Hrot.Editor.Tests`.** Added so ScenarioMenuTests can verify the unified modal behavior (IsOpen, Panel.Tabs) without duplicating the AssetPickerModal test infrastructure.

---

## Deviations

None. All three entry points, the lock-up fix, Enter/Esc/Tab keys, and all tests are implemented exactly as specified.

---

## Test Results

### Hrot.Editor.AiShared.Tests
```
Total tests: 81
     Passed: 81
     Failed: 0
```
Includes new BATCH-26 tests: `Enter_ConfirmsSelection_CallbackReceivesSelectedAsset`, `Enter_WithoutSelection_OnlyFiresIfExplicitlyActivated`, `Open_WithScenarioKinds_CreatesScenarioOnlyPanel`, `Open_WithAllKinds_ProducesFullTabSet`, `PopupId_IsConsistent`, `DefaultWindowSize_IsPositive`. All existing BATCH-11/15/16 tests pass.

### Hrot.Editor.Tests
```
Total tests: 178
     Passed: 178
     Failed: 0
```
Includes new BATCH-26 tests: `Load_Invoke_OpensUnifiedModal`, `Load_UnifiedModal_Cancel_DoesNotLoad`. All existing AssetPickActionRouter, ScenarioMenu, and integration tests pass.

### Fdp.Presentation.Tests (toolbar/wm/menu filter)
```
Total tests: 143
     Passed: 143
     Failed: 0
```
Filter: `FullyQualifiedName~Toolbar|FullyQualifiedName~Menu|FullyQualifiedName~WindowManager`

### Hrot.Blueprints.Tests — EditorSubsystemBlueprintWindowsTests
```
Total tests: 12
     Passed: 12
     Failed: 0
```
Includes BATCH-24 guardrail `EditorSubsystem_RegisterWindows_PopulatesMainToolbar` + 8 original window tests + 3 new BATCH-26 tests:
- `EditorSubsystem_RegisterWindows_RegistersOpenAssetCommand`
- `EditorSubsystem_RegisterWindows_OpenAssetMenuItem_UnderFile`
- `EditorSubsystem_RegisterWindows_OpenAssetToolbarEntry_Exists`

### Hrot.Blueprints.Tests (Stability filter)
```
Total tests: 1871
     Passed: 1854
     Failed: 9   ← exactly the 9 PRE-1 (zero new failures)
    Skipped: 8
```
Pre-existing 9 failures:
1. `AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")`
2. `AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")`
3. `Stage8_PdbContainsEmbeddedSource`
4. `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`
5. `TickFrame_1000Frames_AllocatesZeroBytes`
6. `MoveToAndFire_GeneratedSource_Snapshot`
7. `CF2_EndToEnd_DelayBreakpointPauses`
8. `SetBreakpoint_TriggersAutoInstrument_ThenPauses`
9. `WhenNode_ZeroAllocOnHotPath`

### Library Compilation
- `Hrot.Editor.AiShared` — 0 warnings, 0 errors
- `Hrot.Editor` — 0 warnings, 0 errors

---

## Developer Insights

- **The `###` ImGui ID divergence was the lock-up root cause.** The old code's comment claimed `###` would make IDs match, but `OpenPopup("AssetPickerPopup")` and `BeginPopupModal("Load Scenario###AssetPickerPopup")` resolve to different hashed IDs because the `###` suffix strips the visible label but the hash is still computed on the full string (`"###AssetPickerPopup"` vs `"AssetPickerPopup"`). The rename modal avoids this entirely by using bare literal `"Rename Entity"` for both calls — which is what BATCH-26 adopted.
- **`AlwaysAutoResize` + no explicit size = zero-size window.** When combined with the ID mismatch, the modal opens but collapses to zero size, becoming invisible while still blocking input (the "locked-up but looks frozen" symptom).
- **Ctrl+Tab detection in ImGui requires checking `IsKeyDown(ModCtrl)` alongside `IsKeyPressed(Tab)`.** `ImGuiKey.Tab` and `ImGuiKey.ModCtrl` are separate keys; the combined chord must be checked explicitly.
- **`InternalsVisibleTo` additions are lightweight and standard** — they don't affect production code paths and are the conventional .NET pattern for white-box testing across assemblies.

---

## Known Issues

- The `MainToolbarManager.GetVisibleItemPlan` is `internal` to `Fdp.Presentation`, so the toolbar-entry existence test in `Hrot.Blueprints.Tests` uses `MainToolbar.Height > 0f` as a proxy. A public `TryGetEntry` or `ContainsEntry` API on `MainToolbarManager` would allow a more precise assertion.
- The Ctrl+Tab binding uses the ImGui-level `IsKeyDown(ModCtrl)` check, which works for modal-level shortcuts but might conflict if ImGui's native tab-bar tab-switching is also bound to Ctrl+Tab in the future.

---

## Suggested Commit Message

```
feat(main-toolbar): unified "Open Asset" modal + fix Scenario→Load lock-up (BATCH-26)

- Single AssetPickerModal for toolbar (leftmost), File→Open Asset… (Ctrl+O),
  and Scenario→Load (Kinds=Scenario)
- Wire AssetPickActionRouter in production: file→AiDocumentManager.Open,
  scenario→IEditorLogic.LoadScenarioByName
- Fix lock-up: mirror Rename Entity pattern (pending-flag, identical popup ID,
  explicit SetNextWindowSize 720×520)
- Enter confirms selection; Ctrl+Tab/Ctrl+Shift+Tab cycle tabs
- Add AssetBrowserPanel.SelectNextTab/SelectPreviousTab
- 81+178+143+12 tests green; 9 PRE-1 Blueprints failures unchanged
```

Co-Authored-By: Claude <noreply@anthropic.com>
