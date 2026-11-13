<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Threaded_Scaling.md (on exp/windows-threaded-measure) — ⭐ the FIRST run (before the conversions): the numbers this compares against, and the commands it used
  - STATUS_Mode_And_Serial_Systems.md (on exp/threaded-build) — what changed since: the serial systems converted, the serial work outside the systems cut, the mode set by what is running
  - STATUS_Windows_Findings.md (on exp/threaded-build) — F-1 the committed stepped gate, F-3 (the "not repeatable" was the hash counting padding), F-5 no decimal commas
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §0a the numbers it updates
-->

# HANDOFF — coordinator → WINDOWS session: RERUN on real cores after the conversions (Release, 1 … 12 threads)

**For:** the Windows session (hidden window, `HROT_HIDDEN_WINDOW=1`, always). **From:** coordinator. **Dispatched at `58c58f3e5`**.
🔒 User, `2026-11-13`: *"windows rerun"*. ⛔ Autonomous, measure only: a failing step gets one retry, then its error and log tail in the status
file, and the next step runs — never stop for a question.

## Where to work

Check out **`origin/exp/threaded-build`** at its head (≥ `4070ca7fe`), build **Release** (`dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release`).
Write results on **`exp/windows-threaded-measure`** (merge `exp/threaded-build` into it first), status file
`docs/blueprints/batches/STATUS_Windows_Threaded_Rerun.md`. ⛔ Never commit to `exp/threaded-build`. ⛔ No screenshots in git. Same machine,
same power plan, nothing else heavy running; name the head sha you built.

## The measurements — the first run's commands, so the two compare

| # | measure | command | report — ⭐ BEFORE (first run) vs AFTER, side by side |
|---|---|---|---|
| **V-1** · `CE-4511` | ⭐ the threaded editor, 1 000-man demo-city (the same `scale-rig-demo-city-1000-2s`), `HROT_THREADED_KERNEL=3` **and `HROT_PARALLEL_SIM=1`** (every phase), 1 / 2 / 4 / 6 / 8 / 12 threads | `python scripts\threaded-scale-measure.py --out <dir> --threads 1,2,4,6,8,12 --dll <Release dll>` (the branch's own script now reads `[FrameStats]` itself — no scratch parser) | sim tick · UI frame playing / paused · BrainTick · the group · EQS · CPU cores used · working set · staging MB |
| **V-2** · `CE-4512` | ⭐ what is STILL serial: each system's ms at 1 and 8 threads, the group's members one by one | from V-1 | the ranked table, % of the tick at 8 — and which of the first run's top five (Stress, Callout, Threat, WeaponDispatcher, StaffComposer) moved |
| **V-3** · `CE-4513` | separate node processes, `HROT_PARALLEL_SIM=1` at `HROT_SIM_THREADS` 1 / 2 / 4 / 8 / 12, and once with the switch off | `python scripts\scale-rig.py --terrain demo-city --men 1000 --json <f>` per run | CGF work ms + fps, BrainTick, top systems (the two `CycloneEgressSystem` passes named), cores, SimHost work |
| **V-4** · `CE-4514` | ⭐ **stepped determinism on Windows with the committed gate** (it hashes FIELD bytes, not padding — the first run's "not repeatable" was the padding) | `python scripts\stepped-hash.py --threads 1,4,12` with `HROT_PARALLEL_SIM=1`, then `--kernel 0` (switch off) twice | exit code and the per-type verdict; on a difference `--rows <T> --bytes` names the entities and the bytes |
| **V-5** · `CE-4515` | ⭐ **language-neutral on a Czech machine** (R-4221): the editor and a node started under this machine's locale print `[FrameStats] … mean 8.9 ms` with a DOT; the opt-out's log line at start | read the logs of V-1 / V-3 | the two log lines, verbatim |

## Report (top of the status file)

The head sha built · V-1 before / after table · V-2 ranked list · V-3 per node, before / after · V-4 verdict · V-5 the two lines · anything
surprising, one line each.

---
_coordinator lane, 2026-11-13_
