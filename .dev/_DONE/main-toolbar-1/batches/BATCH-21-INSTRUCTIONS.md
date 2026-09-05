# BATCH-21: Scenario lifecycle menu commands + Workspace dynamic submenu
**Tasks:** MTB-P7-T1, MTB-P7-T3   **Phase:** 7 — Scenario Menu, Workspace, Retirement   **Est:** ~10h
**Dependencies:** BATCH-05 (shell commands + MenuCommandAdapter), BATCH-15 (AssetPickerModal),
BATCH-20 (SaveAsDialog + DEC-9). Surfaces the new UI (resolves much of DBT-2).

> Do T1 then T3 in sequence; do NOT advance until the current task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §12.1 (scenario menu) + §12.2 (Workspace submenu).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P7-T1, MTB-P7-T3.
4. Existing code (read):
   - `Hrot/Subsystems/Hrot.Editor/IEditorLogic.cs` — `NewScenario`, `SaveCurrentScenario`,
     `SaveScenarioAs`, `LoadScenarioByName`, `LoadedScenarioName`, `AvailableScenarios`,
     `GetMigrationSidecarsForCurrentScenario`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` — `OpenDocuments`, `Active`,
     `Activate(AiDocument)`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerModal.cs` (Kinds filter),
     `Recipes/SaveAsDialog.cs`, `ShellSaveCommands.cs` (DEC-9 requestSaveAs).
   - `Fdp.Presentation` `GlobalMenuRegistry` + `MenuCommandAdapter` (BATCH-05),
     `WindowManager.ShellCommands`, `AssetKindIcons.GetIconKey`.

## Task 1 — Scenario lifecycle menu commands (MTB-P7-T1) — §12.1
Register scenario shell commands (in the editor composition root, `Hrot.Editor`) and surface them as
**Scenario** main-menu items via `MenuCommandAdapter`. Keep the command HANDLERS in a testable
registrar (e.g. `ScenarioMenuCommands`) operating over `IEditorLogic` + the modal/dialog seams:
- **New** (`scenario.new`) → `IEditorLogic.NewScenario`.
- **Save** (`scenario.save`) → `SaveCurrentScenario`; when no scenario loaded
  (`LoadedScenarioName` null/empty) → route to Save-As (the scenario Save-As path).
- **Save As…** (`scenario.saveAs`) → the unified `SaveAsDialog` (scenario branch / `SaveScenarioAs`).
- **Load…** (`scenario.load`) → open the `AssetPickerModal` with **`Kinds = AssetKindFilter.Scenario`**;
  on pick → `IEditorLogic.LoadScenarioByName(picked.Name)`.
- **Migration History…** (`scenario.migrationHistory`) → list
  `GetMigrationSidecarsForCurrentScenario()` for the loaded scenario (scenario-only; do NOT put it in
  any creation dialog/browser). `IsEnabled` only when a scenario is loaded.
This **resolves DBT-2** for the Load picker + Save-As surfacing — wire the real modal/dialog open here.

**Tests required (`ScenarioMenuTests`, fakes for `IEditorLogic` + modal/dialog seams):**
- `MenuItems_Registered_UnderScenario` — the five commands are registered under a "Scenario" menu path.
- `New_Invoke_EditorLogic` / `Save_Invoke_EditorLogic` / `SaveAs_Invoke_EditorLogic` — invoking each
  command calls the matching `IEditorLogic`/dialog path (recording fakes).
- `Load_OpensScenarioFilteredModal` — invoking Load opens the picker with `Kinds == Scenario`; on a
  fake pick → `LoadScenarioByName(name)` is called.
- `MigrationHistory_ListsSidecars_ForLoadedScenario` — invoking it returns/surfaces
  `GetMigrationSidecarsForCurrentScenario()` for the loaded scenario (and is disabled when none loaded).

## Task 2 — Workspace dynamic submenu (MTB-P7-T3) — §12.2
A **read-only dynamic submenu** ("Workspace") rebuilt from live state each frame, in `Hrot.Editor`.
Keep the model in a testable builder (e.g. `WorkspaceMenuBuilder.Build(docManager, editorLogic)` →
an ordered list of entries):
- One entry per `AiDocumentManager.OpenDocuments` (mark active `●`, dirty `*`), each prefixed with its
  kind icon (`AssetKindIcons.GetIconKey(doc.Kind)`).
- One entry for the loaded scenario (`IEditorLogic.LoadedScenarioName`) when present, with the scenario
  icon.
- Selecting an open-document entry → `AiDocumentManager.Activate(doc)`. (Scenario entry: read-only or
  re-load via LoadScenarioByName — keep minimal; the success conditions only require listing + doc
  activate.)
- The submenu is **rebuilt from live state on each build** (no stale caching).

**Tests required (`WorkspaceMenuTests`, fake `AiDocumentManager` + `IEditorLogic`):**
- `Lists_OpenDocuments_AndLoadedScenario` — the built model lists every open doc + the loaded scenario,
  with the active/dirty markers and the correct kind `IconKey` per entry.
- `SelectDocument_CallsActivate` — selecting a doc entry calls `AiDocumentManager.Activate` with that doc.
- `RebuiltFromLiveState_EachBuild` — changing the open-docs/loaded-scenario between two `Build` calls
  yields a correspondingly changed model (no stale state).

## Hard constraints
- Do NOT delete `ScenarioBrowserPanel` yet (MTB-P7-T2) or the AssetBrowserWindows (T4/T5). Keep the
  menu logic in testable registrars/builders (logic separated from ImGui). REUSE `MenuCommandAdapter`,
  `AssetPickerModal`, `SaveAsDialog`.
- Do NOT delete/modify legacy/assembly-loading code. No scope creep beyond T1/T3.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.Tests`, `Hrot.Editor.AiShared.Tests`, + the hot suites `Fdp.Toolkits.Tests`
  + `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-21-REPORT.md`: files changed, the scenario command registrar
  + Load-picker/Save-As wiring (and which DBT-2 items are now resolved), the Workspace builder, each new
  test + assertions, paste actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
