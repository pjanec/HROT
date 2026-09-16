# BATCH-26: Unified "Open Asset" modal launcher + fix Scenario→Load lock-up
**Tasks:** UX feature + bug fix (post-completion)   **Est:** ~10h
**Dependencies:** BATCH-15 (AssetPickerModal), BATCH-16 (AssetPickActionRouter), BATCH-24/25 (toolbar).

## Goals (from interactive testing)
1. **One modal mechanism** for opening assets, used by THREE entry points:
   - **Toolbar "Open Asset" button** (leftmost group, `browser/open` icon) → all kinds.
   - **File → Open Asset…** menu item + **Ctrl+O** hotkey → all kinds.
   - **Scenario → Load** → the SAME modal, filtered to `Kinds = Scenario` only.
2. **Pick opens the asset immediately:** file kinds → `AiDocumentManager.Open`; scenario →
   `IEditorLogic.LoadScenarioByName` — via the existing `AssetPickActionRouter` (wire it in production).
3. **Modal UX:** Esc cancels; **Enter confirms the current selection** (if any); a **hotkey cycles tabs**.
4. **FIX the Scenario→Load lock-up:** today clicking Scenario→Load blocks the main menu and shows no
   modal. Make the modal reliably visible (see "Lock-up fix" below).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. Existing code:
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerModal.cs` — `Open(options, callback, lastOpened?)`,
     `DrawModal(title)`, `HandleActivated`/`HandleCancel`, `IsOpen`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` — `Tabs`, `Selection`,
     `AssetActivated`, `DrawContent()` (tabs/tree/filter).
   - `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickActionRouter.cs` — `Route(asset)`.
   - `EditorSubsystem.cs` — `_scenarioPickerModal` + `_scenarioPickerModal?.DrawModal("Load Scenario")`
     (~L1827, DrawUI top-level, RIGHT AFTER the WORKING "Rename Entity" modal at ~L1776-1824 — use that
     as the reference pattern), `ScenarioMenuCommands` wiring (~L2442), `AiDocumentManager`, `_editorLogic`,
     `windowManager.MainToolbar`/`ShellCommands`/`GlobalMenu`, a `SilkIconProvider`.
   - `ToolbarCommandAdapter` (toolbar button per command), `MenuCommandAdapter` (menu item per command),
     `EditorHotkeyDispatcher` (Ctrl+O via the command's `DefaultKey`).

## Lock-up fix (diagnosis to act on)
The scenario modal is drawn at a VALID scope (same place as the working "Rename Entity" modal) and the
popup IDs now match — yet it shows nothing and blocks the menu (a modal that's open-but-invisible blocks
input). Make the modal bulletproof by **mirroring the working "Rename Entity" modal exactly**:
- Drive opening with a **pending flag** consumed at the DrawUI top level (NOT inside any menu/menu-bar):
  the entry points set `_pendingOpenAssetKinds`; at the top-level draw point call `OpenPopup(ID)` once
  when the flag is set (then clear it), then `BeginPopupModal(ID, …)` — using the **identical ID string**
  for both `OpenPopup` and `BeginPopupModal` (the rename modal uses the same literal "Rename Entity" for
  both — do the same; avoid `##`/`###` divergence).
- **Set an explicit reasonable size** before `BeginPopupModal` (e.g. `SetNextWindowSize(new Vector2(720,
  520), ImGuiCond.Appearing)`) so the modal can never collapse to an invisible/zero size.
- Ensure `DrawModal` is called every frame at the DrawUI top level (it is). Confirm the menu click path
  actually reaches `Open()` (the `ScenarioMenuCommands` Load handler → `openPicker` → modal open) — add a
  headless test that invoking the `scenario.load` command sets the modal's `IsOpen` true.
You MAY refactor `AssetPickerModal.DrawModal` to this flag+explicit-size pattern, or wrap it — keep its
public test seams (`HandleActivated`/`HandleCancel`/`IsOpen`) working.

## Scope
### A. Wire `AssetPickActionRouter` in production (the pick callback)
- Construct an `AssetPickActionRouter` in `EditorSubsystem` with `openDocument = a => _aiDocumentManager?.Open(a)`
  and `loadScenario = name => _editorLogic?.LoadScenarioByName(name)`. Use `router.Route` as the modal's
  pick callback for the **all-kinds** picker. (Scenario-only picker may route scenario→Load directly or
  via the router — either is fine; keep one consistent path.)

### B. "Open Asset" command + surfaces (all kinds)
- Register a shell command `shell.openAsset` (DisplayName "Open Asset…", `IconKey = "browser/open"`,
  `DefaultKey = Ctrl+O`, always enabled). Handler → request-open the modal with `Kinds = All`,
  pick → `router.Route`.
- Surface it:
  - **Toolbar:** a leftmost group — use `ToolbarCommandAdapter.Register(MainToolbar, ShellCommands,
    "shell.openAsset", iconProvider, sortOrder)` with a sortOrder that places it FIRST (lower than the
    Time group's 0 — e.g. -10, or renumber groups so Open-Asset is leftmost) + a separator after it.
  - **Menu:** `MenuCommandAdapter.Register(GlobalMenu, ShellCommands, "shell.openAsset", "File/Open Asset…")`.
  - **Hotkey:** Ctrl+O fires it via the existing perspective-level `EditorHotkeyDispatcher` (it already
    pumps `ShellCommands`).

### C. Scenario → Load uses the same modal, Kinds = Scenario
- Re-point the `ScenarioMenuCommands` Load handler so it requests-open the SAME modal with
  `Kinds = AssetKindFilter.Scenario`; pick → `LoadScenarioByName(asset.Name)`. Remove/replace any
  now-redundant `_scenarioPickerModal` plumbing so there's ONE modal instance + ONE DrawModal call.

### D. Modal UX (in `AssetPickerModal` / `AssetBrowserPanel`)
- **Enter** confirms: when `Enter` pressed and `panel.Selection != null` → `HandleActivated(Selection)`.
- **Esc** cancels (already wired) → `HandleCancel`.
- **Tab-cycle hotkey:** add programmatic tab switching to `AssetBrowserPanel` (e.g. `SelectNextTab()` /
  a settable active-tab index applied via `ImGuiTabItemFlags.SetSelected` for one frame) and bind a key
  in `DrawModal` (e.g. **Ctrl+Tab** → next tab, **Ctrl+Shift+Tab** → prev; document the chosen keys).
- Double-click still activates (existing `AssetActivated`).

## Tests
- `AssetPickerModal`: Enter-confirms-selection (set a Selection via the panel seam, simulate Enter →
  `HandleActivated` fired with that asset); Esc still cancels; opening with `Kinds=Scenario` vs `All`.
- `AssetBrowserPanel`: `SelectNextTab`/active-tab-index cycles through `Tabs` (wraps).
- `AssetPickActionRouter` production wiring: a test (or reuse BATCH-16 tests) that file→openDocument,
  scenario→loadScenario.
- Scenario `scenario.load` command invocation sets the modal open (headless).
- The "Open Asset" command registered with `Ctrl+O` + under File menu + a toolbar entry exists.
- The BATCH-24 guardrail (`EditorSubsystem_RegisterWindows_PopulatesMainToolbar`) + the 8 window tests
  stay green.

## Hard constraints
- ONE modal mechanism (no duplicate pickers). Keep all wiring null-safe (RegisterWindows must not throw
  on a bare `new EditorSubsystem()`). Reuse `AssetPickActionRouter`, `AssetPickerModal`,
  `AssetBrowserPanel`, the adapters. Do NOT delete legacy code. Zero new warnings; no test weakening.

## Definition of done
- Library projects compile cleanly (running-editor MSB3027/3021 file-LOCK copy errors are environmental,
  NOT compile errors — confirm compilation; do NOT run suites while the editor is open if that corrupts
  them — note any environmental failures explicitly and re-run after).
- WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`: new + existing tests pass; `Hrot.Editor.AiShared.Tests`,
  `Hrot.Editor.Tests` 0-failed; `Fdp.Presentation.Tests` toolbar/wm classes (by filter) 0-failed;
  `Hrot.Blueprints.Tests` (Stability filter) stays at exactly the 9 PRE-1.
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-26-REPORT.md`: the unified modal mechanism, the lock-up fix
  (what was wrong + the rename-modal-mirroring pattern + explicit size), the three entry points, the
  router wiring, Enter/Esc/tab-cycle keys, tests, test-run summaries.

If something cannot be done as specified, stop and report why rather than stubbing it.
