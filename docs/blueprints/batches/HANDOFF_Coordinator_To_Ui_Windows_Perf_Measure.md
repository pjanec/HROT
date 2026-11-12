<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §11 the aggregate, §12–§17 (on exp/perf) POC 2–4 as built and the managed reads
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target shape; §14.4 (on exp/threaded-poc-3) POC 3's verdict
  - HANDOFF_Coordinator_To_Ui_Windows_POC_2_Measure.md — the previous Windows run on this machine (the method this repeats; its baseline 266 ms)
-->

# HANDOFF — coordinator → the Windows session: MEASURE the combined performance branch and the threaded editor POC 3 (one machine, one session)

**For:** the ui session on the user's Windows machine (Ryzen 7 6800H, 8 cores). **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen.
🔒 User, `2026-11-12`: *"yes, write the Windows handoff"*.

⛔⛔ **Every process HIDDEN** (`HROT_HIDDEN_WINDOW=1` — the scripts set it). ⛔ **Autonomous** (R-4156). ⛔ **Measure only — change no code.**
Run the parts ONE AFTER THE OTHER, never together.

## Part 1 — the simulation, branch EXACTLY **`exp/perf`** (≈ 1 h 30)

`exp/perf` = POC 2 + POC 3 + POC 4 of the simulation's throughput + the managed-reads flip, in ONE branch.

```
git fetch origin exp/perf
git checkout exp/perf                                    # record `git log -1 --format=%h`
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
python scripts/parallel-sim-measure.py --pin --poc3 --out <scratch>/P1-poc3
python scripts/parallel-sim-measure.py --pin --poc4 --out <scratch>/P1-poc4
set HROT_BRAIN_PRESCORE=1
python scripts/parallel-sim-measure.py --pin --threads 1 2 4 8 --mode throughput --out <scratch>/P1-threads
set HROT_BRAIN_PRESCORE=
```

| it answers | |
|---|---|
| ⭐ the CGF frame with EVERYTHING on vs serial (POC 2's Windows run: 266 → 181 ms) | the all-on columns |
| the sampled timer, the threat pre-score, "stamp only on a real change", the cluster-cache fix on real cores | `--poc3`'s columns |
| one worker pool vs two: frame, garbage per frame, threads per node | `--poc4`'s columns |
| the brain's thread curve (POC 2: 101 → 51 ms, flat after 4) | P1-threads |
| SimHost with off-frame egress (POC 2: 92 → 40 ms) | the egress columns |

## Part 2 — the threaded editor, branch EXACTLY **`exp/threaded-poc-3`** (≈ 30 min)

```
git fetch origin exp/threaded-poc-3
git checkout exp/threaded-poc-3                          # record the sha
dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release
python scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only
python scripts/threaded-poc2-measure.py --out <scratch>/P2 --mode 3 --no-guard
```

⭐ Unlike POC 2, this editor does NOT freeze (VM: 0 faults, pause and save work) — so this is the first real number for **the editor's frame while
playing, with the simulation on its own thread**, vs the switch OFF (POC 2's Windows: ~90 ms OFF; the UI's own cost ~16 ms).

| it answers | |
|---|---|
| ⭐ the drawing frame playing and paused, OFF vs `=3` | `[FrameStats]`, the drawing-frame split |
| the sim tick on its thread and the S → A sync per tick | `[POC2]` sync means |
| faults, pause, save | the session log |

## Report

`docs/blueprints/batches/STATUS_Ui_Windows_Perf.md`, pushed onto **`exp/perf`** only (⛔ that file only; raw outputs outside the repository): the
machine and both shas · Part 1 per system and whole frame (ms, fps, speed-up vs serial, garbage, threads) · Part 2 frames and sync both modes ·
the noise (repeat Part 1's serial column once) · anything that failed, with the log lines.

**Done when** the status file is pushed. ⛔ No screenshots or recordings in git.

---
_coordinator lane, 2026-11-12_
