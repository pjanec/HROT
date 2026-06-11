# BATCH-24: Populate the main toolbar (wire the toolbar groups) — BUG-2
**Tasks:** BUG-2 (surfacing fix; not a tracker task)   **Phase:** post-completion bug fix   **Est:** ~6h
**Dependencies:** BATCH-03/07/08/09 (the toolbar sections + AiDebugCommands exist but are unwired).

## Problem
The main toolbar band renders (correct height/inset) but is **EMPTY** — nothing registers entries into
`WindowManager.MainToolbar` in production. Wire the toolbar groups in `EditorSubsystem.RegisterWindows`.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/main-toolbar-1/DESIGN.md` §4 (toolbar), §7 (time group), §8 (perspective group), §9 (AI-debug group).
3. Existing pieces (read; they are built + unit-tested, just unwired):
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/PerspectiveToolbarSection.cs` — **self-registers**
     a "PerspectiveGroup" entry in its ctor: `new PerspectiveToolbarSection(wm, iconProvider, wm.MainToolbar, sortOrder)`.
   - `Hrot/Engine/Hrot.Presentation/Panels/MainToolbarTimeControlSection.cs` — ctor takes
     `ITimeTransportFacade`; has `Render()` (does NOT self-register — call
     `wm.MainToolbar.RegisterEntry("TimeControlGroup", sortOrder, 64f, section.Render)`).
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/AiDebugCommands.cs` — `Register(register, registry)`
     registers Continue/StepOver/StepInto/StepOut/Pause (+blueprint StepBack) shell commands.
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ToolbarCommandAdapter.cs` —
     `Register(toolbar, commands, commandId, iconProvider, sortOrder, perspective?)` adds a toolbar entry per command.
   - `EditorSubsystem.cs`: `RegisterWindows(windowManager)` — has `windowManager.MainToolbar`,
     `windowManager.ShellCommands`, a `DebugSessionRegistry` (~L1902), `_timeController`
     (`MasterSyncController`), `_previewController`, and a `SilkIconProvider` (the docked host builds
     `assetBrowserIconProvider` ~L2052 — reuse or build one from `windowManager.Atlas`).

## Scope — wire three toolbar groups in `EditorSubsystem.RegisterWindows`
Register with ascending `sortOrder` ranges and separators between groups (§4.3), e.g.
Time(0–9) │ Perspective(20–29) │ AI-debug(40–49). Keep all wiring **null-safe** (do NOT make
`RegisterWindows` throw on the bare-subsystem path — see the guardrail).

### A. Perspective group (§8) — easiest
- `new PerspectiveToolbarSection(windowManager, iconProvider, windowManager.MainToolbar, sortOrder: 20)`
  (it self-registers). Keep a field reference so it isn't GC'd.

### B. AI-debug group (§9)
- Register the AI-debug shell commands once: `AiDebugCommands.Register(windowManager.ShellCommands.Register, debugRegistry)`
  (use the existing `DebugSessionRegistry`). Then for each common command id
  (`AiDebugCommands.ContinueId/StepOverId/StepIntoId/StepOutId/PauseId`) call
  `ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands, id, iconProvider, sortOrder: 40+i)`.
  (Blueprint-only StepBack may be added too if its command is registered.) Add a `RegisterSeparator`
  before the group.

### C. Time-control group (§7)
- `MainToolbarTimeControlSection` needs an `ITimeTransportFacade`. The editor has no ready facade
  (`MasterSyncTimeControllerAdapter` is a different interface). **Create a small
  `EditorTimeTransportFacade : ITimeTransportFacade`** (in `Hrot.Presentation` or `Hrot.Editor`)
  adapting the editor's time controller (`_timeController` `MasterSyncController` + `_previewController`):
  map `IsPaused`/`TotalTime`/`TimeScale`/`TogglePlayPause`/`Step`/`Stop`/`SetTimeScale`/`Is*Enabled`
  onto the controller's existing API (mirror what `TimeControlStatusBarSection` does with the same
  controller). Then `var tc = new MainToolbarTimeControlSection(facade);
  windowManager.MainToolbar.RegisterEntry("TimeControlGroup", 0, 64f, tc.Render);`. If the controller
  API can't cleanly back a field of the facade, document the mapping. (If genuinely infeasible in this
  pass, wire A+B, register a separator placeholder, and record the time group as a follow-up — but
  prefer completing it.)

## Guardrail test (do NOT regress RegisterWindows)
Extend `EditorSubsystemBlueprintWindowsTests` (or a new test) with:
`EditorSubsystem_RegisterWindows_PopulatesMainToolbar` — `new EditorSubsystem(); RegisterWindows(wm);`
then assert `wm.MainToolbar` has entries (e.g. `wm.MainToolbar.Height > 0` and the perspective/AI-debug
entries are present via the manager's plan/height). The existing 8 `EditorSubsystemBlueprintWindowsTests`
MUST stay green (RegisterWindows must not throw on the bare subsystem — null-guard any optional deps).

## Hard constraints
- No scope creep beyond toolbar-group wiring + the time facade adapter + the guardrail test.
- Do NOT delete/modify legacy code. Do NOT weaken/skip tests; zero new warnings (TreatWarningsAsErrors).
- Keep `RegisterWindows` null-safe (the BATCH-23 corrective null-guarded the scenario-menu wiring — do
  the same for any toolbar deps that may be null in the bare-subsystem unit-test path).

## Definition of done
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings). NOTE: if the editor is running, the build
  may show MSB3027/MSB3021 **file-lock** copy errors into `Hrot.ClusterRunner/bin` — those are
  environmental (close the running editor), NOT compile errors; confirm the library projects compile.
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New + existing `EditorSubsystemBlueprintWindowsTests`
  pass; `Hrot.Blueprints.Tests` (Stability filter) stays at exactly the 9 PRE-1 failures;
  `Hrot.Editor.Tests` + `Fdp.Presentation.Tests` (toolbar classes, by filter) + `Fdp.Toolkits.Tests` +
  `Hrot.SimHost.Tests` 0-failed.
- Write `.dev/main-toolbar-1/reports/BATCH-24-REPORT.md`: what was wired (group → sortOrder), the time
  facade adapter mapping, the guardrail test, paste test-run summaries.

If something cannot be done as specified, stop and report why rather than stubbing it.
