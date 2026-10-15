<!--STATUS
state: LIVE — RE-DISPATCHED (rule 1a: backend's branch never contained the first dispatch, f3c85142e) at the commit that rewrites this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: "The item" — CE-2193. CE-2192 is WITHDRAWN (misdiagnosed), see HISTORY.
stale-below: ## ⛔ HISTORY — the CE-2192 measurement and item; do not build it.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Terrain_World.md — §4.4 OWNS the collider grid's fit to the terrain (W9, CE-3018), where CE-2193's as-built belongs
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §9g, the live ua-air-attack run that found it
  - docs/DESIGN_Building_Interiors.md — §3k, the warhead round's contact burst (CE-2192 was about it; it is not defective as far as measured)
-->

# HANDOFF — backend: a unit outside the collider grid is silently unhittable (CE-2193) · CE-2192 WITHDRAWN

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Re-dispatched at the commit that rewrites this file**
(rule 1a: `git merge-base --is-ancestor f3c85142e origin/backend` ⇒ no). Scope FROZEN there; if a later document invalidates it, STOP and
report (R-106).

## ⛔ CE-2192 is WITHDRAWN — do not build it

⚠ My measurement was wrong, not the engine. Every target in the first `ua-air-attack` stood at |x| or |y| ≥ 1 000 m on `basic-desert`,
outside its collider grid (±750 m). With the same A-10 gun run on a pickup moved to (500, −150), the GAU-8 rounds report
`outcome: Hit, hit: <the pickup>` and the pickup dies (live, `2026-10-15`). ⇒ the "detonated 13 m beyond, `hit: null`" was the out-of-grid
miss below, not the burst ordering. The `Spent` skip in `HitResolutionSystem.cs:108` was never shown to lose a hit.

## The measurement behind CE-2193

| | |
|---|---|
| `SpatialHashGrid.Add` (`SpatialHashGrid.cs:142`) | an entity whose cell is outside the grid: `return; // Out of bounds` — never added, no word |
| `RaycastSolverSystem` (`:131`) | a round's narrow phase walks only `grid.QueryNeighbors` ⇒ that entity cannot be met by any round |
| `SpatialGridFit.For` (CE-3018) | the grid is fitted to the TERRAIN's bounds, never to where entities stand ⇒ a unit placed off the terrain stays outside |
| live, `ua-air-attack` | 1 358 door-gun rounds at a truck at (0, 6 000): 0 hits. Moved to (−350, 300): 9 of its first 30 hit and it died |

⇒ a scenario that places a unit off its terrain gets a unit that is invisible to rounds, near-miss, avoidance and the radius search, and
nothing says so — the first hour of this live check went on it.

## The item

| row | item — ⭐ my lean | acceptance — in `SpatialHashSystem`'s own suite |
|---|---|---|
| **CE-2193** | ⭐ `SpatialHashSystem` counts the colliders it could not place, and the FIRST time an entity is dropped it logs one warning naming it, its position and the grid's extent (*"… is outside the collider grid […]: no round, avoidance or radius search can meet it"*) — once per entity per terrain fit, never per frame. ⛔ Rejected: widening the fit to the entities (the grid follows the terrain by design, W9; a unit off the terrain is an authoring error to surface, not to absorb) · a scenario-load check only (misses a unit that drives off) | a collider at (2 000, 0) on a ±750 m terrain produces exactly one warning over 100 frames; one at (100, 0) none; a terrain re-fit that covers it again stops the count |

## Gates — the report contract (CLAUDE.md rule 8)

One row per gate, verbatim command, counts, delta; reds confirmed pre-existing against your base sha; clean tree; `tracker-counts.py --check`.

## ⛔ HISTORY — the withdrawn CE-2192 (first dispatch, `f3c85142e`)

> # HANDOFF — backend: a warhead round that bursts on the ground never strikes the unit it flew through (CE-2192)
>
> **For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: scope
> FROZEN there; if a later document invalidates it, STOP and report (R-106).
>
> ## The measurement (`2026-10-15`, live `ua-air-attack` ②, SimHost shot log)
>
> An A-10 gun run on a still pickup (106, collider ✓, layers 1|2) at (1000, 0): 51–53 GAU-8 **HEI** rounds, **every one `Detonated` ~13 m
> BEYOND the truck** (`end` (1013, ±4, 0)), `hit: null`, the truck at Health 100. Linear from the muzzle (−46, 0, 49.4) to the end, the round
> is **≈ 0.6 m up at x = 1000** — inside the hull's 0…1.9 m band — so the final segment's raycast crosses the truck.
>
> | the order of events, from the code | |
> |---|---|
> | `BallisticsSystem.cs:177-207` — the frame a warhead round's segment crosses the ground, it is cut to the ground point, **bursts there, and is marked `Spent`**; then its raycast for the same segment is published (`:210`) — the comment: *"Its last raycast still goes out (a unit in the way is struck first — a contact burst)"* | the INTENT is a contact burst on the unit |
> | `HitResolutionSystem.cs:108` — `if ((bp.Warhead & WarheadRound.Spent) != 0) continue;` — *"a warhead round that already burst … is kept only so late raycasts resolve; it must not burst again"* | ⛔ so the unit in that final segment is never struck: the round burst on the ground first |
>
> ⇒ any HE round fired shallow (a gun run, a rocket run, a door gun with HE, a tank's HE at a low angle) that reaches the ground just past a
> vehicle in one frame's segment (~20–70 m at 1 100 m/s) never hits it, and the ground burst is 13 m off (30 mm HEI fragments do nothing to
> 5 mm at 13 m). The plunging mortar of CE-1032 never shows it — its last segment is near vertical.
>
> ## The item
>
> | row | item — ⭐ my lean | acceptance — in `BallisticsSystem` / `HitResolutionSystem`'s own suites |
> |---|---|---|
> | **CE-2192** | ⭐ a raycast from the round's **burst frame** is still resolved when its hit lies **before** the burst point along the segment: the unit is struck (its damage and penetration as any hit) and the burst moves to the hit point; ⛔ a LATE raycast (from an earlier frame) on a spent round stays skipped, as today | a 30 mm HEI round whose last segment crosses a truck 13 m before the ground strikes the truck and bursts on it; the CE-1032 roof / mortar rails unchanged; a round that bursts on the ground with no unit in its last segment unchanged |
>
> FYI, behaviors already shipped (`behaviors`): a gun's / rocket's aim on an entity is its MIDDLE, not its ground point
> (`AirAttackNodes.AimPoint`); a door gun's round keeps its helicopter's velocity (A4 in `FireProcessingSystem`, and `LedAtAircraft` now reads the
> carrier the same way — cross-lane, both railed in your `FireProcessingSystemTests`); a carrier in flight never meets its own riders
> (`DroneImpactSystem`, railed in `DroneImpactSystemTests`; `LineOfFire.RidesWith` is internal now).
