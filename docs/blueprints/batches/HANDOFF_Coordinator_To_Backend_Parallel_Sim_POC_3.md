<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §10 R-4208 and the emitter fix, §11 the aggregate, ⭐ §12 R-4209 the sampled timer, §12 (on exp/parallel-sim-2) POC 2 as-built
  - ../../DESIGN_Egress_Change_Detection.md — §7 the off-frame state egress (CE-3427) this extends
  - STATUS_Parallel_Sim_POC_2.md (on exp/parallel-sim-2) — the Q-0 profile and D-1…D-12 this batch follows
  - HANDOFF_Coordinator_To_Backend_Threaded_POC_3.md — the other lane's experiment; independent
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — POC 3 of the simulation's throughput: the cheap wins POC 2's profile found

**For:** a backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there. ⭐ **Still an experiment.**
⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table, continue; a failed premise stops THAT item (R-106).

## Where to work

⭐ A NEW branch **`exp/parallel-sim-3`** from **`exp/parallel-sim-2`** (the pre-score, `ParallelGuard`, the profile tooling, the measure script
with `--poc2` and `all_ms`), with **`coordinator`** merged in. Status file `docs/blueprints/batches/STATUS_Parallel_Sim_POC_3.md` ON THAT BRANCH.
⛔ Nothing merges into a lane; ⛔ do not push to `backend`. Everything opt-in, default OFF — except R-4209, which is a default. Extend
`parallel-sim-measure.py` with a `--poc3` column set so ONE command on the 8-core Windows machine measures every item (every node HIDDEN).
⚠ The Windows session is measuring POC 2 now on `exp/parallel-sim-2` — fetch its status file when it lands and take its numbers into your verdict.

## The items — measure each before and after (CGF, 1 000 men, the rig)

| # | item | basis | done when |
|---|---|---|---|
| **T-1** | ⭐ **the sampled query timer (R-4209, approved)**: `QueryTimings` times 1 call in 16 and scales up; call counts exact; still on by default, `HROT_QUERY_TIMINGS=0` still turns it off | §12, POC 2 D-1 (~13 ms of the brain) | the brain ms: always-timed vs sampled vs off; the rig's query table still sane (a rail: counts exact, times within a stated error); 0 B |
| **T-2** | ⭐ **a change gate in `EqsSensorConfigEgressTranslator`** — it builds every sensor's topic every frame, then compares (POC 2 D-4: 17 ms, the CGF's biggest translator) | Egress §2's hazards (a gate must not miss a seek / restore / wipe) | egress ms before / after; the replication rails and the egress equivalence rails green |
| **T-3** | ⭐ **pre-score the other brain decisions** the way POC 2 did the posture: threat ranking, attack approach, weapon choice — and the posture SENSORS (`TopThreat` / `KeepPair`, 15 %) | POC 2 Q-4 / D-8 (−38 %) | the brain ms per decision; the serial-vs-parallel rail per decision at 1 / 2 / 4 / 8 threads; `ParallelGuard` silent |
| **T-4** | **the write-back hunt** — what changes brain memory during the tick and changes it back before the frame ends (POC 2 D-11: the 4 KB tier stays ~50 % unchanged-dirty after "stamp only on change") | §10.2 | the writer(s) named with `file:line`; the fix if small; the unchanged-dirty share after |
| **T-5** | **one replica, two consumers, off the main thread**: the replica sync moves off the main thread (POC 2 D-7: 11.8 ms on it), and the recorder's capture joins the off-frame egress scan on the same replica (D-12) | Egress §7, §14 picture | egress + recorder ms on the frame before / after; a recording made both ways replays identically (a rail); the replication gate as POC 2 ran it |
| **T-6** | ⭐ **the verdict** — the CGF frame before / after everything (VM), each item's gain, the cost to finish, go / change / stop per item, the Windows command | — | top of the status file; folded into Parallel Simulation §9–§12 |

## Rules

- **Correctness first**: every changed path has its rail (serial-vs-parallel in Deterministic mode where it applies); the determinism rails and a
  scenario-regression subset at the end (a red is a finding; base sha named).
- **0 B per steady tick** (R-2084); one core budget (cores − 2).
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** T-6 is written — however far T-1…T-5 got.

---
_coordinator lane, 2026-11-12_
