<!--STATUS
state: LIVE
build-state: DISPATCH — BACKEND lane. Make the test suites TRUSTWORTHY: fix the integration-suite host
  CRASH (un-gateable, R-131), eliminate the static-order FLAKES (DEBT-AIB-030), triage the stable reds.
  Investigation-led — root-cause each BEFORE fixing; do not filter-around (R-131).
updated: 2026-08-26
current-answer: this handoff. Durable record for findings: extend TESTING_Harness_And_Goldens.md /
  DESIGN_Regression_Net.md and the DEBT ledger — ⛔ do NOT create a new testing design.
known-conflict: touches TEST projects + possibly ClusterRunner/SimHost production if the crash is a real
  bug. ⛔ Disjoint from the UI/CGF lane's Slice A (scenario session) — coordinate only if a SimHost prod fix overlaps.
-->
# HANDOFF — **Test-suite reliability: the crash, the flakes, the stable reds** *(BACKEND lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Allocate ids in your lane's convention *(a new **`QA-`** prefix is clean; or continue `ST-`)*; state every id *(rule 5)*.
> 🎯 **The goal: a RED means a real defect.** Today the suites cry wolf *(rotating flake)* and one cannot even finish *(crash)*, so the regression net can't be trusted — which is exactly what the harness exists to provide.

## 0. ⛔ DISCIPLINE
Investigation-led *(decide-and-log; stop the ITEM not the batch — R-106)*. ⛔⛔ **R-131 — no permanent filter-around.** A skip/quarantine is a **last resort with a filed cause + a follow-up id**, never a silent hide. **Root-cause BEFORE fixing** — measure on a clean base worktree, never infer from a diff. Codebase-memory not connected ⇒ the **CLI**, ⛔ not grep-only. Build the AFFECTED test project *(not the solution)*; build once then `--no-build`.

## 1. ⭐⭐⭐ W1 — the `ClusterRunner.Integration.Tests` TEST-HOST CRASH *(the big one — un-gateable today)*
📐 **Evidence (measured by the UI lane, `2026-08-26`, `REPORT_Cgf_AxisB_Egress_And_Cleanups.md` §F17):** the suite **ABORTS mid-run at a DIFFERENT test count each run** *(`38` on one tree, `76` on another)* — a **test-host crash**, not a set of failures. ⇒ ⛔ the headline count is NOT comparable between runs, and the suite **cannot gate** — the very suite that proves cross-node invariants *(scenario load, replication, time-sync)*.
| task | the one thing not to get wrong |
|---|---|
| ⭐⭐⭐ **Root-cause the abort.** Candidates named by prior reports: the **DDS-allocator crash** *(`ClusterRunner.Integration.Tests`, historically)*, resource pressure on a 16 GB box, and per-test DDS-domain/participant leakage across classes. ⭐ Measure: run the full suite with per-test diagnostics, find the first crashing test + what it leaks | ⛔ *"it's flaky"* is not a root cause — a crashing host is a **resource/lifecycle bug**, find it |
| ⭐⭐ **Fix it so the suite RUNS TO COMPLETION** — deterministic pass/fail, no host abort *(likely: dispose DDS participants per test/collection, cap parallelism, or serialize the DDS-touching collection)* | ⭐ once it completes, the `--mode all` round-trip *(AX-011/012, now green filtered)* and the scenario/replication rails become GATEABLE |
| ⭐ **acceptance** | the full `ClusterRunner.Integration.Tests` runs to completion on a clean box and gives a **stable** pass/fail across 3 repeats; the residual reds are then triaged as W3 |

## 2. ⭐⭐ W2 — the `ComponentTypeRegistry` STATIC-ORDER FLAKE *(`DEBT-AIB-030`)*
📐 **Evidence:** in `Hrot.SimHost.Tests` *(and `Fdp.Toolkits.Tests` `GizmoRegistry`/`StatelessGizmoRegistry`)* the **failing test IDENTITY ROTATES run-to-run** *(observed `StagingEntityExtractorTests` / `EditLoadClusterOpHandlerTests` / `FullBranchPipelineTests` / …; count varies 0–5)*, and **every named one PASSES under `--filter`/in isolation.** ⇒ a **shared static registry** *(`ComponentTypeRegistry`, and the gizmo registries)* whose state leaks across test classes; whichever runs first wins.
| task | the one thing not to get wrong |
|---|---|
| ⭐⭐⭐ **Root-cause the shared static state** — which registry, and why a second class sees the first's registrations *(or a half-initialised registry)* | ⚠ `BinaryInterpreter<T>` open-generic id sharing + static `[ComponentId]` registration are the usual suspects |
| ⭐⭐ **Fix by ISOLATION, not by ordering** — per-collection reset / a fixture that rebuilds the registry, or make registration idempotent+scoped. ⛔ NOT `[Collection]` ordering hacks that just hide it | ⭐ the test is that the SAME set passes on repeated **full** runs, not just in isolation |
| ⭐ **acceptance** | `Hrot.SimHost.Tests` + `Fdp.Toolkits.Tests` give the **identical** result across 3 repeated full runs — the rotating identity is GONE. Close `DEBT-AIB-030` with the fix cited |

## 3. ⭐ W3 — triage the STABLE pre-existing reds *(each: real-bug-fix OR stale-assertion-correct)*
⛔ These are NOT flakes — they reproduce identically on a clean base worktree. Classify + fix each; ⛔ a red left standing must be a **newly-filed real defect with a rail**, not a shrug.
| red *(suite)* | likely class |
|---|---|
| `LogArchiveExtractionServiceTests` ×5 *(`Hrot.Core.Tests`)* | real — investigate the extraction service |
| `EntityDragGizmoTests` ×3 *(`Hrot.Presentation.Tests`)* — `_dragOffset` + a pick-token assertion | likely stale-assertion; ⚠ **coordinate with UI/CGF lane if the fix touches gizmo production** |
| `EventSerializationHelperTests` ×2 | JSON-shape assertion — stale vs real |
| `E1_CognitiveRuntimeModule…RegistersExactlySixSystemsInOrder` *(expected 6, actual 7)* | **stale COUNT assertion** *(the `CgfLogicPackTests` 18→19 family)* — correct the count if 7 is right |
| `DangerAreaProviderTests…ZeroAllocAfterWarmup` *(`Fdp.Toolkits.Tests`)* | a GC-noise assertion — make it robust or justify |
| `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` *(`SimHost`)* | real — investigate |
| `ModeStartupRails…(ig)` X11 SIGSEGV *(T3)* | **environmental** — the non-headless IG boot needs a virtual display; make the rail acquire/skip-with-cause a display, ⛔ don't let a missing `:DISPLAY` read as a code regression |

## 4. ⭐ DONE — acceptance
- **W1:** the integration suite runs to completion, stable across 3 repeats; ⇒ the round-trip/replication rails GATEABLE. **W2:** the rotating flake eliminated *(identical result across 3 full runs)*; `DEBT-AIB-030` closed. **W3:** each stable red fixed or re-filed as a real defect with a rail; the stale count/shape assertions corrected.
- Every fix **red-proved** *(inverse edit)*. ⛔ No new skip without a filed cause + follow-up id *(R-131)*. Working tree clean after every run; golden movement stated.

## 5. ⭐ LANE & GATES
⭐ **BACKEND lane.** ⭐ **Yours:** the test projects *(`ClusterRunner.Integration.Tests`, `Hrot.SimHost.Tests`, `Fdp.Toolkits.Tests`, `Hrot.Core.Tests`, `Hrot.Presentation.Tests`)* + any PRODUCTION lifecycle/registry bug a root-cause exposes *(DDS disposal, the static registry)*. ⚠ **If a fix touches gizmo production `Hrot.Presentation`** *(the `EntityDragGizmoTests` triage)*, that is the UI/CGF lane's Slice A neighbourhood — coordinate / sequence. ⛔ Do NOT touch scenario-session / toolbar / menu code. ⭐ rule-4 re-pull.
**Gates (rule 8):** per-workstream before/after counts across **3 repeats** *(the whole point is stability)* · `--no-build` column · base-sha proof for every "pre-existing" · `tracker-counts.py` · `rulings-check.py` · the ids. **When done:** fold the crash + flake root-causes into **`TESTING_Harness_And_Goldens.md`** *(or `DESIGN_Regression_Net.md`)* and the DEBT ledger — ⛔ the report is ephemeral; the durable record is the testing design.
