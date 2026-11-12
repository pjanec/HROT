<!--STATUS
state: LIVE — C-1…C-6 first pass done
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

## C-6 — the verdict *(Opus, `2026-11-12`; folded into the design as [§14.4](../../DESIGN_Threaded_Simulation_Presentation.md))*

**Decision: GO — change nothing in §14's shape; finish the work list below.** Composing the simulation as its own node is what POC 2 lacked, and
it works:
- the node is built over S and the presentation over A, in one pass, with a rail that walks the built objects (0 / 0);
- the threaded editor runs the scripted session on the 1 000-man rig **green twice** (16 of 17 steps; the one red is a route this editor does not
  have) with **0 faults**;
- **pause settles** and **save works**;
- A updates every tick;
- the drawing thread no longer waits for a tick (VM: world frames 118–146 ms beside a 100–197 ms node frame);
- the sync of ALL types costs **6.2–6.3 ms + 0.7 ms** per tick once warm.

| what is proven | evidence |
|---|---|
| ✅ the composition split | `HROT_THREADED_KERNEL=3`: A created before the bootstrap, `_world` = A, `_nodeWorld` = S; the walk rail `WorldReferenceWalker` green after 9 hand-outs fixed (§C-1) |
| ✅ the guard sees reads as well as writes | `WorldGuard` READ checks; the stable list below is the measured cost to finish (C-2) |
| ✅ the threaded editor runs | 2 runs green, single-threaded and threaded (§C-3) |
| ✅ four UI systems left the tick | `CanvasMenuUpdateSystem`, `MenuOrders`, `MenuShortcuts`, `HatArbiter` run in a presentation step on A (C-4's first four) |

**The real cost to finish S-1…S-6 — the guard's stable list, in order:**
1. **`StationMode` per world** — the node's `StationMode` (a managed singleton both worlds hold) closes over S (`SharedApplicationBootstrapper.cs:239-240`),
   so every presentation read of the station mode reads S's `ReplayState` (~10 000 reads a run, a volatile flag — low risk, but the rule of §14.1
   "an object both worlds hold never changes in place" fails). Install one per world.
2. **The after-kernel objects** — `UnitDebugSettingsMemory` / `BrainObservation` read and write `UnitDebugSettings` in A from the worker. ⇒ the
   presentation side, with the setting riding to S as UI state. ✅ The hot-reload drain was handed S (`AiHotReloadCoordinator`, verified by the last run below).
3. **C-4 — the gizmo split** (the next build item): `SelectionRequestSystem` / `SelectionNotification` / `ToolActivationDrain` / `CenterOnEntity` and
   `GizmoGroup`'s pickers and tools. ⛔ They cannot move one by one: the tool activation mutates the gizmo managers that tick on S, and S's gizmos read
   the selection. ⇒ ONE move: a main-side gizmo buffer, the pickers + tools + pick-box producers + selection on the main thread, and the selection
   riding to S as UI state each tick (§14.1 row 6).
4. **C-5 — commands** for the guard's UI → A writes: the HTTP `edit_component` / patch routes (`EntityInfo`, `SimTransform`), the clock routes
   (`GlobalTime`, `WorldEpoch`), the follow camera's `SimTransform`, the HTTP spawn that publishes straight onto S's bus. ✅ Everything published on A's
   bus already reaches S one tick later (the spawn, delete and orders of the runs went through it).
5. **A read that writes** — `BlueprintTierSpec.Memory` reaches a blackboard through the RW accessor even to read it (188 writes into A from HTTP
   reads): a reads-are-reads path P-1 missed.
6. **The four replication systems** that mutate managed components in place (POC 2's P-1 list).

**Rejected:**
- *Fix the walk's hits by hand-out only* — four kinds escape a construction walk: a `Func<EntityRepository>`, a shared singleton's closure, objects
  built after start-up, and a callback into the presentation. ⇒ the walk AND the run-time guard, never one alone.
- *Move the selection systems alone* — S's gizmos read the selection (point 3).

**Still unknown:**
- **The drawing frame on real hardware.** The VM draws in software GL, and its frame times include that. ⇒ the Windows command:
  `python scripts/threaded-poc2-measure.py --out <dir> --mode 3 --no-guard` (it runs the same session OFF and with `=3`, every window hidden).
- Typed commands (C-5) are not built.
- The masked sync was not run in mode 3 (these runs copied ALL types: correct first, cheap second).

## The items

| # | item | state | sha | note |
|---|---|---|---|---|
| C-1 | the composition split + the walk rail | ✅ walk green at boot (0 / 0) | `840dc6ea4` `d47a9812a` | §C-1 |
| C-2 | cached queries / captured objects on one side by construction; the read guard | ✅ read guard built; the remaining list measured | `fd2253a72` `4de442bf3` | C-6 point 1–2 |
| C-3 | the threaded editor runs | ✅ green twice, 0 faults, pause settles, save | `fd2253a72` `4de442bf3` | §C-3 |
| C-4 | the UI work leaves the tick | ⚠ 4 systems moved; the gizmo split is the next build item | `d47a9812a` | C-6 point 3 |
| C-5 | typed commands | ⛔ not built; the bus forward carries published UI events | — | C-6 point 4 |
| C-6 | the verdict; the Windows command; §14 as built | ✅ | this commit | top · design §14.4 |

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

**Threaded** (the worker ticks S for good; the drawing thread takes the world lock each frame, runs the presentation step; guard ON):

| run | steps | faults | node frame (worker) | drawing frames with the world | map-only frames | sync (ALL types) |
|---|---|---|---|---|---|---|
| 1 | 16 / 17 | 0 | 158–197 ms | 1 465, mean 129–146 ms | 416, mean 109–115 ms | 18 ms + 10 ms (incl. the guard's false positive) |
| **2** | **16 / 17** | **0** | 100–135 ms | 1 659, mean 118–121 ms, p95 134–144 | 530, mean 104–106 ms | **6.2–6.3 ms + 0.74 ms** |

Between the runs: the whole sync muted for the guard (the map rebuild and event forward counted as "A read by the sim side", 4.6 M); the canvas
menu's state and the order form kept in A (UI-only singletons the sync had copied S's stale value over, 1 524 hits).
