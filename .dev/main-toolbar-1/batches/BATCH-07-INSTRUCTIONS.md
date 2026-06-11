# BATCH-07: TransportIcons helper + MainToolbarTimeControlSection
**Tasks:** MTB-P3-T1, MTB-P3-T2   **Phase:** 3 — Toolbar Groups   **Est:** ~9h
**Dependencies:** Phase 1 (`MainToolbarManager`, IconWidgets). T2 consumes T1's helper.

> Do T1 fully (extract + status-bar still builds/renders) before T2. Do NOT advance to T2 until T1's
> tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §7 (Time Control Group).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P3-T1, MTB-P3-T2.
4. Existing code (read):
   - `Hrot/Engine/Hrot.Presentation/Panels/ClusterTimeControlStatusBarSection.cs` — holds
     `enum BtnShape {Play,Pause,Step,Stop}`, `DrawTransportButton(id,size,shape,enabled)→bool`,
     `DrawShape(dl,shape,pos,size,dim,hovered)`, `FormatRate`, `TimeRates[]`, `Render()`.
   - `Hrot/Engine/Hrot.Presentation/Facades/ITimeTransportFacade.cs` — `IsPlayPauseEnabled`,
     `IsStepEnabled`, `IsStopEnabled`, `IsPaused`, `TotalTime`, `TimeScale`, `TogglePlayPause()`,
     `Step()`, `Stop()`, `SetTimeScale(float)`.
   - `Hrot/Engine/Hrot.Presentation.Tests/ImGuiTestFixture.cs` — headless ImGui frame fixture
     (use it for any test that touches ImGui draw).

---

## Task 1 — Extract `TransportIcons` helper (MTB-P3-T1) — §7
**File (NEW):** `Hrot/Engine/Hrot.Presentation/Panels/TransportIcons.cs` (same assembly as the
sections, so both the status-bar and the new toolbar section use it; do NOT introduce a new
project dependency).
- Move the vector-shape logic out of `ClusterTimeControlStatusBarSection`: the `BtnShape` enum (make
  it public on `TransportIcons`), `DrawShape(...)`, and `DrawTransportButton(id, size, shape,
  enabled)→bool` become `public static` members of `TransportIcons`.
- Refactor `ClusterTimeControlStatusBarSection` to call `TransportIcons.DrawTransportButton(...)`
  instead of its private copies. **No visual change** — keep the same shapes/sizes/dim/hover.
- Keep `FormatRate`/`TimeRates` where most natural; if both sections need `FormatRate`, move it to
  `TransportIcons` too (public static) and have the status-bar call it.

**Tests required:**
- `TransportIconsTests.Draw_AllShapes_Headless_NoThrow` (in `Hrot.Presentation.Tests`, using
  `ImGuiTestFixture`): inside a headless frame, call `TransportIcons.DrawTransportButton` for each
  `BtnShape` value at 64px, both enabled and disabled → no throw; assert the disabled call returns
  false (no click in headless).
- The status-bar section must still build and its render path still work (add a
  `ClusterTimeControlStatusBarSection` smoke test under the fixture if none exists:
  `Render_Headless_NoThrow` with a fake facade) — proving the refactor didn't break it.

## Task 2 — `MainToolbarTimeControlSection` (MTB-P3-T2) — §7
**File (NEW):** `Hrot/Engine/Hrot.Presentation/Panels/MainToolbarTimeControlSection.cs`
A toolbar section reading the **same** `ITimeTransportFacade`, rendering at **64 px** via
`TransportIcons`: Play/Pause (face from `IsPaused`), Step (`IsStepEnabled`), Stop (`IsStopEnabled`)
as a group, plus `HH:MM:SS.mmm` time text and a multiplier selector (`TimeScale` + popup that calls
`SetTimeScale`). The status-bar section is unaffected.

**Split logic from ImGui draw so it is unit-testable headlessly** (mirror the BATCH-03/05 approach):
expose pure/headless seams so tests don't need real mouse clicks, e.g.
- `static BtnShape PlayPauseFace(bool isPaused)` (or an instance property) → `Play` when paused else `Pause`.
- `static string FormatTime(double totalSeconds)` → `HH:MM:SS.mmm`.
- action handlers `OnPlayPause()/OnStep()/OnStop()` that invoke the facade **only when** the
  corresponding `Is*Enabled` is true (so the gating is testable without ImGui), and a
  `OnSelectRate(float)` that calls `SetTimeScale`.
`Render()` wires button results (from `TransportIcons.DrawTransportButton`) + the rate popup to these
handlers. Re-read facade state every frame.

**Tests required (`MainToolbarTimeControlTests`, fake `ITimeTransportFacade`):**
- `PlayPause_Click_CallsTogglePlayPause` — `OnPlayPause()` calls facade `TogglePlayPause` when
  `IsPlayPauseEnabled`; and is a no-op when disabled.
- `Step_Click_CallsStep_GatedByIsStepEnabled` and `Stop_Click_CallsStop_GatedByIsStopEnabled`.
- `PlayPauseFace_ReflectsIsPaused` — face == Play when `IsPaused` true, Pause when false.
- `TimeText_FormatsTotalTime` — e.g. `3661.234s → "01:01:01.234"` (assert exact string).
- `RateButton_OpensSelector_SetsTimeScale` — selecting a rate via `OnSelectRate(2.0f)` calls
  `SetTimeScale(2.0f)`.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. No scope creep beyond the two new files +
  the `ClusterTimeControlStatusBarSection` refactor to call `TransportIcons`.
- No visual/behavioral change to the status-bar section.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Presentation.Tests`, plus the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`.
  (NOTE: if `Hrot.SimHost.Tests` shows a lone `EqsModuleTests` "EditablePolyline not registered"
  failure, that is the PRE-3 ordering flake — re-run; it passes clean. Do NOT try to fix it.)
- Write `.dev/main-toolbar-1/reports/BATCH-07-REPORT.md`: files changed, where TransportIcons/FormatRate
  landed, the headless seam used for T2, each new test + assertions, paste actual test-run summaries,
  and the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
