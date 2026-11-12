<!--STATUS
state: LIVE
updated: 2026-11-12
current-answer: ⭐ §7 THE CRITICAL REVIEW (decisions U-1…U-6, fixes F-1…F-16, plan P-1…P-7, unmeasured) · then §0–§6 — the MEASURED inventory the overnight threaded + parallel build works from
stale-below: nothing
known-rot: none
known-conflict: DESIGN_Threaded_Simulation_Presentation.md §12 D-12 (on exp/threaded-kernel; "GET routes read A on the main thread") — SUPERSEDED by the user's rule that the debug API sees EVERY component ⇒ routes are SIM-side by default (Q111-E); §14.6 "every frame" vs exp/threaded-poc-4 §14.2.0 "once per tick" — open decision D-1 below
related-designs:
  - DESIGN_Threaded_Simulation_Presentation.md — OWNS the target shape (§14, §14.1, §14.6, §14.7 and the two pictures); this file is the measured work list for it
  - DESIGN_Parallel_Simulation.md — OWNS per-entity parallelism (§18); the census (CE-4449, exp/entity-census) completes its part of this inventory
  - blueprints/DESIGN_Time_Architecture.md — §17 stepped mode; the run-controls section below
-->

# The threaded editor + per-entity parallelism — the MEASURED work inventory *(coordinator, `2026-11-12`)*

🔒 User, `2026-11-12`: *"measure anything necessary so that an overnight autonomous session can … implement the complete threaded solution with
parallelism"* — and *"I can actually run 3 sessions in parallel"*. Four read-only sweeps on `exp/threaded-poc-4@251c68538` (graph + grep;
⚠ `check_index_coverage` is not in the CLI — completeness is graph + grep, not proven). Line numbers are on that commit.

## 0. The open decisions (the user's) — the overnight run needs them first

| # | decision | ⭐ lean |
|---|---|---|
| D-1 | the gizmo block: once per tick or every frame? (branch §14.2.0 says once per tick — the user's word there; coordinator §14.6 said every frame) | every FRAME, but its world-bus events (`ConstructionOrder` / `DestructionOrder`, `DataDrivenGizmoSystem.cs:289,317`) consumed only when a new sync has arrived ⇒ overlays refresh per tick, tools answer the mouse per frame (at the measured 4.8 Hz tick a per-tick drag ghost would move 5×/s) |
| D-2 | action dispatch: ~25 of ~40 handlers write UI state (camera, designate, possess, toggles, tool activation), `Delete` writes A from the worker (`EditorSubsystem.cs:2680`) | the dispatcher moves to the presentation step; its world-writing handlers (Delete, Terrain*, Paste / Duplicate, Embark / Disembark, orders) become commands; `DebugStatePatch`, `EffectSpawn`, `WreckSpawn`, `Lifespan` stay in the tick |
| ~~D-3~~ | ⛔ **WITHDRAWN** — a fixed step: 🔒 user, `2026-11-12`: *"how fixed simulation behaves if computer is overloaded? isnt it leading to the spiral of death? that would not work for me; dynamic time slice is not so big issue"*. Catch-up steps spiral when a step costs more than its dt; capping them makes the simulation fall behind real time — neither fits. ⇒ the DYNAMIC step stays (capped at 0.25 s as today). The "giant steps" the Windows run saw (~210 ms) come from the lock starvation (§2), not from the dynamic step. Interpolating positions between snapshots stays optional — it works with a dynamic step too (each snapshot carries its sim time) | — |
| D-4 | the copies, measured: (a) the S→A copy into the UI world — it must exist because A is ONE long-lived object (44 world-keyed side tables, ~650 bindings) the UI also writes; (b) the replica R for off-frame egress (CE-3427, built) — a stable world the scan reads while the next tick runs; (c) a "last-tick view" for per-entity parallel reads — proposed, not built. ⇒ (b) IS a last-tick view (synced at the end of tick N, immutable while N+1 runs) ⇒ **(c) is R, not a third copy**. (a) stays; what changes is WHO copies and from WHAT | ⭐ two copies: R (one per tick, on the pool) shared by egress, the recorder and the sim's cross-entity reads; A from S under a FAIR lock hand-over first (cheap, fixes the starvation); A from R on the main thread only if a double-buffered R is measured affordable (2 × the replica's memory) |
| D-5 | ⭐ **REVISED (user, `2026-11-12`: "it is worth it, what would we gain?")** — what the threaded editor NEEDS is only that every UI write EXECUTES ON THE SIM THREAD AGAINST S; it does not need every write rewritten as an `EditOp`. ⇒ ONE sim-side job queue (the same `SimJobs` the HTTP split creates, §3) carries UI writes as they are: each site posts its existing code, retargeted from A to S (`Edits.Submit` callers keep `Edits.Submit`). ⛔ The full `EditOp` unification gains almost nothing (user, `2026-11-12`: "are you sure that one replication path is what would help? how related to undo this is, we do undo via taking world snapshot"): UNDO is a world snapshot — `WorldUndoRecorder` captures the whole world at a bracket's begin and restores it (`WorldUndoRecorder.cs:9-17, 50-73`), so ANY write inside a bracket is undoable whatever path it takes, and it records only in the network-less Edit mode; REPLICATION of state goes through egress from the owner's components whatever wrote them — `Edits` adds only routing to the owner when this node is not it. ⇒ not needed | ⭐ the minimal marshal; the POC (CE-4455) proves the hard parts both shapes share: results read back at once (`EditTicket`, the gizmo snap-back — U-2, U-3), undo on S (U-1) — the bracket's snapshot taken on the sim thread; with ghosts, S is untouched during a drag, so the 'before' is simply S at the drop, scenario new / load + a full resync (U-4) |
| D-6 | DECLARED access per parallel system (writes own T · reads others' T from last tick · emits commands): today `ParallelGuard` catches a write to another entity at run time, but nothing catches a NEW cross-entity READ of in-phase-written data (it would read a half-written tick silently) and the last-tick mask would be a hand list. Declarations ⇒ the guard checks reads too, the R mask is derived, a regression fails a rail. 0 cost in Release | yes — only for the systems that run per entity in parallel |

## 1. Every UI write into the simulation — ≈ 133 sites, 17 kinds *(UI-writes sweep)*

⭐ **Four structural findings first:**

| # | finding | evidence | consequence |
|---|---|---|---|
| U-1 | **undo records the WRONG world in mode 3** | `WorldUndoRecorder` installed on `_world` (`EditorSubsystem.cs:350`), which is A since `:2109`; capture / restore `WorldUndoRecorder.cs:72-73` | the recorder moves to S; the edit brackets travel inside the commands |
| U-2 | `Edits.Submit` gates, journals and reports on the repo it is GIVEN — A | `Edits.cs:23-77` (StationMode / ReplayState gate, `EditJournal`, `EditOutcomes`) | enqueue the `EditOp`, the sim thread calls `Edits.Submit(S, op)`; callers that read the `EditTicket` synchronously (`ScenarioClockEdit:49`, `FormView:504`, `KnowledgeOrders:17`) get an async outcome |
| U-3 | gizmo commits read the write route back at once and snap back on `Refused` | `IEntityComponentWriter` / `EntityWriteRouter.For` | the snap-back moves to the ghost, driven by the outcome |
| U-4 | editors write the world DIRECTLY when the scenario is not running | `EntityAiEditModel` (ingress `*Now`, `RoeSystem.Apply`, `ApplyParams`), `RepositoryAdapter`, `TerrainBlockOps`, `WeatherEdit`, `ParamUsage`, `MacroArgs`, `HeaderParams` | each needs a carrier |

⭐ **The precedent for asynchronous edits already exists** (measured `Edits.cs:21-80`, `EditRequests.cs:11`): `Edits.Submit` returns an
`EditTicket(RequestId, Route)`; when this node does NOT own the component the route is `Routed`, the edit travels to the owner, and the outcome
arrives LATER through `EditOutcomes` / `EditJournal` keyed by the request id. ⇒ in the threaded editor the UI is simply a non-owner and S the
owner: every UI edit takes the `Routed` path that cluster edits already take. ⚠ Not yet checked: that every caller handles `Routed` (the
gizmo snap-back reads the route at once — U-3); undo (U-1) and the edit-mode direct writes (U-4) have no carrier and are design work, not
mechanical — ⇒ a small POC before the overnight conversion (CE-4455).

| kind | sites (file:line) | carrier today | undo | ghost |
|---|---|---|---|---|
| move / rotate | `EntityDragGizmo` commit 261, preview 278/284; `GroupTransform` 233, preview 260/266; `EntityRotatorGizmo` 250, preview 256 | `EntityWriteRouter` attribute writes | yes (`EditSteps`) | ⭐ YES — previews into a ghost in A, the drop one command |
| camera pose | `MapCameraLayer:225` (`GetComponentRW<SimTransform>(cam)`), `:232` | `EntityCreationRequest` (camera entity) | — | UI state or a command |
| delete | `SelectionInteractionSystem.DeleteEntity:315/317`; `EditorSubsystem:1979` (⚠ a duplicate of CE-4410's one path — route through it); Player `:1159/1167` | `EditOp.Delete` / `DestroyEntityCommand` | editor yes | — |
| spawn / place | `ScenarioSpawnAdapter:545/558`; `EditorZoneAdapter:101,116`; `RulesBundle:43`; `RuleEditorServicesFactory:121` | `EntityCreationRequest`, `EditOp.Create`, zone commands | via Submit (rules not bracketed) | placement ghost is draw-only |
| paste | `EntityClipboard.Paste:127-136` (from `PasteGizmo:74`) | `EditOp.Create` | yes | draw-only |
| edit component / field | `FormView:504`; ⚠ `RepositoryAdapter:73/75` via `SetComponentAsUserStep:83-86` (from `ComponentEditWindow:177`, `ComponentReflector:424` — a direct write of ANY component / singleton); `EntityRenameModal:128`; `BrainSettings:112,119,163,164,171`; `EntityPlayableView:41,48`; `PlayableOps:32-35`; `HrotForms:536` | `EditOp.Write` / Add / Remove; the inspector has none ⇒ a reflective SetComponent command | yes | — |
| polyline / route | `VertexEditGizmo:283/285`; `RouteWaypointGizmo:345/347` | `UpdateEntityCommand` (drop the local `SetManagedComponent`) | yes | already session-local points |
| mission plan | `ScenarioMissionService:182,194,206` | `MissionControlIntent` + `EditOp.Commit` | yes | handle local |
| orders | `EntityOrders:172,281,294-295`; `EmbarkOrders:23,34`; `KnowledgeOrders:17`; commander `HrotForms:263,447`, `ScenarioOrbatAdapter:334,350` | `EditOp.Command` / `Interact`, intent events | orders no; SetCommander yes | — |
| possession | `EntityOrders:218,232,241,254` (control frames = a per-frame input stream) | events | — | — |
| weather / terrain / clock | `WeatherEdit.Set:39/41` (no undo); `TerrainBlockOps:19,29,42` (no network carrier); `ScenarioClockEdit:49,59` | mixed | partly | — |
| rules / macros / params / header | `HeaderParamsSchemaSource:80`; `ParamUsage.Rename:107,115`; `EntityBindContext:52`; `MacroArgs:79`; `RuleDetails:261,280`; `RulesWindow:195`; `InstructorEndControl:78` | `RuleCommands`, `EditRequests`, direct | partly | — |
| AI assignment / live variable | `EntityAiEditModel:108,114,205-247`; `BlueprintLiveValueWriter:196` → `DataBreakpointManager` staging (⭐ an existing queue the kernel drains in PreFrame) | events when running, direct otherwise | own history | — |
| debug settings | `GizmoPins:63`, `AiTraceActions:69` | `PatchDebugStateCommand` (already a command) | — | — |
| scenario new / load | `ScenarioFileService.NewScenario:80-85,140` (`SoftClear`, `WorldEpoch.Advance`, `GlobalTime`, `WorldResetEvent`) from `EditorScenarioSession.ClearWorld:192`, `EditorSubsystem:409` | — | — | ⚠ in mode 3 it clears A ⇒ an S command + a full resync |
| undo / redo | `EditUndoCommands:43,48,52` → `WorldUndoRecorder` whole-world restore; `RuleDetails:322-323`; AI-edit / header histories; Player `GizmoUndoStack:1354,1365` | — | — | see U-1 |
| run / cluster (≈ 30) | `ClusterTimeTransportAdapter:165-425` (21), `EditorSubsystem:999,1002,2674`, `EditorScenarioSession` ×6, `RuleEditorServicesFactory:101` | ✅ ALREADY commands — the control-plane bus (`FdpEventBus.cs:87`, thread-safe publish) | — | — |

**UI-only (≈ 75 sites, no command):** selection (`EcsSelectionState:193-245` and its publishers), tool activation, menus and shortcuts, `DebugGizmoLayer:501-704`, order form, canvas menu, hierarchy order, `MapDisplayComponent`, effects (21), gizmo settings. ⚠ Effects are "recorded, never saved" — created in A they are not recorded by a recorder on S. ⚠ A-only components (`MapDisplayComponent`, `SelectionStateComponent`) must be excluded from the sync (`EntityRepository.TwoWorlds.cs:112-127` copies every row component today).
**Not classified (9):** `ScenarioVerification:745`, `EditorSubsystem:6060`, `EditorNodeBootstrapper:318`, `OccurrenceSlotWriter:95,122`, `VariableRowSources:222-237`, `DirectorTakeSession:135-136`.

## 2. Run controls, stepping, the tick rate, hot reload *(run-controls sweep)*

| finding | evidence | plan |
|---|---|---|
| ✅ toolbar / HTTP / preview play-pause-step-speed publish INTENTS on the node's own bus — effectively a command queue | `ClusterTimeTransportAdapter.cs:157-215`, `ManagedEventStream.cs:99,122`, swapped on the worker `HrotNodeContext.cs:67` | keep |
| ⛔ four AI debuggers + the breakpoint manager write `MasterSyncController` DIRECTLY, unlocked | `MasterSyncTimeControllerAdapter.cs:37-60`; callers `BlueprintDebugSession`, `BTreeDebugSession`, `HsmDebugSession`, `DataBreakpointManager.cs:592` | route through intents (the breakpoint-hit path is already on the worker) |
| ⛔ **Run-to never runs in threaded mode** | `EditorSubsystem.cs:3049-3062` (`if (threaded) … else _runTo?.Tick()`); its `until` reads A (`:1667`, `RunToController.cs:173`) | tick `RunToController` on the worker between ticks; TURBO = N node frames, one sync at the end; `until` reads S; `/sim/run-to` a command |
| ⚠ readouts read worker-written clock state unlocked | `MasterSyncController` fields `:32-48`; `_clusterTime` | a clock-status struct published with the sync |
| ⚠ HTTP `/sim/step` can see the new time with the OLD world | `DebugApiHost.cs:653-722` | wait for "clock moved AND a sync completed after it" (a sync counter) |
| ⭐ **the tick-rate anomaly = LOCK STARVATION** | the main thread holds `WorldLock` from `Program.cs:704` to `:782` (~99 % of the frame), `Monitor` is unfair; Windows: ~130 ms of waiting per tick playing, ~100–120 ms paused (the same) | ⭐ D-4 (the snapshot) removes it; interim: a fair hand-over or release before `EndDrawing`. Proof: split `tickWatch` into tick / lock-acquire / sync / yield |
| stepped mode on the worker | §17's waits are per frame inside `ModuleHostKernel`, thread-independent; the worker has no `SynchronizationContext` (no CE-4158 hang) | — |
| ⛔ no world-STATE hash route; `DeterminismRails` compares id maps + panels, not values | `DeterministicSimulationTests.ComputeStateHash` (`Fdp.Core.Tests:402`) only | add `GET /world/hash` (on S, between ticks) + a `DeterminismRails` case: N steps, threaded vs `HROT_THREADED_SINGLE=1` |
| hot reload desync: the "build blocks the frame" suspect is REFUTED (out-of-process, `ConfigureAwait(false)`) | `AiBehaviorsRebuild.cs:43-58,125` | likely a round that completed without node 1 or the preview stop acting on A; measure: log every `_inSync` change + grep `ejected (heartbeat timeout)` (`ClusterMaster.cs:783`); ⭐ the reload's UI subscribers (catalog, documents, breakpoints, log — `EditorSubsystem.cs:2248-2258,5345-5351,2533`, `:2265`) run on the worker ⇒ queue them to the presentation step |

## 3. The HTTP debug API — 223 routes *(routes sweep)*

| target | count |
|---|---|
| SIM-side (between ticks on the sim thread; sees all of S) | **116** — 45 reads, 35 writes, 36 cluster / clock / replay |
| UI-side | **82** — 73 UI + 9 cluster reads from the published views |
| BOTH (split) | **5** — `POST /entities/{id}/debug-settings`, `POST /scenario/save`, `POST /director/play`, `GET /director/moments`, `POST /director/promote` |
| thread-free | **20** |

**The mechanical change:** `MainThreadJobQueue` is generic ⇒ TWO instances, `SimJobs` (drained between ticks) and `UiJobs` (drained at `EditorSubsystem.cs:3104`); `RouteEntry` gets `Side = Sim` by default; `HandleRequestAsync` (`:2780`) picks the queue ⇒ ~150 one-line routes need no edit, ~82 get a label, the 5 are split. A rail over `_routes` (as `EveryRouteIsDocumentedTests`): every route has a deliberate side.
**Three couplings:** polling loops that must run where `IEditorLogic.Update` runs (`PublishAndAwait:2546`, `/run-mode:570`, `/sim/step:653`, load / new `:2266,:2304`, `/director/stills`) — poll the published `ClusterView` instead; the panel routes depend on draining before `PanelSnapshot.ClearCaptured()` (`:3086-3104`) — stay on the UI queue there; mission commits await an ack now arriving on the sim thread.
**Write-through-read paths:** `GET /entities[…]`, `/field`, `/diff/*`, `/trace`, `/blueprints`, `/variables` reach brain memory through RW resolvers (`BrainDiagnosticsTranslator.cs:109`, `BlueprintStateTranslator.cs:147`, `Variables.cs:39-52`) ⇒ a read-only `TryFindSlot`. ⚠ `POST …/component` writes by reflection directly (`DebugApiService.cs:3306,3364-3388`). ⚠ 8 routes race TODAY (HTTP thread reading A: terrain ×2, doors, los, hearing, shots, detonations, world/info — `DebugApiHost.cs:982-1048`).

## 4. The gizmo block move *(gizmo sweep)*

Composition: `MapInteractionPack.Build`; "GizmoExecution" = stateless → globalManager → dataDriven → selfCheck (`MapInteractionPack.cs:238-239`, the CE-259r order is load-bearing); `MapInteraction.InteractionSystems` (`MapInteraction.cs:303-304`); `GizmoInteractionModule.Tick` swaps the interaction bus at `:88`; `ScenarioEditorModule.RegisterSystems:124-129` (selection pair, tool drain, centre-on-entity). Already moved: `CanvasMenu` (`EditorSubsystem.cs:2734`), `MenuOrders` / `MenuShortcuts` / `Hats` (`:2775`), debug settings UI (`:2795`). ⚠ `RunPresentationStep` runs every world frame (`:3059`) BEFORE `canvas.Update` (`:3120`) — misses that frame's input. ⚠ `SelectionInteractionSystem.Tick` runs on main (`:3121`) but reads the interaction bus the worker swaps — a race today.
**The ordered edits:** ① `MapInteraction.cs:303` — `SimulationSystems` + `PresentationSystems`, `InteractionSystems` unchanged (headless hosts unchanged by construction) · ② `ScenarioEditorModule.cs:103-130` — one static builder + an `InteractionDeps` flag · ③ `EditorSubsystem.cs:2299-2307` set it in mode 3 · ④ `:2726-2731`, `:2769-2779` — the module gets `SimulationSystems`; the presentation gizmo block = `GizmoFramePanel.Publish` → `_gizmoBuffer.EndFrame(dt)` → interaction-bus swap → contextIngress → `PresentationSystems` → the ScenarioEditor list → `PublishFrame()` · ⑤ gate per D-1 on `_twSyncN` (`:2961`) · ⑥ move the block after `:3121` · ⑦ drop the worker's gizmo-buffer calls (`:2994-2997`, `:3013-3019`) in mode 3 · ⑧ selection owned by A (`:2831-2832`; `SyncRowsCore` excludes A-owned components) · ⑨ tool commits + `Delete` as commands, rails: pick / drag / rotate / place / delete while playing.
**Headless hosts** (CGF `CgfSubsystem.cs:789`, SimHost `SimHostApp.cs:499`, Player `:903`, ReplayBrowser `:511`): unchanged — they never read `ComposedWorlds`.
**Hazards (inferred):** `BreakpointManager` handed to the gizmo managers (`MapInteractionPack.cs:177,184`) belongs to S; overlays read the spatial grid, rebuilt in place each tick (`SpatialHashSystem.cs:98,120`) ⇒ copy it or publish it; A's bus publishes reach A's readers only through S (~2 ticks).
**The row-copy gap:** rows only for S's `IsSelected` (`:2942-2948`), capped silently at 64 (`:2758,2947`). The shown-but-unselected entities: Brain tabs (`BrainObservation.cs:32`), pinned / float Details (`DetailsViewWindow.cs:125`), pinned graph tabs (`AiDocument.PinnedEntity`), Watch pins (`VariableRowSources.cs:290`), gizmo pins (`GizmoPins.cs:40`) ⇒ ONE main-thread interest set (`Show(entity)` when drawn, linger like `BrainObservation`), rows for selected ∪ interest ∪ pinned, the buffer grown; a rail in `TwoWorldsTests`.

## 5. The gates — exact commands

Build the TEST project, then `--no-build` (CLAUDE.md stale-binary traps); after merging another branch, `dotnet restore <tests.csproj>` once.

| area | project | classes |
|---|---|---|
| two worlds, reads, record / replay, pool | `FDP/Engine/Fdp.Core.Tests` | TwoWorldsTests, PureReadsTests, ManagedReadsReplayEquivalenceTests, ManagedMutationCheckTests, AccessTraceOffTests, AsyncRecorderTests, ParallelSyncTests, ParallelEntitiesTests, ChunkIterationTests |
| kernel, time | `FDP/Engine/Fdp.ModuleHost.Tests` | KernelFrameTimingsTests, ModuleHostKernelTimeTests, PausedAndSteppedExecutionTests |
| gizmo frame | `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests` | PublishedGizmoFrameTests, GizmoFramePanelTests |
| statics, parallel systems, gizmo core, clock | `FDP/Toolkits/Fdp.Toolkits.Tests` (rotating reds, DEBT-AIB-030 — filter) | UtilityStaticsRaceTests, RaycastSolverSystemTests, ThreatEvaluationParallelRailTests, BlueprintTickStampOnChangeTests, GlobalGizmoManagerTests, DataDrivenGizmoSystem*, StatelessGizmoSystemTests, MasterSyncControllerTests, SlaveSyncControllerTests, AiHotReloadCoordinatorTests |
| picking | `FDP/Engine/Fdp.Presentation.Tests` | EntityPickerGizmoTests, DebugGizmoLayer*Tests, GizmoLayerEntityHitTestTests |
| map, gizmos, drag, run toolbar | `Hrot/Engine/Hrot.Presentation.Tests` | MapInteractionPackTests, ScenarioEditorModuleTests, SelectionInteractionSystemTests, EntityDragGizmoTests, VertexEditGizmoTests, RouteWaypointGizmoTests, Map3DPickingTests, RunToControllerTests, MainToolbarTimeControlTests · ⚠ 38 `*FrameRail` classes need `xvfb-run -a` (they skip silently without a display) |
| editor | `Hrot/Subsystems/Hrot.Editor.Tests` | TheViewportInteractionIsSharedTests (edit ② touches it), TheEditorRunsTheOrchestratorCoreTests, AiHotReloadCoordinatorTests |
| hosts | ClusterRunner.Tests · Player.Tests · SimHost.Tests | TheMapIsNotSelectionGatedRails, CgfSubsystemTests, ContinuousDragTests, EntityRotatorGizmoActivationTests |
| replication, hot reload (slow, background) | `Hrot.ClusterRunner.Integration.Tests` | DragDropIntegrationTests, HeadlessGizmoStreamingTests, SplitAuthoritySpawnTests, Eqs/HotReloadTests, DebugApiBatch09/11 — ⚠ a known pre-existing allocator crash: report, never a pass |
| determinism, preview | `Hrot/Runner/Hrot.SystemTests` | `scripts/run-system-tests.sh [--no-build] DeterminismRails` · PreviewLifecycleRails · AiBehaviourHotReloadTests |
| scenarios | headless editor | `NO_BUILD=1 OUT=<dir> scripts/scenario-regression.sh [names]` (prefix `HROT_THREADED_KERNEL=3`) |
| runtime checks | the editor | `HROT_WORLD_GUARD=1`, `HROT_POC3_WALK=exit` via `scripts/threaded-poc2-measure.py --out <dir> --mode 3` (no test sets these) |
| perf | Release runner | `scripts/threaded-poc2-measure.py`, `scripts/parallel-sim-measure.py --poc4` (⚠ fix its decimal-comma regex) |

Measured build costs (CLAUDE.md): full solution ~115 s warm `--no-restore` (3 min on this VM for exp/perf), one project ~8 s, restore ~79 s.

## 5a. ⭐ A board task, decided by the user (`2026-11-12`: "add both") — Windows scheduling for every HROT process (CE-4452, Parallel Simulation §19)

| task | done when |
|---|---|
| at start-up every HROT process opts out of power throttling (`SetProcessInformation(ProcessPowerThrottling)`, execution speed + timer resolution), the sim thread and the pool threads per thread too; `timeBeginPeriod(1)` while a simulation runs; the sim thread AboveNormal, the pool's frame lane Normal, its background lane BelowNormal; console hosts clear `ENABLE_QUICK_EDIT_MODE`; the pool sizes its FRAME lane to the performance cores (`GetSystemCpuSetInformation`, `EfficiencyClass`) and lets the background lane use the efficiency cores | ⭐ one shared start-up helper every host calls (shared and unified — CLAUDE.md); a rail per setting where Windows APIs can be faked; Linux a no-op; the Windows throttling measurement (CE-4453) re-run with the code instead of `powercfg` |

## 6. Still to come
- **Per-entity parallelism:** the census (CE-4449, `exp/entity-census`, running) — each system READY / CONVERTIBLE / GLOBAL, the last-tick set, the prize model. ⇒ track C's task list.
- **The board:** written once D-1…D-5 are answered and the census lands.

## 7. ⛔ CRITICAL REVIEW `2026-11-12` — four independent adversarial reviews *(user: "isnt this a good time to take a critical look on the proposal from different perspectives … trying to find gaps and flaws?" · "you can of course read the docs first to know intentions behind implementations")*

Perspectives: concurrency · behaviour semantics · hosts / time / cluster · cost and the plan. Each read the owning designs first (docs/ then .dev/);
evidence is `file:line` or a design §, else marked INFERENCE. Duplicates merged; ⭐ = found by two or more reviews.

### 7.1 Decisions for the USER (each with the coordinator's lean)

| # | decision | ⭐ lean | why |
|---|---|---|---|
| U-1 | ⭐ ONE behaviour rule in both modes? R-4207 / R-4208 keep the brains SERIAL in Deterministic mode ⇒ regression, films, stepped runs and tests would validate a different behaviour than 1 000-man live play (the cover over-allocation, the callout chorus, deferred sensors exist only live) | ⭐ yes — the per-entity rule in BOTH modes (it is deterministic at any thread count); re-baseline the goldens once; mark the "serial brains" halves of R-4207 / R-4208 superseded | one implementation per concept; tests must exercise what runs live |
| U-2 | `--mode all` runs the editor's kernel on the worker and CGF / SimHost on the main thread IN ONE PROCESS ⇒ shared statics, one pool, one HTTP queue collide (`Program.cs:705`, `SubsystemOrchestrator.cs:236-243`; F-21's remedy was never carried forward) | every kernel of the process on the ONE worker, in sequence; one sim job queue per kernel | the shared pool / mode / timer statics collide otherwise |
| U-3 | the Player: R-4205 rules it threaded, the inventory lists it unchanged | ⭐ editor first; the Player out of THIS round (its possession input edges, `GizmoUndoStack`, Delete stay as they are), added after | smaller night, one host proven first |
| U-4 | ⭐ the last-tick view: D-4 said "it is R"; three reviews show R is a FULL copy that BORROWS live singletons (`EntityRepository.Sync.cs:154-166` — incl. the spatial grid rebuilt in place) and misses ingress / UI jobs / deferred creations | ⭐ a SEPARATE masked copy taken SERIALLY FROM S AT THE START OF THE SIMULATION PHASE (so it holds this tick's ingress, ghosts, UI jobs, deferred creations); R stays for egress / recorder only | correct timing by construction; fixes the "remote positions one more tick late" finding too |
| U-5 | ⭐ which reads go to last tick: systems run one after another (no dependency scheduler, J-3) ⇒ data written by an EARLIER system in the phase is already safe live | only types written by the SAME system (or a system running concurrently) go to last tick | removes needless latency on perception → squad → order chains; shrinks the copy |
| U-6 | the Deterministic / Throughput switch is a process static set by an environment variable, not derived from the node's time mode | derive it per kernel from the time controller (or state that it IS the time mode) | a second owner of one concept |

#### 7.1a The user's answers (`2026-11-12`) — R-4214, R-4215

🔒 *"U1 okay, U2 okay, U3. I don't understand how player be done differently than all the others, because they all share the code and they should stay the
same. There is should be no difference between how player is handled from how the others are handled. U4, okay. U5, okay. U6, I don't understand if
we can derive determinism from the time mode. We never need to have different determinism modes for different part of the system, even in mode --
all. Either everything is deterministic or not."*

| # | outcome |
|---|---|
| U-1, U-2, U-4, U-5 | ✅ approved as leaned (R-4214) |
| U-3 | ⭐ **NO host is handled differently (R-4215).** Measured: the threaded mode lives INSIDE `EditorSubsystem.cs` (the only host file naming `HROT_THREADED_KERNEL` / `ComposedWorlds` / the gate, beside the kernel and core files) while every host already shares `HrotNodeContext` and `SharedApplicationBootstrapper` (`PlayerApplication.cs:235,849`). ⇒ the prototype broke "shared and unified": ⭐ the threading moves into the SHARED node frame and presentation shell, so the editor, the player and every windowed host get it by construction, and headless hosts run the same code with no presentation world. ⇒ track A's first build item |
| U-6 | ⭐ **one process-wide setting, never per part, `--mode all` included (R-4215).** ⚠ CORRECTED by the user (`2026-11-12`: "Parallel simulation is not always deterministic because it depends on the delta time, which is varying if we are not using stepped mode."): the per-entity rule makes the result INDEPENDENT OF THREAD COUNT AND SCHEDULING — parallelism adds no nondeterminism of its own; it does NOT make a run deterministic. A run is deterministic only when its INPUTS are: a fixed / stepped dt, background jobs (EQS) waited for, no live network input — exactly what the TIME mode decides (stepped / Run-to vs continuous, Time §17). ⇒ determinism is ONE process-wide property owned by the time mode; ⭐ lean (to confirm): no separate Deterministic / Throughput switch for the parallel part, since the parallel part behaves the same either way; `UtilityPrescore`'s Throughput-only gate (`UtilityPrescore.cs:31`) goes |

### 7.2 Design fixes the reviews require (no user decision — they make the rule TRUE)

| # | fix | evidence |
|---|---|---|
| F-1 | ⭐ the POOL carries the tick context and the current entity into every batch (try/finally per entity) — today `AsyncLocal` does not reach `FdpWorkerPool` workers ⇒ the guard, the tracer and the census are blind in exactly the threaded + parallel configuration; an access with no current entity inside a parallel phase throws in Debug | census status; `STATUS_Threaded_POC_4` "For F-5" |
| F-2 | `world.Bus.Publish` inside a parallel phase routes to the current batch's event list (61 direct publish sites in behaviour code; `NativeEventStream.Write` can lose an event across a resize, `:88-124`, `:273-296`) | concurrency B1 |
| F-3 | an armed debug probe / data breakpoint / step breakpoint makes its system SERIAL (a hit rewinds the whole world from a pool worker, `DataBreakpointManager.cs:586-589`) | concurrency B2 |
| F-4 | an unmanaged singleton holding native handles is a REFERENCE: the spatial grid's `NativeArray`s are shared with A by the copy and rebuilt in place each tick (`SpatialHashGrid.cs:23-44`, `SpatialHashSystem.cs:79,98,120`) — publish / double-buffer it; audit every singleton with native or pointer fields | concurrency B3 |
| F-5 | writes to ANOTHER entity only as field-level or append commands (`SetComponentFieldRaw`), never a whole-struct set; `GetComponentRW` on a non-own entity throws in Debug; commands are OPERATIONS (add, or, set-if), never a value computed from last tick (`ScenarioVerbs.cs:136-139` counter) | concurrency M2, behaviour M-3 |
| F-6 | "write own directly" = VALUE writes on components already present; an add / remove (upsert `SetComponent` → `AddComponent`, `_structuralChanges++`) is deferred | concurrency M3 |
| F-7 | the managed table's lazy chunk allocation takes a lock (`ManagedComponentTable.cs:186-190`; the native table already does) | concurrency M4 |
| F-8 | deferred child-sensor creation: the parent link written in the same buffer, `Ensure` idempotent while a creation is pending (no duplicate sensors), the child used from the next tick | concurrency M6, behaviour M-5 |
| F-9 | cover recovery is not built: a `Since` field on `CoverClaim`, a re-check in `Claim()` that drops a lost tie (`PeekAndFireNodes.cs:353,1198`, `VehicleFightFromCoverNodes.cs:925`); fold into DESIGN_Peek_And_Fire §10.7 | behaviour M-1 |
| F-10 | callout de-duplication applied at command playback in fixed order (AQ105 E: the same call within 10 m / 2 s is not repeated) | behaviour M-2 |
| F-11 | squads: a per-JOB ownership set (squad → members), exclusive within the phase | concurrency M10 |
| F-12 | ⭐ seek / restore / branch-to-live: on the existing epoch signal (`WorldEpoch`, `EgressSignals.AllDirty`) force a FULL S→A sync, a full R sync and a rebuilt last-tick copy before the next phase (playback sets the version clock back, `PlaybackSystem.cs:71`; copies skip EQUAL versions) | hosts B2 |
| F-13 | hot reload COMMITS as a sim job between ticks (the registry merge, `HsmActionDispatcher` tables swapped whole); a widened read mask copies its new types at the swap | concurrency M8, hosts minor |
| F-14 | never wait on the simulation while holding A's lock — waits poll the published view | concurrency M9 |
| F-15 | the off-frame egress scan is cluster latency, not background: frame lane / Normal priority; a MACHINE-wide core budget when several node processes share one box | hosts M5 |
| F-16 | ParallelGuard extended: today it only catches a writable ref in a READ-ONLY body, and production `HROT` has none; tests turn on the read check and the world guard (no test sets them) | concurrency M7, cost M8 |

### 7.3 The plan — what would make the night produce nothing

| # | finding | fix |
|---|---|---|
| P-1 | ⭐ NO COMMON BASE: `git merge-tree` of exp/threaded-poc-4 and exp/read-check already conflicts (`EntityRepository.cs`, `AccessTrace.cs`, the threaded design); `coordinator` has none of the parallel machinery | ONE integration branch (exp/perf + threaded-poc-4 + read-check + measure-2), conflicts resolved and built, BEFORE dispatch; every handoff stamped there |
| P-2 | track B needs track A's sim job queue; F-1 must land before anything parallel is trusted | sequence: base → F-1 + the sim job queue → B and C fan out |
| P-3 | ⭐ the speed-up model (2.5× at 8 cores) is perfect scaling; measured scaling flattened at 4 threads, today's everything-on (163–173 ms) is already above the model's 4-core 125 ms | label it an UPPER BOUND until the Windows scaling run (CE-4451) gives a measured 8-core point |
| P-4 | ⭐ serial costs the model omits, on the editor's sim thread every tick: S→A sync 5 ms, lock wait (8–16 ms INFERENCE), R sync 4 ms, last-tick copy 2–3 ms, command playback ~2 ms, GC (2.36 MB / frame), memory bandwidth ⇒ ~15–30 ms | a per-tick LEDGER from `tickWatch` (K-1 is running) |
| P-5 | "done" undefined; perf cannot be measured on a 4-core VM | night-done = FUNCTIONAL (guard at 0, rails green, threaded == single-threaded hash); perf on Windows the next day |
| P-6 | the gates cannot see behaviour drift: `DeterminismRails` compares id maps, not values; no `/world/hash`; no 0 B rails for the new buffers; frame rails skip without `xvfb-run`; ClusterRunner.Integration.Tests cannot gate (pre-existing crash) | `/world/hash` + a threaded-vs-single N-step hash case; a VALUE-level golden for the squad / callout chain; 0 B rails per new buffer; fixtures set the guard switches; a `--mode all` gate row |
| P-7 | rate limits (CLAUDE.md: three parallel agents hit the 5-hour limit twice) and large tasks | every item landable alone; a ranked list per session; R-106 stop-and-report |

### 7.4 Unmeasured, still load-bearing

| assumption | where it gets measured |
|---|---|
| the threaded 4.8 Hz tick is lock starvation | K-1 (running, exp/measure-2) |
| per-entity work scales past 4 threads | CE-4451 (running, Windows) |
| the per-read check's cost on the real rig | R-2 (running, exp/read-check) |
| no hidden cross-entity writes beyond the census — 36 systems were silent; rules, embarkation, custody, fire-and-movement were NOT exercised; BrainTick's "read-after-create 162 681 / run" is unexplained | K-2 (running) + a census on a rules scenario and a fire-and-movement scenario |
| MEMORY per node: S, A, R, the last-tick copy, SoD snapshots, the undo ring, recorder keyframes, pooled snapshots — vs the ≤ 4 GB budget (1 000 men already peak at 1.88 GB); × the nodes in `--mode all` | ⛔ not measured anywhere — a new item |
| the "off-frame egress alone makes the CGF slower" anomaly (252 → 337 ms) | ✅ CE-4457 (Windows, `STATUS_Ui_Windows_Egress_Anomaly.md` on exp/perf): **did NOT reproduce** over 14 columns on the same production code — serial 248–281 ms, both switches on 266–284 ms (noise ~30 ms); contention, scan workers, pool budget, recording and the timer each ruled out; the egress scan thread uses 2–4.5 % of one core. ⇒ a machine-state effect of that session (it also hit low system memory), INFERENCE. The SimHost benefit holds: 99–107 → 38–54 ms |
| how often units commit to cover / see a new contact on the SAME tick | ⛔ a contact-onset scenario count — a new item |

### 7.5 Sound — the reviews found no flaw in
the census method and its short conversion list · the one pool's two lanes (no deadlock; the caller drains unclaimed batches) · the read check is free with its flag off (±1 ns) · the UI-write inventory and the HTTP split · the UI frame gain (97 → 15.6 ms) · the D-3 and D-5 withdrawals · the no-dependency-scheduler verdict · cluster time with the sim on a worker (the slave clock is wall-clock derived, barriers snap) · Run-to's fix · no target deconfliction to race (grep only) · room clearing, embarkation and possession go through single serial arbiters · orders already travel one frame late on the bus.

⚠ Doc rot found (concurrency m4): Parallel Simulation on `coordinator` cites "§16 J-3" and the one-pool section, which exist only on exp branches; its section order runs 14 → 11.1 → 18 → 19 → 18.2 — to be tidied when the branches merge.

### 7.6 ⭐ The last measurements are in *(`2026-11-12`; status files on exp/measure-2, exp/integration, exp/perf)*

| question (§7.4) | answer |
|---|---|
| the threaded 4.8 Hz tick | ⭐ CE-4450 K-1: the worker waits **150–175 ms / tick to acquire the world lock playing, 82–106 paused** — ≈ ONE drawing frame: the main thread holds the world for its WHOLE frame; a fair hand-over changed nothing. ⇒ the fix is SHAPE, not fairness: the main thread must not hold the world across its whole frame (copy from a published snapshot / a short lock only around the world-reading part) |
| ⛔ new race | ⭐ K-1: the ORCHESTRATION bus (`_node.EventBus`) is published to and swapped by the worker OUTSIDE the world lock and read / published by the main thread ("Collection was modified", 3 of 3 runs once the hand-over was fair) ⇒ it becomes a command queue — a blocking item before any lock change |
| hot-reload desync | ⭐ K-3: the reload DRAIN blocks the node frame **3.7–4.4 s** (threaded or not) against a 5 s heartbeat eject, and a node returning while the cluster operates is not re-admitted ⇒ NOT threading: build the staging off the frame, keep only the swap on it |
| the query timer | ✅ K-4: off by default — BrainTick −8 % |
| census on heavier combat | ✅ K-2: no change to the verdict; 21 last-tick types on the CGF, the masked copy **2.6–2.7 ms** (full 10.8–11.3 ms) |
| ⭐⭐ Windows throttling | ⭐ CE-4453: **a node with no visible window runs at about HALF speed** — Windows puts windowless nodes in EcoQoS by default (clock ~2.6 vs ~3.8–4.1 GHz); hidden CGF 228 → **121 ms** opted out, SimHost 101 → **44 ms**; minimised = hidden; a visible console is not throttled. ⇒ ⚠ every earlier Windows number was throttled; ⭐ the opt-out (inventory §5a, CE-4452) is a ~1.9× win for every headless node TODAY, independent of threading |
| the off-frame egress anomaly | ✅ CE-4457: did not reproduce (machine state, INFERENCE) |
| memory per node | ✅ CE-4456 N-1: CGF 1.77–2.10 GB at 1 000 men, 2.94 GB at 3 000, ~3.8 GB at 5 000 (fits only with two fixes: chunk-version arrays sized for `MAX_ENTITIES`, 84–217 MB per world even when empty — `NativeChunkTable.cs:56,65`; the recording's ~0.5 GB of buffers). ⛔ the EDITOR is over 4 GB at 1 000 men (4.24–4.29 GB, threaded or not — A adds only ~106 MB; ~1.2 GB unattributed); `--mode all` 5.65 GB |
| same-tick cover / contact | ✅ CE-4456 N-2: two units committing to the same point on one tick NEVER happened (0 in ~88 000 ticks) ⇒ cover recovery (F-9) is a rare tie-break; new contacts arrive in BURSTS (up to 96 units on one tick) ⇒ callout de-dup (F-10) must expect tens; a cover-thrashing unit found — CE-3444 (behaviors) |
| the integration base | ✅ CE-4456: `exp/integration` built, 0 errors; one read path, one pool; a merge defect (test component ids colliding across branches) fixed |
