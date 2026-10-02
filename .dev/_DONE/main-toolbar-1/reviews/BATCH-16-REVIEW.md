# BATCH-16 Review
**Status:** ✅ APPROVED (with DBT-2 follow-up)   **Date:** 2026-06-11

## Summary
MTB-P5-T5/T6: recursive scenario relpath enumeration (`ScenarioEnumeration`, wired into
`AvailableScenarios`) + nested-name save; and `AssetPickActionRouter` (Scenario→LoadScenarioByName,
file→Open). Completes Phase 5.

## Issues Found
### Issue 1 (deferred → DBT-2, P1): router/hosts not yet wired into production
**Problem:** `AssetPickActionRouter` + the BATCH-15 hosts are implemented/tested but not instantiated
at a production composition point — nothing surfaces the browser yet. **Resolution:** surfacing the
browser is a Phase 7 concern (Workspace/Scenario menu); recorded DBT-2 (P1, target Phase 7) to ensure
the production glue lands before FINAL. T6's named success-condition tests pass, so the task's
acceptance bar is met.
### Issue 2 (lead-handled): worker did not emit a report
The worker rambled and never wrote BATCH-16-REPORT.md; I compiled it from the reviewed code.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: `ScenarioNestedNameTests` + `AssetPickActionRouterTests` → **21 passed, 0 failed**.
- `ScenarioEnumeration.EnumerateRelPaths` read: recursive `scenario.json`-marker walk, root excluded,
  `/`-normalized, ordinal-sorted, empty for missing root. Wired into `AvailableScenarios` via
  `EditorSubsystem` (verified the diff line). `Save*` create nested folders (Path.Combine + CreateDirectory).
- `AssetPickActionRouter.Route` read: delegate-seam, correct kind routing, default no-op, null-guard.
- Scope: 2 new source + 2 new test files + 1 EditorSubsystem wiring line. No legacy deletions.

## Test Quality
Good. Enumeration tests use a temp tree and assert exact nested relpath set (excluding marker-less
dirs), nested-folder creation, and save→enumerate round-trip. Router tests assert file→Open (not load)
and Scenario→LoadScenarioByName(relpath) (not open) via recording fakes. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P5-T5, MTB-P5-T6 → `[x]`. **Phase 5 complete.** DBT-2 (P1) tracks the production
router/host wiring for Phase 7.

## Commit Message
```
feat(main-toolbar): scenario nested-name enumeration + pick-action router (MTB-P5-T5, T6)

ScenarioEnumeration.EnumerateRelPaths recursively lists scenario relpaths by scenario.json marker
(/-normalized, sorted); wired into AvailableScenarios. Save* create nested folders. AssetPickActionRouter
routes picks: Scenario→LoadScenarioByName(relpath), file kinds→AiDocumentManager.Open, via delegate
seams (unit-testable). Production host/router surfacing deferred to Phase 7 (DBT-2). Tests: 21 new.
Completes Phase 5.
```
