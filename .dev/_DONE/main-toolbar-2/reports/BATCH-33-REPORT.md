# BATCH-33 REPORT — MTB2-T4: active-save-target resolver + Save Scenario + dynamic Save label

**Date:** 2026-06-12 · **Model:** deepseek-v4-pro · **Branch:** blueprint-integ-1

---

## Implementation Summary

### Part A — `ShellSaveCommands.cs`

Extended `ShellSaveCommands.Register()` with **5 trailing optional seams** (all default `null`):

| Seam | Type | Purpose |
|------|------|---------|
| `isScenarioContext` | `Func<bool>?` | Returns `true` when the Editor perspective is active (scenario context) |
| `hasLoadedScenario` | `Func<bool>?` | Returns `true` when a scenario is loaded |
| `saveScenarioAction` | `Action?` | Saves current scenario via `IEditorLogic.SaveCurrentScenario()` |
| `requestScenarioSaveAs` | `Action?` | Opens the scenario Save-As dialog (seed + dialog + confirm) |
| `describeActiveTarget` | `Func<string>?` | Dynamic label for shell.save: `"Save [scenario: Name]"` or `"Save [kind: Name]"` |

**Behavior changes (when seams are supplied):**

- **`shell.save` handler**: branches on `isScenarioContext` — if `true`, calls `saveScenarioAction` and returns; otherwise falls through to existing per-kind document save logic.
- **`shell.save` `IsEnabled`**: `isScenarioContext ? hasLoadedScenario : docManager.Active != null`
- **`shell.save` `DynamicDisplayName`**: set when `describeActiveTarget != null`; otherwise `null` (DisplayName "Save" used).
- **`shell.saveAs` handler**: branches on `isScenarioContext` — if `true`, calls `requestScenarioSaveAs`; otherwise existing `requestSaveAs(doc)`.
- **`shell.saveAs` `IsEnabled`**: same triage shape as shell.save.
- **`scenario.save`** (`"scenario.save"`): registered only when `saveScenarioAction != null`. DisplayName: "Save Scenario", Category: "File", IsEnabled: `hasLoadedScenario`.
- **`scenario.saveAs`** (`"scenario.saveAs"`): registered only when `requestScenarioSaveAs != null`. DisplayName: "Save Scenario As…", Category: "File", IsEnabled: `hasLoadedScenario`.
- **`shell.saveAll`**: unchanged.

**Back-compat:** When all new seams are null, the three original commands behave byte-identically to before; `scenario.save`/`scenario.saveAs` are NOT registered.

**New constants:**
- `ShellSaveCommands.ScenarioSaveId = "scenario.save"`
- `ShellSaveCommands.ScenarioSaveAsId = "scenario.saveAs"`

### Part B — `EditorSubsystem.cs`

Wired the seams in the existing `ShellSaveCommands.Register()` call (~L2343):

- `isScenarioContext: () => windowManager.CurrentPerspective == "Editor"`
- `hasLoadedScenario: () => !string.IsNullOrEmpty(_editorLogic?.LoadedScenarioName)`
- `saveScenarioAction: () => _editorLogic?.SaveCurrentScenario()`
- `requestScenarioSaveAs: openScenarioSaveAs` — local Action mirroring the existing `ScenarioMenuCommands` `openSaveAsDialog` path: `new ScenarioSaveAsAsset(...)` → `new SaveAsDialog(...)` → `dialog.Confirm()`. Guard: `_editorLogic != null && _newAssetServices != null`.
- `describeActiveTarget: () =>` returns:
  - `"Save [scenario: {LoadedScenarioName}]"` when in Editor perspective with a loaded scenario
  - `"Save [{kind}: {name}]"` when an active document is present
  - `"Save"` otherwise

All wiring is null-safe — bare `RegisterWindows` does not throw.

**ScenarioMenuCommands wiring left unchanged** (as directed by spec — duplicated lines accepted).

---

## Design Decisions

1. **Parameter naming**: The new scenario save action is `saveScenarioAction` (not `saveScenario`) to avoid shadowing the existing per-kind `SaveDelegate? saveScenario` parameter. This preserves source-level back-compat.

2. **DynamicDisplayName wiring**: Only set when `describeActiveTarget != null`. When null (back-compat / no injection), the descriptor's `DynamicDisplayName` stays `null` and the existing `DisplayName: "Save"` is used.

3. **Scenario saveAs seam**: Mirrors the exact `new ScenarioSaveAsAsset → SaveAsDialog → Confirm()` pattern from `ScenarioMenuCommands.openSaveAsDialog` (~L2500-2520). The batch spec explicitly permits duplication here; `ScenarioMenuCommands` wiring is untouched.

4. **Null-safety**: All new seams are optional and default to `null`. The production wiring passes concrete delegates, but a bare-ctor `RegisterWindows` (no seams) won't throw — `scenario.save`/`scenario.saveAs` simply aren't registered.

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs` | Added 2 command-id constants, extended `Register()` signature with 5 trailing optional seams, branched `shell.save`/`shell.saveAs` on scenario context, set `DynamicDisplayName`, registered `scenario.save`/`scenario.saveAs` conditionally |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs` | Added 7 exact named tests (see below) |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Wired new seams in `ShellSaveCommands.Register()` call; defined local `openScenarioSaveAs` Action |

---

## Tests Added (7 exact names)

All in `SaveCommandsTests`:

1. **`Save_InScenarioContext_CallsSaveScenario_NotDocument`** — `isScenarioContext = true`, spy `saveScenarioAction`; invoke `shell.save` handler → `saveScenarioAction` fired, document save NOT fired.
2. **`Save_InDocumentContext_CallsDocumentSave_NotScenario`** — `isScenarioContext = false`, active Blueprint doc; invoke `shell.save` → `saveBlueprint` fired, `saveScenarioAction` NOT fired.
3. **`SaveAs_InScenarioContext_RequestsScenarioSaveAs`** — `isScenarioContext = true`, spy `requestScenarioSaveAs`; invoke `shell.saveAs` → `requestScenarioSaveAs` fired, `requestSaveAs` NOT fired.
4. **`Save_IsEnabled_ReflectsActiveTarget`** — Four combinations: scenario-loaded (enabled), scenario-not-loaded (disabled), doc-active (enabled), doc-none (disabled).
5. **`DynamicDisplayName_NamesKindAndAsset`** — `describeActiveTarget = () => "Save [scenario: test-move]"` → captured `DynamicDisplayName()` returns `"Save [scenario: test-move]"`.
6. **`ScenarioSave_Command_RoutesToSaveScenario`** — Verifies `scenario.save`/`scenario.saveAs` are registered with correct DisplayName/Category; their handlers route to the respective spies.
7. **`NullSeams_PreserveLegacySaveBehavior`** — ALL new seams null → `shell.save`/`shell.saveAs` behave as before; `DynamicDisplayName` is null; `scenario.save`/`scenario.saveAs` NOT registered.

---

## Test Results

### SaveCommands tests (filtered)
```
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 33 ms
```

### Hrot.Editor.AiShared.Tests (full, stability-filtered)
```
Passed!  - Failed:     0, Passed:  1065, Skipped:     0, Total:  1065, Duration: 4 s
```

### Hrot.Editor.Tests (stability-filtered)
```
Passed!  - Failed:     0, Passed:   183, Skipped:     0, Total:   183, Duration: 769 ms
```

### Build
```
Hrot.Editor — 0 Warnings, 0 Errors
```

---

## Challenges

- **C# discard (`_`) not supported** in this codebase's C# version for deconstruction variables. Resolved by using named `unusedActions` variable instead of `_`.
- **`AiDocument.Name` doesn't exist** — the `describeActiveTarget` lambda initially used `.Name` directly on `AiDocument`, but the property is on `.Asset.Name`. Corrected after first build failure.

## Integration Notes

- `scenario.save` and `scenario.saveAs` commands are **registered** in the command set but **not placed in any menu or toolbar** — that's the scope of BATCH-34 (T5).
- The `DynamicDisplayName` on `shell.save` is set when `describeActiveTarget` is provided; toolbars that render command labels should use `DynamicDisplayName ?? DisplayName`.
- `openScenarioSaveAs` reports status via `_saveAllStatus` like the existing document Save-As path.
- Existing `SaveCommandsTests` all pass unchanged (back-compat verified by both `NullSeams_PreserveLegacySaveBehavior` test and all 5 pre-existing tests passing).
