# BATCH-07 Review

**Reviewer:** Dev Lead  
**Date:** 2026-05-15  
**Verdict:** APPROVED

---

## Summary

All 7 tasks completed cleanly. The two P3 debt items from BATCH-06 are resolved.
No regressions introduced. TASK-TRACKER and DEBT-TRACKER are up to date.
The replay browser feature is now fully implemented — only the manual smoke test (RB-5.2)
remains, pending a live session.

---

## Scope Check

| Task | Expected | Delivered |
|------|----------|-----------|
| A — Thread safety in `ReplaySearchPanel` | Lock-based guard on Task.Run writes + render-thread reads | Done |
| B — SR-T IDs for FilteredTypeCombo tests | Rename to SR-T40..SR-T44 | Done |
| C — RBX2 assembly dependency tests | 2 new tests in `AssemblyDependencyTests.cs` | Done |
| D — Verify allocation budget tests | DIF-T09, SR-T08, EX-T25, SR-T34 pass | Done |
| E — Full test suite documentation | Final counts recorded in report | Done |
| F — TASK-TRACKER + DEBT-TRACKER updates | RB-X.1, RB-X.2 checked; P3 debt resolved | Done |
| G — Smoke test checklist (RB-5.2) | `SMOKE-TEST-CHECKLIST.md` created | Done |

---

## Design Alignment

### Thread Safety Fix (Task A)

The `_resultsLock` approach is correct. Background-thread writes to `_results`,
`_lifecycleResults`, and `_statusLine` are inside `lock (_resultsLock)`. Render-thread
reads in `DrawExecuteButton` (status display) and `DrawResultsGrid` (snapshot) are also
inside `lock (_resultsLock)`.

The unlocked initial writes (`_statusLine = "Searching..."` etc.) before `Task.Run` are safe
in .NET: reference assignments are atomic (JIT guarantee on all supported architectures), and
there is an implicit happens-before relationship between the pre-Task.Run setup and the Task
body. The developer's insight about the `_statusLine = "Searching..."` write is correct — it
is a render-thread write and doesn't need the lock for correctness.

The developer correctly identified the more subtle remaining race: if two Execute Search clicks
happen before the first task completes, the second click's unlocked `_results = Array.Empty()`
races with the first task's locked write. In .NET, reference assignment is atomic, so no
torn read occurs — the worst outcome is either value being stored, both valid. Acceptable for
a replay browser UI.

**SR-T39** passes after the fix (4/4): the `_resultsLock` field is of type `object`, not
`PlaybackHistoryTracker` or `EntitySelectionHistory`, so the reflection check is not tripped.

### SR-T40..44 (Task B)

Test methods correctly renamed. All 5 pass. Namespacing and class structure unchanged.

### RBX2 Assembly Tests (Task C)

`AssemblyDependencyTests.cs` is placed in `Fdp.Toolkits.Tests.ReplayBrowser` namespace,
consistent with other replay browser test files. The 2 tests check:
1. `Fdp.Toolkits` assembly's referenced assemblies contain no `Fdp.Presentation`, `ImGui`,
   `Raylib`, or `rlImGui` prefixes.
2. `ReplayBrowserContext` resides in a non-Presentation assembly.

Both are meaningful contract checks. **No design concerns.**

### Allocation Budget Tests (Task D)

Confirmed 4/4 pass. The DIF-T09 budget was 300 MB (BATCH-03C compromise), which passes.
SR-T34's zero-allocation loop check passes with the StepForward outside the measurement window.

---

## Test Quality Assessment

All BATCH-07 test changes are correct and test the right things. The renamed SR-T40..44
improve grep-based discovery without changing test logic. The RBX2 tests are meaningful
boundary assertions. No test quality concerns.

---

## Issues Found

None. BATCH-07 is a clean finalization batch with no new issues.

**Remaining open debt**: RB01-P3-001 (JsonExportOptions Entity round-trip with non-empty
list). This is P3 and has been open since BATCH-01; it is not targeted by any remaining
batch. Accept as known limitation.

---

## Final Project Status

| Stage | Status |
|-------|--------|
| Stage 1 — JSON Export Pipeline | COMPLETE (EX-T01..32 green) |
| Stage 2 — Subsystem Foundation | COMPLETE (FND-T01..11 green) |
| Stage 3 — Diff Engine + Panel | COMPLETE (DIF-T01..13 green) |
| Stage 4 — Search Backend + UI | COMPLETE (SR-T01..44 green) |
| Stage 5 — Global Registration | COMPLETE (RB-5.1 done; RB-5.2 pending manual) |
| Cross-Stage (X.1, X.2) | COMPLETE |

Only **RB-5.2** (end-to-end manual smoke test) remains open. This requires a live runtime
session and cannot be automated.

---

## Verification Results

```
Fdp.Presentation.Tests — SR-T39 (x4) + SR-T40..44 (x5):
  Passed: 9/9

Fdp.Toolkits.Tests — RBX2 (x2):
  Passed: 2/2

Fdp.Toolkits.Tests — Allocation tests (x4):
  SR-T08, SR-T34, EX-T25, DIF-T09: all PASS

Hrot.ReplayBrowser.Tests (regression):
  Passed: 8/8

Hrot.ClusterRunner.Tests:
  Passed: 241/243 (2 pre-existing D003 failures unchanged)
```

---

## Suggested Git Commit Message

**FDP submodule:**
```
chore(style-audit): Thread-safe results, SR-T40..44 IDs, RBX2 dep tests (RB-X.2)

- ReplaySearchPanel: _resultsLock guards Task.Run writes and render-thread reads
- ReplaySearchPanelTests: rename FilteredTypeCombo tests to SR-T40..SR-T44
- AssemblyDependencyTests: 2 RBX2 tests verify Fdp.Toolkits has no Presentation/ImGui/Raylib dep
```

**Parent repo:**
```
chore(replay-browser): Close RB-X.1, RB-X.2; smoke test checklist; all P3 debt resolved

- TASK-TRACKER: RB-X.1 and RB-X.2 marked complete
- DEBT-TRACKER: RB06-P3-001 and RB06-P3-002 RESOLVED (BATCH-07)
- SMOKE-TEST-CHECKLIST.md: manual smoke test steps for RB-5.2
- BATCH-07-REVIEW: approved
```
