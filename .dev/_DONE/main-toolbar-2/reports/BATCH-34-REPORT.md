# BATCH-34 REPORT — MTB2-T5: unified File menu + perspective display-label "Scenario"

**Date:** 2026-06-12 · **Model:** deepseek-v4-pro · **Branch:** blueprint-integ-1

---

## Implementation Summary

### Part A — `WindowManager.cs` (perspective display-label)

Added three members to `WindowManager`:

| Member | Purpose |
|--------|---------|
| `_perspectiveLabels` | `Dictionary<string, string>` — maps perspective id → display label override |
| `RegisterPerspectiveLabel(perspective, label)` | Public registration API; idempotent (last-write-wins) |
| `GetPerspectiveLabel(perspective)` | Returns the registered label, or falls back to the perspective id itself |

**`RenderPerspectiveMenu` change:** The `Gui.MenuItem()` call now uses `GetPerspectiveLabel(perspective)` for the item text, while `SelectPerspective(perspective)` continues to use the raw id. **`BuildPerspectiveMenuModel` signature is unchanged** — it returns tuples keyed by perspective id, not label.

The `"Editor"` perspective key is **never renamed** — it retains its identity for cluster node/subsystem name and ~10 `PerspectiveBound` window keys.

### Part B — `EditorSubsystem.cs` (File menu wiring)

**1. Perspective label registration** (near the existing icon key registrations, ~L3050):
```csharp
windowManager.RegisterPerspectiveLabel("Editor", "Scenario");
```

**2. File menu save entries** (after the existing `File/Open Asset…` registration, ~L3081):
Added 5 `MenuCommandAdapter.Register` calls, each null-safe guarded with `ShellCommands.Get(id) != null`:

| Menu Path | Command ID | Guard |
|-----------|-----------|-------|
| `File/Save` | `shell.save` | always present (registered by `ShellSaveCommands.Register` earlier) |
| `File/Save As…` | `shell.saveAs` | always present |
| `File/Save All` | `shell.saveAll` | always present |
| `File/Save Scenario` | `scenario.save` | present when `saveScenarioAction` seam supplied |
| `File/Save Scenario As…` | `scenario.saveAs` | present when `requestScenarioSaveAs` seam supplied |

All 5 guards pass on a bare-ctor `RegisterWindows` because both scenario seams are always provided in the production `ShellSaveCommands.Register()` call.

**`ScenarioMenuCommands` left untouched** — its existing Scenario-menu Save/Save-As entries remain in place (DBT-A2 deferred).

---

## Design Decisions

1. **Null-safety via `Get(id) != null` guard**: Instead of a single `if (_editorLogic != null)` block, each registration individually checks whether the command exists. This is more robust — if a future refactor removes one command, only that menu item drops, not all five.

2. **Perspective label is display-only**: The label does NOT participate in `GetPerspectives()`, `BuildPerspectiveMenuModel()`, `SelectPerspective()`, or `IsPerspectiveActive()`. It only affects the render text in `RenderPerspectiveMenu`. This is the minimal change possible — zero risk of breaking perspective switching logic.

3. **Test file placement**: `PerspectiveLabelTests.cs` is a new dedicated file in `Fdp.Presentation.Tests/ImGui/WindowManager/` — self-contained, following the pattern of `PerspectiveMenuTests.cs`. The `EditorSubsystemBlueprintWindowsTests.cs` test is appended to the existing file, following its existing patterns.

4. **No `BuildPerspectiveMenuModel` signature change**: Other consumers (tests, callers) rely on this method returning tuples keyed by perspective id. Changing it would be a breaking change affecting unrelated code.

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` | Added `_perspectiveLabels` dictionary, `RegisterPerspectiveLabel()`, `GetPerspectiveLabel()`; modified `RenderPerspectiveMenu` to use label for display text |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Added `RegisterPerspectiveLabel("Editor", "Scenario")`; added 5 null-safe File menu `MenuCommandAdapter.Register` calls for save commands |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/PerspectiveLabelTests.cs` | **New file** — 2 tests: `PerspectiveLabel_OverridesDisplay_NotId`, `SelectPerspective_UsesId_NotLabel` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs` | Added `EditorSubsystem_RegisterWindows_FileMenuHasSaveCommands` test |

---

## Tests Added (3 exact names)

### PerspectiveLabelTests (new file)

1. **`PerspectiveLabel_OverridesDisplay_NotId`** — `wm.RegisterPerspectiveLabel("Editor","Scenario")` → `wm.GetPerspectiveLabel("Editor") == "Scenario"`; `wm.GetPerspectiveLabel("BTree") == "BTree"` (unset → id).

2. **`SelectPerspective_UsesId_NotLabel`** — After registering the label, `wm.SelectPerspective("Editor")` → `wm.IsPerspectiveActive("Editor")` true AND `wm.IsPerspectiveActive("Scenario")` false (id drives switching, not the label).

### EditorSubsystemBlueprintWindowsTests (appended)

3. **`EditorSubsystem_RegisterWindows_FileMenuHasSaveCommands`** — After `RegisterWindows` on bare subsystem: traverses `wm.GlobalMenu.Root.Children["File"].Children`, asserts `ContainsKey` for `"Save"`, `"Save As…"`, `"Save All"`, `"Open Asset…"`, `"Save Scenario"`. Also asserts `wm.GetPerspectiveLabel("Editor") == "Scenario"`.

---

## Test Results

### PerspectiveLabel tests (filtered)
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 7 ms
```

### EditorSubsystemBlueprintWindows tests (filtered)
```
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 2 s
```

### Build
```
Hrot.Editor — 0 Warnings, 0 Errors
Fdp.Presentation — 0 Warnings, 0 Errors
```

(Pre-existing CS0618/CS8601/CS8602 warnings in Hrot.Blueprints.Tests are from other files — no new warnings introduced.)

---

## Challenges

- **None.** The changes were straightforward — the existing infrastructure (GlobalMenuRegistry, MenuCommandAdapter, ShellSaveCommands) provided well-defined seams. The batch spec was precise about every line to change and every test to write.

---

## Integration Notes

- The `Editor` perspective now displays as **"Scenario"** in the Perspective menu, while all internal switching, window ownership, and `PerspectiveBound` keys continue to use `"Editor"`.
- The dynamic Save label from T4 (`DescribeActiveTarget`) flows through automatically because `shell.save` already has `DynamicDisplayName` set — `MenuCommandAdapter.ApplyLeafNode` wires it onto the `MenuItemNode.DynamicLabel`.
- `ScenarioMenuCommands` Save/Save-As entries in the Scenario menu still exist (DBT-A2).
- All wiring is null-safe — bare-ctor `RegisterWindows` does not throw.
