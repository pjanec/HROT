<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-18); scope frozen there.
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — THE design: §12 the demo scenarios and the cqb-village map, §13 the lane split
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §16 DA-22 the roof stair house (house-3f), §13a the sector areas UI-5 draws
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — §8 the CQB test house picture (UI-3), §5a the report log (UI-6)
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — CommandPicture (UI-5), the report routes (CC-12, CC-13)
  - docs/DESIGN_Building_Interiors.md — OWNS building templates, storeys, openings, stairs, the roof slab (UI-1…UI-3)
-->

# HANDOFF — ui: the platoon demos' buildings, terrain and displays *(UI-1 … UI-7)*

**For:** the ui lane (`ui`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your scope
is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-18` (R-2046): *"Pls prepare tasks and split the acroas usual lanes (ui, backend, behaviors). As ui lane might not have as
much to do as other lanes it could help creating terrains or something."* The platoon attack / defence / clearing programme
([DESIGN_Platoon_Attack_Defend_Clear](../../designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md), approved) needs a terrain
with enterable buildings — **measured: `test-town`'s and `demo-city`'s buildings are all solid prisms** — and displays that let a person
watch a command chain work. This is the ui lane's part. ⭐ **Build against the design's §12.1 map and §6a roof table.**

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later; push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-1xxx`). **You take over two behaviors rows**: `CE-2285` (house-3f, the roof) and `CE-2290` (cqb-village) —
   append a dated line to each naming your id; behaviors will not build them.
3. UI-1 … UI-4 need nothing from another lane. UI-5 / UI-6 need behaviors' command machinery (`CE-2291`…`CE-2295`) — build the drawing
   against the documented component shapes and a test fixture, wire the live data when it lands, or STOP those two and report.

## 2. The items

| id | what | basis | acceptance |
|---|---|---|---|
| **UI-1** | the **shared building templates folder** `Recipes/Buildings/` (copied to the app's output, the path `TerrainAssets.DefaultSharedBuildingsRoot` already searches after a terrain's own `buildings/`); move `house-2f` there; `bt-range` keeps working unchanged | `TerrainAssets` (the lookup is built; the folder is not) | `bt-range` loads both houses from the shared folder (its `DemoPremisesTests` and the navmesh rails stay green); no template left in two places |
| **UI-2** | **`house-3f`** + the expander change: the template's `roof` accepts **rings** like `floor` — the roof is the top storey, its floor leaves the stairwell open, its only walls are a ≈ 3 × 3 m **stair house** with a door and a **1 m parapet**; the stair runs from the ground to the roof (FDP Terrain — say the cross-lane edit in the commit) | Defend §16 DA-22 (`CE-2285`, taken over) | a navmesh path from the front door to the roof through the stair house; the parapet gives crouched cover by the stance thresholds; ⭐ a person on the roof is seen from the street only above the parapet |
| **UI-3** | the **CQB test house** template (14 × 10 m, one storey: a hallway with front and back doors, rooms A, B, C, D (11 m²), a closet E through D, side doors N into A and S into C) — moved here from backend's BR-1 (its room graph is now derived by the survey sensor, BR-12) | Building Clearing §8 picture `img/cqb-house.svg` | it loads; every door opens; the navmesh reaches every room; placed in `bt-range` 60 m from `house-2f` (its premises rows added) |
| **UI-4** | the terrain **`cqb-village`** (`Recipes/Terrain/cqb-village/`): exactly the design's §12.1 map — OBJ (`house-3f`) with `house-2f` H1 / H3 beside it, H4 / H5 / H6 and SNP (`house-3f`) across Main Street, the Cross Road and the intersection, the walled compound CMP (2 m concrete, a west gate, four 1.2 m sandbag posts, a solid shed), the hedge row, the 1 m low wall, the wreck, the berm and the support-line berm; its **`premises.json`** rows for the sight lines the demos need | design §12.1, PC-12 (`CE-2290`, taken over) | the terrain loads on every host; premises verified: the support line sees OBJ's south windows (both storeys) · SNP sees OBJ and the support line · the hedge route is hidden from OBJ's south face · the CMP gate is seen from the NE sandbag post |
| **UI-5** | **the map shows the command picture**: the `SquadSector` and NoFire **area entities** (they are already map overlays, R-2003) styled by kind — a squad sector outlined in its unit's colour, NoFire hatched red; for the SELECTED commander its `CommandPicture` elements as symbols (size class, MG / sniper / AT flags, age fading) and a label with its platoon task's phase | Defend §13a DA-24; Command Chain §6 `CommandPicture` | a test fixture with two sectors, one NoFire room and three elements renders as specified (frame / snapshot rail); live once `CE-2291` lands |
| **UI-6** | a **Reports window** (editor + ExCon): the report log — time, kind + subtype, from → to (names), the SPOT fields, age (now − `ObservedAt`), `Synthetic` marked — with filters by unit and kind; reads the same per-node report log the HTTP route serves (PC-11) | design §12.4 PC-11; Building Clearing §5a | a fixture of 20 reports shows in order, filters work, a synthetic one is marked; live once the log lands |
| **UI-7** | ⭐ *offer* — **placement of the demo scenarios** of design §12.2: the cast at the positions and network ids given there (Blue 1 9200…, Red 9300…), saved as `scenarios/<name>/scenario.json`; behaviors then sets each unit's order params and writes the checks | design §12.2 | each scenario loads on `cqb-village` with the right entities, sides and hierarchy (squads under their platoon, men under their squads); ⚠ orders may be left empty — behaviors fills them |

## 3. Report

One row per item: built / blocked (why) · the gate commands with counts · the ids you allocated · the design sections you folded
as-built (a deviation goes back into the owning design, as-built rule). ⭐ For UI-4, the premises table as the test printed it.
