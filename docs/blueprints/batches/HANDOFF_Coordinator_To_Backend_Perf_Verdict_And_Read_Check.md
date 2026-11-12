<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — ⭐ §18.2 the last-tick copy declared by construction and the per-read check this measures; §12–§17 what exp/perf carries
  - HANDOFF_Coordinator_To_Backend_Perf_Branch_Validation.md — the validation this finishes NARROWLY (its V-4 full regression was stopped by the user)
-->

# HANDOFF — coordinator → backend (VM): finish the exp/perf verdict NARROWLY, and measure the per-read check

**For:** the free backend VM session (the one that validated `exp/perf`). **From:** coordinator. **Dispatched at `497f620ff`**, scope frozen.
🔒 User, `2026-11-12`: *"it was blindly testing all scenarios like if that can prove that the system features are ok"* — ⇒ ⛔ NO broad suites,
NO scenario regression, NO "all test projects". A branch is proven through the CHANGED features' own rails (CLAUDE.md T-1); V-1, V-2, V-3, V-5,
V-6 already did that. ⛔⛔ **Autonomous** (R-4156).

## Part 1 — on **`exp/perf`** (you are its only writer)

| # | item | done when |
|---|---|---|
| **P-1** | the 10 reds not yet checked at the base — `Fdp.Examples.Scenarios.Tests` (9) and `Fdp.Presentation.Tests` (1): run EXACTLY those tests at the base (`786e99599`, your `/home/user/hrot-base` worktree) | each red named pre-existing (base sha) or a branch finding with its cause |
| **P-2** | V-7 the rig's garbage per frame, `exp/perf` vs the base: the 1 000-man rig, `HROT_QUERY_TIMINGS=0` both sides (launch the rig from a script FILE — your note on `pkill -f ClusterRunner.dll`) | the two numbers per node; a regression is a finding |
| **P-3** | ⭐ **the verdict** — merge-ready or not, with what must be fixed first; the 31 suites of untouched projects stay unrun and the verdict says so | the top of `STATUS_Perf_Branch_Validation.md` |

## Part 2 — on a NEW branch **`exp/read-check`** from **`exp/entity-census`** (its `EntityCensus` per-thread "current entity" is the hook)

The design (§18.2): during a parallel phase the repository's own read / write methods redirect a CROSS-entity access — a read to the last-tick
copy, a write into the per-batch command buffer — keyed by the per-thread current entity; a read of a type outside the declared mask throws in
Debug. ⛔ The open question is its COST: the tracer counted ~10⁹ world accesses per run, so one check per access may matter.

| # | measurement | done when |
|---|---|---|
| **R-1** | a micro-benchmark (BenchmarkDotNet or a Release stopwatch loop, 0 B) of `GetComponentRO<T>` / `GetComponentRW<T>` / `TryGetComponent` / managed `GetComponent`: ① today · ② with a `static readonly`-style "parallel phase active" flag checked first, flag OFF (serial code) · ③ flag ON, own entity · ④ flag ON, other entity → served from a copied table | ns per access for each, and the delta vs today |
| **R-2** | the same check prototyped in the real read path (opt-in switch), the 1 000-man CGF rig serial: the frame with the check compiled in and the flag off vs today | ms per frame; ⭐ the verdict: affordable as designed, or what shape is (e.g. a separate repository view type for parallel phases) |
| **R-3** | what bypasses it: count the raw-access paths used inside per-entity systems (`GetComponentTable`, spans, pointers taken once — the census's blind spot) | the list with `file:line` |

## Report

Part 1 in `STATUS_Perf_Branch_Validation.md` on `exp/perf`; Part 2 in `docs/blueprints/batches/STATUS_Read_Check.md` on `exp/read-check`, its
verdict folded into Parallel Simulation §18.2. Run anything slow in the background; never poll. ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-12_
