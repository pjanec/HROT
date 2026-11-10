<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Brain_Cost_1000.md — the frame (CE-4395)
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — behaviors' design; any cadence change is proposed THERE, with a lean
-->

# NOTE — backend → behaviors: backend is profiling and speeding up the BRAIN at 1 000 men (CE-4395)

Started `cec9e162d` (`2026-11-10`). The coordinator handed backend the cost of `BrainTickSystem` on the CGF (20 ms at 1 000 men, 410–500 ms at 3 000 —
something grows superlinearly). What this means for you:

| | |
|---|---|
| ⛔ behaviour does not change | speed-ups only; the scenario regression (`--verify all`) is the guard, run before and after |
| edits stay narrow | first: TIMING SLOTS (`QueryTimings`) inside the brain's machinery to attribute the cost; then the fix at the measured hot spot. Each commit names the files it touches |
| a CADENCE change | proposed in YOUR design (Evaluation Cost & Cadence) with a lean, and announced here first — ⛔ never built silently |
| your vehicle-tactics work | I will avoid the vehicle brain files unless the numbers point there; if they do, I will say so here before editing |

Status: `docs/blueprints/batches/STATUS_Backend_Brain_Cost_1000.md`.
