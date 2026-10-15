<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Terrain_World.md — §4.4 / W9 OWN the two grids' fit to the terrain; CE-3018 is the fit, this is its loud check
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Collider_Grid.md — CE-2193, the same warning for the collider grid (built)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7f, the air-defence probe that found it
-->

# HANDOFF — backend: a unit outside the PERCEPTION grid is seen and heard by nobody, silently (CE-2194)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**:
scope FROZEN there; if a later document invalidates it, STOP and report (R-106).

## The measurement (`2026-10-15`, a live air-defence probe on `basic-desert`)

| | |
|---|---|
| the cluster log | `[Terrain] 'basic-desert': perception grid origin (0,0) cell 5 m; collider grid origin (-750,-750) cell 5 m` |
| `basic-desert.world.geojson` | `bounds: [0, 0, 600, 600]` ⇒ both grids are fitted correctly (CE-3018): the collider grid keeps its centred default, the perception grid covers [0, 1000) |
| the probe | a Hostile rifleman, two MANPADS gunners and two technicals at x ≥ 0, a Friend truck at (−150, 150) in the open, an idle helicopter at (−150, −100, 30): **every target memory empty for 40 s**; the same units with the truck at (150, 300) see it |
| `LocalGridBuilderSystem` | `_grid.Add` of an out-of-grid position is a silent no-op (`SpatialHashGrid.cs:142`) ⇒ the unit is invisible to sight and hearing, and nothing says so |

⇒ the same hole CE-2193 closed for the collider grid, on the other grid — and the two grids cover DIFFERENT ground on a terrain smaller
than both, so a unit can be hittable and invisible at once (here: x ∈ [−750, 0)). ⚠ It cost the air-defence probe an hour.

## The item

| row | item — ⭐ my lean | acceptance — in `LocalGridBuilderSystem`'s own suite |
|---|---|---|
| **CE-2194** | ⭐ `LocalGridBuilderSystem` warns once per entity per terrain fit when it drops one (name, position, the perception grid's extent), exactly as CE-2193's `SpatialHashSystem` does — one shared helper if that is cheaper. ⛔ Rejected: widening the perception default to the collider default (the grids follow the terrain by design, W9 — a unit off the terrain is an authoring error to surface) | an entity at (−150, 150) on a [0, 600]² terrain produces exactly one warning over 100 rebuilds; one at (150, 150) none; a re-fit that covers it stops it |

## Gates — the report contract (CLAUDE.md rule 8)

One row per gate, verbatim command, counts, delta; reds confirmed pre-existing against your base sha; clean tree; `tracker-counts.py --check`.
