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
| C-1 | the composition split + the walk rail | ⏳ | | §C-1 |
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
