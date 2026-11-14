# BATCH-55 — SaveAs Enter-bleed-through (A22) + File→Scenario→Save Scenario As does nothing (A23)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
Touch ONLY: `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs`,
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (+ a NodeEditor.UI test file only if trivially useful).

## A22 — SaveAs dialog auto-confirms on the Enter that opened it
Repro: New → recipe picker → press **Enter** to pick a recipe → the SaveAs browser opens AND is immediately
confirmed by the SAME Enter keystroke (asset created with the default name, no chance to type). Double-click works
(no Enter). Cause: the picker confirms on Enter and opens the SaveAs dialog in the same ImGui frame; the dialog's
Enter-confirm paths then fire on the still-down Enter.

**Fix — swallow Enter until it is released after Open:** in `SaveAsBrowserDialog`:
- Add `private bool _swallowEnter;`. Set `_swallowEnter = true;` in `Open(...)`.
- At the TOP of `DrawFrame` (before any Enter handling), update it:
  ```csharp
  if (_swallowEnter && !ImGui.IsKeyDown(ImGuiKey.Enter) && !ImGui.IsKeyDown(ImGuiKey.KeypadEnter))
      _swallowEnter = false;
  ```
- Guard EVERY Enter-confirm path with `!_swallowEnter`:
  - `DrawNameField`: `if (nameEnter && !_swallowEnter) ConfirmActive();`
  - `DrawButtons` global-Enter block: add `&& !_swallowEnter`.
  - `DrawOverwritePopup` Enter handler: add `&& !_swallowEnter` (defensive).
  While Enter is held from the opening keystroke, `_swallowEnter` stays true → no premature confirm; once the user
  releases and presses Enter again, it confirms normally. (Esc, mouse, and typing are unaffected.)

## A23 — File→Scenario→Save Scenario As does nothing
Cause: in `EditorSubsystem`, the `openSaveAsDialog` seam passed to `ScenarioMenuCommands.Register` (≈ line 2659)
constructs the OLD `Hrot.Editor.AiShared.Recipes.SaveAsDialog` and calls `Confirm()` immediately with no UI — so
nothing is shown and nothing saves. The working scenario Save-As is the local `Action openScenarioSaveAs` (≈ line
2332), which opens the `SaveAsBrowserDialog` and calls `SaveScenarioAs` on confirm (it is already wired as the
shell's `requestScenarioSaveAs`).

**Fix:** replace the entire `openSaveAsDialog:` lambda body in the `ScenarioMenuCommands.Register(...)` call with a
delegation to the working flow:
```csharp
openSaveAsDialog:     cb => openScenarioSaveAs(),
```
(`openScenarioSaveAs` performs the browser + `SaveScenarioAs` itself, so the `cb` is not needed — do NOT also call
`SaveScenarioAs` or you double-save.) Confirm `openScenarioSaveAs` is in scope at the call site (same method body —
it is defined earlier in the same `RegisterWindows`/`Initialize` method). Remove the now-dead local
`ScenarioSaveAsAsset`/`SaveAsDialog` construction in that lambda. Do NOT change `openScenarioSaveAs` itself or the
`requestScenarioSaveAs:` wiring.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/NodeEditor.UI.csproj
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0`; builds 0 warnings.

## Definition of done
- New → recipe → Enter opens the SaveAs dialog and WAITS for the name (no auto-confirm); a deliberate second Enter
  confirms. Double-click still works. File→Scenario→Save Scenario As opens the SaveAs browser and saves on confirm.
- Builds 0 warnings; both suites `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-55-REPORT.md`.

If something cannot be done as specified, STOP and report why rather than stubbing.
