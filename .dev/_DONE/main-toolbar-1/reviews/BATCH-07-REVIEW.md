# BATCH-07 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P3-T1/T2: extracted `TransportIcons` (BtnShape/DrawShape/DrawTransportButton/FormatRate/
FormatTime/TimeRates) from the status-bar section (refactored to delegate, no visual change) and
added `MainToolbarTimeControlSection` (64px transport group + time + rate selector).

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors. Touched project `Hrot.Presentation` rebuilds
  `--no-incremental` with **0 warnings** (TWAE clean); the 10 solution-wide warnings are pre-existing
  in other projects (not BATCH-07).
- New tests run by lead: TransportIcons(6) + MainToolbarTimeControl(10) + ClusterTimeControl smoke(2)
  + RouteWaypointGizmoTests → **21 passed, 0 failed**.
- **"Pre-existing failure" audit:** worker reported `RouteWaypointGizmoTests.OnCommit_WritesBackToEcs`
  failing. In my class-filtered run it **passes** — it's another nondeterministic test-isolation
  flake (shared ECS/ordering state), not deterministic and not caused by BATCH-07 (which touches only
  time-control panels). Folded into PRE-4.
- Status-bar refactor diff is pure delegation to `TransportIcons.*` — no shape/size/behavior change.
- `FormatRate` now uses `InvariantCulture` (was localizing the decimal separator → a real bug on the
  Czech-locale dev machine, and required for the exact-string tests). Defensible correctness fix
  within the moved helper, documented.
- Scope: 2 new source files + status-bar refactor + 3 test files. No legacy deletions, no scope creep.

## Test Quality
Strong. Action tests assert facade call-counts for enabled AND disabled (gating proven both ways);
`PlayPauseFace` reflects `IsPaused`; `FormatTime` asserts 3 exact strings; rate selector asserts
`SetTimeScale` argument. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P3-T1, MTB-P3-T2 → `[x]`. Phase 3 continues (T3/T4/T5 remain).

## Commit Message
```
feat(main-toolbar): TransportIcons helper + MainToolbarTimeControlSection (MTB-P3-T1, T2)

Extract BtnShape/DrawShape/DrawTransportButton/FormatRate/FormatTime/TimeRates into shared
static TransportIcons (Hrot.Presentation/Panels); refactor ClusterTimeControlStatusBarSection
to delegate (no visual change; FormatRate now invariant-culture). New MainToolbarTimeControlSection
renders the 64px transport group + HH:MM:SS.mmm time + multiplier selector off the same
ITimeTransportFacade, with headless seams (PlayPauseFace/FormatTime/OnPlayPause/OnStep/OnStop/
OnSelectRate). Tests: 18 new, all pass.
```
