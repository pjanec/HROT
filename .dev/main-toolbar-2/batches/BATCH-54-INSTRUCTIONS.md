# BATCH-54 — consolidate scenario menu into a File → Scenario submenu (all always-enabled) (BUG-A21)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
Touch ONLY: `Hrot/Subsystems/Hrot.Editor/ScenarioMenuCommands.cs`,
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`,
`Hrot/Subsystems/Hrot.Editor.Tests/ScenarioMenuTests.cs`.
Do NOT touch `ShellSaveCommands.cs` or `SaveCommandsTests.cs`.

## Goal (user-specified)
- The standalone top-level **"Scenario"** main menu disappears.
- The File menu's currently-**disabled** "Save Scenario" / "Save Scenario As…" items are removed.
- The **File** menu gains a **"Scenario"** submenu containing, ALL ALWAYS-ENABLED:
  **New Scenario, Load Scenario, Save Scenario, Save Scenario As, Migration History**.

## Background (already verified — do not re-investigate)
- `EditorCommandsImpl.Register` does `_commands[id] = …` → **last registration wins**. `ScenarioMenuCommands.Register`
  runs AFTER `ShellSaveCommands.Register` in `EditorSubsystem`, so re-adding `scenario.save`/`scenario.saveAs` here
  (always-enabled) overrides `ShellSaveCommands`' gated versions. The unified `shell.save` (Ctrl+S) scenario branch is
  independent and unaffected. No `ShellSaveCommands` change is needed.

## Part 1 — `ScenarioMenuCommands.cs`
1. Change `MenuPrefix` from `"Scenario"` to **`"File/Scenario"`** (so every item lands under File → Scenario).
2. **Re-add** the `scenario.save` and `scenario.saveAs` registrations (they were removed in BATCH-53), with the same
   handlers as before AND **always enabled**:
   - `scenario.save` (SaveId), display **"Save Scenario"**, `isEnabled: () => true`, handler:
     ```csharp
     if (string.IsNullOrEmpty(editorLogic.LoadedScenarioName))
         openSaveAsDialog(name => editorLogic.SaveScenarioAs(name));
     else
         editorLogic.SaveCurrentScenario();
     ```
   - `scenario.saveAs` (SaveAsId), display **"Save Scenario As…"**, `isEnabled: () => true`, handler:
     `openSaveAsDialog(name => editorLogic.SaveScenarioAs(name));`
3. **Rename the display names** of all five so they read fully inside the generic File menu, and make ALL of them
   `isEnabled: () => true`:
   - `scenario.new` → **"New Scenario"**
   - `scenario.load` → **"Load Scenario…"**
   - `scenario.save` → **"Save Scenario"**
   - `scenario.saveAs` → **"Save Scenario As…"**
   - `scenario.migrationHistory` → **"Migration History…"** — change its `isEnabled` from the
     `!string.IsNullOrEmpty(LoadedScenarioName)` gate to **`() => true`** (always available; the handler already
     no-ops gracefully when there is no loaded scenario).
   (The `RegisterCommand` helper already composes `menuPath = $"{MenuPrefix}/{displayName.Replace("…","")}"`, so these
   become `File/Scenario/New Scenario`, `…/Load Scenario`, `…/Save Scenario`, `…/Save Scenario As`,
   `…/Migration History`.)
4. Order the five registrations: New, Load, Save, Save As, Migration History (so the submenu reads in that order).

## Part 2 — `EditorSubsystem.cs`
Remove the two File-menu surfacings of the gated scenario save commands (≈ lines 3235-3241):
```csharp
if (windowManager.ShellCommands.Get(... ScenarioSaveId) != null)
    MenuCommandAdapter.Register(..., ScenarioSaveId, "File/Save Scenario");
if (windowManager.ShellCommands.Get(... ScenarioSaveAsId) != null)
    MenuCommandAdapter.Register(..., ScenarioSaveAsId, "File/Save Scenario As…");
```
Delete both `if`-blocks entirely (the File → Scenario submenu now provides these, always-enabled). Leave the
File/Open Asset, New Asset, Save, Save As…, Save All registrations unchanged. Do NOT change the
`ScenarioMenuCommands.Register(...)` call site (its `MenuPrefix` change handles the relocation).

## Part 3 — `ScenarioMenuTests.cs`
Update to the new structure:
- The scenario items now live under the **`File` → `Scenario`** submenu path (not a top-level `Scenario` node).
  Update the menu-tree navigation/assertions accordingly (e.g. `File` node → `Scenario` child → its five leaves).
- The submenu has **five** items: New Scenario, Load Scenario, Save Scenario, Save Scenario As, Migration History.
- Re-add assertions that `scenario.save` / `scenario.saveAs` are registered and invoke the editor-logic save paths
  (mirror the pre-BATCH-53 Save/SaveAs tests), and assert all five are **enabled** (`IsEnabled() == true`),
  including Migration History.
- Keep the tests real and meaningful; do not weaken. If the GlobalMenuRegistry/menu-tree helper can't express a
  3-level path (`File/Scenario/<item>`), STOP and report (do not work around it).

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
`Failed: 0`; build 0 warnings.

## Definition of done
- No top-level Scenario menu; File → Scenario submenu with New/Load/Save/Save As/Migration History, all enabled; the
  old disabled File/Save Scenario(+As) items gone; Ctrl+S unchanged. Build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-54-REPORT.md`: the MenuPrefix change, re-added always-enabled Save/SaveAs,
  the EditorSubsystem removal, the test updates, build/test summary.

If something cannot be done as specified, STOP and report why rather than stubbing.
