<!--STATUS
state: LIVE — in progress
updated: 2026-11-12
current-answer: C-6 the verdict (top, when written) · then the items
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §14 the target shape · §14.3 POC 2's verdict (why S must be composed)
  - HANDOFF_Coordinator_To_Backend_Threaded_POC_3.md — the dispatch (at 83314af49)
  - STATUS_Threaded_POC_2.md — what this builds on (pure reads, the sync, the published gizmo frame, WorldGuard)
-->

# STATUS — POC 3 of the threaded editor: COMPOSE the simulation as its own node

**Where:** branch [`exp/threaded-poc-3`](https://github.com/pjanec/HROT/tree/exp/threaded-poc-3), from `exp/threaded-poc-2` (`23dc7797d`) with
`coordinator` merged (`5f5105d21`, clean). ⛔ Nothing goes to a lane. Switch **`HROT_THREADED_KERNEL=3`** (default OFF ⇒ today's editor);
`HROT_THREADED_SINGLE=1` keeps the node frame on the main thread (the composition proven before the threading).

## C-6 — the verdict

⏳ not yet.

## The items

| # | item | state | sha | note |
|---|---|---|---|---|
| C-1 | the composition split + the walk rail | ✅ walk green at boot (0 / 0) | `840dc6ea4` `d47a9812a` | §C-1 |
| C-2 | cached queries / captured objects on one side by construction; the read guard | | | |
| C-3 | the threaded editor runs | | | |
| C-4 | the UI work leaves the tick | | | |
| C-5 | typed commands | | | |

## C-1 — the composition split *(Opus, `2026-11-12`)*

**How it is composed (mode 3):** A (`_presWorld`) is created BEFORE the bootstrap; the bootstrapper builds the node over its own `ctx.World` = S;
`RegisterHostSystems` hands `_world` = A to everything it builds and keeps `_nodeWorld` = S; right after the bootstrap A takes a full copy of S
(tables, singletons, the event registrations) and after each node frame the POC 2 sync (`SyncPresentationWorld`).

**The rail — walk what was BUILT** (`Fdp.Core.WorldReferenceWalker`, `HROT_POC3_WALK=1|exit`): from the kernel (its modules and systems) every path
to A, and from the canvas plus every panel / window / layer / view / browser / inspector the editor holds every path to S. Fields, collections and
delegate closures are followed; a world is a leaf.

**The walk, iterated to green** (`HROT_POC3_WALK=exit`, one boot ≈ 1 min, the walk itself 0.5 s):

| walk | node objects reaching A | presentation objects reaching S | what was done |
|---|---|---|---|
| 1 | 5 | 47 | the 47 all went through closures capturing the editor (`<>4__this`) — the composition root holds both worlds by design ⇒ the editor and its bootstrapper are leaves (⛔ NOT the node context: a presentation object holding it reads S) |
| 2–3 | 5 | **0** | `MenuOrders`, `MenuShortcuts`, `HatArbiter` (scheduled in the kernel, built over A — `MapInteractionPack.cs:378,448`) and `CanvasMenuUpdateSystem` (its `CanPaste` closure) ⇒ ⭐ **moved OFF the tick in mode 3**: a presentation step runs them on the main thread against A after the sync (`RunPresentationStep`) — the first four of C-4. `DataBreakpointManager` + its system ⇒ built over `ctx.World` (a breakpoint watches the simulated world) |
| 4 | 6 | 0 | `BlueprintDebugSession` (`AiDebugSessionComposer.ComposeBlueprint(…, _world, …)`) ⇒ `ctx.World` (the probe reads simulated entities) |
| 5 | 4 | 0 | all four through ONE closure: the breakpoint-hit handler `DebugApiService` registers — ✅ **verified by reading it** (`DebugApiService.cs:644-651`): it writes five "last hit" scalars and asks S's `NetworkEntityMap`, never A ⇒ a recorded exception (the walk stops at the service), ⚠ but it IS a callback from the tick into a presentation object: in threaded mode those scalars are written from the worker |
| **6** | **0** | **0** | ✅ |

⚠ **What the walk cannot see:** objects created AFTER start-up (at a scenario load), queries built lazily, and a world reached through a
`Func<EntityRepository>` (`() => _world`) — the walker does not call delegates. ⇒ the guard (C-2) covers those at run time.

## C-3 — the composed editor runs

**Single-threaded** (`HROT_THREADED_SINGLE=1`, the node frame on the main thread, then the sync, then the presentation step; guard ON): ✅ **16 of 17
steps** — load the 1 000-man rig, select, play 60 s, **pause settles**, step ×5, spawn / move / delete, two orders, the Edit-state move, stop,
**save**. 0 worker faults, 0 presentation-step faults. The one red, *"check unsaved: Not found"*, is a route this editor does not have (the same red
in A-1's single-world run, `STATUS_Threaded_Analysis_Instruments.md` §A-1). ⭐ **A updates every frame**: ~100 syncs / 5 s; ALL types,
**2–3 ms warm** (8.9 ms the first), rows + map + events 0.2–0.3 ms.

**The guard's list in that run — the measured cost to finish:**

| hit | ⇒ |
|---|---|
| HTTP routes write `BlueprintBlackboard4096` / `256` into A (128 + 60) — `BlueprintTierSpec.Memory` reaches the blackboard through the RW accessor even to READ it | ⭐ a reads-are-reads path P-1 missed |
| `IOrderForm` (the UI's own form), `WorldEpoch`, `GlobalTime` singletons set in A by the UI / HTTP | the order form: UI state (outside the singleton copy); the two clocks: HTTP writes ⇒ commands |
| `SelectionRequestSystem` writes `SelectionStore` / `SelectionState` in A, `UnitDebugSettingsMemory` writes `UnitDebugSettings` in A — from the tick | both reach A through a `Func` the walker cannot call; ⭐ both are on C-4's move list (selection, the afterKernel objects) |
| the HTTP spawn publishes `EntityCreationRequest` straight onto S's bus | a command (C-5) |
| `SimTransform` written by `layer.Update: Map camera` (2) | the follow camera's own write ⇒ a command, or UI state |
