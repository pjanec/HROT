# BATCH-51 REPORT — SaveAs/New dialog: dbl-click folder expand + overwrite popup UX (BUG-A19)

**Date:** 2026-06-12
**File touched:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs`

## Part A — Double-click folder row toggles expand/collapse

**Change:** Added a double-click handler in `DrawFolderNode` (lines 360–366). After the existing single-click handler, a new block checks `ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)` and toggles `_collapsedFolders` for the folder's full path — removing it if collapsed, adding it if expanded. It also sets `_destination = fullPath`, so double-click selects the folder in addition to toggling expansion.

**Net behavior:** Single-click selects; double-click selects + expands/collapses.

## Part B — Overwrite confirmation popup UX

Three sub-fixes applied to `DrawButtons` and `DrawOverwritePopup`:

### B.1 — Esc/Enter gating while popups are open

**Change in `DrawButtons` (lines 448, 480–481, 486–495):**

- Added `bool noPopupOpen = !_pendingOverwriteConfirm && _newFolderTarget == null;`
- The Esc-key condition now gates on `noPopupOpen`: `if (ImGui.Button("Cancel", ...) || (noPopupOpen && ImGui.IsKeyPressed(ImGuiKey.Escape)))`. The Cancel **button** click still works unconditionally; only the Esc **key** is gated so the popup can consume it.
- The global Enter block is wrapped in `if (noPopupOpen)`, preventing Enter in the main window from firing while a popup is open.

This means: Esc in the overwrite (or new-folder) popup now dismisses ONLY the popup and returns to the SaveAs dialog — it no longer cascades to close everything.

### B.2 — Overwrite popup: Enter confirms, default focus on Overwrite button

**Change in `DrawOverwritePopup` (lines 560–585):**

- `ImGui.SetKeyboardFocusHere()` before the Overwrite button when `IsWindowAppearing()` — gives the Overwrite button keyboard focus on open.
- `ImGui.SetItemDefaultFocus()` after the Overwrite button — marks it as the default action for Enter/Tab navigation.
- Added an explicit Enter/KeypadEnter handler: `if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)) { ConfirmOverwrite(); ImGui.CloseCurrentPopup(); }` — in addition to clicking the Overwrite button.
- Existing Esc behavior is unchanged: it sets `_pendingOverwriteConfirm = false` and closes only the popup. Combined with B.1's gating, the main dialog stays open.

### New-folder popup benefit

The new-folder popup (`DrawNewFolderPopup`) already had Esc dismissal and the B.1 gate applies equally to it (`noPopupOpen` checks both `_pendingOverwriteConfirm` and `_newFolderTarget`), so Esc in the new-folder popup also returns to the SaveAs dialog without closing everything.

## Build & Tests

- **Build:** `NodeEditor.UI` — 0 warnings, 0 errors.
- **Tests:** `NodeEditor.UI.Tests` — **59 passed, 0 failed, 0 skipped.** All 5 existing `SaveAsBrowserDialogTests` remain green (`ConfirmActive`, `ConfirmOverwrite`, `PendingOverwriteConfirm` covered headlessly). The overwrite gating is ImGui-level and runtime-verified.
