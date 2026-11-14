<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — ⭐ §18 the per-entity rule as built (§18.2a declared reads, §18.2c the phase-added authority, §18.5 the brains phase, §18.10 on by default); the line "a DIFFERENT result from today's serial one (a friend's claim seen a tick later)" decides D-1 ①
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §15 the threaded node
  - ../DESIGN_Time_Architecture.md — §17 stepped mode
  - STATUS_Threaded_Fixes.md — X-1…X-12, the batch this follows; its open residue is here
  - NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md — L-1…L-7 (L-2 / L-1 wait on Windows, not here)
-->

# HANDOFF — coordinator → `backend`: the determinism / parallel residue after the threaded fixes

**For:** the `backend` lane. **From:** coordinator. **Dispatched at `<sha>`**; ⛔ **scope FROZEN there** — later documents are FYI; one that
invalidates an item ⇒ STOP that item and report it (R-106, never the batch). ⭐ Autonomous, no questions: each item's decision is below; anything
left open is decided the closest way and named in the report.
🔒 User, `2026-11-14`: *"what determinism/parallelization issues are still open? lets give those to a backend lane"*.

## 0. Start

`git fetch origin backend coordinator` · on `backend`, `git merge origin/coordinator` · ⚠ **`dotnet restore` every test project you will run once
after the merge** (the third stale-binary trap — `Hrot.Terrain.Synth` / `.Random` arrived unrestored in the coordinator merge today) · push
`chore: started determinism residue at <sha>`.

## 1. The items — in this order

| # | row | what | decision (with its basis) |
|---|---|---|---|
| **D-1** | **CE-3448** | the rules under the parallel brain phase: 26 / 190 red with `HROT_PARALLEL_SIM=1` | ② ⭐⭐ **a verb's refusal must keep its own reason** when it runs as a batch follow-up — today the journal says *"an action failed"*; user-visible, the phase is ON by default (R-4224). Fix in the follow-up path (the reason travels with the follow-up, ONE path for every verb), not per verb. ① objectives reaching Achieved later: ⭐ **a lag of exactly the one tick the rule designs in** (§18 *"a friend's claim seen a tick later"*; §18.2c *"begins on the next tick"*) is correct — the assertion takes the parallel timing (a rail that holds on BOTH kernels, e.g. "achieved within one tick of the serial tick"), ⛔ never a wider window; **a lag over one tick, or a different outcome, is a defect** — fixed. Report the 26 split into ① designed lag / ① defect / ② |
| **D-2** | **CE-3441** | a `SimInstant` of ±∞ crashes every scenario golden snapshot | ⭐ the row's lean: `SimInstantJsonConverter` writes a non-finite value as a marker (`"-inf"` / `"+inf"`) and reads it back — ONE place for every `SimInstant` field; "never" stays a real value; a round-trip rail. ⭐ then run `scenario-regression.sh` on the 12 combat / squad scenarios that failed with it (the row's run) and report which still fail. ⚠ CE-1471 (`GET /entities` fails on ±∞, ui) is the same family: check whether the same converter fixes it; if so tick it too, else name what differs |
| **D-3** | **CE-3447** | `DebugApiOrdersTests.CE1078_…AndRmbDropsAWaitingShot` red (29 rounds where 28), serial too | ⭐ **bisect first** (`git bisect` between the last green and `25cbd799e`, the rail alone, `--no-build` after one build per step); fix at the cause; the rail is the gate. ⛔ not a parallel defect unless the bisect says so |
| **D-4** | **CE-3443** | `ParamUsage.Rename` mutates a managed component in place and re-sets the same object | ⭐ build a NEW instance and set it (CLAUDE.md "NEW COMPONENTS": a managed component is immutable and replaced whole); ⚠ `FdpAutoSerializer.DeepClone` copies records by reference — not enough. Rail: a snapshot taken before the rename still holds the old name |
| **D-5** | *(new ids from your block, `CE-3449`+)* | ⭐⭐ **the survey, on what the user runs now** | `HROT_READ_CHECK=2` (+ `HROT_READ_CHECK_OUT`) and `python3 scripts/stepped-hash.py --threads 1,4,8` on `showcases/city-battle` (the showcase — new behaviour lands there daily) and on the squad / combat regression scenarios. Every undeclared read and every divergence: fixed here when in engine / toolkit / backend code; ⛔ in a lane's behaviour code (showcase-brains, showcase-world) → a row naming the lane, not a fix. ⚠ the 2026-11-13 survey named `Gesture` (the brain phase's utility input) — I find no brain read of it on `coordinator` now (grep); the survey settles it |
| **D-6** | **CE-6004** *(showcase-world's row — a cross-lane MEASURE, said)* | the obstacle bake debounces on WALL time (`StaticObstacleBake.cs:52,167`, R-244) | ⭐ **measure only**: `stepped-hash.py --threads 1,4,8` on CE-6003's scenario (`roadblock-street`), several runs. Equal ⇒ note it on the row. Divergent ⇒ ⛔ **do not change R-244** (a user ruling): write the measured case and a lean (sim time, CLAUDE.md "never the wall clock in a step") into the row for the user |

## 2. Out of this batch — and why

| | |
|---|---|
| CE-3445 (a rare stepped divergence, 1 in 36) | ⛔ deferred by the user (STEER, 2026-11-13); its long series is the Windows W-4 item (CE-4540) |
| CE-4569 / CE-4570 (X-3 equal at 1/4/8, X-4 the paused entry) | ⏳ Windows confirmation (L-1, L-2) — the Windows lane |
| CE-4563 / CE-4564 / CE-4567 (the serial tick, system-level parallelism, the inline threshold) | ⏳ throughput, not correctness; HELD for the coordinator's design (T-1 / T-2) |
| CE-4438 (brains write back unchanged bytes) | throughput |

## 3. Rules and gates

| | |
|---|---|
| ⭐ **T-1 first** — each item's own suite | `ScenarioRulesTests` & co. (`Objective\|Rule\|Trigger`, Hrot.SimHost.Tests) on BOTH kernels; the `SimInstant` converter's suite (Fdp.Core); `DebugApiOrdersTests`; the macro / param suite owning `ParamUsage`; `PerEntityRuleTests` for anything touching the phase |
| ⭐ every new rail red-proved by one compiling mutation; run with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` and with the phase off | — |
| ⛔ no allocation per frame (R-2084), language-neutral output (R-4221) | — |
| ⭐ as-built into the designs | D-1 → Parallel Simulation §18.2c (the follow-up keeps the reason; the one-tick rule for rules); D-2 → the `SimInstant` owner (SV-1) |
| ⭐ report per the gate-report contract (CLAUDE.md rule 8) | one row per gate with counts; every red confirmed pre-existing at the base sha; ids allocated; `STATUS_Determinism_Residue.md` |
