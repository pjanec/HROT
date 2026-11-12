<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — OWNS the overnight plan these measurements finish: §0 the decisions, §2 run controls / the tick rate / hot reload
  - ../../DESIGN_Parallel_Simulation.md — §18 / §18.1 per-entity parallelism, the census verdict, R-4212 / R-4213
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target shape
  - HANDOFF_Coordinator_To_Ui_Windows_Scaling_Measure.md — the Windows half, running in parallel
-->

# HANDOFF — coordinator → backend (VM): the LAST measurements before the overnight threaded + parallel build

**For:** a free backend VM session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there.
🔒 User, `2026-11-12`: *"write the measurement handoff … one VM lane and one windows lane is free"* · *"we do not want a timing taking 12% so
whatever helps"* (R-4213). ⛔⛔ **Autonomous** (R-4156); decide, record with a claim table, continue; a failed premise stops THAT item (R-106).

## Where to work

⭐ A NEW branch **`exp/measure-2`** from **`exp/threaded-poc-4`** (the threaded shape + `exp/perf` + the world guard), with
**`exp/entity-census`** (the census instruments, `EntityCensus`, `LastTickProbe`, `scripts/entity-census-report.py`) and **`coordinator`** merged
in. Status file `docs/blueprints/batches/STATUS_Pre_Build_Measurements.md` ON THAT BRANCH. ⛔ Never push to `exp/perf`, `exp/threaded-poc-4`,
`exp/entity-census` or any lane. Instruments opt-in, default OFF — except K-4, which changes a default (R-4213).

## The items

| # | measurement | proves / settles | done when |
|---|---|---|---|
| **K-1** | ⭐ **the threaded tick, split**: in the worker loop (`EditorSubsystem.cs:2973-3040`) time separately the `TickNodeFrame` work, the wait to ACQUIRE `WorldLock`, the sync's hold, the `WorkerYield` wait; count the main thread's `MainExit`s while the worker is blocked. Run `scripts/threaded-poc2-measure.py --mode 3` on the 1 000-man rig, playing and paused. Then prototype a FAIR hand-over (the worker sets a "wants" flag; `MainTryEnter` (`Program.cs:704`) yields or waits ≤ 2 ms for the worker's sync) and measure again | the inventory's §2 claim: the halved tick rate (Windows ~4.8 Hz) is LOCK STARVATION, not tick work; the fair hand-over's effect on the tick rate AND the drawing frame | the four timings, both states, before / after the hand-over; the prediction (lock-acquire ≈ 100–130 ms) confirmed or refuted |
| **K-2** | **the census on COMBAT-HEAVY scenarios**: the 1 000-man rig with two sides engaged (`scale-rig.py --sides 2`, or the heaviest combat scenarios in `scenarios/`), `HROT_ENTITY_CENSUS=1`, CGF and SimHost; does any of the 36 systems that made NO per-entity access in the first run write another entity, and do new last-tick types appear | the census's own "what would change it" (N-8) | the delta table vs the first census: new cross-entity writes, new last-tick types, the masked copy's new cost |
| **K-3** | **the hot-reload desync** (inventory §2): an env-gated log of every `_inSync` change (`ClusterMaster`, path: heartbeat eject vs a round's `Targets`), grep `ejected (heartbeat timeout)` (`ClusterMaster.cs:783`), time `DrainPendingCallbacks` and each `OnReloadCompleted` subscriber; stages `load,play,edits,hotreload,edit2,save`, OFF then `=3` | the root cause, so the overnight build fixes it rather than chases it | the cause named with the log lines |
| **K-4** | ⭐ **R-4213: the per-query timer OFF by default** in every host (`QueryTimings.Enabled` default false); `scale-rig.py`, `parallel-sim-measure.py`, `threaded-poc2-measure.py` set `HROT_QUERY_TIMINGS=1`; `/diagnostics/queries` gains an `enable` switch; the R-4209 sampling kept for when it is on | the 11.9 % of BrainTick the census measured | BrainTick before / after on the rig (census N-6 method); the rig's query table still produced when it asks; 0 B; the query-timer rails green |
| **K-5** | ⭐ **the verdict** — what each measurement settled, anything that changes the overnight plan (`DESIGN_Threaded_Work_Inventory.md`), folded into that document and §18 | | top of the status file |

## Rules

- Measure before claiming; numbers from runs, with the commands. Run anything slow in the background; never poll. ⛔ No screenshots in git.
- 0 B per steady frame with every instrument OFF (R-2084).

**Done when:** K-5 is written — however far K-1…K-4 got.

---
_coordinator lane, 2026-11-12_
