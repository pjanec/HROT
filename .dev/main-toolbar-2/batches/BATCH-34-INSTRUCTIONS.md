# BATCH-34 — MTB2-T5: unified File menu + perspective display-label "Scenario"

**Task:** MTB2-T5 (Item 3) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/main-toolbar-2/TASK-DETAIL.md` (`MTB2-T5`) · DESIGN DEC-A7. **Depends:** MTB2-T4 (scenario commands).

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file.

## ⚙️ RULES (non-negotiable)
1. Do this ONE objective only. Touch ONLY the files listed. No drive-by edits/renames.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression/`#if false`).
3. Add the EXACT named tests; assert real values; fail if code is wrong.
4. DO NOT STOP until build = 0 warnings AND test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests + final summaries. No litter.
6. **CRITICAL:** do NOT rename the `"Editor"` perspective key anywhere (it is also the cluster node/subsystem name +
   ~10 `PerspectiveBound` window keys). Only add a display-label.

## Objective
(a) Show the `Editor` perspective as **"Scenario"** in the Perspective menu (display-label only; id stays `Editor`).
(b) Add unified **File menu** entries for Save / Save As… / Save All / Save Scenario / Save Scenario As… (Open Asset
is already under File). The dynamic Save label flows automatically (T3/T4 already set it on `shell.save`).

## Scope — ONLY these files
### Part A — perspective display-label
`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`:
- Add `private readonly Dictionary<string, string> _perspectiveLabels = new();`
- `public void RegisterPerspectiveLabel(string perspective, string label) => _perspectiveLabels[perspective] = label;`
- `public string GetPerspectiveLabel(string perspective) => _perspectiveLabels.TryGetValue(perspective, out var l) ? l : perspective;`
- In `RenderPerspectiveMenu` (~L650-663): render the item text with `GetPerspectiveLabel(perspective)` but keep
  `SelectPerspective(perspective)` using the **id**. (i.e. `Gui.MenuItem(GetPerspectiveLabel(perspective), …)` →
  `SelectPerspective(perspective)`.) **Do NOT change `BuildPerspectiveMenuModel`'s signature** (other consumers/tests
  rely on it) — only the render text uses the label.

### Part B — unified File menu wiring
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (in `RegisterWindows`):
- `windowManager.RegisterPerspectiveLabel("Editor", "Scenario");` (near the other perspective wiring, inside the
  null-safe block — guard if MainToolbar/perspective wiring is conditional).
- After the existing `shell.openAsset` File-menu registration (search `MenuCommandAdapter.Register(... "File/Open Asset…")`),
  add (only if the command exists — these are registered by `ShellSaveCommands.Register` earlier in this method):
  - `MenuCommandAdapter.Register(windowManager.GlobalMenu, windowManager.ShellCommands, ShellSaveCommands.SaveId,    "File/Save");`
  - `MenuCommandAdapter.Register(..., ShellSaveCommands.SaveAsId,  "File/Save As…");`
  - `MenuCommandAdapter.Register(..., ShellSaveCommands.SaveAllId, "File/Save All");`
  - `MenuCommandAdapter.Register(..., ShellSaveCommands.ScenarioSaveId,   "File/Save Scenario");`
  - `MenuCommandAdapter.Register(..., ShellSaveCommands.ScenarioSaveAsId, "File/Save Scenario As…");`
  (The last two commands exist only when the T4 scenario seams were wired — they are in production. If a command is
  not registered, `MenuCommandAdapter.Register` throws; guard each with `windowManager.ShellCommands.Get(id) != null`
  or only register the scenario ones when `_editorLogic != null`.)
- **Do NOT modify `ScenarioMenuCommands`** or its existing menu entries. (Removing now-duplicate Scenario-menu
  Save/Save-As entries is deferred — see DBT-A2; out of scope here.) Keep all wiring null-safe (bare-ctor
  `RegisterWindows` must not throw).

## Tests — EXACT names
`FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/` (new `PerspectiveLabelTests.cs` or add to an existing WM test):
- `PerspectiveLabel_OverridesDisplay_NotId` — `wm.RegisterPerspectiveLabel("Editor","Scenario")`;
  `wm.GetPerspectiveLabel("Editor") == "Scenario"`; `wm.GetPerspectiveLabel("BTree") == "BTree"` (unset → id).
- `SelectPerspective_UsesId_NotLabel` — after registering the label, `wm.SelectPerspective("Editor")` →
  `wm.IsPerspectiveActive("Editor")` true and `wm.IsPerspectiveActive("Scenario")` false (id drives switching, not
  the label).

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs`:
- `EditorSubsystem_RegisterWindows_FileMenuHasSaveCommands` — after `RegisterWindows`, traverse the **public**
  `wm.GlobalMenu.Root.Children`: assert `Children["File"].Children` contains `"Save"`, `"Save As…"`, `"Save All"`,
  `"Open Asset…"`, and `"Save Scenario"`. Also assert `wm.GetPerspectiveLabel("Editor") == "Scenario"`.
  (MenuItemNode.Children is a public `Dictionary<string,MenuItemNode>`; navigate by the path-leaf segment names.)

> Use the EXACT leaf strings you register (e.g. `"Save As…"`, `"Save Scenario As…"`) as the `Children` keys.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --filter "FullyQualifiedName~PerspectiveLabel"
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~EditorSubsystemBlueprintWindows"
```
All `Failed: 0`. (Full `Fdp.Presentation.Tests`/`Blueprints.Tests` suites are flaky/PRE — use the filters; introduce
no NEW failures.)

## Definition of done
- `Editor` perspective shows as "Scenario" in the menu (id unchanged, switching still by id); File menu has
  Save/Save As…/Save All/Save Scenario/Save Scenario As… (+ existing Open Asset…). Null-safe; `ScenarioMenuCommands`
  untouched.
- The 3 named tests pass; build 0 warnings; filtered suites `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-34-REPORT.md`: files changed, menu paths added, tests, final summaries.

If something cannot be done as specified, STOP and report why.
