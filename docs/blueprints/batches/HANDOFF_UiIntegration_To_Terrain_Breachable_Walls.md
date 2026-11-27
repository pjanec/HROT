# HANDOFF — `unreal-ig` → `terrain`: breachable walls (R-11022) — stages S1–S4

| | |
|---|---|
| **From** | the HROT-integration session (`unreal-ig`), 2026-11-27 |
| **For** | `terrain` |
| **Dispatched at** | `unreal-ig` @ the commit that adds this file (the design at `e6356cdaf`) |
| **User, verbatim** | *"we do not want to remove the whole wall, it's a nonsense. A wall is always breached. Only in part. Never the whole wall disappears."* (R-11022) · *"can you please break these to stages and tracked items? I would like to give most of it to the terrain lane"* |
| **Design** | ⭐ [DESIGN_Breachable_Obstacle_Walls.md](../../designs/engineering/DESIGN_Breachable_Obstacle_Walls.md) — **§8 (the review's answers) SUPERSEDES §2 A-1 and §5 D-2 / D-3 / D-4 where they differ**; §9 the stages. UML: §3 |
| **Scope** | frozen at the dispatch sha; documents that change after it are FYI only — a later document that invalidates an item ⇒ STOP that item and report it |

## The frame

**Goal:** a sandbag wall or a barrier line placed in a scenario is a terrain-feature WALL; it is breached IN PART by a charge, a player / ram-ordered
tank and the bulldozer's point cut (all existing paths — verified by the review); a road it crosses is closed to traffic per lane and re-opens only where
a passable gap is wide enough; every node agrees.

**Fences:** ⛔ no second breach mechanism (ruling 9) — the breach paths are used unchanged. ⛔ Road closure reads the REPLICATED breach table, never the
re-cut prisms (review V-2: those are baked only on navmesh nodes, on a wall-clock debounce). ⛔ No new struct field on `TerrainFeature` (the recorded
layout). ⛔ Base / imported walls never close roads (V-7). ⛔ Walls are scenario content placed before a run (V-6).

## The items

| stage | id | what | design | accept |
|---|---|---|---|---|
| **S1** | **CE-11069** | per-material default wall thickness in `FeatureKinds` (sandbags 0.6 m, concrete 0.4 m; others as today) | §8 V-4 | a rail: a sandbags wall feature expands with 0.6 m; contact strength of a 1 m sandbag wall ≥ a ramming car's minimum (`contact-breach.json`) |
| **S1** | **CE-11070** | ⭐ road closure by scenario feature walls: a LINE-blocker rule (a wall crossing a lane edge to edge closes it; a lane re-opens only where a Passage ≥ `minDriveThroughM` lies inside it), per lane for SUMO and the road graph, from the walls + `DoorStates.Of(…).Damage`; recompute on a change of the FREE-wall Passage set only | §8 V-1, V-2, V-7; §3a | rails: a wall across closes; a 0.8 m hole keeps it closed; a 4.7 m cut opens one lane of a 7 m road; a diagonal wall closes; a house hole does not recompute; every node agrees (no navmesh dependency); 0 B steady |
| **S2** | **CE-11071** | the roadblock CONTENT: `roadblock-blast`'s 6 × 1 × 1 m concrete barrier → a concrete feature wall in the scenario; the city-battle roadblock's walls placed in the SCENARIO (not the macro, which keeps its non-wall members) | §8 V-3, §5 D-4 | the scenarios' regression rows re-pinned: the charge breaches the wall in part, the rest stands, infantry pass |
| **S3** | **CE-11072** | ⭐ the acceptance scenario `va-dozer-breach-wall` (`demos/navigation-and-terrain/`, Unreal's ask 4): a 12 m sandbag feature wall across a road, a dozer on a point order, a SUMO car | §6 (+ §8 V-8, V-9) | `checks.json`: the wall gets a Passage; its other panels stand; the dozer passes; the road CLOSED before (the car stops) and OPEN after (the car passes) — ⭐ it measures what the review could not (SUMO stopping at a wall, the vehicle navmesh through a 4.7 m gap) |
| **S4** | **CE-11073** | ⏳ **waits for the user's two answers** — the obstacle tool's *sandbag wall* / *jersey barrier* draw a feature-wall LINE (D-1, changes R-4264 for these two types), and whether a lone 3 m piece (removed whole) may stay | §5 D-1 | do not start before the answers; the dispatching session relays them |

The gateway side (S5, CE-11074: scenario terrain features on DPS so Unreal draws the wall, gaps placed by Centre / Normal / From–To) stays with `unreal-ig`.

**Order:** S1 → S2 / S3 (S3 needs CE-11070 for its road rows). Report per the gate-report contract; fold the as-built into the design (§9 marks each stage).
