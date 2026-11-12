<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §11 the aggregate; §12 (on exp/parallel-sim-2) POC 2 as-built
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target shape; §14.3 (on exp/threaded-poc-2) POC 2's verdict
  - HANDOFF_Coordinator_To_Ui_Windows_Parallel_Sim_Measure.md — POC 1's Windows run (the same machine, the method this repeats)
-->

# HANDOFF — coordinator → the Windows session: MEASURE BOTH POC 2 experiments on 8 real cores (one machine, one session)

**For:** the ui session on the user's Windows machine (Ryzen 7 6800H, 8 cores). **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen.
🔒 User, `2026-11-12`: *"if we need testing on real windows pls write handoff (best one for both tests - same machine)"*.

⛔⛔ **Every process HIDDEN** — `HROT_HIDDEN_WINDOW=1` (both scripts set it); no window may appear. ⛔ **Autonomous** (R-4156). ⛔ **Measure only —
change no code.** Run the two parts ONE AFTER THE OTHER (never both at once — they compete for the cores).

## Part 1 — simulation throughput, branch EXACTLY **`exp/parallel-sim-2`** (≈ 1 h)

```
git fetch origin exp/parallel-sim-2
git checkout exp/parallel-sim-2                 # record `git log -1 --format=%h` (at least b06adac9f)
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
python scripts/parallel-sim-measure.py --pin --poc2 --out <scratch>/P1
set HROT_BRAIN_PRESCORE=1
python scripts/parallel-sim-measure.py --pin --threads 1 2 4 8 --mode throughput --out <scratch>/P1-threads
set HROT_BRAIN_PRESCORE=
```

| it answers | from |
|---|---|
| ⭐ the brain pre-score's gain on 8 cores (VM, 2 threads: BrainTick 92 → 57 ms) and its thread curve | `thrps` / `rec-all` columns; the P1-threads run |
| the per-query timer's cost (VM: ~13 ms of the brain) | the timer-off column |
| Q-1 (stamp only on a real change) and Q-2 (off-frame egress) on real cores | their columns |
| the whole CGF frame: serial vs everything on | `rec-all` vs serial (the POC 1 Windows baseline was 242 ms) |

## Part 2 — the threaded editor, branch EXACTLY **`exp/threaded-poc-2`** (≈ 30 min)

```
git fetch origin exp/threaded-poc-2
git checkout exp/threaded-poc-2                 # record the sha (at least 23dc7797d)
dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release
python scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only
python scripts/threaded-poc2-measure.py --out <scratch>/P2 --no-guard
python scripts/threaded-poc2-measure.py --out <scratch>/P2-guard
```

⚠ Known before you start (POC 2's verdict): with `HROT_THREADED_KERNEL=2` the UI world FREEZES after a few syncs (a cached query faults the
simulation tick). ⇒ the threaded numbers measure the **main thread's own cost** (draw + panels without the simulation) and the **sync cost**
(the VM: tables ≈ 9 ms, rows + map + events ≈ 3–4 ms) — NOT a working threaded editor. That is still the number we lack: the VM draws in
software (600–730 ms a frame), so the main thread's real cost is unknown.

| it answers | |
|---|---|
| the drawing frame playing, switch OFF vs `=2` | `[FrameStats]` |
| the S → A sync per tick on real cores | `[POC2]` sync means |
| the faults and the guard's list | the `--guard` run |

## Report

`docs/blueprints/batches/STATUS_Ui_Windows_POC_2.md`, pushed onto **`exp/parallel-sim-2`** only (⛔ that file only, no code; raw outputs stay
outside the repository). In it: the machine and both shas · Part 1's columns (per system and whole frame, ms and fps, the speed-up vs serial) ·
Part 2's frames and sync means both modes · the noise (repeat Part 1's serial column once) · anything that failed, with the log lines.

**Done when** the status file is pushed. ⛔ No screenshots or recordings in git.

---
_coordinator lane, 2026-11-12_
