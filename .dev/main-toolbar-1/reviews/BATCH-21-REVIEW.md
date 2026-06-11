# BATCH-21 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P7-T1/T3: `ScenarioMenuCommands` (5 shell commands under a "Scenario" menu; Load opens the
Scenario-filtered `AssetPickerModal`, Save-As uses `SaveAsDialog`) + `WorkspaceMenuBuilder` (live
read-only submenu of open docs + loaded scenario).

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: `ScenarioMenuTests` (14) + `WorkspaceMenuTests` (11) → **25 passed, 0 failed**.
  Suites green: Hrot.Editor.Tests 181, AiShared 1024, Fdp.Toolkits 1856, SimHost 585.
- `ScenarioMenuCommands`: ids `scenario.new/save/saveAs/load/migrationHistory`, MenuPrefix "Scenario",
  handlers over `IEditorLogic`, surfaced via `MenuCommandAdapter`; Load opens `AssetPickerModal`
  (`Kinds=Scenario`)→`LoadScenarioByName(name)` (single call; no call on cancel); MigrationHistory lists
  sidecars and is loaded-gated. Matches §12.1. EditorSubsystem wires the registrar + draws the modal.
- `WorkspaceMenuBuilder`: `WorkspaceMenuEntry` (IconKey/Label/IsActive/IsDirty/OnSelect),
  rebuilt-from-live-state each `Build`, OnSelect→`Activate`. Matches §12.2.
- Scope: 2 new registrar/builder + 2 test files + EditorSubsystem wiring. No deletions (T2/T4/T5 pending).

## Test Quality
Strong. Scenario tests assert each command invokes the right `IEditorLogic`/dialog path via recording
fakes, Load filter == Scenario + pick→LoadScenarioByName + cancel→no-call, migration sidecars listed.
Workspace tests assert listing (markers + icon keys), `Activate` on select, and live-rebuild between
calls. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P7-T1, MTB-P7-T3 → `[x]`. **DBT-2 substantially resolved** (Load picker + Save-As
surfaced); residual = docked-window host + file-kind `AssetPickActionRouter` wiring → MTB-P7-T4.

## Commit Message
```
feat(main-toolbar): scenario lifecycle menu + Workspace submenu (MTB-P7-T1, T3)

ScenarioMenuCommands registers New/Save/SaveAs/Load/MigrationHistory shell commands under a
"Scenario" menu via MenuCommandAdapter; Load opens the Scenario-filtered AssetPickerModal →
LoadScenarioByName, Save-As drives SaveAsDialog (resolves most of DBT-2). WorkspaceMenuBuilder
builds a live read-only submenu of OpenDocuments (active/dirty + kind icon) + loaded scenario;
select → AiDocumentManager.Activate. Tests: 25 new. EditorSubsystem wires both.
```
