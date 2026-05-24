# BATCH-07 Report — Final Cleanup: Style/Allocation Audits, Documentation Hygiene

**Status**: COMPLETE  
**Date**: 2026-05-15  
**Batch**: BATCH-07  
**Tasks**: RB-X.1 (Documentation Hygiene), RB-X.2 (Style and Allocation Audits)

---

## 1. Summary

All seven tasks completed. The thread-safety debt (RB06-P3-001) was resolved using a
`_resultsLock` object; the five FilteredTypeCombo tests were promoted to SR-T40..SR-T44
(RB06-P3-002); two new RBX2 assembly dependency tests were added; all four allocation budget
tests confirmed passing; the smoke test checklist was created; TASK-TRACKER.md and
DEBT-TRACKER.md were updated to close RB-X.1 and RB-X.2.

---

## 2. Task Completion

| # | Task | Status | Notes |
|---|------|--------|-------|
| A | Fix thread safety in `ReplaySearchPanel` (RB06-P3-001) | DONE | Lock-based approach; SR-T39 still passes |
| B | Add SR-T IDs to FilteredTypeCombo tests (RB06-P3-002) | DONE | SR-T40..44 — all 5 pass |
| C | Add RBX2 assembly dependency tests | DONE | 2 new tests pass |
| D | Verify allocation budget tests pass | DONE | 4/4 pass (SR-T34, SR-T08, EX-T25, DIF-T09) |
| E | Run full test suite and document results | DONE | See section 4 |
| F | Update TASK-TRACKER.md and DEBT-TRACKER.md | DONE | RB-X.1, RB-X.2 marked done; P3 debt resolved |
| G | Create RB-5.2 smoke test checklist | DONE | `.dev/replay-browser-2/SMOKE-TEST-CHECKLIST.md` |

---

## 3. Files Changed

### FDP submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP\`)

| File | Change |
|------|--------|
| `Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` | Added `_resultsLock`, wrapped Task.Run assignments and render-thread reads in `lock` |
| `Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/SearchPanel/ReplaySearchPanelTests.cs` | Renamed 5 FilteredTypeCombo tests to SR-T40..SR-T44 |
| `Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/AssemblyDependencyTests.cs` | New file — 2 RBX2 tests |

### Parent repo (`d:\Work\IOS-IG-SimHost-FDP-2\`)

| File | Change |
|------|--------|
| `.dev/replay-browser-2/TASK-TRACKER.md` | Marked RB-X.1 and RB-X.2 done |
| `.dev/replay-browser-2/DEBT-TRACKER.md` | Marked RB06-P3-001 and RB06-P3-002 RESOLVED (BATCH-07) |
| `.dev/replay-browser-2/SMOKE-TEST-CHECKLIST.md` | New file — RB-5.2 manual smoke test steps |

---

## 4. Testing Results

### SR-T39 (ReplaySearchPanel decoupling — must survive thread-safety fix)

| Test | Result |
|------|--------|
| SR_T39_Panel_HasNoFieldOfForbiddenHistoryTypes | PASS |
| SR_T39_InvokeSeekRequested_CallsDelegate_ExactlyOnce | PASS |
| SR_T39_InvokeEntitySelected_CallsDelegate_ExactlyOnce | PASS |
| SR_T39_MultipleInvocations_AccumulateInLog | PASS |
| **Total** | **4/4** |

### SR-T40..44 (renamed FilteredTypeCombo tests)

| Test | Result |
|------|--------|
| SR_T40_FilterTypes_EmptyFilter_ReturnsAll | PASS |
| SR_T41_FilterTypes_NullFilter_ReturnsAll | PASS |
| SR_T42_FilterTypes_MatchingFilter_ReturnsOnlyMatching | PASS |
| SR_T43_FilterTypes_NoMatch_ReturnsEmpty | PASS |
| SR_T44_FilteredTypeComboFieldDrawer_TargetType_IsTypeType | PASS |
| **Total** | **5/5** |

### RBX2 (assembly dependency tests — new)

| Test | Result |
|------|--------|
| RBX2_FdpToolkitsAssembly_DoesNotReference_PresentationOrUI | PASS |
| RBX2_ReplayBrowserContextAssembly_DoesNotReference_PresentationOrUI | PASS |
| **Total** | **2/2** |

### Allocation budget tests

| Test | Result |
|------|--------|
| SR_T08_CompiledPredicate_AllocationBudget | PASS |
| SR_T34_ZeroAllocation_LoopBodyAllocatesNothingOnNoMatch | PASS |
| EX_T25_LargeRecording_NoBigHeapAllocation | PASS |
| DIF_T09_AllocationBudget_1000Calls_Under300MB | PASS |
| **Total** | **4/4** |

### Hrot.ReplayBrowser.Tests

**8/8 pass** — no regressions.

### Hrot.ClusterRunner.Tests

**241/243 pass** — 2 pre-existing D003 DataDrivenGizmoPredicate failures (InvalidCastException,
pre-dated this batch).

### Full FDP.sln (for context — many pre-existing failures)

Selected assemblies with new/replay-related tests:
- `Fdp.Toolkits.Tests.dll`: 1083 passed, 30 failed (pre-existing; includes Vis2D gizmo, Fbt, etc.)
- `Fdp.Presentation.Tests.dll`: 107 passed, 18 failed (pre-existing Vis2D gizmo failures)

The 30 Fdp.Toolkits.Tests failures are all pre-existing (not replay browser related). The 2 new
RBX2 tests pass.

---

## 5. Developer Insights

### Q1: Issues encountered — what was harder than expected?

The thread-safety fix required more care than the instructions suggested. The instructions
presented several alternative approaches (volatile, Interlocked.Exchange, lock) and noted
that `volatile` + `Interlocked.Exchange` cannot coexist. The lock approach was cleanest but
required touching `DrawResultsGrid` as well as `DrawExecuteButton` — the instructions' note
about reading status in `DrawResultsGrid` was easy to miss. Additionally, the render-thread
read of `_statusLine` inside `DrawExecuteButton` (the `TextDisabled` call) also needed the
lock, even though it's in the same method as the write path — both the "searching..." write
(from the render thread) and the result write (from the background thread) need to be
coordinated.

One subtlety: the `_statusLine = "Searching..."` and `_results = Array.Empty<>()` writes
at the start of the button handler are on the render thread, which means they don't strictly
need the lock for safety — but taking a snapshot via the lock in `DrawResultsGrid` ensures
the render thread always sees a consistent pair of `(_results, _lifecycleResults)` rather
than a mix from different searches.

### Q2: Weak points spotted going forward

The `ReplaySearchPanel` class is growing in complexity. It handles mode switching, StructEdit
session lifecycle, preset management, search execution (async), and result rendering — all in
one file (~315 lines). A future concern is that the `_searchTask` field is never awaited or
cancelled. If the user clicks "Execute Search" rapidly, multiple `Task.Run` closures can race
to write `_results`; the lock prevents torn reads but does not prevent a stale task from
overwriting the results of a newer one. A `CancellationTokenSource` pattern would be needed
for correctness under rapid re-search. This is currently acceptable for a UI tool but worth
noting if the search becomes slower (e.g. large recordings).

The `BuildDrawer` method creates a fresh `BehaviorRegistry` on every call. This is called
on every mode switch. If `BehaviorRegistry` initialization is expensive (e.g. it scans
assemblies), this could cause hitches. Not observed in current tests, but worth a profiling
note.

### Q3: Design decisions beyond the spec

- The lock snapshot in `DrawResultsGrid` captures **both** `_results` and `_lifecycleResults`
  atomically in a single lock acquisition, even though only one of them is displayed per mode.
  This was not explicitly required by the spec but ensures that if mode switches occur mid-task,
  neither list is partially updated.

- The `_statusLine` read in `DrawExecuteButton` (the `TextDisabled` call) is read under a
  separate short-lived lock rather than trying to reuse the lock from `DrawResultsGrid`. This
  keeps the lock scope minimal and avoids holding it during ImGui calls.

- The `AssemblyDependencyTests.cs` was placed in a new top-level `ReplayBrowser/` folder
  rather than the `Audit/` subfolder (which already contains `RegistryAuditTests.cs`), because
  the new tests are structural/dependency checks rather than registry audits. This keeps the
  two concerns separate and matches the namespace `Fdp.Toolkits.Tests.ReplayBrowser` used in
  the batch spec.
