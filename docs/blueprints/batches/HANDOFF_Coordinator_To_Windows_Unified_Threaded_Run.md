<!--STATUS
state: LIVE — re-dispatched 2026-11-13 (player / server instead of a threaded `-m all`), WAITING for CE-4530…CE-4533 (the unify batch) to be ticked on exp/threaded-build
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Unify_Threaded_Defaults_On.md — the batch this tests (the unified branch, defaults ON, the Settings switches)
  - STATUS_Windows_Threaded_Rerun.md (on exp/windows-threaded-measure) — the previous run: every BEFORE number
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §0a the numbers and the manual test sheet
-->

# HANDOFF — coordinator → WINDOWS session: the UNIFIED threaded build, defaults ON — tested before the user's manual tests

**For:** the Windows session (hidden window, `HROT_HIDDEN_WINDOW=1`, always). **From:** coordinator. **Dispatched at `1d459ad0c`**.
🔒 User, `2026-11-13`: *"windows run and automated test of course first so i can run something what has been tested already at least a bit
automatically"* (R-4224). ⛔ Autonomous, measure only: a failing step gets one retry, then its error and log tail in the status file, and the
next step runs.

**Start when** `CE-4530`…`CE-4533` are ticked `[x]` on `exp/threaded-build`'s tracker. Build its head, **Release**; results on
`exp/windows-threaded-measure` (merge first), status `docs/blueprints/batches/STATUS_Windows_Unified_Run.md`. Name the sha built.

| # | measure | report |
|---|---|---|
| **W-1** · `CE-4537` | ⭐ **defaults ON with NO environment variables** (the user's experience): the editor starts threaded and parallel; `View ▸ Settings ▸ Performance` shows them ON; toggle each, restart, confirm the saved file in the user folder is obeyed, then back ON | each step ok / not, the settings file's path |
| **W-2** · `CE-4538` | the numbers on real cores, before (the rerun) vs after: `threaded-scale-measure.py --threads 1,2,4,6,8,12` and `scale-rig.py --terrain demo-city --men 1000` (separate node processes) with NO env vars (the defaults), at 1 / 4 / 8 / 12 threads via the setting, and with the setting OFF | the before / after tables; what is still serial at 8 threads |
| **W-3** · `CE-4539` | ⭐ **player + server with the defaults ON** (`-m server`, then `-m player` joined to it, hidden windows): 5 min playing, move / fire / a scenario load, 0 faults; ⭐ separate orchestrator + CGF + SimHost processes with NO environment variables run the parallel phases (W-2's scale rig, read their logs); `-m all` starts serial, every window drawn (the user's AI-debug mode) | each step ok / not; the player's frame and the server's tick |
| **W-4** · `CE-4540` | stepped determinism: `stepped-hash.py --threads 1,4,12`, both kernels; and CE-3445's long saved-world series on real cores (in the background, the rest meanwhile) | exit codes; any divergence with its saved worlds |
| **W-5** · `CE-4541` | ⭐ **the manual test sheet (§0a), run once, start to end** — so the user starts from something already walked through | every step ok / not, with the log line for any not |

---
_coordinator lane, 2026-11-13_
