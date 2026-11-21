<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §18 / §18.1 the census and its prize model (which assumes perfect scaling inside a system); §11.1 the previous Windows numbers
  - HANDOFF_Coordinator_To_Backend_Pre_Build_Measurements.md — the VM half, running in parallel
-->

# HANDOFF — coordinator → the Windows session: does a parallel system keep scaling past 4 threads?

**For:** the ui session on the user's Windows machine (Ryzen 7 6800H, 8 cores / 16 logical). **From:** coordinator. **Dispatched at `89883010a`**.
🔒 User, `2026-11-12`: *"one windows lane is free"* · *"expect at least 8 core machine and the parallelization in use"*.
⛔⛔ **Every process HIDDEN** (`HROT_HIDDEN_WINDOW=1` — the scripts set it). ⛔ **Autonomous** (R-4156). ⛔ **Measure only — change no code**
except the measurement script's options (below).

## Why

The census prize model (2.5× at 8 cores, 2.8× at 16) ASSUMES a parallel system scales perfectly. The last Windows run saw the brain pre-score
flatten after 4 threads — but the CGF was PINNED to 7 logical CPUs (`--pin`: orchestrator 2 · cgf 7 · simhost 7), so the flattening may be the
pin, not the code.

## The branch — exactly **`exp/perf`** (at least `5add60bbe`)

```
git fetch origin exp/perf
git checkout exp/perf
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
```

## What to run (one after another, never together; set `HROT_QUERY_TIMINGS=0` for every run — the timer costs ~12 % of the brain)

| # | run | answers |
|---|---|---|
| **S-1** | `HROT_BRAIN_PRESCORE=1`, **pinned**: `python scripts/parallel-sim-measure.py --pin --threads 1 2 4 6 8 --mode throughput` | the baseline curve on the pin (as before) |
| **S-2** | ⭐ the same **UNPINNED**, CGF given all 16 logical CPUs — `--threads 1 2 4 6 8 12 16` (if the script's pool budget caps at cores − 2, set `HROT_SIM_THREADS` per column, as POC 1 D-4 provides) | does the pre-score keep scaling past 4 when the CPUs are there; where it flattens |
| **S-3** | S-2 again with ONLY the CGF node running heavy (the SimHost and orchestrator idle as far as the rig allows — say how) | is the flattening contention with the other two nodes on the same machine |
| **S-4** | per system in S-2's columns: BrainTick (and its pre-score part), Stress, Threat — the script's `all_ms` | which system scales, which does not |
| **S-5** | ⚠ the script's decimal-comma bug: `threaded-poc2-measure.py`'s regex misses `15,6` on this machine — fix the regex to accept `,` if you touch it (a measurement-script fix is allowed) | — |

## Report

`docs/blueprints/batches/STATUS_Ui_Windows_Scaling.md`, pushed onto **`exp/perf`** only (⛔ that file and any script fix only): the machine,
the sha, the commands verbatim, the curves per column (whole frame, BrainTick, the pre-score, Stress, Threat — ms and speed-up vs 1 thread),
pinned vs unpinned vs CGF-alone, the noise (repeat one column), anything that failed with the log lines. ⛔ No screenshots or recordings in git.

---
_coordinator lane, 2026-11-12_
