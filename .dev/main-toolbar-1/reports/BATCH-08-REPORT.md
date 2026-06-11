# BATCH-08 Report

## Implementation Summary

### T3 — Perspective menu relocation (MTB-P3-T3)

**WindowManager.cs** — extracted perspective enumeration into public testable seams:

- `GetPerspectives()` — distinct, sorted `OwningPerspective` values of `PerspectiveBound` windows
- `IsPerspectiveActive(string p)` — `p == CurrentPerspective` helper
- `BuildPerspectiveMenuModel()` — returns `IReadOnlyList<(string Perspective, bool IsChecked)>` (pure data, no ImGui calls)
- `SelectPerspective(string p)` — dispatch seam that calls `SwitchPerspective(p)`
- `GetPerspectiveIconKey(string p)` — first non-null `IconKey` among that perspective's windows (used by T4)

**Removed:**
- `RenderPerspectiveSwitcher()` call removed from `BeginMainMenuBar` at line 400 (was line 344); replaced with `RenderPerspectiveMenu()` call
- Entire `RenderPerspectiveSwitcher()` method (was lines 540–627) — inline buttons with `TitleBarColor` accenting
- Note: the `TitleBarColor`-based accenting logic was tied to the removed method; `TitleBarColor` *property* itself is preserved on `ManagedWindow` for other uses as mandated

**Added:** `RenderPerspectiveMenu()` — top-level "Perspective" menu rendering one checkable `MenuItem` per perspective

### T4 — Perspective toolbar radio-group + IconKey (MTB-P3-T4)

**ManagedWindow.cs** — added `string? IconKey { get; set; }` (default null, mirrors `TitleBarColor` pattern; additive, backward-compatible).

**WindowManager.cs** — added `GetPerspectiveIconKey(string p)` returning the first non-null `IconKey` among a perspective's `PerspectiveBound` windows.

**PerspectiveToolbarSection.cs** (new) — in `Fdp.Presentation.WindowManager`:
- Constructor takes `WindowManager`, `IIconProvider`, `MainToolbarManager`, `sortOrder`, optional `perspective` filter
- Self-registers render delegate with `MainToolbarManager`
- `BuildRadioModel()` → `IReadOnlyList<PerspectiveRadioEntry>` (pure data seam)
- `OnSelect(string p)` → calls `WindowManager.SelectPerspective(p)`
- `Render()` — draws one `ToggleIcon` per perspective as radio group; exactly one toggled; clicking non-active → `OnSelect`; missing/unresolved `IconKey` → text button fallback

## Design Decisions

- **Radio behavior via post-hoc toggle correction:** `IconWidgets.ToggleIcon` always flips `ref bool isToggled` on click. We capture the pre-click state and restore it when the operator clicks the already-active entry (no-op). This avoids dimming the active icon via `enabled=false` and uses existing `ToggleIcon` API without modification.
- **Text fallback mirrors the old switcher style:** inactive perspectives get `" Perspective "` label, active get `"[Perspective]"` — the same formatting the old `RenderPerspectiveSwitcher` used.
- **PerspectiveToolbarSection is in `Fdp.Presentation.WindowManager`** (not Hrot), alongside `ToolbarCommandAdapter` — consistent with the batch's "alongside the other toolbar adapters/sections" directive for Fdp.Presentation.

## Deviations

None. All requirements implemented as specified.

## Test Results

### New tests — all pass unfiltered

**PerspectiveMenuTests** (10 tests, 0 failed):
```
MenuLists_DistinctPerspectives_Sorted
GetPerspectives_EmptyWhenNoPerspectiveBoundWindows
GetPerspectives_ExcludesGlobalWindows
Select_CallsSwitchPerspective
SelectPerspective_FiresOnPerspectiveChanged
SelectPerspective_SamePerspective_NoOp
Checked_EqualsCurrent
IsPerspectiveActive_MatchesCurrent
BuildPerspectiveMenuModel_ReturnsTuplesWithPerspectiveAndChecked
PerspectiveButtons_NoLongerInMenuBar
```

**PerspectiveToolbarTests** (9 tests, 0 failed):
```
ExactlyOneToggled_EqualsCurrentPerspective
ExactlyOneToggled_AfterSwitchingPerspective
ClickNonActive_SwitchesPerspective
ClickActive_IsNoOp_StaysOnSamePerspective
OnSelect_FiresPerspectiveChangedEvent
MissingIconKey_FallsBackToTextButton
UnresolvableIconKey_FallsBackToTextButton
FirstNonNullIconKey_UsedForPerspective
GetPerspectiveIconKey_ReturnsFirstNonNull
```

### Existing tests — all pass

- **WindowManagerTests:** 29 passed, 0 failed
- **ManagedWindowTests + MainToolbarManagerTests + ToolbarCommandAdapter tests:** all pass (74 total in the combined class filter)

### Hot suites — 0 failed

```
Fdp.Presentation.Tests (class filter):  81 passed, 0 failed, 0 skipped
Fdp.Toolkits.Tests (Stability filter): 1856 passed, 0 failed, 0 skipped
Hrot.SimHost.Tests (Stability filter):  585 passed, 0 failed, 3 skipped (pre-existing)
```

### Full solution build

- **0 errors, 0 warnings from changed code** (27 pre-existing warnings unchanged)

## Developer Insights

- **WindowManager._windows is a `Dictionary<string, ManagedWindow>`** — insertion-order iteration is deterministic but not guaranteed by spec. `GetPerspectives()` sorts explicitly, and `GetPerspectiveIconKey` uses `FirstOrDefault` (stable within a given dictionary state, but the "first" may not be the semantically most relevant window). This is acceptable per §8.1 — the spec says "first non-null IconKey."
- **ToggleIcon radio group correction** requires the caller to manage the `ref bool` post-click. This works but is subtle. If `IconWidgets` ever gained a `RadioIcon` variant, `RenderToggleIconEntry` could be simplified.
- The old `RenderPerspectiveSwitcher` pushed `TitleBarColor` onto ImGui button style colors. The new Perspective menu uses standard ImGui `MenuItem` checkable entries and does not apply per-perspective colors. The toolbar section also does not apply colors (it uses `ToggleIcon` backgrounds). If colored menu entries are desired later, that's a separate enhancement.

## Known Issues

None.

## Suggested Commit Message

```
feat(main-toolbar): Perspective menu + toolbar radio-group + ManagedWindow.IconKey (MTB-P3-T3, T4)

- Extract GetPerspectives(), BuildPerspectiveMenuModel(), SelectPerspective() seams
- Remove RenderPerspectiveSwitcher; replace with top-level "Perspective" menu
- Add ManagedWindow.IconKey + WindowManager.GetPerspectiveIconKey()
- Add PerspectiveToolbarSection (ToggleIcon radio group, text fallback)
- Tests: PerspectiveMenuTests (10) + PerspectiveToolbarTests (9) — all pass
```

## Files Changed

| File | Change | Details |
|------|--------|---------|
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` | Modified | +`GetPerspectives()`, +`IsPerspectiveActive()`, +`BuildPerspectiveMenuModel()`, +`SelectPerspective()`, +`GetPerspectiveIconKey()`, +`RenderPerspectiveMenu()`, −`RenderPerspectiveSwitcher()` (~46 lines removed) |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs` | Modified | +`string? IconKey { get; set; }` (line ~88) |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/PerspectiveToolbarSection.cs` | **New** | Radio-group toolbar section with `BuildRadioModel()`/`OnSelect()` seams |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/PerspectiveMenuTests.cs` | **New** | 10 headless tests for perspective menu logic |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/PerspectiveToolbarTests.cs` | **New** | 9 headless tests for toolbar radio-group logic (fake `IIconProvider`) |

### Removal confirmation

`RenderPerspectiveSwitcher()` was called at `WindowManager.cs:344` inside `BeginMainMenuBar`. That call is now `RenderPerspectiveMenu()` at `WindowManager.cs:400`. The entire `RenderPerspectiveSwitcher()` method (was lines 540–627) is deleted. The method name now only appears in an XML doc comment on `RenderPerspectiveMenu` noting it supersedes the former method.
