# BATCH-31 — MTB2-T2: Save icon in the main toolbar

**Task:** MTB2-T2 (Item 2) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/_DONE/main-toolbar-2/TASK-DETAIL.md` (`MTB2-T2`)

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file.

## ⚙️ RULES (non-negotiable)
1. Do this ONE objective only. Touch ONLY the files listed below. No drive-by edits/renames.
2. NEVER hide a problem to pass a build: no excluding assets, no `[Skip]`/commented/weakened tests, no stubs, no
   diagnostic suppression, no `#if false`. If blocked, STOP and report why.
3. Add the EXACT named tests; they must assert real values and fail if the code is wrong.
4. DO NOT STOP until build = 0 warnings AND the test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests changed + final test summaries. No litter.

## Objective
Surface the existing `shell.save` command (already Ctrl+S) as an icon button in the **MainToolbar**, immediately to
the right of the "Open Asset" button. Add a `"shell/save"` atlas cell so the icon resolves.

## Scope — ONLY these files
1. `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs` — add `["shell/save"] = "<cell>"` to
   `DefaultCellMap`. Pick a currently-UNUSED famfamfam-silk cell that reads as a "disk/save" glyph (used cells today:
   a1–a6, b1–b9, c8–c12, d2–d9, e1–e8, f1–f9, g1–g5 — choose a free one, e.g. in row `g6`+/`h`+; document your choice).
   It must NOT collide with any existing cell in the map. (Optionally also add `"shell/saveAs"`, `"shell/saveAll"`
   to distinct free cells — their commands already declare those IconKeys.)
2. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — in `RegisterWindows`, right after the existing "Open Asset"
   toolbar registration (search for `openAssetId` + `ToolbarCommandAdapter.Register(... sortOrder: -10)` and the
   `RegisterSeparator("ToolbarSep_OpenAsset", sortOrder: 0)`), add:
   `ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
   Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId, toolbarIconProvider, sortOrder: -9);`
   Place it BEFORE the existing `ToolbarSep_OpenAsset` separator (so Open Asset │ Save │ separator │ …). Must be
   inside the existing `if (windowManager.MainToolbar != null) { … }` block and null-safe (bare-ctor `RegisterWindows`
   must not throw). Do NOT change `shell.save` behavior/keybinding. Do NOT remove the old blueprint-specific save
   button in this batch.
3. Tests:
   - `Hrot/Editor/Hrot.Editor.AiShared.Tests/Adapters/AssetKindIconsRegistrationTests.cs` — add
     `ShellSave_Icon_Resolves_DistinctCell`.
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs` — add
     `EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry` (mirror the existing
     `EditorSubsystem_RegisterWindows_PopulatesMainToolbar` / Open-Asset-entry guardrail in that file — use the same
     `MainToolbar.GetVisibleItemPlan(...)` / entry-id seam it already uses).

## Tests — EXACT names + what they assert
- `ShellSave_Icon_Resolves_DistinctCell` — build a `SilkIconProvider` over a headless `IconAtlas` (mirror the existing
  tests in that file); assert `provider.TryGet("shell/save", out _)` is true AND its cell
  (`provider.KeyToCellMap["shell/save"]`) is **distinct** from every asset-kind cell and `folder`/`folder_open`.
- `EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry` — after `RegisterWindows` on a test
  `EditorSubsystem`, the `MainToolbar` visible-item plan contains an entry whose id == `ShellSaveCommands.SaveId`
  (`"shell.save"`), positioned right of the Open-Asset entry (sortOrder -9 < the `ToolbarSep_OpenAsset` 0). Assert it
  exists (and, if the existing guardrail asserts ordering, assert Save's sortOrder).

## Build & test commands (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj ^
  --filter "FullyQualifiedName~EditorSubsystemBlueprintWindows"
```
`Hrot.Editor.AiShared.Tests` must be `Failed: 0`. The filtered `EditorSubsystemBlueprintWindows` tests must be
`Failed: 0`. (The FULL `Hrot.Blueprints.Tests` suite has ~9 known PRE-1 failures unrelated to this batch — do NOT try
to fix those and do NOT introduce any NEW failure; the filter above isolates the relevant class.)

## Definition of done
- `shell/save` cell added (distinct); Save toolbar entry registered at sortOrder -9 next to Open Asset; null-safe.
- Both named tests pass; build 0 warnings; `Hrot.Editor.AiShared.Tests` `Failed: 0`; filtered guardrail `Failed: 0`.
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-31-REPORT.md`: cell chosen for `shell/save` (+ any saveAs/saveAll),
  files changed, tests added, final test summaries.

If something cannot be done as specified, STOP and report why.
