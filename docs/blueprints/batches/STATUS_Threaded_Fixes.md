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
| X-9 an HTTP move dropped; a malformed one answered ok | CE-4575 | DONE | S0 item 5 made points local metres, but three places still used the geodetic form: `EntityOrders.Move` (HTTP), `RtsCommands.OrderPoint` (RTS click) and the designation overlay's reader. The converter also refused `[x, y]`. Now one form (`OrderPoints`), the converter reads 2 or 3 numbers, and `EntityOrders.Order` refuses what the brain would drop (400). Rails `MenuOrdersTests.X9_*`, `S0TypesTests.PointLink_ReadsTwoOrThreeNumbers_*` (both red-proved), `DebugApiOrdersTests.X9_*`; the stale CE-2176 `[lat, lon]` assertion rewritten. ⚠ `DebugApiOrdersTests.CE1078_*` is red at `25cbd799e` without these changes (not X-9's, and red with `HROT_PARALLEL_SIM=0` too ⇒ not X-2's; filed [CE-3447](../Blueprint_Issues_Tracker.md)) |
| X-5 the editor crashes on shutdown | CE-4574 | DONE | `SubsystemOrchestrator.Shutdown` never stopped its sim worker, which kept publishing into worlds being disposed. Now `StopSimWorker` (finish the step, run no other, join with no timeout; it logs after 5 s) runs first, and the runner's `finally` calls it before the API host goes. `StagedPresentation.Dispose` joins its own worker with no timeout, and a host step after `_stop` does nothing. Rail `OneSimWorkerTests.X5_*` (start → play → shutdown × 20) red-proved: without the stop, steps ran during and after the shutdown. ⏳ Windows confirm: `python scripts/stepped-hash.py --threads 4,4` (each run ends with `/shutdown`), with no `AccessViolationException` in the editor logs |

## DECISIONS

| id | decision | why (measured) |
|---|---|---|
| D-X1a | the fixture needed no change: it already runs the production phase's declarations (`BrainTickSystem` → `BrainPhase.Prepare`); every throw was a REAL undeclared read | the stacks: all 17 types read by squad plays / library helpers on a member |
| D-X1b | the reading code's owner declares: toolkit types in `BrainReads`, `RoutePlan` (above the toolkit) in `AiBrainReads`, contributed by `CgfBehaviorSetup.LoadFromAiAssembly` | the one loader every host and test goes through ⇒ the drills and the CGF node declare the same set by construction |
| D-X1c | all as tick-start reads, none as stable | `WeaponState` is written in the phase; a stable read is correct only for a type proven unwritten, and several are set through untyped setters grep cannot see |
| D-X1d | the permanent rail runs the drill class in a CHILD test run | `ParallelReadCheck.Compiled` is read once per process (the JIT drops the check when off), so no in-process switch exists |
| D-X2a | the fix is at the SOURCE (a runner per calling thread), not an xUnit collection over the test classes | two worlds ticked on two threads is a real shape (a second in-process node); a collection would only hide it in the tests |
| D-X0 | `CLAUDE.md`: the coordinator's new line said threading is opt-in; the merged code defaults it ON (R-4224) — corrected (5707845fe, a cross-lane edit) | `PerformanceSettings` defaults, `PerformanceStartup` in `Main` |
| D-X9a | one point codec (`OrderPoints`) for every sender and the overlay's reader | three copies had drifted from S0 item 5; the shared-and-unified rule |
| D-X9b | the converter accepts `[x, y]`, z = 0, instead of the senders being made to always write three numbers | the send-time check (`OrderMenu.MalformedIn`) and the route docs already treat `[x, y]` as a point; the mover puts it on the surface |
| D-X9c | the send path does the receiver's own parse (the DTO with `BehaviorParams.JsonOptions`) before it publishes | a refusal that names the reason beats an ok for an order that is silently dropped; one parse per order, nothing per frame |
| D-X5a | the HOST that owns the worker stops it (`SubsystemOrchestrator`), not each subsystem guarding its own dispose | one owner of the thread ⇒ one place for the order; a subsystem cannot know whether the worker is mid-step |
| D-X5b | the join has no timeout (a log after 5 s) | the old `Join(2000)` gave up and disposed under a running step — the crash itself; a hang at exit is visible and debuggable, a native AV is not |
