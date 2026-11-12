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
| N-1 `CE-4463` the one sim job queue | ✅ `SimJobQueue` + `SimLink` + `SimJobs.Run`; drained by the editor's sim loop between ticks | `5ff4efba2` · `73cf90133` |
| N-2 `CE-4464` tick context + current entity on every pool batch | ✅ | `0434820c2` |
| N-3 `CE-4465` two staging buffers + publish queue, no lock | ✅ `StagingWorlds` (bus pairs, packets, rows outside the mask); the editor's mode-3 loop runs with no lock | `5ff4efba2` · `73cf90133` · `b29005891` |
| N-4 `CE-4466` the orchestration bus | ✅ a sim / UI pair: the UI's `ClusterView`, transport and door over its mirror (D-9) | `b29005891` |
| N-5 `CE-4467` run controls | ✅ one transport / run choice / door per thread-owner; Run-to as `IRunTo` + `ThreadedRunTo` (readout) | `0af7681c0` |
| N-6 `CE-4468` UI writes through N-1 | 🔄 the editor verbs, gizmo commits (the shared write router), the camera, HTTP select; ⚠ OPEN: the film director, the review source (§5) | `73cf90133` · `0af7681c0` · `53db71fbf` |
| N-7 `CE-4469` undo / redo on S | ✅ the recorder on S, the ONE history on the UI (`ThreadedUndoSink`, D-19) | `53db71fbf` |
| N-8 `CE-4470` the HTTP routes | ✅ two queues by side (`DebugApiRouteSides`, D-8); UI-side routes read the SHOWN world | `73cf90133` · `0af7681c0` |
| N-10 `CE-4472` the per-entity rule in the repository | ✅ last-tick view (kernel, before the Simulation dispatch), structural changes + publishes deferred to the batch, cross writes checked | `826d715c5` |
| N-11 `CE-4473` the brains per-entity parallel | 🔄 the brain's parallel arm + its shared state (D-21…D-30); gate rail GREEN at 1/2/4/8 and across runs; ⚠ the rail's content does not reach the deferral paths — the full CGF content next (survey) | (this commit) |
| N-14 `CE-4476` the Windows EcoQoS opt-out | ✅ `WindowsScheduling` (process + sim thread + pool, QuickEdit off) | `73cf90133` |

## 2. GATES

⭐ **End-to-end milestone (session 4, `run-session.sh`, Debug, mode 3, strict guard, 1 000-man demo-city rig):** load → select → focus → play →
pause → step ×5 → 3-D view → pause → view off — **all OK, 0 job faults, 0 worker faults, no crash; 0 skipped publishes; publish ~4 ms, pull
~4–5 ms**; guard 17 distinct hits (the D-17 / D-18 / SelectionStore fixes target them).

⭐⭐ **Session 6 (same rig, + spawn, component edit, delete, steps, two orders, an Edit-state move, preview stop, save): every step OK, no crash,
0 faults, and the STRICT world guard at 0 DISTINCT HITS** (no read of S off the sim thread, no write of A by the sim, no UI write lost to a
pull). The one `[FAIL]` is a correct refusal: the unsaved check is meaningful only in Edit, and the script asks while a Preview runs.

| gate | command | result |
|---|---|---|
| N-1 + N-3 rails + TwoWorlds | `dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter "FullyQualifiedName~SimJobQueueTests\|FullyQualifiedName~StagingWorldsTests\|FullyQualifiedName~TwoWorldsTests"` | 14 / 0 / 0 |
| N-2 rail (switches on) | `HROT_ACCESS_TRACE=1 HROT_READ_CHECK=1 HROT_ACCESS_TRACE_OUT=<f> dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter FullyQualifiedName~PoolTickContextTests` | 1 / 0 / 0 |
| switch-off neighbours (0 B tracer, census, parallel, sync) | `… --filter "…PoolTickContextTests\|…ParallelEntitiesTests\|…AccessTraceOffTests\|…EntityCensusTests\|…ParallelSyncTests\|…ChunkIterationTests\|…PureReadsTests"` | 31 / 0 / 2 (the two env-gated rails skip) |
| ⚠ PRE-EXISTING red (not this work) | `CgfLogicPackTests.CE454_TheSquadLayerRunsThroughTheRealCgfPack` — expected 2 merged contacts, got 0 | reproduced IDENTICALLY at the base `fec80a028` in a clean worktree (`/tmp/wt-base`) |
| N-11 gate | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter FullyQualifiedName~ParallelBrainsDeterminismTests` | 1 / 0 — same `ContentHash` at 1/2/4/8 threads and a repeat run |
| N-11 focused | Fdp.Core per-entity + parallel (28/0) · Toolkits behaviour/utility/EQS (805/0) · SimHost brains/stress/SOP/rules (218/0) · the add-once helpers' classes (Toolkits 251/0, SimHost 462/0) | green |
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
| D-16 | the gizmo commit (drag / rotate / place) in mode 3 (U-3) | a per-gizmo command · ⭐ the shared write router | `EntityWriteRouter.For(world)` — the factory EVERY host passes — returns, for a threaded presentation world, a writer that runs S's own router on the sim thread and answers `Requested`; the preview stays a ghost on A (the interaction block re-applies it after each pull); a refused write leaves S untouched, so the next pull snaps it back BY CONSTRUCTION | `EntityWriteRouter.SimThreadWriter` |
| D-17 | the UI-side gizmo managers drew `_breakpointManager.ActiveView` (the SIM world) — §4's predicted hazard, found by the guard | hand them the breakpoint manager · ⭐ a presentation view provider | `PresentationViewProvider(() => A)`: a data-breakpoint hold pauses the sim, whose held state the staging publishes like any tick | `IActiveViewProvider.cs` |
| D-18 | UI-side routes (selection, view) used the service's world (S) | ⭐ the SHOWN world | the service's own doc says "on the shown world": `PresentationWorld` / `PresentationMap` for those routes | `DebugApiService` |
| D-19 | undo / redo in mode 3 (N-7, U-1) — the ONE history also holds the header / AI-edit / take steps (UI state) | move the history to S · ⭐ the history stays the UI's; the recorder runs on S | the recorder is installed on S (the before-snapshot taken on the sim thread, where every write now happens) and hands each step to the UI history through the publish queue (`ThreadedUndoSink`); the step's undo / redo are sim jobs restoring S; availability is a UI-side token cleared by the ring's new `Evicted` event through the same queue; Saved / Opened (raised on the sim thread) reach the history the same way | one history the user sees, one writer of S | `ThreadedUndoSink`, `WorldSnapshotRing.Evicted`, `IUndoStepSink` |
| D-20 | the map camera's settled pose (the last guard hit) | a per-frame mailbox · ⭐ a sim job | it commits only once the view SETTLED (a user-action rate) ⇒ `SimJobs.Run` with the ownership checks on the world it writes; single-world hosts unchanged | `MapCameraLayer.Commit` |
| D-21 | ⭐ the brains as a parallel phase (N-11) | everything inside the per-entity job · ⭐ split by what each step touches | ① SERIAL pre-pass in tier order: who is due, the finished guard, the relayout restart, the cadence reader (this system's own records) · ② the PHASE: each unit's tree / HSM / blueprint on its batch thread · ③ SERIAL post-pass in batch order: finish, cadence bookkeeping, fault finish — the serial walk's own order | the system's dictionaries stay single-threaded by construction; the result is independent of the thread count | `BrainTickSystem.RunParallel` |
| D-22 | a cadenced brain reads its events through ITS window — one field on the world | lock · ⭐ per thread | inside a phase `OpenStepWindow` is per batch thread; the history's window buffers are per LANE (lane 0 serial, one per batch thread) | two threads filling one shared window buffer would hand each other's events back | `EntityRepository.View.cs`, `EventStreamHistory` |
| D-23 | a brain writes its OWN child sensors (other entities) | commands · ⭐ its parts are its own | `PartMetadata.ParentEntity == current` ⇒ own under the rule (read live, written directly) — the census's own-part class (§18.1a) | `EntityRepository.IsOwnPartOfCurrent` |
| D-24 | brain code records into `view.GetCommandBuffer()` (the frame's shared buffer) | pass a buffer everywhere · ⭐ the world answers with the batch's | inside a phase over a world, its command buffer IS the batch's (merged in batch order) | `ISimulationView.GetCommandBuffer` |
| D-25 | a child sensor created in a phase | create + return the placeholder (callers write it ⇒ crash) · ⭐ the existing deferred path | `EqsChildSensor.Ensure` defers in a phase and returns Null (every caller retries next tick — the path non-repository views always had), IDEMPOTENT while pending (per-thread pending list; pending part ids reserved); its lifetime counter a concurrent map | F-8 | `EqsChildSensor` |
| D-26 | brain code registers types LAZILY mid-tick ("a world that never faults pays nothing") and builds per-world indexes "on first use this frame" (the rule tree, the cadence wake table) | lock the type tables · ⭐ before the phase | `BrainPhase`: owners contribute their registrations (once per world), frame preparation (every phase) and their cross-read declarations; the pre-pass runs them. Registration inside a phase is a defect (checks on: thrown) | the type tables are read lock-free by every batch thread | `BrainPhase`, `RuleTree.PrepareFrame`, `DecisionCadence.PrepareFrame` |
| D-27 | the audit's shared scratch / caches (Explore audit) | — | static scratch lists → `[ThreadStatic]` (MemberEvaluation, ObjectiveCensus, MeasuresEvidence, HoldArea); `DecisionCadence` store concurrent (each key touched only by its unit's thread); `Melee.Strikes` atomic; the utility PRE-score serves the serial walk only (in the phase the scoring already runs on every core) | | listed files |
| D-28 | which types the last-tick view copies | the census list by hand · ⭐ measured per content | `HROT_READ_CHECK=2` — the SURVEY: every check records (kind, type, first site) instead of throwing; `HROT_READ_CHECK_OUT=<file>` for a headless node; `BrainReads.Declare` holds the result (Health, EntityInfo, PartMetadata so far). ⚠ The design's target is the generator emitting each asset's set (§18.2) | | `ParallelReadCheck.Survey`, `BrainReads` |
| D-29 | the brains' switch | its own · ⭐ the engine's parallel-simulation switch | `HROT_PARALLEL_SIM=1` (`ParallelEntities.Enabled`, which every converted per-entity system follows) or mode 3 | one switch for "the simulation runs per-entity parallel" | `BrainTickSystem.Parallel` |
| D-30 | the determinism gate's measure | compare a few fields · ⭐ the whole world | `EntityRepository.ContentHash` (+ a per-row breakdown that names the first differing entity / component) | | `EntityRepository.ContentHash.cs` |
| D-31 | brain code that ADDS a component and then writes it ("add default, then GetComponentRW") — the scale rig crashed on two (`Callouts.Say`, `UnderFire.Stamp`); a scan found ~20 more | make own-entity adds direct (they touch shared counters, the add log, managed chunk allocation, and their ORDER would depend on threads) · ⭐ keep F-6 (structural changes deferred) and ADD WITH THE FINAL VALUE | the value is built in a local and added once; present ⇒ the RW path as before; serial behaviour identical. Where the read is in a LATER call (the callout's cooldown), the whole operation defers (`BatchContext.Later`) | `852cf7598`, `767561959` |
| D-32 | a squad commander's drill trace was first ADDED mid-tick (a second write in the same tick built from empty) | ⭐ provision it with the commander | `SquadStateProvisioning` adds it with the squad state (the fact "a roster ⇒ a commander") | `cee930493` |
| D-33 | callouts the brain makes for OTHER units (squad plays) | ⭐ R-4212 ②: a command, heard a tick later | `Callouts.Say` inside the phase queues a follow-up (`BatchContext.Later` + `IPhaseFollowUp`, played serially in batch order after the command buffers) — the policy and AQ105 E's de-dup (F-10) run in a fixed order | `cee930493` |
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
| the rule-message id (`ScenarioVerbs.s_messageSeq`) with parallel rule brains | a process-wide atomic counter: two messages published in one tick by rules on different threads may swap ids (the event content, not the world, differs) | a per-world counter advanced in the post-pass, or the id from (rule entity, tick) |
| the generator emitting each asset's cross reads (§18.2) | `BrainReads` is a hand list (from the survey) | the blueprint / BTree / HSM generators emit, per asset, the types read on other entities; `BrainPhase` unions them |

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
