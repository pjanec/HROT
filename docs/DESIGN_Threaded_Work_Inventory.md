<!--STATUS
state: LIVE
updated: 2026-11-12
current-answer: the whole file — the MEASURED inventory the overnight threaded + parallel build works from
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
| D-5 | ONE command carrier: `Edits.Submit` enqueues (the sim thread applies `Edits.Submit(S, op)`) instead of new command classes. ⭐ Performance: commands carry USER actions (a few per second; the per-frame ones — possession control frames — are already a bus stream; a drag sends nothing until the drop) ⇒ the cost moves from the main thread to the sim thread unchanged (the same gate / journal work) — negligible; the reason is correctness (no UI write races) and one path to maintain | yes |
| D-6 | DECLARED access per parallel system (writes own T · reads others' T from last tick · emits commands): today `ParallelGuard` catches a write to another entity at run time, but nothing catches a NEW cross-entity READ of in-phase-written data (it would read a half-written tick silently) and the last-tick mask would be a hand list. Declarations ⇒ the guard checks reads too, the R mask is derived, a regression fails a rail. 0 cost in Release | yes — only for the systems that run per entity in parallel |

## 1. Every UI write into the simulation — ≈ 133 sites, 17 kinds *(UI-writes sweep)*

⭐ **Four structural findings first:**

| # | finding | evidence | consequence |
|---|---|---|---|
| U-1 | **undo records the WRONG world in mode 3** | `WorldUndoRecorder` installed on `_world` (`EditorSubsystem.cs:350`), which is A since `:2109`; capture / restore `WorldUndoRecorder.cs:72-73` | the recorder moves to S; the edit brackets travel inside the commands |
| U-2 | `Edits.Submit` gates, journals and reports on the repo it is GIVEN — A | `Edits.cs:23-77` (StationMode / ReplayState gate, `EditJournal`, `EditOutcomes`) | enqueue the `EditOp`, the sim thread calls `Edits.Submit(S, op)`; callers that read the `EditTicket` synchronously (`ScenarioClockEdit:49`, `FormView:504`, `KnowledgeOrders:17`) get an async outcome |
| U-3 | gizmo commits read the write route back at once and snap back on `Refused` | `IEntityComponentWriter` / `EntityWriteRouter.For` | the snap-back moves to the ghost, driven by the outcome |
| U-4 | editors write the world DIRECTLY when the scenario is not running | `EntityAiEditModel` (ingress `*Now`, `RoeSystem.Apply`, `ApplyParams`), `RepositoryAdapter`, `TerrainBlockOps`, `WeatherEdit`, `ParamUsage`, `MacroArgs`, `HeaderParams` | each needs a carrier |

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
