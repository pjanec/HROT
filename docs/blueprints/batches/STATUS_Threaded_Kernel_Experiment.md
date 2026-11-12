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

## X-5 — the verdict

⭐ **GO — but change the shape.** Keep R-4205's concept (the kernel on a worker, the drawing never waits for a tick, the UI writes commands); replace
its mechanism with **§12: the presentation keeps World A, the simulation moves to World S, synced S → A through a narrow presentation mask under one
short lock.**

| | measured tonight (cloud VM, Release, 4 cores, software GL) |
|---|---|
| ⭐ **what works** | the editor's WHOLE node frame runs on a worker in the real editor — load, preview, 120 s of play, pause — **0 faults** at 1 000 and 5 000 men · the map and camera stop depending on the simulation: **1 000 men 272 → ~95 ms per frame while playing** (paused ~80), **5 000 men 2 380 → ~115 ms** (paused ~95) · the clock keeps real time · ⭐ the presentation's map-wide data costs **1.6 ms / 16 MB per tick** to copy (vs 12.1 ms / 108 MB for the whole world) |
| ⛔ **what does not** | with ONE world and a lock (tonight's prototype), the panels and input still wait for the world, i.e. for a tick: **2.5 Hz at 1 000 men, ~0.2 Hz at 5 000** · map layers that read the world in `Draw` crash when they race the tick (X4-1) — they had to be skipped on map-only frames · a full-world snapshot is 107 MB (1 000 men) / 515 MB (5 000) and 65 % of it dirty every tick — the leased-pool design (§3) would cost 2–3 such copies |
| ⭐ **why §12 fixes the second row** | the panels / input would wait for a SYNC (~2 ms at 1 000 men) instead of a tick (170 ms – 1.1 s); every layer and panel keeps reading A (one object ⇒ the 44 side tables and ~650 bindings unchanged), A is written only by the sync, under the lock |
| **the real cost to finish** (my estimate, in this lane's batches) | **S-1** the composition split — the editor and the player build the node on S and the presentation on A (the biggest and riskiest: every `_world` handed out decided once; a rail that no presentation object holds S) — ~2 batches · **S-2** the sync: the presentation mask (map-wide whole tables + selected rows), the singleton policy, events and edit outcomes forwarded S → A, A's own entities (effects) kept out of the index sync — ~1 batch · **S-3** writes → commands: ~15 direct writes, 20 `Edits.Submit`, the drag / rotate previews as ghosts in A, undo captured on S, three buses — ~2 batches (unchanged from §11: the WRITE half is the same work in either shape) · **S-4** threaded ON + the §8 verification (equivalence, the scenario regression threaded, a slow-tick inline mode) — ~1–2 batches. ⇒ ~6–7 batches, against §11's estimate which ALSO needed the read half (~650 bindings, 44 side tables) — ⭐ §12 removes that half |
| what would change the recommendation | the composition split turning out to entangle S and A beyond a composition change (a sim module that must call a presentation service synchronously) — S-1's first step is to measure exactly that |
| ⚠ not proven tonight | stepped / deterministic mode with the kernel on the worker (R-12) · the 3-D view (its bodies read the world) · the user's machine (the VM's software GL hides the draw: there, ~20–30 ms frames are expected while playing) |

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

**X-1b — the presentation mask** (the same rig, every table's row logged; 2 playing samples — few, but the split is large and stable):

| copied through | ms per tick (warm) | committed | dirty per tick | tables |
|---|---|---|---|---|
| ALL types | 12.1 | 108 MB | 64 MB | 246 |
| everything the presentation reads (X-2, 125 types) | 5.9 | 65 MB | 31 MB | 123 |
| ⭐ **map-wide** (read for every unit: transforms, info, identity, health, weapons, vehicles, stance …) | **1.6** | **16 MB** | **7 MB** | 86 |
| panel-only as whole tables (journal, decision log, squad cognition, blackboards, histories …) | 4.5 | 50 MB | 24 MB | 38 |
| the brains' memory nobody draws | 6.1 | 43 MB | 34 MB | 123 |

⇒ the panels read the panel-only types for the SELECTED unit(s) — copying those rows is ~free ⇒ a presentation sync ≈ **1.6 ms + a few rows**.

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

**What it is** (on `exp/threaded-kernel`, switch `HROT_THREADED_KERNEL=1`, default OFF ⇒ the editor is unchanged):
- `EditorSubsystem.KernelWorkerLoop` runs the editor's WHOLE node frame (`TickNodeFrame`: the slave, the kernel, the afterKernel hooks incl. hot reload
  and `BrainObservation`, the timeline step, the bus swap, the orchestrator core, the time transport) on a worker thread, holding ONE world lock
  (`Fdp.ModuleHost.ThreadedKernelGate`), with the real elapsed dt of the drawing frames (capped 0.25 s).
- the drawing thread (`Program.cs` loop) tries the lock once per frame (2 ms): **with it** the frame does everything that touches the world (the API's
  jobs, input, selection, the editor logic, the ImGui panels); **without it** the frame moves the camera and draws the map only — from a PUBLISHED copy
  of the gizmo frame (`DebugPrimitiveBuffer.PublishFrame` / `LeasePublished` / `ReaderFrame`, a triple buffer) and only the layers that read no world
  (gizmos, grid, 2-D terrain — `MapCanvas.DrawFilter`). When it missed the lock, the worker yields at its next tick boundary (≤ 50 ms) so the panels
  refresh once per tick.

**Measured** (Release, cloud VM 4 cores + software GL, hidden, the 1 000-man rig ≈ 13 800 entities, Preview played 120 s then paused 40 s; per-kind frame
clock `[X-4F]` in the log):

| | inline (today) | threaded prototype |
|---|---|---|
| ⭐ the drawing frame while PLAYING | **272 ms** (3.7 fps) | **≈ 95 ms** (≈ 10.5 fps) — map-only frames 91–101 ms, world frames 96–112 ms |
| the drawing frame while PAUSED | 116 ms | ≈ 80 ms (map-only 70, world 88) |
| the tick (node frame) while playing | 167 ms inside the frame | 160–200 ms on the worker (4–6 Hz); the clock kept real time (121.9 s simulated in 120 s) |
| panels / input refresh while playing | every frame (3.7 Hz) | **≈ 2.5 Hz** — the frames that get the world (~1 per tick) |
| faults | — | **0** worker faults over the whole run (load, preview, 120 s play, pause) |

⭐ **Read:** playing ≈ paused — the property the user is buying (§8 ⑧) — holds for the MAP and the camera: the drawing frame no longer contains the
simulation. ⚠ On this VM the floor is the software rasteriser (~75 ms to draw the scale-1 terrain, which also takes CPU from the worker); on the user's
GPU the draw was 14.5 ms, so the same split should give a ~20–30 ms map frame while playing. ⚠ The panels and input still wait for the world, i.e.
for a tick — that is the prototype's built-in limit, and exactly what §12's inversion removes (the presentation would wait for a SYNC, not a tick).

**What broke and how it was found:**

| # | what | how found | handled |
|---|---|---|---|
| X4-1 | ⛔ map layers read World A in `Draw`: `EffectLayer` queried it during a worker tick ⇒ `IndexOutOfRangeException` in `QueryCandidateIndex.Build` ⇒ the process aborted | the first threaded run crashed (stack in the log) | map-only frames draw only world-free layers; the other layers (cursor, effects, projectiles, 3-D bodies, traffic, map previews) draw on world frames — a real fix is §12 (they read A, which is only written under the lock) |
| X4-2 | the gizmo frame is written by the kernel's gizmo systems and read by the map ⇒ needed its own published copy | design reading before the build | triple-buffered publish in `DebugPrimitiveBuffer` (~4 000 primitives per frame, a memcpy) |
| X4-3 | Run-to's turbo step ticks the node frame from the drawing thread | inventory (§6 row 2865) | ⛔ shortcut: Run-to disabled in threaded mode |
| X4-4 | nothing on the node frame needed the GL thread | 0 faults over load + preview + play | ⭐ D-7 confirmed in the REAL editor, not only the PoC |

**At 5 000 men** (≈ 55 000 entities; snapshot **515 MB**, a warm full copy **61 ms**, cold 164–273 ms while playing; ticks ~1.1 s): inline the editor
draws **one frame every 2.4 s** while playing (update 2.2 s, paused 246 ms); threaded, map-only frames **~110–120 ms** while playing (paused ~95), 0 faults —
⛔ the panels got the world ~once per 5 s.

**Shortcuts named:** one world + a lock instead of §12's two worlds · map-only frames skip the world-reading layers and the panels (they would flicker on a
visible window) · Run-to off · input is processed on world frames only (the camera on every frame) · no commands — the UI writes World A directly on
the frames that hold the lock (correct, but it is why the lock exists) · the 3-D view not measured (its bodies read the world) · 2 runs.

## Decisions taken (claim tables)

| decision | code — how it is | design — how it was meant |
|---|---|---|
| run on `exp/threaded-kernel` from `coordinator`; status here and on `behaviors`, not `backend` | the user asked behaviors to follow a handoff addressed to backend | handoff "Where to work" |
| X-1 in the REAL editor (`--mode editor`, the scale rig) through an opt-in probe after the node frame, not in a test harness | `EditorHarness` (`ClusterRunner.Integration.Tests`) is a hand-built mini editor — not the real composition | handoff X-1 "the editor's REAL world" |
| the prototype keeps the editor's ONE world and adds a world LOCK (the node frame on a worker; the drawing thread does world work only on frames that get the lock; the map draws a published copy of the gizmo frame) — ⛔ not the full §12 inversion | building the node on a second world means choosing every `_world` the editor hands out (hundreds of sites) — not a night's work; the lock version runs the REAL node frame off the main thread and shows what breaks | §12 is the target; the prototype tests its two load-bearing claims: the node frame runs off the main thread (D-7), and the map / camera can run at the render rate (D-10). What would change it: none — it is a stepping stone |
| map-only frames draw only the layers that read no world (gizmo primitives, grid, 2-D terrain) | ✔ X4-1: `EffectLayer.Draw` queried World A during a tick ⇒ `IndexOutOfRangeException` ⇒ the process aborted | §12 D-10 |
