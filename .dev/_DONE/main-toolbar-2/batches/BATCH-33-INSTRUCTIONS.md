# BATCH-33 — MTB2-T4: active-save-target resolver + Save Scenario + dynamic Save label

**Task:** MTB2-T4 (Item 3) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/_DONE/main-toolbar-2/TASK-DETAIL.md` (`MTB2-T4`) · DESIGN DEC-A4/A5/A6 + "Active-save-target model".
**Depends on:** MTB2-T3 (`DynamicDisplayName` — already merged).

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. Read `ShellSaveCommands.cs` fully first.

## ⚙️ RULES (non-negotiable)
1. Do this ONE objective only. Touch ONLY the files listed. No drive-by edits/renames.
2. NEVER hide a problem to pass a build: no excluding assets, no `[Skip]`/commented/weakened tests, no stubs, no
   diagnostic suppression, no `#if false`. If blocked, STOP and report why.
3. Add the EXACT named tests; they must assert real behavior and fail if the code is wrong.
4. DO NOT STOP until build = 0 warnings AND the test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests changed + final test summaries. No litter.

## Objective
Make Save/Save-As resolve the **active save target** (document when a document is the active context, else the
scenario), add explicit `scenario.save` / `scenario.saveAs` commands, and give Save a dynamic
`"Save [{kind}: {name}]"` label — all via **injected seams** (no ImGui; no direct `IEditorLogic`/`WindowManager`
dependency inside `ShellSaveCommands`). Back-compat: with all new seams null, behavior is byte-identical to today.

---

## PART A — `ShellSaveCommands.cs` (library + unit tests)
**File:** `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`

1. Add command id constants: `public const string ScenarioSaveId = "scenario.save";`
   `public const string ScenarioSaveAsId = "scenario.saveAs";`
2. Extend `Register(...)` with **trailing optional** seams (after the existing `report` param, all default null):
   - `Func<bool>? isScenarioContext = null`
   - `Func<bool>? hasLoadedScenario = null`
   - `Action? saveScenario = null`
   - `Action? requestScenarioSaveAs = null`
   - `Func<string>? describeActiveTarget = null`
3. `shell.save` handler — **branch first** on scenario context:
   ```
   if (isScenarioContext?.Invoke() == true) { saveScenario?.Invoke(); return; }
   // else: existing per-kind active-document logic (unchanged)
   ```
4. `shell.save` `IsEnabled`:
   `() => (isScenarioContext?.Invoke() == true) ? (hasLoadedScenario?.Invoke() == true) : docManager.Active != null`.
5. `shell.save` descriptor: set `DynamicDisplayName: () => describeActiveTarget?.Invoke() ?? "Save"`.
6. `shell.saveAs` handler — branch first: `if (isScenarioContext?.Invoke() == true) { requestScenarioSaveAs?.Invoke();
   return; }` else existing `requestSaveAs(doc)` logic. Same `IsEnabled` shape as save.
7. Register `scenario.save` (DisplayName "Save Scenario", Category "File", `IsEnabled = () => hasLoadedScenario?.Invoke()
   == true`, handler → `saveScenario?.Invoke()`) **only when `saveScenario != null`**. Register `scenario.saveAs`
   (DisplayName "Save Scenario As…", handler → `requestScenarioSaveAs?.Invoke()`, `IsEnabled = hasLoadedScenario`)
   **only when `requestScenarioSaveAs != null`**.
8. **Back-compat:** when `isScenarioContext`/`saveScenario`/etc. are null, the three original commands behave exactly
   as before (no scenario branch taken; `DynamicDisplayName` defaults to `() => "Save"` only if you set it — keep it
   null when `describeActiveTarget` is null so existing label stays "Save" via `DisplayName`). Existing
   `SaveCommandsTests` must still pass unchanged.

### Tests — add to `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs` (EXACT names)
Use a recording `register` delegate (capture descriptor+handler by id) + a fake `AiDocumentManager` (existing tests
already show the pattern) + simple bool/Action spies for the new seams.
- `Save_InScenarioContext_CallsSaveScenario_NotDocument` — `isScenarioContext = () => true`, spy `saveScenario`;
  invoke the `shell.save` handler → `saveScenario` fired, no per-kind document save delegate fired.
- `Save_InDocumentContext_CallsDocumentSave_NotScenario` — `isScenarioContext = () => false`, active Blueprint doc
  with a non-empty SourceFilePath; invoke `shell.save` → `saveBlueprint` fired, `saveScenario` NOT fired.
- `SaveAs_InScenarioContext_RequestsScenarioSaveAs` — `isScenarioContext = () => true`, spy `requestScenarioSaveAs`;
  invoke `shell.saveAs` handler → `requestScenarioSaveAs` fired, document `requestSaveAs` NOT fired.
- `Save_IsEnabled_ReflectsActiveTarget` — scenario ctx: enabled iff `hasLoadedScenario` true; document ctx: enabled
  iff `docManager.Active != null`. Assert all four combinations on the captured `shell.save` descriptor's `IsEnabled`.
- `DynamicDisplayName_NamesKindAndAsset` — set `describeActiveTarget = () => "Save [scenario: test-move]"`; the
  captured `shell.save` descriptor's `DynamicDisplayName()` returns `"Save [scenario: test-move]"`.
- `ScenarioSave_Command_RoutesToSaveScenario` — with `saveScenario` spy, the captured `scenario.save` handler fires
  `saveScenario`; assert `scenario.save` is registered (and `scenario.saveAs` when `requestScenarioSaveAs` set).
- `NullSeams_PreserveLegacySaveBehavior` — with ALL new seams null: `shell.save`/`shell.saveAs`/`shell.saveAll`
  behave exactly as the pre-existing tests expect; `scenario.save`/`scenario.saveAs` are NOT registered.

---

## PART B — production wiring in `EditorSubsystem.cs`
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — the existing `ShellSaveCommands.Register(...)` call
(search `ShellSaveCommands.Register`, ~L2343). Add the new named args:
- `isScenarioContext: () => windowManager.CurrentPerspective == "Editor"`
- `hasLoadedScenario: () => !string.IsNullOrEmpty(_editorLogic?.LoadedScenarioName)`
- `saveScenario: () => _editorLogic?.SaveCurrentScenario()`
- `requestScenarioSaveAs: openScenarioSaveAs` — define a local `Action openScenarioSaveAs = () => { … };` that
  performs the SAME seed+dialog+confirm as the existing scenario Save-As path (mirror EditorSubsystem ~L2506–2519:
  `new ScenarioSaveAsAsset(_editorLogic.LoadedScenarioName ?? "Unnamed")` → `new SaveAsDialog(scenarioAsset,
  _newAssetServices, saveScenarioAs: saveAsScenarioDelegate)` → `dialog.Confirm()`). Guard `_editorLogic != null &&
  _newAssetServices != null`. **Do NOT change the existing `ScenarioMenuCommands` `openSaveAsDialog` wiring** — a few
  duplicated lines here are acceptable; do not refactor shared scenario state.
- `describeActiveTarget: () =>` returns:
  - scenario context (`CurrentPerspective == "Editor"`) + loaded scenario → `$"Save [scenario: {LoadedScenarioName}]"`;
  - else active doc present → `$"Save [{_aiDocumentManager.Active.Kind.ToString().ToLowerInvariant()}: {_aiDocumentManager.Active.Name}]"`;
  - else → `"Save"`.

Keep all wiring null-safe (bare-ctor `RegisterWindows` must not throw). Do NOT register the scenario commands in a
menu here (that's T5/BATCH-34) — just supply the seams so they exist in the command set.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~SaveCommands"
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0`. (Then a full `Hrot.Editor.AiShared.Tests` run should also be green — the lead will run it.)

## Definition of done
- `ShellSaveCommands` resolves scenario-vs-document for save/saveAs, registers `scenario.save`/`scenario.saveAs` when
  seams present, sets the dynamic Save label; back-compat preserved (null seams). EditorSubsystem wires the seams
  null-safely. The 7 named tests pass + existing SaveCommandsTests pass.
- Build 0 warnings; filtered `SaveCommands` `Failed: 0`; `Hrot.Editor.Tests` `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-33-REPORT.md`: seam signatures, the scenario-context rule, the dynamic
  label format, files changed, tests added, final summaries.

If something cannot be done as specified, STOP and report why.
