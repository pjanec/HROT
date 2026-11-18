<!--STATUS
state: LIVE — for the coordinator · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Terrain_Moved_Rows.md — the three rows this answers
  - HANDOFF_Coordinator_To_Terrain_Thermal_Drivers.md — CE-4662, done earlier the same day
  - STATUS_Terrain_Queue.md — the gate rows
-->

# NOTE — terrain → coordinator: the moved rows and the thermal half — what was found

## Done

| row | result | sha |
|---|---|---|
| **CE-4662** thermal drivers, environment half | a thermal class on every terrain material AND ground surface (exported for the image generator), "blocks infrared" on a smoke cloud, `WeatherConditions.Since` on the wire | `c786c4b0e` |
| **CE-4664** ground heat marks | the `GroundHeatMarks` record (493 / descriptor 242 / TKB 7209), its writer, `MarkWarmth`, and the entry point backend's `HeatSourceSystem` calls — [NOTE to backend](NOTE_Terrain_To_Backend_Ground_Heat_Marks.md). Its scenario `th-vehicle-left-warm-spot` is backend's with CE-4658 | `fbcb5ccb6` |
| **CE-3471** a pig through its pen's wall | ⭐ a real hole in the SHARED hull guard (every vehicle and animal): a mover held at a wall restarts from rest, its steps shrink, a float32 coordinate lands EXACTLY on the face, and the T = 0 crossing was read as "already inside" and let through. Now a point on or inside a wall may move out, never deeper. Not "another mover": every animal position writer was swept | `2f8eaa0c3` |

Every new rail red-proved (one guard rail stated as not a reproduction). Scenarios: `farm-under-fire`, `bd-destruction`, `tt-ground-shaping`, `tt-nav-los` pass.

## ⚠ For you to know

| finding | where |
|---|---|
| ⚠ **CE-5022 filed** — `tt-dozer-breach` 15/16 ("Fence Rifle gets through the cut"): red with CE-3471's change switched OFF too; it passed 16/16 at `6718271d5` (`2026-11-16`) ⇒ a merge since then broke it. Not bisected | tracker CE-5022 |
| `SimHostCoreLogicPackTests` asserted 16 systems while the pack had 22 at base (3 were terrain's own minefield writers — my miss) → now 23 | `fbcb5ccb6` |
| two LANE rulings wait for review: **R-5065** (ground surfaces get thermal classes — TD-9 named materials only) · **R-5066** (the smoke cloud carries "blocks infrared" as a FIELD: every cloud is the one bare TKB type) | [DECISIONS_Terrain.md](DECISIONS_Terrain.md) |
| backend is asked to use terrain's `ThermalPowerClass` / `HeatMarkEnd` enums in `ThermalSignatureDto` (one enum each), and `ThermalDriversOps` is now a `partial` class | NOTE to backend |

## ⚠ CE-3424 — measured, NOT closed: a decision for you

📐 The one-side march, 1 000 men on scale-1 (Debug, 4 cores, real time): **4–5 / 12** sampled movers moved; a 40-man probe 18 / 40.
- Paths are no longer the cause (69–89 µs, ~59 nodes per plan, never capped).
- **19 of the 22 stuck report `Arrived` after 2–3 m** toward a goal hundreds of metres away: the planner answers an unreachable goal with the nearest polygon (`DotRecastNavmeshProvider.PlanPath` drops `complete` on purpose), and the follower calls that "arrived".
- The best reachable polygon is ~3 m from the man's START ⇒ HE is cut off. Lean (not measured): the rig places each squad's men on a 2 m grid beside a road point, on scale-1's steep ground ⇒ a navmesh island.
- And the brain holds intent 19 while the muscle runs 15 (the re-orders do not reach it).

| question | lean |
|---|---|
| fix the rig's placement (men ON the road) and re-measure? | ⭐ yes, first — cheap, terrain can do it; if the movers then move, the rest is a rig artifact |
| should a partial path end `FailedUnreachable`, not `Arrived`? | ⭐ yes — a squad that "arrived" 300 m short is a product defect; it is navigation (backend's) |
| why do the brain's re-orders not reach the muscle? | ⏳ unmeasured; worth one look on the kept cluster |

📄 [DESIGN_Scale_Rig.md](../../DESIGN_Scale_Rig.md) §5.3 · tracker CE-3424.
