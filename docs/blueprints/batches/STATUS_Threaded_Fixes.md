<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the item table and DECISIONS
stale-below: nothing
related-designs:
  - docs/DESIGN_Parallel_Simulation.md — §18.2a the declarations as built
  - docs/DESIGN_Threaded_Simulation_Presentation.md — §15 the threaded node
  - docs/blueprints/DESIGN_Time_Architecture.md — §17 stepped mode
-->

# STATUS — the threaded branch's fixes (X-1 … X-12)

Handoff: `HANDOFF_Coordinator_To_Backend_Threaded_Fixes.md` (coordinator, re-dispatched `c1a231dc7`, frozen there). Branch `backend`; merged
`origin/coordinator@36dde0e06` (5decc3439), started marker 8c3e6868c.

| item | row | state | evidence |
|---|---|---|---|
| X-1 declared reads, the drill fixture + squad code | CE-7035 | DONE | 4ffbbcd7c, 7c2e82f4c, 72552251d — 78 → 11 → 2 → 0 reds; drills 244 / 244 with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` and 244 / 244 with `HROT_PARALLEL_SIM=0`; `DrillsUnderTheReadCheckTests` red-proved (`DoorState`) |
| X-2 the parallel suites sharing a run | CE-7036 | DONE | the phase runner was one per job type per PROCESS (`Runner<TJob>.Instance`) ⇒ per calling thread; combined filter 9 red before, 297 / 297 × 5 after; `ParallelEntitiesTests.X2_*` red-proved (the shared runner throws) |

## DECISIONS

| id | decision | why (measured) |
|---|---|---|
| D-X1a | the fixture needed no change: it already runs the production phase's declarations (`BrainTickSystem` → `BrainPhase.Prepare`); every throw was a REAL undeclared read | the stacks: all 17 types read by squad plays / library helpers on a member |
| D-X1b | the reading code's owner declares: toolkit types in `BrainReads`, `RoutePlan` (above the toolkit) in `AiBrainReads`, contributed by `CgfBehaviorSetup.LoadFromAiAssembly` | the one loader every host and test goes through ⇒ the drills and the CGF node declare the same set by construction |
| D-X1c | all as tick-start reads, none as stable | `WeaponState` is written in the phase; a stable read is correct only for a type proven unwritten, and several are set through untyped setters grep cannot see |
| D-X1d | the permanent rail runs the drill class in a CHILD test run | `ParallelReadCheck.Compiled` is read once per process (the JIT drops the check when off), so no in-process switch exists |
| D-X2a | the fix is at the SOURCE (a runner per calling thread), not an xUnit collection over the test classes | two worlds ticked on two threads is a real shape (a second in-process node); a collection would only hide it in the tests |
| D-X0 | `CLAUDE.md`: the coordinator's new line said threading is opt-in; the merged code defaults it ON (R-4224) — corrected (5707845fe, a cross-lane edit) | `PerformanceSettings` defaults, `PerformanceStartup` in `Main` |
