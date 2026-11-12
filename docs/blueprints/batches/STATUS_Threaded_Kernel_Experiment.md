<!--STATUS
state: LIVE — the experiment, in progress
updated: 2026-11-12
current-answer: ⭐ X-5 the verdict (top) · X-1 the publish measured · X-2 the inventory · X-4 the prototype
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS the concept (R-4205); §12 is this experiment's answer to §11
  - HANDOFF_Coordinator_To_Backend_Threaded_Kernel_Experiment.md — the dispatch (at 74456b8c9, stamped c3380f3b4)
-->

# STATUS — the threaded kernel, an overnight experiment

**Who / where:** the handoff was addressed to backend; the user asked the **behaviors** lane to run it (`2026-11-11`). Work is on
[`exp/threaded-kernel`](https://github.com/pjanec/HROT/tree/exp/threaded-kernel) (from `coordinator` at `c3380f3b4`). ⛔ Nothing merged into a lane.
This file lives on the experiment branch and is copied to `behaviors` (not `backend` — that lane's tree stays untouched).

## X-5 — the verdict *(filled at the end)*

⏳

## X-1 — the real publish, measured

**How:** `FDP/Engine/Fdp.ModuleHost/PublishProbe.cs` (opt-in `HROT_PUBLISH_PROBE=1`) in the REAL editor's frame (`EditorSubsystem.Update`, after
the node frame), Release, the cloud VM (4 cores, software GL), hidden window, uncapped. The rig `scale-rig.py --terrain scale-1 --men 1000
--vehicles 50 --aircraft 10` (1 263 authored entities ⇒ **≈ 13 800 live entities** with their parts), opened for editing, Preview played 120 s,
paused 40 s. Every 30th frame the probe copies World A five ways: the production call (`SyncFrom` + `CopyClockFrom`, as `OnDemandProvider`), and
the background modules' mask / ALL types (incl. transient + `NoPreview`), each WARM (a kept repository brought up to date the frame before, so it
carries ONE tick's changes) and COLD (`SoftClear` first, as `SnapshotPool.Return` leaves it). Raw data: the scratchpad (`x1/rel1000.probe.jsonl`).

| ms per copy (median · p90) | playing (14 samples) | paused (24) |
|---|---|---|
| production `SyncFrom` (background mask, kept repo) | **17.3 · 24.1** | 1.1 · 1.3 |
| background mask, warm | **13.1 · 14.6** | 1.0 · 1.2 |
| background mask, cold (`SoftClear`) | 18.8 · 21.8 | 2.3 · 2.9 |
| ALL types, warm | 13.8 · 15.0 | 1.0 · 1.3 |
| ALL types, cold | 20.0 · 22.2 | 2.6 · 3.3 |

| size | |
|---|---|
| committed per snapshot | **107 MB** (213 tables in the background mask, 246 for all types; 19 / 43 managed tables; 9 100 / 11 700 managed references) |
| dirty per tick while playing | **≈ 69 MB** (65 % of the snapshot — F-5 confirmed: a publish is close to a full copy) · cold: 88 MB |
| entity index copy | 0.7 ms warm · 2.7 ms cold |
| garbage per copy | **2 KB** steady (a cold first copy 250–400 KB: table registration by reflection) |

**Where the time goes** (playing, warm, per table; the top of 246): `BlueprintBlackboard4096` **3.6 ms** (321 chunks, 21 MB), `BlueprintBlackboard1024`
1.0, `SquadCognitiveState` 1.0, `ReportOutbox` 0.8, `BrainJournal` 0.6, `UtilityDecisionLog` 0.5, `TargetMemory` 0.5, `PathTrace` 0.4 … ⭐ **the top
eight are the brains' own memory** — state the map and the panels never draw. ⇒ a presentation mask that leaves them out is the obvious lever (§12).

**The frame on this VM (no threading), for scale:** playing **272 ms** (update 167 · draw 79 · ui 11) vs paused **116 ms** (update 19 · draw 74 ·
ui 10). ⚠ The VM is ~2.5× slower than the user's Windows machine and draws with software GL (draw 75 ms vs 14.5 there); ui's Windows numbers
(Evaluation Cost §11h): 98.8 vs 32.4 ms, and ~6.5 ms per `SyncFrom` of the background mask — the same copy measures 13–17 ms here.

⇒ ⭐ **Decision basis for X-3:** one extra full copy per tick costs ~1/5 of the simulation's own time on the worker (VM: 14 of ~150 ms; Windows est.
6.5 of ~63). Affordable on the WORKER; ⛔ never on the drawing thread. Memory: 107 MB per copy ⇒ a pool of three is ~320 MB — the narrow mask
matters for memory as much as for time.

## X-2 — the inventory

Method: ripgrep over the non-test sources + `scripts/cbm.sh search_code` as a cross-check (it covers tests and docs, and splits patterns, so its totals
were not used as counts — e.g. `ConditionalWeakTable<EntityRepository` 23 there vs 44 declarations by rg). ⚠ Coverage could not be checked
(`check_index_coverage` is not in the CLI). At `exp/threaded-kernel` from `coordinator@c3380f3b4`.

### What the presentation READS

| category | count | the point |
|---|---|---|
| singletons read (typed calls) | **16 types, 165 calls** | only 6 are in `SyncFrom`'s shared list: `NetworkEntityMap` (45 calls), `ITkbDatabase` (26), `GlobalTime` (24), `ScenarioDescriptionResident`, `LiveTerrainBakeInfo`, `IOrderForm`, `ZoneEnvironmentData`, `ShotTraces`, `DetonationTraces`, `CanvasContextMenuState` would be MISSING from a snapshot (F-1 confirmed); the FDP inspector reads every singleton by reflection |
| managed components read | **11 types**, 5 not snapshotable (`NoPreview`): `BehaviorStartRecord` (16 refs), `RoutePlan` (14), `ActiveMissionPlan` (13), `PausedTask`, `SopStartRecord` | all 11 are `record`s (immutable by the CLAUDE.md rule) ⇒ a shallow copy of their references is SAFE to read |
| unmanaged components read | **114 types**, 1 256 typed refs; most-read `SimTransform` 247, `EntityInfo` 159, `NetworkIdentity` 130, `TkbIdentity` 89 | not snapshotable: `EffectAnchor`, `EffectFlight`, `Lifespan`, `LightweightRound`, `DebugState` |
| side tables keyed by the world object | **44** `ConditionalWeakTable<EntityRepository,…>` (Presentation 17, FDP 18, AI 4, Common 3, Gameplay 1, Editor 1) | incl. `MapUiStates` (selection, tools, canvas, hat arbiter), `EditOutcomeLog`, `EditJournal`, `EditOp` gate / step recorder, `LiveRefScope`, `KillFeed`, `ShotLog` … ⛔ a view that is a different object each publish resets them all, silently (F-13 confirmed: 44) |
| bindings to the World A object | Hrot.Presentation: 47 fields · 103 `Func<EntityRepository>` refs · 33 casts (`as`/`is`/cast) · ~374 parameters; Hrot.Editor 12 · 18 · 0; AiShared 3 · 33 · 7 | ~650 mentions in Presentation alone; many casts reach `MapUiStates` (F-14 confirmed) |
| ⭐ map layers that read World A in `Draw` (found by X-4, not by grep) | `EffectLayer` crashed the prototype (`QueryCandidateIndex.Build` IndexOutOfRange during a worker tick) | the local map is NOT only a terminal over gizmo primitives (finding X4-1) |

### What the presentation WRITES

| category | count | the point |
|---|---|---|
| bus publishes | **~100 sites on THREE buses**: World A's bus ~55, the orchestration bus ~35 (`ClusterTimeTransportAdapter` 21), the interaction bus ~16 | all three are lock-free `Write` vs locked `Swap` (F-4 applies to all three, not one) |
| routed writes | `Edits.Submit` 20 · component-writer / router 3 gizmos · `RequestEntityCreation` 6 · `RepositoryAdapter.SetComponent` 6 | ⚠ the "routed" paths write World A DIRECTLY on the calling thread (`Edits.cs:23-60` writes locally when this node owns the component) — routed ≠ queued |
| direct simulation writes from UI | ~15 sites: drag / group / rotate previews (`SimTransform`, `VehicleState`), `TerrainBlockOps`, `WeatherEdit`, route / polyline gizmos, `OccurrenceSlotWriter`, `SelectionInteractionSystem` `DestroyEntity`, `ScenarioFileService` (`SoftClear`, epoch, clock), the debug API's reflection write, `EntityAiEditModel` (6), `OrderForm` / `OrdersOffered` singletons | |
| UI-only state written into World A | `SelectionStateComponent` (6), `MapDisplayComponent`, `CanvasContextMenuState`, `SortedHierarchyData`, the effects' entities (`EffectSystems.cs:300-475` — creates and destroys entities) | 4 of them are kernel systems ⇒ they move with the kernel unless moved out |
| in-place managed mutation | `ParamUsage.cs:110-121` (confirmed), `UnitDebugSettingsMemory.cs:43-48` | |
| undo | `WorldUndoRecorder` captures / restores the WHOLE world on the main thread; `EditSteps` brackets in 10 places + every `Edits.Submit`; 4 history closures write the world | |
| main-thread frame work besides the kernel | ~16 calls in `EditorSubsystem.Update` (`EditorSubsystem.cs:2700-2865`): Run-to, the API drain, director, verify, staging remap, canvas, selection, gizmo publish + `EndFrame`, the node frame, the afterKernel hooks (hot reload, `BrainObservation`, debug settings, regeneration), the orchestrator core + its cache, the time transport, the editor logic (loads, `SoftClear`), the rename drain, `PollAcks` | |
| HTTP API | **223 routes: 201 on the main thread** (84 GET, ~117 mutating — ~43 write World A, ~33 the cluster / clock), 16 already read the world from the HTTP thread today | |

## X-3 — the solution

⭐ Written as [§12 of the design](../../DESIGN_Threaded_Simulation_Presentation.md): **invert the worlds** — the presentation KEEPS World A (every
binding and side table unchanged), the simulation moves to a second repository World S on the worker, synced S → A through a declared PRESENTATION
mask under one short lock at each tick's end; the UI's writes to simulated state become commands into S. Keeps R-4205's concept, overturns its
mechanism (a leased pooled snapshot read by the presentation). 13 decisions with their claim tables, the rejected alternatives, the cost.

## X-4 — the prototype

⏳

## Decisions taken (claim tables)

| decision | code — how it is | design — how it was meant |
|---|---|---|
| run on `exp/threaded-kernel` from `coordinator`; status here and on `behaviors`, not `backend` | the user asked behaviors to follow a handoff addressed to backend | handoff "Where to work" |
| X-1 in the REAL editor (`--mode editor`, the scale rig) through an opt-in probe after the node frame, not in a test harness | `EditorHarness` (`ClusterRunner.Integration.Tests`) is a hand-built mini editor — not the real composition | handoff X-1 "the editor's REAL world" |
| the prototype keeps the editor's ONE world and adds a world LOCK (the node frame on a worker; the drawing thread does world work only on frames that get the lock; the map draws a published copy of the gizmo frame) — ⛔ not the full §12 inversion | building the node on a second world means choosing every `_world` the editor hands out (hundreds of sites) — not a night's work; the lock version runs the REAL node frame off the main thread and shows what breaks | §12 is the target; the prototype tests its two load-bearing claims: the node frame runs off the main thread (D-7), and the map / camera can run at the render rate (D-10). What would change it: none — it is a stepping stone |
| map-only frames draw only the layers that read no world (gizmo primitives, grid, 2-D terrain) | ✔ X4-1: `EffectLayer.Draw` queried World A during a tick ⇒ `IndexOutOfRangeException` ⇒ the process aborted | §12 D-10 |
