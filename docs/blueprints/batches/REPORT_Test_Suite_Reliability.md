<!--STATUS
state: LIVE
doc-type: batch report (EPHEMERAL — the durable record is docs/TESTING_Harness_And_Goldens.md §7,
  the Area N tracker rows QA-001..QA-013, and the closed DEBT-AIB-030 row).
updated: 2026-08-26
current-answer: §1 the root cause · §2 what shipped · §3 the gate table · §4 what is NOT fixed
design-basis: HANDOFF_Test_Suite_Reliability.md (dispatch dbdc5e783) · TESTING_Harness_And_Goldens.md §7
  (written by this batch) · R-131 (no permanent filter-around) · rule 8 gate-report contract.
-->
# REPORT — **Test-suite reliability: the crash, the flakes, the stable reds** *(BACKEND lane)*

> 📌 **Dispatched at `dbdc5e783`.** Rule-1b started-marker `3d50fa0b5`. Branch
> `claude/blueprint-macro-feature-sdmspn`. ⛔ No PR.
> ⭐ **IDs allocated (rule 5): `QA-001` … `QA-013`** — a new prefix, per the handoff's own suggestion.
> 🎯 **The goal was "a RED means a real defect."** ⭐ It is now true for three of the four suites in scope.

---

## 1. ⭐⭐⭐ THE ROOT CAUSE — **one leak, three faces, and it was never flakiness**

⛔⛔ **The handoff listed the crash, the rotating flake and the stable reds as three workstreams.
📐 W1's three "different proximate causes" turned out to be ONE defect** — and the biggest surprise is
that the dominant term was **not** the thing anyone had been looking at.

```
a node teardown releases the KERNEL but not the REPOSITORIES it ran on
  → RSS climbs monotonically            (measured 4.1 → 9.9 GB in 45 s of one run)
  → 77 × OutOfMemoryException           (EntityIndex / NativeChunkTable ctors)
  → a harness CONSTRUCTOR throws
  → xUnit does NOT call Dispose on an instance whose ctor threw
  → its DDS participant + background id-allocator poll thread survive
  → that thread calls dds_take on a dead handle
  → unhandled exception on a NON-TEST thread ⇒ the whole host process dies
```

| the symptom, as previously recorded | ⭐ what it actually was |
|---|---|
| *"aborts at a different count every run"* — `BP-378`, `F17` | the host **ran out of memory and died**; the count is how far it got |
| *"the failure identity ROTATES"* — `DEBT-AIB-030` et al | under pressure, whichever test allocates at the wrong moment loses |
| *"every named one PASSES under `--filter`"* | a filtered run never reaches the pressure |
| three causes *(DDS `-3`, a ModuleHost timeout, `OutOfMemoryException`)* | ⛔ **one root cause, three faces** |

### ⭐⭐ The instrument is the story

⛔ A leaked repository throws nothing, logs nothing and fails no assertion. `QA-004` added
`EntityRepository.LiveInstanceCount` / `IsDisposed` plus an opt-in origin tracker
(`FDP_TRACK_REPO_LEAKS=1`). **It found the two real leaks in minutes after weeks of the wrong theory:**

| measured, ONE five-subsystem harness round-trip | leaked |
|---|---:|
| at dispatch | **32** |
| after the world-ownership fix (`QA-001`) | **2** |
| after `SnapshotPool` + `OnDemandProvider` (`QA-006`) | ✅ **0** |

🔴🔴 **`QA-006` is a PRODUCT defect, not a test defect.** `ModuleHostKernel.Initialize` builds a
`SnapshotPool` with `warmupCount: 10` and nothing ever released it ⇒ **every node teardown in the
shipping runner leaked ten `EntityRepository` instances**, each an `int[1_000_000]` free list plus one
`NativeChunkTable` per registered component. ⚠ *Pooling* and *ownership* are not the same thing: a
recycler with no end of life is a leak by construction.

### ⛔ Two ledger claims MEASURED FALSE

| the standing claim | 📐 measured |
|---|---|
| *"ids are assigned in registration order, and parallelism makes that order observable"* — `Q52` §6.3 · `ST-026` · `AX-023` | **false.** `GetOrRegisterManaged` **requires** an explicit `[ComponentId]` and throws without one ⇒ ids are **deterministic**. What parallelism makes observable is **`ComponentTypeRegistry.Clear()`** |
| *"the `--mode ig` X11 SIGSEGV means the rail needs a virtual display"* — handoff W3 | **false.** `ModeStartupRails` already acquires one (`XvfbDisplay`, `:155`). It was an intermittent environmental fault, and the most recent full T3 before this batch was **107 / 0 / 0** |

---

## 2. ⭐ WHAT SHIPPED

| id | | |
|---|---|---|
| **QA-001** | `HrotNodeContext` is `IDisposable` (kernel → world) | the **four** consumers that received a world disposed the kernel and none the world; the **three** that build their own all dispose it ⇒ a missing CONTRACT, not four oversights |
| **QA-002** | `HrotRunnerHarness` cleans up and rethrows on a failed boot | xUnit does not `Dispose` an instance whose ctor threw |
| **QA-003** | `HostedIdAllocatorServer.RunLoop` cannot kill the process | ⛔ not a swallow — logged at ERROR, kept on `LastFault`, loop stops |
| **QA-004** | `LiveInstanceCount` / `IsDisposed` + `FDP_TRACK_REPO_LEAKS=1` | the instrument |
| **QA-005** | `DataBreakpointManager` disposes its post-tick snapshot; both hosts dispose the manager + pre-tick snapshot | two more repositories per node lifetime |
| **QA-006** | `SnapshotPool` + `OnDemandProvider` track and dispose every repository they create — **pooled or leased** | the dominant term |
| **QA-007** | `DtoDiagnosticMapper` maps `FixedString32/64/128` through `ToString()` | `/events` was rendering a name as a byte-buffer object |
| **QA-008** | a `DisableParallelization` collection for registry MUTATORS + a source-scan rail | ⛔ scoped, **not** the suite-wide disable `DEBT-AIB-030` proposed |
| **QA-009** | the gizmo sentinels lose their `[ComponentId]` | the absence of the attribute IS the invariant |
| **QA-010** | `currentRecordPassesAge` starts `true` | an un-timestamped log archived EMPTY |
| **QA-011** | three stale `EntityDragGizmoTests` corrected + a **new** grab-offset rail | ⚠ test-only — nothing enters gizmo production |

### ⭐ New rails *(5)*

`TheHarnessReleasesEveryWorldTests` ×2 *(delta not absolute; refuses to pass vacuously; prints
construction stacks on failure)* · `TheRegistryMutatorsAreSerialisedTests` ×1 *(source scan)* ·
`OnDragUpdate_PreservesTheGrabOffset` ×1 · the `IsReplayActive` precondition in `FullBranchPipelineTests`.

---

## 3. ⭐⭐ GATES *(rule 8 contract)*

⭐ **Build once, then `--no-build` for every run** — every row below is `--no-build` except the first
build of each project. ⚠ **Working tree clean after every suite run; no golden moved in this batch**
*(no panel content changed — `QA-007` alters `/events` JSON for `FixedString` fields, which no golden
captures; the conformance goldens come from `PanelSnapshot`, a different path)*.

### 3a. `Hrot.ClusterRunner.Integration.Tests` — **W1's acceptance**

`dotnet test <proj> --no-build` *(runs 3–5 are the SAME binary)*

| run | tree | discovered | passed | failed | skipped | `OutOfMemoryException` | outcome |
|---|---|---:|---:|---:|---:|---:|---|
| 1 | dispatch | ⛔ **Unknown** | 35 | 52 | 3 | 🔴 **77** | **ABORTED** — `Test host process crashed : dds_take failed: -3` |
| 2 | +`QA-001/2/3` | 265 | 77 | 185 | 3 | ⚠ 323 | ✅ completed — **the crash is gone** |
| 3 | +`QA-004/5/6` | **267** | 212 | 52 | 3 | ✅ **0** | ✅ completed |
| 4 | *(same binary)* | **267** | 209 | 55 | 3 | ✅ **0** | ✅ completed |
| 5 | *(same binary)* | **267** | 211 | 53 | 3 | ✅ **0** | ✅ completed |

⭐⭐ **Stability across the three repeats:** the failing SET is **52 identical names in all three**;
the union is 55 — the extra 3 are all `Eqs` cases that come and go. ⇒ **the rotating-identity signature
is gone except for three named cases.** 📐 RSS: run 1 climbed 4.1 → 9.9 GB inside 45 s; run 3 held
0.7–1.1 GB.

⚠ **Total moved 265 → 267 because this batch adds two rail tests.**

**Base-sha proof for the 52** *(the only sound method — `F17`'s filtered comparison)*: at `dbdc5e783`,
the same **28 classes** filtered → ⛔ **still ABORTS**: `Total tests: Unknown`, 26 passed / 39 failed,
0 OOM, `Test host process crashed`. **38 of the 52 are confirmed red at base, and there is ZERO base
red outside the set.** The remaining 14 could not be evaluated **because the base tree cannot finish
even a filtered subset** — which is the defect this batch fixed. ⇒ **no red in runs 3–5 is
attributable to this change.**

### 3b. `Hrot.SimHost.Tests` — **W2, first half**

| | discovered | passed | failed | skipped | failing names |
|---|---:|---:|---:|---:|---|
| before ×3 | 771 | 767 / 766 / 764 | **1 / 2 / 4** | 3 | ⛔ **rotating** — `StagingEntityExtractor`, `EditLoadClusterOpHandler`, `LiveFromReplay`, `FullBranchPipeline` |
| after ×3 | 772 | **768 / 768 / 768** | **1 / 1 / 1** | 3 | ✅ **identical** — `FullBranchPipelineTests` only |

### 3c. `Fdp.Toolkits.Tests` — **W2, second half**

| | discovered | passed | failed | failing names |
|---|---:|---:|---:|---|
| before ×3 | 2037 | 2037 / 2036 / 2036 | 0 / **1 / 1** | ⛔ rotating between `SC_GZ022_2` and `SC_GZ004_2` |
| after ×3 | 2037 | ✅ **2037 / 2037 / 2037** | ✅ **0 / 0 / 0** | — |

⭐⭐ **This is the suite `DEBT-AIB-030` is named for.** ⛔ It has been *"neither a red nor a green is
evidence"* for ~40 batches; it is now **2037/2037 three times running**.
📐 `DangerAreaProviderTests…ZeroAllocAfterWarmup` — the handoff's GC-noise candidate — **did not fail
in any of the three runs**; no change was needed.

### 3d. `Hrot.Core.Tests` — **W3**

| | discovered | passed | failed |
|---|---:|---:|---:|
| `--filter LogArchiveExtractionServiceTests`, before | 12 | 7 | 5 |
| `--filter LogArchiveExtractionServiceTests`, after | 12 | ✅ **12** | ✅ 0 |
| whole suite, at BASE `dbdc5e783` | 134 | 127 | **7** |
| whole suite, after | 134 | 132 | ⚠ **2** — see §4 |

⇒ ⭐ **7 → 2**, and the two survivors are the SAME two the base run shows.

### 3e. `Hrot.Presentation.Tests` — **W3**

`--filter EntityDragGizmoTests`: **5 / 8 → 8 / 8** *(8 = 7 existing + the new grab-offset rail)*.

### 3f. `Hrot.ClusterRunner.Integration.Tests` — `--filter EventSerializationHelperTests`

**1 / 3 → 3 / 3.**

### 3g. Script gates

| gate | verdict |
|---|---|
| `python3 scripts/tracker-counts.py --check` | ✅ `open 102 / done 346 (+1 refuted)` |
| `python3 scripts/rulings-check.py` | ✅ 25/25 verified · ⚠ **one staleness WARN, expected and benign**: `DataBreakpointManager.cs` changed after the ledger, because `QA-005` added `IDisposable`+`Dispose` to it. `R-63`'s quote is `_liveRepo.SyncFrom(_postTickSnapshot);` — untouched, and the ruling is unaffected |
| `T3` (`run-system-tests.sh`) | see §3h |

⚠⚠ **A finding about a gate itself:** `tracker-counts.py` only counts rows matching `**BP-\d+`. ⛔ So
`CE-`, `TM-`, `ST-` and the new `QA-` rows are **invisible to it**, and *"open 102 / done 346"* is a
**BP-only** figure, not the tracker's size. The new Area N rows are therefore correctly absent from the
table. Reported, not changed — widening the regex is the coordinator's call.

### 3h. `T3` — the E2E slow lane

`bash scripts/run-system-tests.sh` — ⭐ run **asynchronously**, per the tiering rule.

📐 **`Passed! — Failed: 0, Passed: 107, Skipped: 0, Total: 107, Duration: 9 m 34 s`** *(exit 0)*.

⭐⭐ **Fully green, including `ModeStartupRails … (ig)`** — the case the handoff listed as an X11
SIGSEGV needing a virtual display. It was already acquiring one; the fault was intermittent and
environmental, and the previous full T3 *(UXI-05, same day)* was also 107/0/0.

---

## 4. ⚠ WHAT IS **NOT** FIXED — stated plainly

| # | | |
|---|---|---|
| **`QA-012`** | `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` | ⭐ **narrowed, not shrugged at.** `ReferenceReplayLoadHandler` accepts `PrepareLive` **only** while `IsReplayActive` and otherwise skips the branch SILENTLY. That precondition is now railed — **and it passes**, which REFUTES the obvious hypothesis. ⇒ the defect is in the branched recording's Prepare/Finalize **write path**. Needs a batch that can carry a record/replay investigation |
| **`QA-013`** | the **52** stable integration reds | ⛔ **not new** — the suite could never finish, so they had never been seen at once. Base-proof in §3a. ⭐ **This is a programme, not a batch**: 28 classes across replication, cluster transition, recording, mission control, EQS. Coordinator scoping needed |
| ⚠ | 3 `Eqs` cases that vary between runs | `Eqs_DistributedTopology_RejectsStaleEpochResults`, `Eqs_MidEvaluationAbort_SilentlyDropsQueryWithoutLeaking`, `CheapLosTest_BelowThreshold_FlagsMeaningfulZero`. ⭐ The residual after the memory fix — a much smaller target than the original signature |
| ⚠ | `Hrot.Map.Common.Tests.EcsPatchContextTests` ×2 | `FlushDirtyMarks_DeduplicatesOrdinals`, `FlushDirtyMarks_CallsSmartEgressForTouchedComponents`. **STABLE** *(fail in isolation too, so not a flake)* and ✅ **proved PRE-EXISTING at `dbdc5e783`** *(§5)*. In `SmartEgress`/patch-context code this batch never touched; they arrived with the merged `Q59`/Axis-B work. ⭐ Found only because they surfaced while gating `QA-010` — **filed, not fixed**, and they belong to whoever owns that merge |

⛔ **No new skip or quarantine was added anywhere in this batch** (`R-131`), and **two existing
`STABILITY(Flaky)` traits were DELETED with the defect they labelled**.

---

## 5. 📐 BASE-SHA APPENDIX

| claim | how it was proven |
|---|---|
| the 52 integration reds are pre-existing | detached checkout of `dbdc5e783`, rebuild, filtered run of the same 28 classes — §3a |
| `EcsPatchContextTests` ×2 | ✅ **PRE-EXISTING, proved.** Detached checkout of `dbdc5e783`, rebuild, whole `Hrot.Core.Tests` suite: **134 discovered, 127 passed, 7 failed** — the 5 `LogArchiveExtractionServiceTests` *(fixed by `QA-010`)* **and these same 2**. ⇒ this batch took the suite from **7 reds to 2**, and added none |

---

## 6. ⭐ HANDBACK TO THE COORDINATOR

1. ⭐⭐⭐ **`QA-013` — scope the 52.** They are the first honest picture this suite has ever produced.
   ⛔ Do not read them as a regression; read §3a's base proof first.
2. ⭐⭐ **`QA-006` is a PRODUCT leak, not a test one.** Ten repositories per node teardown, in the
   shipping runner. Worth telling whoever owns runtime memory budgets.
3. ⭐ **Three ledger rows now carry a FALSE mechanism** and should be corrected where they are quoted:
   `Q52` §6.3, `ST-026`, `AX-023` — component ids are deterministic; the mechanism is `Clear()`.
   `DEBT-AIB-030` itself is closed with the correction in its own text.
4. ⚠ **`tracker-counts.py` counts only `BP-` rows** (§3g). Widening it is a one-line change with a
   large count delta — the coordinator's call, not mine.
5. ⭐ **`QA-012`** needs a record/replay batch.
6. ⚠ **Rule-4 re-pull, done before the final commit.** The coordinator branch moved
   `dbdc5e783 → dabd35715` **during** this batch *(UI Slice A / `CE-046..048`, the MCP agent-surface
   programme, a lane-table correction)*. ⛔ **My scope stayed FROZEN at the dispatch sha** — nothing
   there was adapted to. 📐 **Three files overlap** and are flagged for the merge, all in disjoint
   regions: `Hrot.CGF/CgfSubsystem.cs` *(I touched only `Shutdown()`)*,
   `Hrot.Editor/EditorSubsystem.cs` *(likewise)*, and `Blueprint_Issues_Tracker.md` *(I appended a new
   Area N at the end; they added rows in Areas L/M)*. ⭐ Nothing in those commits invalidates an item of
   this batch.
