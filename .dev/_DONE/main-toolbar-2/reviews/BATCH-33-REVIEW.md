# BATCH-33 Review — MTB2-T4

**Status:** ✅ APPROVED · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- `ShellSaveCommands.Register`: 5 trailing optional seams added (`isScenarioContext`, `hasLoadedScenario`,
  `saveScenarioAction`, `requestScenarioSaveAs`, `describeActiveTarget`). Seam named `saveScenarioAction` (not
  `saveScenario`) to avoid colliding with the pre-existing `saveScenario` file-delegate param — sensible.
- `shell.save`/`shell.saveAs` branch on `isScenarioContext()` first (→ `saveScenarioAction`/`requestScenarioSaveAs`,
  `return`), else the existing per-kind/document logic. `IsEnabled = scenario ? hasLoadedScenario : Active!=null`.
  `DynamicDisplayName` set only when `describeActiveTarget != null`. `scenario.save`/`scenario.saveAs` registered only
  when their seams are non-null.
- EditorSubsystem wires all 5 seams null-safely; `openScenarioSaveAs` mirrors the existing scenario Save-As path;
  `ScenarioMenuCommands` wiring untouched.
- Tests invoke the captured handlers and assert the correct spy fired and the others did NOT (scenario vs document
  vs saveAs), all four IsEnabled combinations, the dynamic label string, scenario.save routing, and — crucially —
  `NullSeams_PreserveLegacySaveBehavior` (legacy delegate fires, doc marked clean, no DynamicDisplayName, scenario
  commands absent). Real behavior, no tautologies.
- Build `Hrot.Editor` 0 warnings; `SaveCommands` filter 12/12; `Hrot.Editor.Tests` 183/183; `Hrot.Editor.AiShared.Tests`
  full green per worker (1065) — consistent with the filtered run.

## Issues
None. (Seam rename noted above; back-compat preserved.)

## Pending (lead runtime, non-blocking)
- Live check (after T5 wires the File menu): Ctrl+S saves active doc in canvas perspectives, scenario in Editor
  perspective; Save tooltip reads `Save [kind: name]`.

## Commit
`feat(main-toolbar2): perspective-aware Save + Save Scenario + dynamic Save label (MTB2-T4)`
