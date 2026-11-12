<!--STATUS
state: LIVE — the overnight build is RUNNING (branch exp/threaded-build)
updated: 2026-11-12
current-answer: the morning report (top) · then NEXT (the resume point after a compaction)
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — §8 the decision list; §1 UI writes; §2 run controls; §3 HTTP; §4 gizmo move; §7 the review
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §13 shared state, §14 target picture, §14.6 gizmo block
  - ../../DESIGN_Parallel_Simulation.md — §18–§18.2 the per-entity rule
  - HANDOFF_Coordinator_To_Backend_Overnight_Threaded_Build.md — the handoff (dispatched at e9638d40b, re-stamped f28f53609)
-->

# STATUS — the overnight threaded build (branch `exp/threaded-build`)

**Branch:** `exp/threaded-build` from `exp/integration@406659433`, `coordinator@f28f53609` merged (the handoff, R-4216 / R-4217, inventory §8.2a).
Started marker `1cd3c2dbc`.

## 1. DONE

| item | state | commit |
|---|---|---|
| N-1 `CE-4463` the one sim job queue | 🔄 seam built + rails (`SimJobQueue`, `SimJobs.Run`, `SimLink`); editor wiring next | `5ff4efba2` |
| N-3 `CE-4465` two staging buffers + publish queue | 🔄 seam built + rails (`StagingWorlds`); editor wiring next | `5ff4efba2` |
| N-2 `CE-4464` tick context + current entity on every pool batch | ✅ `AccessTrace.CaptureFlow/SwapFlow` restored per batch on a worker (both lanes); `ParallelEntities` + `ForEachParallel` set `ParallelReadCheck.Current`; the phase flag per thread; no current entity ⇒ throw | (this commit) |

## 2. GATES

| gate | command | result |
|---|---|---|
| N-1 + N-3 rails + TwoWorlds | `dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter "FullyQualifiedName~SimJobQueueTests\|FullyQualifiedName~StagingWorldsTests\|FullyQualifiedName~TwoWorldsTests"` | 14 / 0 / 0 |
| N-2 rail (switches on) | `HROT_ACCESS_TRACE=1 HROT_READ_CHECK=1 HROT_ACCESS_TRACE_OUT=<f> dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter FullyQualifiedName~PoolTickContextTests` | 1 / 0 / 0 |
| switch-off neighbours (0 B tracer, census, parallel, sync) | `… --filter "…PoolTickContextTests\|…ParallelEntitiesTests\|…AccessTraceOffTests\|…EntityCensusTests\|…ParallelSyncTests\|…ChunkIterationTests\|…PureReadsTests"` | 31 / 0 / 2 (the two env-gated rails skip) |
| red-proofs (each a compiling mutation, reverted) | `scratchpad/redproof.sh` | ① torn tick: publish into the pulled buffer, no claim ⇒ red · ② drain only the pulled tick's packet ⇒ red · ③ spin instead of skip ⇒ red · N-1 LIFO drain ⇒ red · N-2 the worker's flow restore removed ⇒ red |

## 3. DECISIONS

| # | problem | options | chosen | why | where |
|---|---|---|---|---|---|
| D-1 | where the seams live (R-4215: no host handled differently) | in `EditorSubsystem` (the prototype) · in `Fdp.Core` shared by every host | ⭐ `Fdp.Core.Threading` (`SimJobQueue`, `StagingWorlds`) — any host / kernel instantiates them; one queue per kernel (N-13) | built ONCE, reachable from every host and every UI write site (they hold a world, nothing else) | `FDP/Engine/Fdp.Core/Threading/` |
| D-2 | how a UI write site reaches the queue with no new plumbing | pass the queue to 133 sites · a static · ⭐ a link on world A | ⭐ `SimLink` — a node-local managed singleton on A; `SimJobs.Run(world, job)` posts when A carries it, runs at once otherwise | a call site already holds A; switch OFF ⇒ no link ⇒ today's code path, unchanged | `SimJobQueue.cs` |
| D-3 | events / replies of a publish the UI never pulls (it pulls only the LATEST buffer) | keep them in the buffer (lost when skipped over — the first cut did, found while writing the rail) · ⭐ tick-tagged packets in their own FIFO | ⭐ packets: the UI drains every packet up to the tick it pulls | "every event once, in order, skipped ticks included" (handoff N-3) | `StagingWorlds.Pull` |
| D-4 | a row copy stamps the destination chunk with ITS OWN version — with two alternating sources an equal version from the other source would make the table sync skip a stale chunk | force-copy chunks · a sentinel version · ⭐ rows copy only types OUTSIDE the table mask | masked types arrive whole with the table sync; out-of-mask types are never table-synced, so their versions decide nothing | `EntityRepository.SyncRowsFrom(…, skipTypes)` |
| D-6 | the read check's "parallel phase" flag was process-wide | keep global · ⭐ per thread | the UI thread reading A while the sim runs a phase would have every A read treated as cross-entity; ParallelEntities sets it per batch | `ParallelReadCheck.Active` |
| D-7 | the editor's non-UI half in mode 3 | keep it on the UI thread (races with the sim) · ⭐ SIM side | ⭐ the editor logic / scenario session, the debug-API service, the mission routes, the file service run OVER S on the sim thread (`SimStep`); the UI gets decorators whose verbs are jobs (`ThreadedEditorLogic`, `ThreadedScenarioSession`) | one writer of S; the UI never touches a sim object | `EditorSubsystem.SimStep` |
| D-8 | the HTTP routes (N-8) | one queue on the UI thread · ⭐ two queues by side | ⭐ the host marks each request's side from its template (`DebugApiRouteSides`, a prefix list); `RunOnMainThread` routes UI-side requests to the UI queue; the rest drain on the sim thread between ticks | §3's split, with one classification function instead of 237 edits | `MainThreadJobQueue.UiQueue` |
| D-9 | ⭐ UI READS of sim-side state (the save gate, the cluster view, the time readouts) — the first run CRASHED on one (`ScenarioSaveGate.CanSave(S)` from the toolbar: a query rebuilt under the reader) | pass reads through ("benign" — a shortcut, rejected) · ⭐ published views | ⭐ (a) every BUS is a sim / UI PAIR carried by the staging (events down per tick exactly once, the UI's publishes up between ticks) ⇒ the UI keeps its OWN read models (`ClusterView`, the time transport, the door) over its mirror of the orchestration bus; (b) values only the sim can compute (the save gate, args) are computed after each tick on the sim and published (a readout) | the read models are already event-driven, so a mirror bus makes them thread-owned by construction | `StagingWorlds` bus pairs |
| D-10 | a mission facade read by the UI (Orders tab) AND used by the sim-side routes | one object, two threads · ⭐ one per world | the UI's over A, the sim's over S (each polls its own acks) | a facade is bound to the world it serves | `EditorSubsystem` `_simMissionService` |
| D-11 | the strict guard | the guard flagged an S read only in the presentation's trace context · ⭐ with no lock, ANY off-thread read of S | `WorldGuard.Strict` in mode 3 | it lists every remaining crossing with its stack | `WorldGuard.cs` |
| D-12 | the UI session's door, run choice and HTTP | one door for both threads · ⭐ one door per thread-owner | the UI's `ThreadedScenarioSession` owns a door over its own raw verbs (jobs) and the UI run choice binds it (prompts on the UI thread); the sim session keeps its door for HTTP (routes pass their refusing asker); each transport's run choice installs its save interception on its own session | prompts belong to the UI thread; a refusing asker belongs to HTTP — as before, now each on its own thread | `ThreadedScenarioSession`, `EditorSubsystem` |
| D-13 | the editor's UI commands (tool, select, centre, rename, property commit) in mode 3 | post a job publishing on S's bus · ⭐ publish on the UI world's bus | they are EVENTS: published on A's bus they ride UP with the staging today, and are read in place once the interaction block runs over A (R-4211) | `ThreadedEditorLogic` |
| D-14 | the session readout | recompute per tick · ⭐ only on change | `ReadIfChanged` allocates only when a value the UI reads changed; the migration sidecars (a file read) only when the loaded scenario changed | hot path R-2084 | `ThreadedScenarioSession.ReadIfChanged` |
| D-15 | finding who writes a NaN position | read 1 000 lines · ⭐ an instrument | `NonFiniteTransformCheck` (`HROT_NAN_CHECK=1`, off = folded away): after each system, the first system that leaves a non-finite `SimTransform` is named with the entity's components — found `CarKinematicsSystem` in one run | `FDP/Engine/Fdp.ModuleHost/Diagnostics/NonFiniteTransformCheck.cs` |
| D-5 | `SimLink` needs a component id | — | `GlobalComponentIds.SimLink = 475` (free in every lane and in every test) | — | `GlobalComponentIds.cs` |

## 4. BUGS FIXED

| bug | test that catches it |
|---|---|
| ⭐ **cars (and every passenger they carried) went to a NaN position on their first step — PRE-EXISTING** (switch OFF too; hidden in Release builds, where the `DistanceToContext` assert is compiled out — every Debug end-to-end run aborted): a Catmull-Rom path whose first two waypoints coincide (it starts at the vehicle's own position) has a zero tangent there; the Hermite sampler normalised it unguarded ⇒ NaN heading × 0 speed ⇒ NaN desired velocity ⇒ NaN speed ⇒ NaN position. Fix: the curve tangent falls back to the chord, then +X (the linear branch's guard) — `TrajectoryPoolManager.SampleTrajectory` | `DegenerateCurveTangentTests` |
| a ROW of a type the destination world never registered was silently dropped ("the next table sync brings it" — never, for an out-of-mask type) | `StagingWorldsTests.RowsCopyOnlyTypesOutsideTheMask_…` |

## 5. OPEN
| item | why open | what it needs |
|---|---|---|
| the film director in mode 3 | it ticks on the UI thread but writes checkpoints / restores / steps on the world and the kernel directly (`EditorSubsystem` director host) | a split: its run-control half as sim jobs with a pending state, its camera / capture half on the UI |
| the review source (`ReviewSourceOf`) in mode 3 | it clears and reads the world it is given (A) | the same split |
| Run to … from the toolbar (N-5) | the toolbar holds the concrete `RunToController` (sim side in mode 3) and calls it from the UI thread | an `IRunTo` seam; a threaded implementation (commands as jobs, status from the readout) |
| undo / redo (N-7) | `WorldUndoRecorder` is installed on A and the sim session's Saved / Opened events reach `EditHistory` (UI) from the sim thread | the recorder on S, brackets on the sim thread, the history's UI half fed by replies |

## 6. NUMBERS
(N-16, later)

## RULES FROM THE USER DURING THE NIGHT
🔒 *"when you are making a Decision, Take the one that follows the best practices and fits the engine architecture. Do not do any shortcuts, no hacks please."* · *"run only focused test suites … only those that are really necessary for the trouble you are testing."*

## NEXT (the resume point)
⭐ IN PROGRESS: the editor wired (jobs + staging, no lock; builds; session run 2: load / select / focus / play OK, 0 job faults) — then a Debug
assert (`StandardInputs.DistanceToContext` NaN) killed it; a switch-OFF Debug control run decides whether it predates the work. Next: D-9 (bus pairs
in `StagingWorlds`, the UI's own ClusterView / transport / door over the mirror, the session readout) → §4 the gizmo block on the UI thread
(R-4211; removes the SelectionRequestSystem / camera guard hits) → N-14 commit (written: `WindowsScheduling.cs` + rail, not built yet) →
wire N-1 + N-3 into the editor's mode-3 worker loop (drain jobs between ticks, publish after
each tick, pull at the UI frame start, remove `ThreadedKernelGate`'s world lock from that path) → N-4 orchestration bus → N-5 run controls → N-6 …
