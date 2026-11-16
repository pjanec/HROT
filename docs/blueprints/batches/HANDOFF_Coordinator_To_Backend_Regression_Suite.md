<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../DESIGN_Scenario_Regression.md — ⭐ THE design: §11 the two-tier suite (RT-1…RT-8 as approved, RT-4 / RT-9 withdrawn), §3 the checks grammar, §4 the golden, §8 the verifier as built
  - HANDOFF_Coordinator_To_Backend_Feature_Scenarios.md — the feature scenarios (animals, motorcycles) — run as part of this batch, in the order of §1
-->

# HANDOFF — coordinator → `backend`: the TWO-TIER REGRESSION SUITE (R-4279; RT-1…RT-8) + the feature scenarios

**For:** the `backend` lane (the test-suite harness is yours). **From:** coordinator. **Dispatched at `816edc3b9`**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-16`: *"i guess the truly trustworthy and reproducible and usable for overnight regression runs is only the fully deterministic
run … overnight (slow and long) we migh need to test also the replication (clusterruneer -m all or something like that)"* · *"RT-4 not necessary
… its the responsibility of each lane to provide working stuff"* · *"no nightly claude task"* · *"approved. pls write handoffs"* — R-4277, R-4279.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait; decide by the claim table (code + design) and record an `R-3xxx` lane decision with what would change it; a failed premise
stops ONE item (R-106); a scenario that fails because the FEATURE is wrong is a finding to fix where it lives (or file to its lane), never a check to
loosen; commit, push and update `STATUS_Backend_Regression_Suite.md` after each item.

## 0. Start
Merge `origin/coordinator`; `dotnet restore` the runner and the test projects you run; push `chore: started regression suite at <sha>`. Read §11 of
the design END TO END first (R-129), then §2–§4 and §8 (the verifier as built).

## 1. The items, IN THIS ORDER
| # | item | done when |
|---|---|---|
| **1** | **RT-3 the golden compares VISIBLE state** — position, health, modes and every field a scenario's `checks.json` reads; internal blackboards, `BrainDiagnostics`, `BehaviorStartRecord` and the like left out; a golden difference is a WARNING (exit 0, listed in the summary), never a fail; ONE command re-captures (`--verify-capture`, already built) | `farm-under-fire` and `ua-drone-shelter` PASS with their current goldens re-captured once (📐 measured today: 19 / 19 expectations pass, the goldens fail on internal hashes and the new `RadarSignature`) |
| **2** | **the feature scenarios** — `HANDOFF_Coordinator_To_Backend_Feature_Scenarios.md` in full (the pillion's throw, 5 motorcycle + 5 animal scenarios) | as that handoff says |
| **3** | **RT-5 the cluster tier** — `--verify` accepts `--mode all`: a `ClusterVerifyRunner` + a `ClusterVerifyWorld` (a THIRD `IVerifyWorld`, over the cluster's debug HTTP on the owning node), judged by the SAME `ConditionBook` in SIM time; no golden in this tier (§11.3 UML) | `farm-under-fire` and `ua-drone-shelter` pass under `--mode all`; a deliberately broken expectation fails there too |
| **4** | **RT-6 replication sanity** in every cluster run — every translator's sent vs received samples (`/diagnostics/architecture`) and any exception the module host swallowed ⇒ FAIL, named in the report | a mutation that drops one translator's ingress fails the run |
| **5** | **RT-8 GitHub Actions** — make `docs/ci/scenario-regression.workflow.yml` real at `.github/workflows/scenario-regression.yml`: ONE build job (the binaries as an artifact), then a matrix of one job per scenario for the editor tier and one per scenario for the cluster tier, nightly (cron) + manual; junit per job. Measure on the first manual run: a runner's cores / memory against `--mode all` + Xvfb, and whether DDS works on a runner (loopback-only settings if not) | one manual run green or red per scenario in the Actions view; findings in the status |
| **6** | **RT-7 one condition set** — move the 81 Python live checks of `scripts/utility-demo-check.py` into each scenario's `checks.json`; the script keeps only its launch helpers | `utility-demo-check.py` holds no condition; every moved scenario passes in the editor tier (or its red is filed) |
| **7** | **the 41 scenarios with no `checks.json`** — each gets one, or moves to `scenarios/showcases/` with "demo, not a test" in its description | `find scenarios -name scenario.json` outside showcases ⇒ every one has a `checks.json` |
| **8** | `docs/CAPABILITIES.md` — the ✅ / ⚠ / ⏳ marks of every scenario you ran come from its result | — |

⛔ NOT in scope: a merge smoke (RT-4, withdrawn), a nightly Claude task (RT-9, withdrawn), the 1 / 4 / 8-thread hash (RT-2 — it stays the parallel
rule's own gate).

## 2. Gates and report
The verifier's own suites (`DirectorRunnerTests`, `ScenarioChecksTests`) green and extended (T-1); each new rail red-proved by a compiling mutation;
the report names every defect a scenario found and where it was fixed (designs folded, obligation ⑤), the gate-report contract (rule 8), ids from
your block. ⭐ Fold the as-built into `DESIGN_Scenario_Regression.md` §11 (build-state BUILT, the UML true).
