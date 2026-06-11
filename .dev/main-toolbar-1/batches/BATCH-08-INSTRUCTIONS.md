# BATCH-08: Perspective menu (relocate) + perspective toolbar radio-group
**Tasks:** MTB-P3-T3, MTB-P3-T4   **Phase:** 3 — Toolbar Groups   **Est:** ~9h
**Dependencies:** Phase 1 (`MainToolbarManager`, IconWidgets, `IIconProvider`). Cohesive perspective work.

> Do T3 then T4 in sequence; do NOT advance until the current task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §8 (Perspective Group & Menu).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P3-T3, MTB-P3-T4.
4. Existing code (read):
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — `CurrentPerspective`,
     `SwitchPerspective(string)`, `RenderPerspectiveSwitcher()` (~L540, called at ~L344 inside
     `BeginMainMenuBar`), perspective enumeration `_windows.Values.Where(Scope==PerspectiveBound)
     .GroupBy(OwningPerspective)`.
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs` — has settable
     `Vector4? TitleBarColor { get; set; }` (mirror this for `IconKey`).
   - `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/WindowManagerTests.cs` — `TestWindow`
     subclass + `RegisterWindow`/`SwitchPerspective` headless pattern.
   - `MainToolbarManager`, `IconWidgets` IconHandle overloads, `IIconProvider` (BATCH-03).

---

## Task 1 — Perspective menu, relocated out of the menu bar (MTB-P3-T3) — §8
**This task's mandate (§8.1) explicitly includes REMOVING the in-menu-bar perspective buttons** —
that is the intended relocation, NOT a forbidden legacy deletion.
- Extract the perspective enumeration into a public testable method on `WindowManager`:
  `IReadOnlyList<string> GetPerspectives()` → distinct `OwningPerspective` values of
  `PerspectiveBound` windows, **sorted** (stable). Add a small helper
  `bool IsPerspectiveActive(string p) => p == CurrentPerspective` if useful.
- **Stop drawing `RenderPerspectiveSwitcher()` inside `BeginMainMenuBar`** (remove the call at
  ~L344). Replace with a **top-level "Perspective" menu** built from `GetPerspectives()`: each
  perspective is a checkable menu entry (checked = active) whose selection calls
  `SwitchPerspective(p)`. Build it via a testable seam (e.g. a method that returns the
  `(perspective, isChecked)` list + a `SelectPerspective(p)` that calls `SwitchPerspective`), then
  render that as an ImGui menu. Remove the now-unused `RenderPerspectiveSwitcher` (and its private
  button-drawing helpers) since it is fully superseded — keep `TitleBarColor` accenting available
  for other uses (do not delete the property).

**Tests required (`PerspectiveMenuTests`, headless via `TestWindow` registration):**
- `MenuLists_DistinctPerspectives_Sorted` — register windows across perspectives (with duplicates)
  → `GetPerspectives()` returns the distinct set, sorted, no dupes.
- `Select_CallsSwitchPerspective` — `SelectPerspective("X")` results in `CurrentPerspective == "X"`.
- `Checked_EqualsCurrent` — the checked flag for each entry equals `(p == CurrentPerspective)`.
- `PerspectiveButtons_NoLongerInMenuBar` — assert the menu-bar build path no longer invokes the old
  switcher (e.g. `RenderPerspectiveSwitcher` removed; verify via the menu-bar render path / a
  recording seam — do NOT assert on a deleted symbol by name if it would not compile).

## Task 2 — Perspective toolbar radio-group + per-perspective `IconKey` (MTB-P3-T4) — §8
- Add optional `string? IconKey { get; set; }` to `ManagedWindow` (mirror `TitleBarColor`; default
  null; additive, backward-compatible).
- Add a testable resolver on `WindowManager`: `string? GetPerspectiveIconKey(string p)` → the first
  non-null `IconKey` among that perspective's windows (or null).
- **New `PerspectiveToolbarSection`** (in `Fdp.Presentation`, alongside the other toolbar
  adapters/sections): given the `WindowManager` + `IIconProvider` (+ a `MainToolbarManager` to
  register into, with sortOrder/perspective group), render one `ToggleIcon` per perspective as a
  **radio group**: exactly one toggled (= `CurrentPerspective`); clicking a non-active one calls
  `SwitchPerspective`. Face from `GetPerspectiveIconKey(p)` via `IIconProvider.TryGet`; when the key
  is missing/unresolved, **fall back to a text-label button**. Split the radio/selection logic from
  ImGui draw so it is headlessly testable (e.g. a pure `BuildRadioModel()` →
  `(perspective, isToggled, hasIcon)[]` + an `OnSelect(p)` handler).

**Tests required (`PerspectiveToolbarTests`, fake `IIconProvider` + `TestWindow`s):**
- `ExactlyOneToggled_EqualsCurrentPerspective` — in the built model, exactly one entry is toggled and
  it equals `CurrentPerspective`.
- `ClickNonActive_SwitchesPerspective` — `OnSelect` of a non-active perspective →
  `CurrentPerspective` becomes it; selecting the already-active one is a no-op (stays).
- `MissingIconKey_FallsBackToTextButton` — a perspective whose `IconKey` is null (or unresolved by
  the provider) is flagged `hasIcon == false` (renders text); one with a resolvable key →
  `hasIcon == true`.

## Hard constraints
- The ONLY removal permitted here is the relocated `RenderPerspectiveSwitcher` + its private
  button-drawing helpers (mandated by MTB-P3-T3/§8.1). Do NOT delete/modify any legacy/assembly code,
  `TitleBarColor`, or anything else. Keep `SwitchPerspective`/`CurrentPerspective` public API intact.
- `ManagedWindow.IconKey` is the only public-API addition (additive). No other scope creep.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. Run `Fdp.Presentation.Tests`
  by CLASS FILTER (PRE-2 full-suite deadlock; also PRE-4 RouteWaypoint flake — ignore both). The hot
  suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` 0-failed with the Stability filter (PRE-3 EQS
  flake → re-run if it appears).
- Write `.dev/main-toolbar-1/reports/BATCH-08-REPORT.md`: files changed, the testable seams used,
  confirmation the in-menu-bar switcher was removed (file:line), each new test + assertions, paste
  actual test-run summaries, and the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
