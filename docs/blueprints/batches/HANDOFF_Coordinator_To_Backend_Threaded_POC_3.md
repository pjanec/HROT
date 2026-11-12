<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §14 the target shape (img/threading-architecture.svg), §14.1–14.2 the anti-race strategy (img/threading-anti-race.svg), ⭐ §14.3 (on exp/threaded-poc-2) POC 2's verdict and the cost to finish
  - STATUS_Threaded_POC_2.md (on exp/threaded-poc-2) — what is built and proven, and why the clone shortcut failed
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_3.md — the other lane's experiment; independent
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — POC 3 of the threaded editor: COMPOSE the simulation as its own node

**For:** a backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there. ⭐ **Still an experiment.**
⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table, continue; a failed premise stops THAT item (R-106).
You may change the design where the code shows a better way — fold it into §14 (as-built rule).

## Where to work

⭐ A NEW branch **`exp/threaded-poc-3`** from **`exp/threaded-poc-2`** (reads are reads, the statics, the S→A sync with table + row masks, the
published gizmo frame, `WorldGuard`, `scripts/threaded-poc2-measure.py`), with **`coordinator`** merged in. Status file
`docs/blueprints/batches/STATUS_Threaded_POC_3.md` ON THAT BRANCH. ⛔ Nothing merges into a lane; ⛔ do not push to `backend`. Switch
`HROT_THREADED_KERNEL=3` (default OFF ⇒ today's editor). The VM proves correctness; leave the Windows command (every window HIDDEN).
⚠ The Windows session is measuring POC 2 now (its status file lands on `exp/parallel-sim-2`) — independent of this.

## Why — POC 2's verdict (§14.3)

The sync, the masks, reads-are-reads and the published gizmo frame WORK. ⛔ The shortcut — clone S and retarget the existing kernel — does not:
~26–28 systems cache an `EntityQuery` or capture the world at construction, so they keep reading A; the first (`MapLayerAssignmentSystem:79`)
throws every tick, the exception skips the sync, **A froze after 8 syncs**, and pause never settled. ⇒ **S must be COMPOSED as its own node**:
the editor's bootstrap builds the node and the presentation in ONE pass (`RegisterHostSystems`), so every `_world` handed out must be chosen once.

## The items — in this order, as far as the night goes

| # | item | done when |
|---|---|---|
| **C-1** | ⭐ **the composition split**: the editor builds the NODE (kernel, modules, the node frame, the orchestrator core, the clock) over S and the PRESENTATION (canvas, panels, layers, the 3-D view, tools) over A, each in one place; a rail that NO presentation object holds S and NO node system holds A (walk the constructed graph — the forwarding-rail pattern) | the rail green; the list of every `_world` hand-out site and which world it got, in the status file |
| **C-2** | the cached-query / captured-object sites (POC 2's ~26–28): each either on the node side (built over S) or on the presentation side (over A) by construction — ⛔ no "retarget at runtime" | `WorldGuard` and a READ guard (a query built on one world walked against the other throws in Debug) silent over the whole session |
| **C-3** | ⭐ the threaded editor RUNS: load the 1 000-man rig → play 120 s → pause → step ×5 → Edit → save, 0 faults; A keeps updating every tick; pause settles | the session script green twice; the drawing frame playing vs paused (VM) and the sync cost |
| **C-4** | **P-3: the UI work leaves the tick** — the gizmo module split (its world writers stay), the 26 tool gizmos, tool activation, centre-on-entity, the selection systems, the four afterKernel objects on the presentation side; a main-side gizmo buffer for the tools | A-4's per-item table re-checked item by item; picking and dragging work while playing |
| **C-5** | **P-7: typed commands** for delete, move (drag = a ghost in A, the drop one command), spawn / place, an order, play / pause / step; refused ⇒ the edit outcome | a rail each; the guard's remaining list = the cost to finish |
| **C-6** | ⭐ **the verdict** — what works, what does not, the real cost to finish S-1…S-6, go / change / stop; the Windows command | top of the status file; folded into §14 |

## Rules

- ⛔ Default OFF; 0 B per steady frame with the switch OFF (R-2084); the editor's feature rails and a scenario-regression subset green with it OFF.
- Measure before claiming; a pre-existing red names its base sha. Run anything slow in the background; never poll. ⛔ No screenshots in git.

**Done when:** C-6 is written — however far C-1…C-5 got.

---
_coordinator lane, 2026-11-12_
