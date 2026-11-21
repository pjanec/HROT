<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the item table and DECISIONS
stale-below: nothing
related-designs:
  - docs/DESIGN_Threaded_Presentation.md — §0a (the switches, the manual sheet), §14.8 (-m all)
  - docs/DESIGN_Parallel_Simulation.md — §18 (the per-entity phases the defaults turn on)
-->

# STATUS — Unify the threaded branches, every optimisation ON by default

Handoff: `HANDOFF_Coordinator_To_Backend_Unify_Threaded_Defaults_On.md` (coordinator branch, dispatched 1d459ad0c) ·
steer `STEER_Coordinator_To_Backend_Unify_Order_And_CE3445.md` (U-6 withdrawn; U-1 → U-4, then READY FOR WINDOWS).
Branch `exp/threaded-build`; started at faf06b8dd (marker ae1c1527a).

| item | row | state | evidence |
|---|---|---|---|
| U-1 merge `exp/next-conversions` + `exp/threaded-review` | CE-4530 | DONE | 13ee6e138, c9c71ef07 (merges, no rebase); the three branches' rails green (Core, ModuleHost, Toolkits review/federation/drills, NED, SimHost drills serial + parallel, editor R4/R5 with and without the guard, RB / timeline, AiShared review, Replay Browser); 2 allocation rails red only in the suite run, green alone; CE454 red since CE-3046 (a stale fixture) fixed 6cab9c0a1 |
| U-1b merge `ui` (steer) | — | DONE | 2a3213788 — traffic 83/83, ImGui renderer rails 10/10 under Xvfb, `[FrameStats]` cs-CZ rail, threaded 1 000-man session 11/11 guard 0 |
| U-2 the switches, ON by default, `View ▸ Settings ▸ Performance` | CE-4531 | DONE | d487f0afd — `PerformanceSettings` (Fdp.Core), `PerformanceStartup` (runner `Main`), `PerformanceMenu`; rails `PerformanceSettingsTests` 7/7, `PerformanceStartupTests` 12/12 |
| U-3 editor / player / server / separate nodes | CE-4532 | DONE | defaults per mode: `PerformanceStartupTests` (editor + player threaded, server / cgf / simhost / orchestrator / traffic parallel, `-m all` serial); the player threaded: D-U3 |
| U-4 the automated proof | CE-4533 | DONE | defaults ON, no env vars unless named — see the U-4 table below |
| U-5 manual sheet | CE-4534 | DONE | Threaded Presentation §0a.2 (each step names the lane run that covered it) |
| U-6 | CE-4535 | WITHDRAWN (steer) | CE-3445 stays open |
| U-7 design fold | CE-4536 | DONE | §0a (+ §0a.1 the switches), §14.8 → §15, §15 (CE-3446), Parallel Simulation §18.10, Review §4 |

## DECISIONS

| id | decision | why (measured) |
|---|---|---|
| D-U2a | precedence: explicit `HROT_*` > `-m all` serial > user file > ON; applied once in the runner's `Main`, library defaults unchanged | the handoff's order; tests and scripts keep choosing explicitly |
| D-U2b | the file is `performance.json` in `UserFiles` (LocalApplicationData/HROT, beside the layouts); a broken file = the defaults, never a throw | a bad file must not stop a node |
| D-U3 | ⛔ **the handoff's premise failed — the threaded node was NOT in the shared node frame** (the staging was composed inside `EditorSubsystem`; a threaded player would draw nothing). 🔒 User: *"of course fix it properly now, no duplications and hacks please, we unify share and parametrize"*. ⇒ **CE-3446 DONE** (d89cf3cf4): the bootstrap composes the staged presentation for every UI-drawn node; the player runs the threaded node like the editor (DESIGN §15) | measured: multiplayer cycle 10/10, guard 0 in every player; editor session 11/11, guard 0 |
| D-U3b | `-m all` serial by default (both switches off); an explicit `HROT_*` still turns them on | the user: "-m all … feel free to turn the parallelism off" |

## U-4 — the automated proof *(defaults ON; the user, `2026-11-13`: "keep the rails testing short on necessary level only, no lengthy all scenario tests")*

| gate | result |
|---|---|
| ① stepped gate, 1 / 2 / 4 / 8 threads, Release, the threaded node + parallel from the SETTINGS (`HROT_PARALLEL_SIM` unset) | ✅ STATE EQUAL at simTime 2 (0 types, 0 SimTransform rows differ); each run's start line `threaded node ON · parallel simulation ON · worker threads N (environment)` |
| ② threaded 1 000-man scripted session (the M-7 chain) ×2 | ✅ 11/11 steps twice (U-1b run + the CE-3446 run), guard 0, 0 faults |
| ③ player + server (the full multiplayer cycle: join, deploy, drive, die, respawn, leave, quit, rejoin) ×2 | ✅ 10/10 twice; players `threaded node ON`, the server `parallel simulation ON`; guard 0 in every player (after CE-3446's map-status fix) |
| separate orchestrator + CGF + SimHost processes (`scale-rig.py --men 1000 --seconds 60`, no env vars) | ✅ each node `threaded node off · parallel simulation ON (settings)`, 0 faults · ⚠ the rig's own budgets MISS frame / garbage and **0 / 12 movers moved — identical with `HROT_PARALLEL_SIM=0`** (CGF 797 vs 817 ms a frame, 2.8 fps): the Debug runner at 1 000 men on the 4-core VM (CE-3425), not the defaults |
| `-m all`, no env vars, windowed, 1 000 men live, 60 s playing | ✅ `[Performance] threaded node off · parallel simulation off (-m all (serial))`, no sim worker started (every window draws), 0 faults, simTime 0 → 61.6 s |
| the review in mode 3 | ✅ `R4_` / `R5_` with the guard 4/4 |
| the touched rails, serial and parallel | ✅ U-1's set (drills serial 228/228 and parallel 228/228, …), the CE-3446 suites |
| the settings OFF give today's behaviour | ✅ `PerformanceStartupTests` (the file obeyed; `-m all` / an explicit variable) — the rig's OFF run above is that behaviour on separate nodes |
| ⛔ NOT run, by the user's instruction | the 5-minute soaks (the cycles above cover the player / server path in ~90 s each) and the background unit + system suites |
| CE-3445 (the rare mode-3 divergence) | not reproduced in this batch's gate; stays OPEN, deferred (steer) |
