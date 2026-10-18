<!--STATUS
state: LIVE — DISPATCHED 2026-10-18; RE-STAMPED the same day (rule 1a: not yet in origin/ui) — scope frozen at the re-stamp commit. A FRAME: you design the layout as step 1.
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — THE reason: §4a the stage (the decision), §1 the scene the battle street must hold
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Platoon_Demos.md — UI-1 (the shared building folder) and UI-2 (house-3f, roof rings) this builds on
  - docs/DESIGN_Building_Interiors.md — OWNS building templates, storeys, openings, stairs, roofs
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — (coordinator) demo-city's road net, traffic, the 34 houses (CE-4028); R-4052's "no procedural buildings" is SumoSharp's viewer buildings only (R-2053)
-->

# HANDOFF — ui: give demo-city quarters *(UI-8 … UI-12)*

**For:** the ui lane (`ui`). **FYI:** the coordinator lane, which owns demo-city's road net, traffic and its 34 houses (CE-4028) — this
adds to that terrain (cross-lane: say it in the commit). **From:** behaviors. ⭐ **Re-stamped `2026-10-18`** (one fence corrected, §2): your
scope is FROZEN at the re-stamp commit; later documents are FYI. If one invalidates an item, **STOP that item and report it** (R-106).

## 0. Why

🔒 User, `2026-10-18`: *"The democity terrain was anyway too boring, adding building there to create some quarters like living ones and
business onr and commercial ones would help. Obstacles to cover behing on the streets could be added as part of the scenario."* It is
the stage for the recorded urban battle ([DESIGN_Urban_Battle_Demo](../../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md) §4a,
`CE-2300`, not scheduled) — and a better city for every demo that runs there. Nothing here is urgent; it comes **after UI-1 and UI-2**.

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later; push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-1xxx`); name them in your report.
3. **Step 1 is a layout picture**: a top view of demo-city with the quarters and the battle street drawn on it, added to
   [DESIGN_Urban_Battle_Demo §4a](../../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md) (an SVG beside the `cqb-village` one is fine).
   Then build against it.

## 2. Fences

- ⛔ **No change to the road net or the traffic** (coordinator's): new buildings stand clear of every lane, as the 34 do.
- ⭐ **Make the buildings however suits** (R-2053): authored placements in `demo-city.houses.json` (merged by `scripts/terrain-from-sumo.py --houses`), a generator of your own, new templates — your choice. ⚠ R-4052's *"no procedural buildings"* is about SumoSharp's own viewer buildings (*"too dense and too unrealistic"*), not about ours. ⭐ Pick what keeps the quarters looking deliberate — a residential street, an office row, a market.
- ⭐ **Keep the existing promise in that file's `$comment`**: every front door faces a sidewalk, and every sidewalk point stays within
  70 m of a front door (the civilian shelter query searches 80 m). Replacing a house is fine; leaving a stretch of sidewalk without a door is not.
- ⭐ **No street cover in the terrain**: parked cars, sandbags, concrete blocks and crates are scenario objects (the existing static
  obstacle types, CE-3136) — behaviors places them per scenario.

## 3. The items

| id | what | acceptance |
|---|---|---|
| **UI-8** | **the quarter plan** (step 1): where the residential, business and commercial quarters lie, and the **battle street** — one street ≥ 300 m long running from an open edge (the convoy's approach and an attack jet's run) through the residential quarter into the market square | the picture is in the design; every new placement is listed in it by quarter |
| **UI-9** | **the templates** (beside `house-2f` / `house-3f` in the shared folder): a **courtyard house** (a walled yard, 2.5 m wall, one gate, a house on one side); an **office block** (3–4 storeys, a stairwell, windows on every storey on the street side, a flat roof reachable by the stairs); a **two-storey shop** with a flat roof and a shopfront; ⭐ *offer* a **market hall** (an open ground floor under a roof) | each loads; every door opens; the navmesh reaches every room and every roof; a roof's parapet gives crouched cover |
| **UI-10** | **the quarters placed**: residential — mostly `house-2f` / `house-3f` and courtyard houses with alleys between; business — office blocks; commercial — two-storey shops round the market square. Along the battle street, buildings on both sides with upper windows facing it, at least one walled compound, and two flat roofs that see along it | the terrain loads on every host; the fences of §2 hold |
| **UI-11** | **an open landing square** near the battle street (≥ 40 × 40 m clear, no wires or trees — a UH-60 lands a squad there) | a helicopter route to it lands without a collision |
| **UI-12** | **rails**: demo-city's own suites stay green — ⭐ start with the feature's rails (`EnterableShelterTests.CE4028_DemoCity_EverySidewalkPoint_HasAnEnterableHouseWithinTheShelterRadius` — the shelter promise — then `Hrot.Traffic.Tests`, e.g. `PedestriansPanicIntoCiviliansTests`, and `ShippedTerrainFilesAreDeployedTests`); add sight-line rows in a `premises.json` for: the battle street seen from an upper window on each side · the landing square seen from one office roof · the compound's yard hidden from the street | the shelter coverage still holds; the premises rows print as the test reports them |

## 4. Report

One row per item: built / blocked (why) · the gate commands with counts · the ids you allocated · the design section you folded as
built (a deviation goes back into the owning design).
