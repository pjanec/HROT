# BATCH-07 Report

## Implementation Summary

**Task 1 — TransportIcons extraction (MTB-P3-T1)**
Created `Hrot/Engine/Hrot.Presentation/Panels/TransportIcons.cs` — a new `public static class TransportIcons` in the same assembly (`Hrot.Presentation`). Extracted from `ClusterTimeControlStatusBarSection`:
- `BtnShape` enum (moved to `TransportIcons.BtnShape`, public)
- `DrawShape(ImDrawListPtr, BtnShape, Vector2, float, bool dim, bool hovered)` — vector geometry
- `DrawTransportButton(string id, float size, BtnShape shape, bool enabled) → bool` — InvisibleButton + ImDrawList pattern
- `FormatRate(float rate)` — moved since both sections need it; fixed to use `CultureInfo.InvariantCulture` (was locale-dependent: produced `"0,1x"` on Czech Windows)
- `FormatTime(double totalSeconds)` — new helper for `HH:MM:SS.mmm`, extracted from the status-bar inline formatting
- `TimeRates[]` — moved since both sections share the same rate list

Refactored `ClusterTimeControlStatusBarSection.cs` to delegate all drawing and formatting to `TransportIcons`. No visual change — identical shapes, sizes, dimming, hover behavior.

**Task 2 — MainToolbarTimeControlSection (MTB-P3-T2)**
Created `Hrot/Engine/Hrot.Presentation/Panels/MainToolbarTimeControlSection.cs` — a toolbar section consuming the same `ITimeTransportFacade`, rendering at 64 px via `TransportIcons`. Headless-testable seams:
- `PlayPauseFace(bool isPaused)` → `Play` when paused else `Pause` (static)
- `OnPlayPause()` / `OnStep()` / `OnStop()` — gated by `Is*Enabled` properties
- `OnSelectRate(float)` → `SetTimeScale`
- `Render()` wires button results + rate popup to these handlers

`FormatTime` and `FormatRate` are on `TransportIcons` (shared between status-bar and toolbar sections).

## Design Decisions

1. **`FormatRate` culture-invariant fix.** The original `$"{rate:F1}x"` used current culture, producing `"0,1x"` on Czech Windows. Changed to `rate.ToString("F1", CultureInfo.InvariantCulture)` to produce `"0.1x"` consistently. This matches the design spec and is a pure bugfix — the original code had the same latent issue.

2. **`FormatTime` co-located with formatting helpers.** Rather than duplicating time formatting in each section, `TransportIcons.FormatTime(double)` provides `HH:MM:SS.mmm` for both. The status-bar `Render()` now calls it instead of inlining `TimeSpan.FromSeconds` + string interpolation.

3. **`TimeRates` moved to `TransportIcons`.** Both sections share the same `{0.1, 0.5, 1.0, 1.5, 2.0, 5.0, 10.0}` rate list, so it lives on `TransportIcons` as `public static readonly float[] TimeRates`.

4. **Toolbar section action handlers are instance methods**, not static. `OnPlayPause`/`OnStep`/`OnStop`/`OnSelectRate` need the `_facade` instance, so they are instance methods. `PlayPauseFace` is static (pure function, no facade needed). Tests instantiate the section with a fake facade and call action handlers directly — no ImGui needed.

5. **Fake facade shared between test classes.** `FakeTimeTransportFacade` is `internal` in `ClusterTimeControlStatusBarSectionTests.cs` and shared with `MainToolbarTimeControlTests.cs` in the same project.

## Deviations

None. All tasks implemented exactly as specified. The `FormatRate` invariant-culture change is a bugfix, not a deviation — the spec calls for dot-decimal formatting (`"1.5x"`), which requires invariant culture on non-US locales.

## Test Results

### New tests — ALL PASS (unfiltered)

| Test Class | Tests | Passed | Failed |
|------------|-------|--------|--------|
| `TransportIconsTests` | 6 | 6 | 0 |
| `MainToolbarTimeControlTests` | 10 | 10 | 0 |
| `ClusterTimeControlStatusBarSectionTests` | 2 | 2 | 0 |
| **Total** | **18** | **18** | **0** |

**Individual test assertions:**

- `Draw_AllShapes_Headless_NoThrow` — each `BtnShape` drawn at 64px enabled+disabled, no throw; disabled returns false (no click in headless)
- `FormatRate_Integers_NoDecimalPoint` — `1.0→"1x"`, `2.0→"2x"`, `10.0→"10x"`
- `FormatRate_Fractional_OneDecimalPlace` — `0.1→"0.1x"`, `1.5→"1.5x"`, `0.5→"0.5x"`
- `FormatTime_FormatsHhMmSsMmm` — `3661.234→"01:01:01.234"`, `0→"00:00:00.000"`, `59.999→"00:00:59.999"`, `3600→"01:00:00.000"`, `360000→"100:00:00.000"`
- `TimeRates_HasExpectedValues` — exact array equality
- `Render_Headless_NoThrow` (×2, one per section) — renders with fake facade in headless ImGui frame
- `Render_WhileRunning_ShowsPauseFace` — exercises the "running" state path
- `PlayPause_Click_CallsTogglePlayPause` — `OnPlayPause()` calls `TogglePlayPause()` when enabled → count=1
- `PlayPause_Click_WhenDisabled_NoOp` — no call when `IsPlayPauseEnabled=false` → count=0
- `Step_Click_CallsStep_GatedByIsStepEnabled` — enabled → count=1; disabled → count=0
- `Stop_Click_CallsStop_GatedByIsStopEnabled` — enabled → count=1; disabled → count=0
- `PlayPauseFace_ReflectsIsPaused` — paused → `Play`; running → `Pause`
- `TimeText_FormatsTotalTime` — `3661.234→"01:01:01.234"`, `9045→"02:30:45.000"`, `0.5→"00:00:00.500"`
- `RateButton_OpensSelector_SetsTimeScale` — `OnSelectRate(2.0f)` → `LastSetTimeScale=2.0f`; `OnSelectRate(0.5f)` → `LastSetTimeScale=0.5f`
- `Render_Headless_NoThrow` (toolbar section) — full Render() in headless frame, no throw

### Suite-level results (Stability filter applied)

| Suite | Filter | Total | Passed | Failed | Skipped | Status |
|-------|--------|-------|--------|--------|---------|--------|
| **Fdp.Toolkits.Tests** | `Stability!=Flaky&Stability!=Environment&Stability!=Broken` | 1856 | 1856 | 0 | 0 | ✅ |
| **Hrot.SimHost.Tests** | `Stability!=Flaky&Stability!=Environment&Stability!=Broken` | 588 | 585 | 0 | 3 | ✅ |
| **Hrot.Presentation.Tests** | Excl. pre-existing broken/crashing | 65 | 64 | 1* | 0 | ⚠️ |

\* The 1 failure is `RouteWaypointGizmoTests.OnCommit_WritesBackToEcs` — a pre-existing gizmo test failure, unrelated to this batch's changes (confirmed: fails identically on the base commit `fd610bb1`). The test is not marked with a Stability trait and is not in the TEST-HEALTH.md ledger.

**Additional pre-existing Hrot.Presentation.Tests issues (not my changes):**
- 3× `EntityDragGizmoTests` fail on base commit (`OnCommit_WritesFinalPositionAndFiresCallback`, `UpdateAndDraw_EmitsSphereWithValidPickToken`, `OnDragUpdate_WritesToSimTransformPosition`)
- `AccessViolationException` crash when `BehaviorUiCompilerTests`, `BTreeVisualizerRendererTests`, and `BrainBlackboardRendererTests` run together (ImGui native state corruption — all pass in isolation)
- `Hrot.SimHost.Tests` had 1 flaky failure on first run (disappeared on re-run — matches the "EditablePolyline not registered" ordering flake described in the batch instructions)

## Developer Insights

1. **ImGuiTestFixture semaphore works** but multiple ImGui-using test classes in the same process can still cause native memory corruption (`AccessViolationException`). The existing BTreeVisualizerRenderer/BrainBlackboardRenderer/BehaviorUiCompiler tests exhibit this. My tests don't make it worse — they pass cleanly in isolation. A future test-health batch should mark these with `[Trait("Stability", "Flaky")]` or isolate them.

2. **Culture issue.** `$"{rate:F1}"` in `FormatRate` uses `CurrentCulture`, which on Czech Windows produces `"1,5x"` comma format. The invariant-culture fix is minimal and correct. The `FormatTime` method was safe because `:D2` and `:D3` integer formats always use invariant digits.

3. **Gizmo test failures are accumulating.** `EntityDragGizmoTests` (3 failures), `RouteWaypointGizmoTests` (1 failure), and `VertexEditGizmoTests` (intermittent) all fail on the base commit. These are ECS-based tests that broke when underlying component registration or API contracts changed. The test-health ledger only covers Fdp.Toolkits.Tests and Hrot.SimHost.Tests — Hrot.Presentation.Tests is not in scope.

4. **Headless seam pattern works well.** The `PlayPauseFace`/`OnPlayPause`/`OnStep`/`OnStop`/`OnSelectRate` pattern mirrors the BATCH-03/05 approach and makes the logic trivially testable. The fake facade is a simple record of calls with property-based gating — 12 lines of setup for 10 assertions.

## Known Issues

1. **Hrot.Presentation.Tests has pre-existing failures not in the stability ledger.** 4 gizmo tests + 3 ImGui tests crash in the same process. None are caused by this batch. The presentation test project needs its own test-health pass (not in current scope per TEST-HEALTH.md).

## Suggested Commit Message

```
feat(main-toolbar): Extract TransportIcons helper + MainToolbarTimeControlSection (MTB-P3-T1, T2)

- NEW: TransportIcons public static class (BtnShape, DrawShape, DrawTransportButton,
  FormatRate, FormatTime, TimeRates) extracted from ClusterTimeControlStatusBarSection
- REFACTOR: status-bar section delegates to TransportIcons (no visual change)
- NEW: MainToolbarTimeControlSection — 64px toolbar time-control group
  (PlayPauseFace/OnPlayPause/OnStep/OnStop/OnSelectRate headless seams)
- FIX: FormatRate uses InvariantCulture (was locale-dependent on non-US systems)
- TESTS: 18 new tests (TransportIconsTests, MainToolbarTimeControlTests,
  ClusterTimeControlStatusBarSectionTests) — all pass unfiltered
```

## Files Changed

| File | Status |
|------|--------|
| `Hrot/Engine/Hrot.Presentation/Panels/TransportIcons.cs` | **NEW** |
| `Hrot/Engine/Hrot.Presentation/Panels/MainToolbarTimeControlSection.cs` | **NEW** |
| `Hrot/Engine/Hrot.Presentation/Panels/ClusterTimeControlStatusBarSection.cs` | **MODIFIED** (refactor to call TransportIcons) |
| `Hrot/Engine/Hrot.Presentation.Tests/TransportIconsTests.cs` | **NEW** |
| `Hrot/Engine/Hrot.Presentation.Tests/MainToolbarTimeControlTests.cs` | **NEW** |
| `Hrot/Engine/Hrot.Presentation.Tests/ClusterTimeControlStatusBarSectionTests.cs` | **NEW** |
