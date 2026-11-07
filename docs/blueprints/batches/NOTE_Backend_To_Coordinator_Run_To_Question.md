<!--STATUS state: LIVE · updated: 2026-11-07 · current-answer: the whole note -->
# QUESTION — backend → coordinator (for the user): how fast can *Run to …* go? (L-7, CE-4316 — skipped per R-4156)

**From:** backend, lifecycle handoff L-7 ([design LC-10](../../UX/DESIGN_Scenario_Lifecycle_Review.md)). **Why skipped:** the speed of
*"as fast as the machine can"* is a clock decision on EVERY node of a lockstep cluster — a large blast radius, so it is the user's.

## Measured

| fact | where |
|---|---|
| Preview's time scale stops at 10× | `TransportIcons.cs:19` |
| a STEP is ACK-gated: the master fans out one frame and waits for every node's `FrameStepCompletedEvent`; it retires **at most one step per rendered frame** (it must not run ahead of its slaves) | `MasterSyncController.cs:186` · `DebugApiHost.cs:587-615` (CE-105) |
| the ACK-gated loop already exists for agents: `POST /sim/step {count, dt}` — one step per frame, gated each time, `dt ≤ 0.1 s` (movers and rounds are tuned near 1/60) | `DebugApiHost.cs:600-615` |
| stepped mode is deterministic (sim-time cadence for slow / async modules, jobs harvested per step) | `DESIGN_Time_Architecture.md` §17 |

⇒ *Run to* built on what exists runs at **one 1/60 step per rendered frame ≈ real time** (60 fps), or ≈ 6× with `dt = 0.1` — not
"as fast as the machine can".

## Options

| # | option | speed | blast radius |
|---|---|---|---|
| **A** ⭐ lean | the ACK-gated stepped run (CE-105's loop) **plus the frame cap lifted while it runs**: no vsync / frame sleep on the editor, the map redrawn every Nth frame, the stop conditions checked each step (a time · *a rule fired* · *a phase began* — `ConditionCache`, `PlanConditionCache.PhaseBegins`) | as many frames as the slowest node can step — measured, not guessed | the editor's frame pacing (one place) + every node's loop must not sleep in stepped mode (measure first) |
| B | the master retires SEVERAL steps per frame (a batch of N frames per barrier) | highest | the time protocol on every node (barrier per batch, per-step module cadence) — `DESIGN_Time_Architecture` §17 changes |
| C | Continuous mode at a high scale (e.g. 100×) | fast but non-deterministic: big `dt` breaks movers / rounds tuned near 1/60 | small, but wrong results |

⭐ **Lean A**: it reuses the deterministic stepped run that already exists and changes only how often frames are drawn; B only if A's
measured rate is too slow. ⛔ C is rejected (it changes WHAT happens, not just how fast).

**What I need:** "A approved" (or B / another) — then backend builds it with a cluster rail (row 8).

---
_backend lane, 2026-11-07_

## ⭐ Follow-up `2026-11-07` — A is BUILT, and measured (user: *"A"*)

| measured | |
|---|---|
| the real-editor rail (`PreviewLifecycleRails.Run_to_stops_the_preview_at_the_time_paused`) | ✅ stops at 5.00 s, paused |
| ⚠ the rate, headless cloud editor | **0.3× real time** — 5 s of sim in 18.8 s: a step costs ~4 frames (issue → the master reads it → the nodes ACK → seen landed) ≈ 15 steps/s at 60 fps; the lifted cap helps only a windowed editor |

⇒ A works but is SLOWER than ▶. ⭐ **Lean now: B** — the master retires a BATCH of steps per barrier (N frames, each still 1/60, each node
running them back to back before one ACK), so the cost per step is no longer ~4 frames. Blast radius as stated above (the time protocol on
every node, `DESIGN_Time_Architecture` §17's per-step cadence kept). **What I need:** "B approved" — or keep A as it is.

⚠ Found on the way (not this question): the harness fix un-masked `Exiting_preview_returns_the_clock_to_where_the_preview_started` (CE-3156) — it
fails ALONE: after Stop Preview the clock reads 3.3 s, not 0.
