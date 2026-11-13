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
| U-1 merge `exp/next-conversions` + `exp/threaded-review` | CE-4530 | in progress | 13ee6e138, c9c71ef07 (merges, no rebase); rails below |
| U-2 the switches, ON by default, `View ▸ Settings ▸ Performance` | CE-4531 | DONE | d487f0afd — `PerformanceSettings` (Fdp.Core), `PerformanceStartup` (runner `Main`), `PerformanceMenu`; rails `PerformanceSettingsTests` 7/7, `PerformanceStartupTests` 12/12 |
| U-3 editor / player / server / separate nodes | CE-4532 | in progress | see D-U3 |
| U-4 the automated proof | CE-4533 | — | |
| U-5 manual sheet | CE-4534 | — | |
| U-6 | CE-4535 | WITHDRAWN (steer) | CE-3445 stays open |
| U-7 design fold | CE-4536 | — | |

## DECISIONS

| id | decision | why (measured) |
|---|---|---|
| D-U2a | precedence: explicit `HROT_*` > `-m all` serial > user file > ON; applied once in the runner's `Main`, library defaults unchanged | the handoff's order; tests and scripts keep choosing explicitly |
| D-U2b | the file is `performance.json` in `UserFiles` (LocalApplicationData/HROT, beside the layouts); a broken file = the defaults, never a throw | a bad file must not stop a node |
| D-U3 | ⛔ **the handoff's premise failed: the threaded node is NOT in the shared node frame.** The staging (World A, `StagingWorlds`, `SimJobQueue`, `SimLink`, the UI read models) is composed inside `EditorSubsystem` only; with the sim worker running, `SubsystemOrchestrator.Draws` (`SubsystemOrchestrator.cs:256`) draws only an `ISimSteppedSubsystem` ⇒ the player's windows would be blank. ⇒ the player resolves **threaded node off, parallel phases ON**; lifting the staging into the shared frame (R-4215) is **CE-3446** | stop the item, never the batch (R-106); the player still gets the per-entity phases and the worker budget, the user's player/server testing is not broken |
| D-U3b | `-m all` serial by default (both switches off); an explicit `HROT_*` still turns them on | the user: "-m all … feel free to turn the parallelism off" |
