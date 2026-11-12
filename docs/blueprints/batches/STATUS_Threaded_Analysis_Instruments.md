<!--STATUS
state: LIVE — in progress
updated: 2026-11-12
current-answer: A-5 the verdict (top, when written) · then A-1…A-4
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §13 (the analysis this batch tests) · §12 (the experiment)
  - HANDOFF_Coordinator_To_Backend_Threaded_Analysis_Instruments.md — the dispatch (at 25bce1e00)
  - STATUS_Threaded_Kernel_Experiment.md — the previous experiment (PublishProbe, ThreadedKernelGate)
-->

# STATUS — the threaded-analysis INSTRUMENTS (evidence, not a solution)

**Where:** branch [`exp/threaded-analysis`](https://github.com/pjanec/HROT/tree/exp/threaded-analysis) — from `exp/threaded-kernel` (`9a2bf95c3`)
with `coordinator` merged (`a5a07fd53`; the one conflict, the design's §12 vs §13, resolved by keeping both). ⛔ Nothing goes to a lane — user,
`2026-11-12`: *"the handoff is for separate branch. It's not for pushing into backend."* (so not even the `chore: started` / status copy the
handoff names). Every instrument is opt-in (an env switch, default OFF) and changes no behaviour.

## A-5 — the verdict

⏳ not yet.

## The items

| # | instrument | state | sha | note |
|---|---|---|---|---|
| A-1 | runtime access tracer (`HROT_ACCESS_TRACE=1`) | ⏳ building (worker) | | |
| A-2 | static reachability (Roslyn) | ✅ first pass | this commit | §A-2 below; per type: [A2_Crossing_Types.md](threaded-analysis/A2_Crossing_Types.md) |
| A-3 | the treatments' costs (`PublishProbe` extended) | | | needs A-1's masks |
| A-4 | presentation-out-of-the-tick dependencies | ✅ first pass (read-only sweep + Opus spot-checks) | this commit | §A-4 below |
| A-5 | verdict + fold into §13 | | | |

## A-2 — static reachability: the fields one side writes and the other reads *(Opus, `2026-11-12`)*

**Tool:** `Hrot/Tools/Hrot.Tools.ReachAnalysis` (`hrot-reach`, Roslyn 4.11 over `HROT.sln`, outside the solution; roots in `roots.json`). Run:
`dotnet run -c Release --project Hrot/Tools/Hrot.Tools.ReachAnalysis -- HROT.sln Hrot/Tools/Hrot.Tools.ReachAnalysis/roots.json <out>` (≈ 2 min).
📐 93 non-test projects, **0 compile errors**, 57 522 methods indexed. **Roots** — simulation: every `IEcsModuleSystem.Execute` / `IEcsModule.Tick` /
`ISystemGroup` / `ILoadPartProvider`, `OrchestratorCore.Tick` + `TickUi`, the hot-reload drain, `HrotNodeContext.StepTimeline`; presentation:
`ManagedWindow` draw, `IMapLayer`, `IMeshLayer3D`, `IPanelViewModel`, the after-kernel `BrainObservation` / `UnitDebugSettingsMemory` /
`RegenerationScheduler`, and **§13.5's 19 presentation-in-tick systems** (the gizmo module's, selection, tool drain, centre-on-entity) counted on the
presentation side (they move out first). A root counts only if its type is constructed in code reachable from a `Main` (34 types dropped, listed
in the scratchpad's `rootsDropped.txt`: example-only, test-host, Stride, debug-only). ⇒ **sim 14 889 reachable methods, UI 21 580.**

**Result:** **647 fields in 186 types** outside the world cross the line (+ 222 world internals — A-1's subject); **20 statics** among them
(+ 94 `[ThreadStatic]` excluded — per thread by construction). Per type: [A2_Crossing_Types.md](threaded-analysis/A2_Crossing_Types.md).

| §13 object | seen by A-2? |
|---|---|
| ✅ confirmed — `ClusterUiCache`, `ClusterView`, `MasterSyncController`, `DebugPrimitiveBuffer`, `BehaviorRegistry`, `TkbDatabase` (`KnowledgeBaseLoadStep.PrepareAsync → TkbDatabase.Clear` vs `TryGetByName`), `DataBreakpointManager`, `MapUserConfig`, `Map3DBuild`, `NetworkEntityMap` (`LightweightRoundIdentitySystem → Register` vs `TryGetNetworkId`), `RoadNetworkHolder`, `ClusterScenarioPanel`, `LiveRefScope`, `ShotLog` | 14 of 25 |
| ⭐ **stop crossing once §13.5's split is done** — `MapCameraViewport`, `GizmoSettingsRegistry`, `GizmoRuleTimings`, `BrainObservation`, `RegenerationScheduler`: every reader / writer is on the presentation side after the move | a CONFIRMATION of the split's value |
| in the world — `UnitDebugSettings` (a singleton) | A-1's |
| ⛔ not visible to a static pass — `TrafficFrame` (the module's lists exposed under another name), `KillFeed`, `EditJournal` (per-world side tables mutated through a local) | aliasing: stated limit below |
| the four §13.7 untraced: `TimelineLaneCatalog`, `GizmoUiStateHub` — no field touched by both sides; `ScenarioPanel` — seen (`ClusterScenarioPanel`, 5 fields, both write); the mission service's acks — not identified as a type | 2 cleared (static only), 1 confirmed, 1 open |

**⭐ The 20 process-wide statics both sides write** (§13 lists none):

| static | thread-safe today? | treatment |
|---|---|---|
| `RunTokens._next` | ✅ `Interlocked` | — |
| `QueryTimings.*` (5) · `OrderMenu._orders` / `_builtFrom` · `ParamsDtoRegistry.*` (3) | ✅ a lock (`Gate`) | — |
| `ScenarioFolderIndex.Cache` · StructEdit's `FieldReadWriterCache` / `RuntimeTypeOpsFactory` | ✅ `ConcurrentDictionary` | — |
| ⛔ `RunModeChoice.PresetCache` (`RunModeChoice.cs:566`) | a plain `Dictionary`, both sides write | lock or `ConcurrentDictionary` |
| ⛔ `UtilityInputNames._names` (`UtilityInputNames.cs:12`) | a lazily built plain `Dictionary` | build once, publish by reference |
| ⛔ `UtilityDecisionCatalog._shared ??=` (`UtilityDecisionCatalog.cs:72`) | two threads may build two registries — registrations on the loser are lost | `Lazy<T>` / `Interlocked.CompareExchange` |
| ⚠ `VehicleBody.s_maxBoundRadius`, `BuildingPanelIndex.s_built` (a counter), `FeatureExpander.s_warnedNoUnique` (log once) | unsynchronised, benign (a max / a diagnostic / a flag) | none needed, or `Interlocked` |

**Instance types not in §13** (from the per-type table; triage by reading the paths): the orchestrator's own state — `ClusterMaster`, `NodeRoster`,
`DistributedTransaction`, `ClusterTimeTransportAdapter`, `RunModeChoice`, `ScenarioDoor`, `EditorScenarioSession` — written by `OrchestratorCore.Tick`
/ `TickUi` and read by the panels (same family as §13.6's `ClusterUiCache` ⇒ **publish an immutable view**); the terrain's load objects —
`TerrainResidency` + its `Staged`, `RecastNavmeshFactory`, `NavTileCache`, `TerrainDefinition` — written by the load steps and the obstacle bake, read
by the 3-D build and the panels (⇒ the load is a worker step; the UI reads the committed result only); the 3-D layers (`TerrainChunks3D`,
`TerrainLayer3D`, `TrafficLayer3D`, `BackgroundMeshBuild`) reached from the SIM side through `MapBuildLoadStep.Commit → IMapBuildStatus.Request3D`
(§13.6's `Map3DBuild` row, wider than it says: the load phase drives the 3-D layers' build). ⚠ The rest — per-call scratch types (`ImportReport`,
`StructureAcc`, `Plan`, `Entry`, `Slot`, `BinaryPatchContext`, `CircleSearch`, `NearestPolyQuery` …) — are the same code on DIFFERENT instances, not
shared state.

**⚠ Limits (so a "not seen" is never read as "safe")**: one instance vs another is not distinguished · a mutation through a LOCAL alias of a field's
object (`var list = _x; list.Add(…)`, a table value returned by `GetValue`) is not a write · a struct mutated through a method is not tracked · BCL
interfaces and virtuals (`IEnumerable`, `object.ToString`) are not expanded · a lambda counts as its enclosing method's · composition by reflection /
string is missed (the dropped-root list is the check) · `check_index_coverage` is not in the CLI (codebase-memory).

## A-4 — what the presentation work relies on by running INSIDE the frame *(read-only sweep, `2026-11-12`; line numbers as read on this branch, the tree was being edited by A-1)*

**The frame, as it really runs** (non-threaded editor): `canvas.Update` (`EditorSubsystem.cs:2832` — `DebugGizmoLayer.Update` hit-tests the
PREVIOUS tick's primitive buffer and publishes interaction events) → `SelectionInteractionSystem.Tick` (`:2833`, already outside the kernel) →
`GizmoFramePanel.Publish` / `_gizmoBuffer.EndFrame` (`:2863-2870`, clears the transient primitives) → `TickNodeFrame` → `Kernel.Update`:
phases PreFrame / Input / BeforeSync → **world bus swap** (`ModuleHostKernel.cs:677`) → **module dispatch** (`:856`, `GizmoInteractionModule`
runs HERE) → **PostSimulation** (`:925`, `ScenarioEditorModule`'s systems) → Export → `afterKernel` (`EditorSubsystem.cs:2914-2927`).

| # | finding (✔ = spot-checked by Opus in code) | why it matters for the split |
|---|---|---|
| ① | ⭐ **`GizmoInteractionModule` is not only gizmos** — it also runs `ActionDispatch` (handlers that write the world: Delete, LoadZone — `EditorSubsystem.cs:2630-2641`), `DebugStatePatchSystem` (writes `DebugState`), `EffectSpawn` / `WreckSpawn` (create entities), `Lifespan` (`DestroyEntity`) — `MapInteraction.cs:303-304` | ⛔ the module cannot move as a whole: S-1 must SPLIT it (world writers stay in the tick, the gizmo group moves). §13.5 does not say this |
| ② | ✔ **the module comment contradicts the kernel**: `GizmoInteractionModule.cs:11-14` says it runs after PostSimulation; the kernel ticks modules at `ModuleHostKernel.cs:856`, BEFORE PostSimulation (`:925`) ⇒ the gizmos run before this frame's selection systems and see LAST frame's `SelectionState` | the "same-frame" assumptions some gizmos document are already one frame late; moving them out loses less than §13.5 feared |
| ③ | ⭐ **the CE-259r picker chain needs its pick boxes produced FIRST, on the same side**: `stateless` (emits anchors / pick boxes — `EntityPresentationGizmo`, `NamedPointGizmo`) → `globalManager` (the pickers, `RtsCommandGizmo`) hit-test `_buffer.GetFrame()` cleared at `EditorSubsystem.cs:2870` (`DebugGizmoLayer.cs:477-480`, `MapInteractionPack.cs:212-239`) | the 6 "mixed" gizmos' PICK BOXES and the pickers must move TOGETHER — §13.5's split (pick boxes on the main thread from copied positions) holds, and this is the reason |
| ④ | **tool gizmos write the world every drag frame**: `EntityDragGizmo` (`GetComponentRW` `SimTransform` / `VehicleState`, `:278-285`), `EntityRotatorGizmo` (`:250-256`), `VertexEditGizmo` / `RouteWaypointGizmo` (`SetManagedComponent` + bus publish), `PasteGizmo` (`Edits.Submit`) | confirms §13.5: on the main thread these need a UI-side ghost and a COMMAND for the commit |
| ⑤ | ⭐ **UI state bound to the world INSTANCE**: `MapUiStates` is static weak tables keyed by `EntityRepository` (`MapUiStates.cs:16-29`; `ToolHintGizmo` reads it), `EcsSelectionState` is built over `ctx.World` (`MapInteractionPack.cs:286-288`; the designation / fire-mission overlays read it) | against a COPIED world these lookups miss. ⭐ It favours §12's shape (the presentation keeps World A — every binding stays) over a per-frame snapshot (§3) |
| ⑥ | **the threaded experiment (X-4) has a live race**: with `HROT_THREADED_KERNEL=1` the drawing reads the published buffer, but `DebugGizmoLayer.HandleInput` (`:335,344`) and `PickEntity` (`:479`) still read the LIVE `_buffer.GetFrame()` — the worker's in-progress buffer — from the main thread | a defect in `exp/threaded-kernel`'s prototype, not in production |
| ⑦ | **`SelectionNotificationSystem` is a frame late**: `SelectionChangedNotification` is published in PostSimulation and readable only after the next swap — its comment (`:28-29`) says same-frame | timing already tolerates a frame of latency |
| ⑧ | **the afterKernel objects**: the hot-reload drain must stay between ticks on the worker (it merges `BehaviorRegistry`, rewrites HSM instances, unloads the ALC) — only its `OnReloadCompleted` events marshal (§13.5 holds); `BrainObservation` + `UnitDebugSettingsMemory` write the `UnitDebugSettings` WORLD singleton (two writers, no lock — `BrainObservation.cs:64`, `EntityBrainView.cs:470`) ⇒ a command / handoff, not a plain move; `RegenerationScheduler` touches no world state ⇒ moves freely | refines §13.5's "the main thread" for two of the four |
| ⑨ | **the count**: 75 gizmo types register in the editor (76 with `EntityDragGizmo`); §13.5's 79 also counts `EntityEditorPolylineGizmo` (its attribute commented out — never registered) and two Replay-Browser-only gizmos. 54 register by reflection — 3 of them only if their assembly is loaded (UNCLEAR in the editor) | §13.5's split into 47 / 26 / 6 could not be reproduced from code (its per-gizmo list is not in the doc) |

### A-4 per item — does moving it OUT of the tick break it?

| item | the dependency (`file:line`) | moving it out breaks it? |
|---|---|---|
| `EntityPresentationGizmo` (mixed) | reads `MapViewSnapshot` written earlier the same frame (`EntityPresentationGizmo.cs:121-125`, `MapViewSnapshot.cs:152`); emits the pick boxes the pickers hit-test (`:166,175-178`) | yes — unless its pick boxes move with the pickers (③) |
| `NamedPointGizmo` (mixed) | emits a pick box (`NamedPointGizmo.cs:58`); does NOT read the zoom (§13.5 said it did) | yes — same reason; `MapPickBroker.SnapToNamedPoint` reads it from the UI |
| `TerrainBlockGizmo`, `RouteGizmo` (mixed) | read `SelectionState` (already last frame's, ②) and `LiveTerrainBakeInfo` | no — needs those on the UI side |
| `DesignationOverlayGizmo`, `FireMissionOverlayGizmo` (mixed) | `EcsSelectionState` bound to `ctx.World` (`MapInteractionPack.cs:286-288`), `NetworkEntityMap`, `SimTransform`, routes | unclear — the world-instance binding (⑤) |
| `EntityDragGizmo` | preview `GetComponentRW` `SimTransform` / `VehicleState` (`EntityDragGizmo.cs:278-285`); reads world-bus `ConstructionOrder` once per tick (`DataDrivenGizmoSystem.cs:289,317`) | yes — world write + once-per-tick event delivery |
| `EntityRotatorGizmo` · `VertexEditGizmo` · `RouteWaypointGizmo` · `PasteGizmo` | world writes / `Edits.Submit` / bus publishes (`EntityRotatorGizmo.cs:250-256`, `VertexEditGizmo.cs:283-285`, `RouteWaypointGizmo.cs:345-347`, `EntityClipboard.cs:127-136`) | yes — commands needed |
| `RtsCommandGizmo` | pick-box hit-test (③) + publishes `SelectionChangeRequest`, `AssignTacticalIntentEvent` (`RtsCommands.cs:47-50`, `EntityOrders.cs:172`) | yes |
| `EntityPickerGizmo` | the CE-259r chain (`EntityPickerGizmo.cs:146-150` → `MapCanvas.cs:85-98` → `DebugGizmoLayer.cs:477-480`) | yes — unless the pick-box producers run first on the same side |
| `LayerControlGizmo` | `draw.SetActiveLayers` on the SHARED buffer (`LayerControlGizmo.cs:530`) — the mask gates the producers' emits; file save (`:555`) | yes while the producers stay on the worker (cross-thread buffer state) |
| `ToolHintGizmo` | `MapUiStates.ToolsOf(world)` — weak table keyed by the world instance (`MapUiStates.cs:16-29`) | yes against a copied world (⑤) |
| `EntityPlacementGizmo` · `PointSequenceGizmo` · `MacroPreviewGizmo` · `MissionPlanEditGizmo` · `ObstaclePlacementGizmo` · `ModalBoxSelectionGizmo` | build `SpawnEntityCommand`s / call facades / the node's `MacroExpander` (`EntityPlacementGizmo.cs:352-391`, `ScenarioSpawnAdapter.cs:291-293,460-507`) | unclear — depends on which thread the publish / facade lands on |
| `MeasureGizmo` · `FdpLocationPickerGizmo` · `AreaShapeGizmo` · `PickBannerGizmo` · `RubberBandGizmo` · `SelectionHighlightGizmo` · `CanvasContextMenuGizmo` | no world write; read UI state or last frame's components | no |
| `SelectionRequestSystem` (PostSimulation) | world-bus `SelectEntityCommand` / `SelectionChangeRequest` (`:85,97`); writes the selection components the gizmos read | yes as it stands — once-per-tick bus delivery |
| `SelectionNotificationSystem` | `SelectionChangedNotification` one frame late (⑦); writes `IInspectorContext`, `ToolController.CancelArmedOn` (`:96-140`) | unclear — must sit with the gizmo managers |
| `ToolActivationDrainSystem` | `ActivateEditorToolEvent` (`:112`, published during module dispatch ⇒ next frame); mutates `GlobalGizmoManager` / `DataDriven` dictionaries (`ScenarioToolRegistrations.cs:163-188`) | yes unless on the gizmo managers' thread |
| `CenterOnEntitySystem` | writes `MapCamera.FocusOn` (`:88`) — a UI object (already cross-thread in X-4) | no — moving it REMOVES a cross-thread write |
| hot-reload drain | `BehaviorRegistry` merge, HSM instances rewritten, ALC unload (`AiHotReloadCoordinator.cs:243,304,~560-600`) | yes for the swap — stays on the worker between ticks; only `OnReloadCompleted` marshals |
| `BrainObservation` · `UnitDebugSettingsMemory` | write the `UnitDebugSettings` world singleton (`BrainObservation.cs:64`); a second writer from `EntityBrainView.cs:470`; file I/O | leaning yes — needs a command / handoff to the worker's store |
| `RegenerationScheduler` | no world access (`RegenerationScheduler.cs:76`); flushes assets | no |

