# BATCH-34 Review — MTB2-T5

**Status:** ✅ APPROVED · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- `WindowManager`: `_perspectiveLabels` + `RegisterPerspectiveLabel`/`GetPerspectiveLabel`; `RenderPerspectiveMenu`
  shows `GetPerspectiveLabel(perspective)` while `SelectPerspective(perspective)` uses the **id**.
  `BuildPerspectiveMenuModel` signature unchanged; `"Editor"` key NOT renamed (DEC-A7 honored).
- `EditorSubsystem`: `RegisterPerspectiveLabel("Editor","Scenario")` + 5 null-safe `MenuCommandAdapter.Register`
  (each guarded by `ShellCommands.Get(id) != null`) for File/Save, File/Save As…, File/Save All, File/Save Scenario,
  File/Save Scenario As…. `ScenarioMenuCommands` untouched (duplicate-removal deferred → DBT-A2).
- Tests assert real values: label override vs id; `SelectPerspective` keyed by id not label; File-menu children
  contain Save/Save As…/Save All/Open Asset…/Save Scenario via public `GlobalMenu.Root.Children` + label == "Scenario".
- Build `Hrot.Editor` 0 warnings; `PerspectiveLabel` 2/2; `EditorSubsystemBlueprintWindows` 14/14.

## Issues
None.

## Pending (lead runtime, non-blocking)
- Live: Perspective menu shows "Scenario" for the map perspective; File menu lists the Save entries; File/Save shows
  the dynamic `Save [kind: name]` label (T3/T4 plumbing).

## Commit
`feat(main-toolbar2): unified File menu + "Scenario" perspective display-label (MTB2-T5)`
