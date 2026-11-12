<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS the experiment: §7 determinism as a mode (R-4207), §8 interleave / quantize, ⭐ §10 after the experiment (R-4208 cover claims; why the brains write back unchanged bytes)
  - STATUS_Backend_Parallel_Simulation_Experiment.md (on `backend`) — the VM's results and the verdict this run completes
-->

# HANDOFF — coordinator → the Windows session: MEASURE the parallel simulation on 8 real cores

**For:** the ui session on the user's Windows machine (8 physical cores). **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there.
🔒 User, `2026-11-12`: *"let's write a handoff to the Windows machine performance test, Make sure you tell it the exact name of the branch to use."*

⛔⛔ **Every process HIDDEN** — `HROT_HIDDEN_WINDOW=1` (the script sets it for every node); ⛔ no window may appear, the user works on this machine.
⛔ **Autonomous — never stop for a question** (R-4156). ⛔ **Measure only — change no code.**

## ⭐ The branch — exactly this one

**`exp/parallel-sim`** — the parallel-simulation experiment, at its head (at least `d424929b0`). ⛔ Not `ui`, not `backend`, not
`exp/threaded-kernel`, not `exp/threaded-analysis`. Do not merge anything into it.

```
git fetch origin exp/parallel-sim
git checkout exp/parallel-sim
git log -1 --format=%h                     # record this sha in the status file
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
```

## What to run — in this order (each run ~3 min per column; run them in the background)

| # | command | what it answers |
|---|---|---|
| **W-1** | `python scripts/parallel-sim-measure.py --pin` | ⭐ the converted systems (threat, stress, callouts) and the whole CGF frame at 1 000 men: serial vs 1 / 2 / 4 / 8 threads, Deterministic mode — the real speed-up the VM (2 threads, ±15 % noise) could not show |
| **W-2** | `python scripts/parallel-sim-measure.py --pin --threads 6 --mode throughput` | the same in Throughput mode at the default budget (cores − 2) |
| **W-3** | `python scripts/parallel-sim-measure.py --pin --content` | ⭐ D-5 "commit by content" live and recorded: what its 64 KB compare costs per frame, and what it saves the recorder (design §10.2 — decides whether the blueprint emitter fix is needed too) |
| **W-4** | repeat W-1's serial and 6-thread columns once | the run-to-run noise on this machine — a gain smaller than the noise is not a gain |

The script prints the systems' ms per column and leaves the raw reports in `parallel-sim-results/` (⛔ not in git).

## Report

Write `docs/blueprints/batches/STATUS_Ui_Windows_Parallel_Sim.md` and push it **onto `exp/parallel-sim`** (the results belong with the experiment) —
⛔ that file only, no code. In it:

| # | row |
|---|---|
| 1 | the machine (CPU, cores / threads), the branch sha, the commands verbatim |
| 2 | W-1 / W-2: per system and the whole frame, ms per column; the speed-up vs serial; fps |
| 3 | W-3: the frame with and without D-5, the compare's own ms, the recorder's bytes per frame both ways |
| 4 | W-4: the noise (the same column twice) |
| 5 | anything that failed or looked wrong (a node that did not start, a column with no result, a fault) — with the log's lines |

**Done when** the status file is pushed. ⛔ No screenshots or recordings in git.

---
_coordinator lane, 2026-11-12_
