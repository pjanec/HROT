<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Building_Interiors.md — §3k OWNS the warhead round and its contact burst (CE-1032 W-3/W-4), where the as-built belongs
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §9e/§9f, the gun runs this blocks (ua-air-attack ②)
-->

# HANDOFF — backend: a warhead round that bursts on the ground never strikes the unit it flew through (CE-2192)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: scope
FROZEN there; if a later document invalidates it, STOP and report (R-106).

## The measurement (`2026-10-15`, live `ua-air-attack` ②, SimHost shot log)

An A-10 gun run on a still pickup (106, collider ✓, layers 1|2) at (1000, 0): 51–53 GAU-8 **HEI** rounds, **every one `Detonated` ~13 m
BEYOND the truck** (`end` (1013, ±4, 0)), `hit: null`, the truck at Health 100. Linear from the muzzle (−46, 0, 49.4) to the end, the round
is **≈ 0.6 m up at x = 1000** — inside the hull's 0…1.9 m band — so the final segment's raycast crosses the truck.

| the order of events, from the code | |
|---|---|
| `BallisticsSystem.cs:177-207` — the frame a warhead round's segment crosses the ground, it is cut to the ground point, **bursts there, and is marked `Spent`**; then its raycast for the same segment is published (`:210`) — the comment: *"Its last raycast still goes out (a unit in the way is struck first — a contact burst)"* | the INTENT is a contact burst on the unit |
| `HitResolutionSystem.cs:108` — `if ((bp.Warhead & WarheadRound.Spent) != 0) continue;` — *"a warhead round that already burst … is kept only so late raycasts resolve; it must not burst again"* | ⛔ so the unit in that final segment is never struck: the round burst on the ground first |

⇒ any HE round fired shallow (a gun run, a rocket run, a door gun with HE, a tank's HE at a low angle) that reaches the ground just past a
vehicle in one frame's segment (~20–70 m at 1 100 m/s) never hits it, and the ground burst is 13 m off (30 mm HEI fragments do nothing to
5 mm at 13 m). The plunging mortar of CE-1032 never shows it — its last segment is near vertical.

## The item

| row | item — ⭐ my lean | acceptance — in `BallisticsSystem` / `HitResolutionSystem`'s own suites |
|---|---|---|
| **CE-2192** | ⭐ a raycast from the round's **burst frame** is still resolved when its hit lies **before** the burst point along the segment: the unit is struck (its damage and penetration as any hit) and the burst moves to the hit point; ⛔ a LATE raycast (from an earlier frame) on a spent round stays skipped, as today | a 30 mm HEI round whose last segment crosses a truck 13 m before the ground strikes the truck and bursts on it; the CE-1032 roof / mortar rails unchanged; a round that bursts on the ground with no unit in its last segment unchanged |

FYI, behaviors already shipped (`behaviors`): a gun's / rocket's aim on an entity is its MIDDLE, not its ground point
(`AirAttackNodes.AimPoint`); a door gun's round keeps its helicopter's velocity (A4 in `FireProcessingSystem`, and `LedAtAircraft` now reads the
carrier the same way — cross-lane, both railed in your `FireProcessingSystemTests`); a carrier in flight never meets its own riders
(`DroneImpactSystem`, railed in `DroneImpactSystemTests`; `LineOfFire.RidesWith` is internal now).

## Gates — the report contract (CLAUDE.md rule 8)

One row per gate, verbatim command, counts, delta; reds confirmed pre-existing against your base sha; clean tree; `tracker-counts.py --check`.
