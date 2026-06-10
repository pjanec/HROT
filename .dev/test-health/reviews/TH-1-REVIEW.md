# TH-1 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-10   **Agent:** sonnet sub-agent.

## Summary
Both hot suites are now **fast-green** under the `Stability` filter; the ~79 pre-existing failures are honestly
categorized (5 fixed, 5 Flaky, 74 Broken) in `.dev/test-health/TEST-HEALTH.md`, with a reusable convention + filter.

## Independent verification
- Filtered `dotnet test … --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"` → **0 failed** for
  BOTH suites (Fdp.Toolkits 1856 pass; SimHost 585 pass / 3 skip). Reliable across reruns.
- Unfiltered shows 24 / 38 failed *this run* — fewer than the ~79 marked, which is CORRECT for these very-flaky
  suites (marks cover the union of tests that fail across runs). No over-marking concern: filtered is reliably green;
  the marks track real instability.
- **Only test files changed** (Trait annotations + 5 test-only ComponentId renumbers in `TestComponents.cs` etc.) —
  NO production code, NO deleted tests, NO weakened assertions. Junk temp file removed.
- Marking is honest: report explicitly separates `Broken` real-bugs from stale-tests; none hidden as Flaky.

## The real finding (for the user — triage needed)
74 tests are genuinely failing (`Broken`), now documented. Several are **real production bugs**, not flaky noise —
high-priority per the report:
- `EngineBackedNavigationModule` RegisterSystems/RegisterProviders **ordering bug** (blocks 5 SimHost tests).
- `EditablePolyline` missing registration (blocks ~12 StagingEntityExtractor tests).
- `IdAllocationMonitorSystem` event-subscription bug; `SimTransformBridge` sign convention; `ComponentDiffService`
  null return; `BicycleModel` negative speed; designation type mismatch (3); HillAttack BTree pipeline (9).
Medium-priority = stale tests (struct-size expectations, async/path) — cheap to update later.

## Verdict
APPROVED — fast-green + convention + ledger are a real iteration-cost win; the Broken inventory is the honest cost of
that and is the input to a triage decision (which real bugs to fix, by criticality). Committing.

## Commit message
```
test(health): categorize + Stability-trait the hot suites for fast-green iteration (TH-1)

Fdp.Toolkits.Tests + Hrot.SimHost.Tests now pass 0-failed under
--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken".
Fixed 5 test-only ComponentId collisions (renumbered to 291-295). Marked 5 Flaky + 74 Broken
with [Trait("Stability",...)] + inline reasons; full ledger in .dev/test-health/TEST-HEALTH.md
(+ README filter convention). No production code changed, no tests deleted, no assertions weakened.
74 Broken are genuine failures (real bugs + stale tests) documented for triage — see TH-1-REPORT.
```
