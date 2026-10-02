# BATCH-51 — SaveAs/New dialog: dbl-click folder expand + overwrite popup UX (BUG-A19)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
Touch ONLY `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs` (+ its test file if needed).

## Part A — double-click a folder row toggles expand/collapse
In `DrawFolderNode`, the folder open state is driven by
`ImGui.SetNextItemOpen(!_collapsedFolders.Contains(fullPath), ImGuiCond.Always)` + the existing mouse-arrow sync.
Today only the triangle toggles; clicking the label only selects. ADD: after `bool expanded = ImGui.TreeNodeEx(...)`,
if the row is double-clicked, toggle collapse state:
```csharp
if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
{
    if (_collapsedFolders.Contains(fullPath)) _collapsedFolders.Remove(fullPath);
    else                                      _collapsedFolders.Add(fullPath);
    _destination = fullPath; // double-click also selects the folder
}
```
Single-click already selects via `ImGui.IsItemClicked()` → `_destination = fullPath` — keep it. (Net: single-click
selects, double-click selects + expands/collapses.)

## Part B — Overwrite confirmation popup UX
Current bug: pressing **Esc** in the overwrite popup cancels EVERYTHING — because `DrawButtons` also runs each frame
and its `if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape)) Close();` fires the same frame, closing
the whole dialog. Fixes:

1. **Gate the main dialog's Enter/Esc while ANY popup is open.** A popup is open when
   `_pendingOverwriteConfirm || _newFolderTarget != null`. In `DrawButtons`, wrap the Esc/Cancel-key and the
   global-Enter handling so they only act when NO popup is open:
   - `if (ImGui.Button("Cancel") || (noPopupOpen && ImGui.IsKeyPressed(ImGuiKey.Escape))) Close();`
   - the `globalEnter` confirm block: add `&& noPopupOpen`.
   where `bool noPopupOpen = !_pendingOverwriteConfirm && _newFolderTarget == null;`
   (The Cancel BUTTON itself may still close on click; only the Esc KEY must be gated so the popup can consume it.)

2. **Overwrite popup (`DrawOverwritePopup`):**
   - **Enter confirms** the overwrite: when the popup is open, `if (ImGui.IsKeyPressed(ImGuiKey.Enter) ||
     ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)) { ConfirmOverwrite(); ImGui.CloseCurrentPopup(); }` (in addition to
     the Overwrite button).
   - **Default focus on the Overwrite button** so it's the default action: `if (ImGui.IsWindowAppearing())
     ImGui.SetKeyboardFocusHere();` immediately before the Overwrite button, and `ImGui.SetItemDefaultFocus();`
     immediately after it.
   - **Tab moves focus between buttons:** with default focus set and ImGui nav, Tab/Shift-Tab already cycle the two
     buttons — just ensure both buttons are submitted normally (they are). (If the app has keyboard nav disabled this
     is a no-op; do not change global IO flags.)
   - **Esc returns to the SaveAs dialog, not close-all:** the popup's existing
     `ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape)` → `_pendingOverwriteConfirm = false;
     ImGui.CloseCurrentPopup();` is correct AND now (with Part B.1 gating) the main dialog will NOT also close. Keep
     it; verify the main window stays open after Esc-in-popup. Do the SAME Esc-gating benefit for the New-folder
     popup (already routed through the same gate).

## Tests
- Build `NodeEditor.UI` + run `NodeEditor.UI.Tests`: `Failed: 0`, 0 warnings. If there are headless tests over
  `ConfirmActive`/`ConfirmOverwrite`/`PendingOverwriteConfirm`, keep them green (the overwrite gating is ImGui-level;
  runtime-verified by the lead). Add a small headless test only if tractable (e.g. that `ConfirmOverwrite` still
  fires `onChosen(Overwrite:true)` and closes) — otherwise keep existing coverage.

## Definition of done
- Double-click a folder row in the SaveAs/New dialog expands/collapses it (and selects it); single-click selects.
- In the Overwrite popup: Enter confirms; Tab moves between buttons; **Esc dismisses ONLY the popup and returns to
  the SaveAs dialog** (the dialog stays open, ready to edit the name) — it no longer cancels everything.
- Build 0 warnings; `NodeEditor.UI.Tests` `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-51-REPORT.md`: the two changes, the Esc-gating, files/tests.

If something cannot be done as specified, STOP and report why rather than stubbing.
