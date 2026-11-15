<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../PLAN_Dynamic_Terrain.md — the programme (CE-4604): its task table P-0…P-6, B-1…B-6 and its acceptance
  - ../../DESIGN_Ground_Patches_And_Road_Blockage.md — CE-4403 / CE-4592 / CE-4593 (§4, §4a, §4c, §9.5)
  - ../../DESIGN_Vehicle_Breaching.md — CE-4600 (§7.4 B-3, §7.5 item 16)
  - ../../DESIGN_Map_3D_Mode.md — CE-4588's rails (the 2-D terrain plan)
  - HANDOFF_Coordinator_To_Terrain.md — the first batch (all 13 tasks, first version built)
-->

# HANDOFF — coordinator → `terrain`: close out dynamic terrain + three red rails (+ an optional design)

**For:** the `terrain` lane (ids `CE-5000`…, `R-5000`…). **From:** coordinator. **Dispatched at `1344f276e`**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-15`: *"I have one lane Free, maybe if there is some non-conflicting tasks, like some fixes, we can give it to them, to the terrain
lane"* · *"Okay, let's write that handoff."*

## 0. Start
`git fetch origin coordinator terrain`; on `terrain`, merge `origin/coordinator`; restore the test projects you will run; push
`chore: started terrain close-out at <sha>`. Status file: `STATUS_Terrain_Closeout.md`, updated at each item's end.

## 1. How you work — AUTONOMOUS
| | |
|---|---|
| ⭐⭐ **decide, never wait** — an open number or choice is yours: decide by the claim table (code + design), build, record an `R-5xxx` lane decision with what would change it | |
| ⛔⛔ **stay OUT of the radar-SAM files** — `backend` (combat, perception, air: `GuidedRoundSystem`, `AimAndFireExecutor`, `AirDefence*`, `AirAttack*`, the EQS sensors, `NedTkbCatalog`'s vehicles) and `ui` (`VisualFamily`, `ShapeKit`, 3-D vehicle bodies) are building it now | a needed edit there ⇒ STOP that item and report |
| ⭐ **a defect is fixed where it lives**, with a rail in that feature's own suite (T-1); the owning design folded (obligation ⑤) | |
| ⭐ a failed premise stops ONE item | |

## 2. The items
| # | item | done when |
|---|---|---|
| **1** | ⭐ **Close out dynamic terrain** ([CE-4604](../Blueprint_Issues_Tracker.md)). Four rows are still OPEN though your lane built most of them: **CE-4403** (the pad under imported houses), **CE-4592** (the ground-shaping tool), **CE-4593** (ditches, foxholes), **CE-4600** (a heavy vehicle breaks weak barriers — B-3b; §7.5 item 16, the barrage numbers that waited on craters P-4). For each: measure the as-built against its design's acceptance, BUILD what is genuinely left, then close the row with its evidence (rails, design section). Then run the programme's acceptance (the plan's §-acceptance, live where it says live, three runs) | every row of the plan's task table closed or carrying a named remainder; `PLAN_Dynamic_Terrain.md` build-state BUILT (or the named remainder); `CAPABILITIES.md` §2b's "designed and planned" mark updated to what is real; `USAGES.md` row if one becomes real |
| **2** | **CE-4588 — three rails red** before any recent work (confirmed at `b9945da28`): `FilmFrameRail.CE1380_A2DFilmFrame_IsTheWindowsMap_…` (`:243`), `Map3DFrameRail.CE1227_V_DemoCity_TheTerrainIsARetainedLayer_UnderTheGizmos` (`:1729`), `PresentationGizmoTests.TerrainWorldGizmo_FillsTheWorld_OnTheBottomLayer_AndLabelsBuildings` (`:217`, 6 triangles where 4 are expected) — all the 2-D terrain plan. Root-cause each: a code defect ⇒ fix the code; a rail that pins a superseded design ⇒ re-point it to the design as it is now, saying which design section | the three green under `xvfb-run -a` (display rails skip silently otherwise — report counts, not "passed"); each one's cause in the row |
| **3** | **the stale geo rail**: `Hrot.Core.Tests` `HrotEnvironmentTests.CreateGeoTransform_HasNoDefaultOrigin_…` is red since UTM-everywhere ([CE-4396](../Blueprint_Issues_Tracker.md), R-4197: `LocalOnlyFrame.ToGeodetic` throws by design). Read R-4197 / the UTM design first; re-point the rail to the as-designed behaviour (⛔ never relax production to make it pass). Allocate a row for it | green; the row closed with its cause |
| **4** | ⏸ **OPTIONAL, only after 1–3: DESIGN (no build) [CE-4594](../Blueprint_Issues_Tracker.md) — the terrain editor's user-friendly way** (woods, walls along a line, planting trees, obstacles, ground shapes, ditches). The machinery exists (Terrain Authoring T-2 / U-7, CE-4429); this is the UX — 🔒 user: *"These deserve a new design with a lot of thinking what would be the user-friendly way how to do that"*. Binding: Ground §4d (one feature family, one patch type, the stack). INVENTORY first, mockups in house style (`scripts/mockup_kit.py`, R-4143), leans per decision for the user | a `docs/` design with INVENTORY, UML, mockups and leans, `build-state: DESIGN`; ⛔ nothing built |

## 3. Gates
T-1 first (each feature's own suite); every touched behaviour or sim rail with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`;
each new rail red-proved by a compiling mutation; 0 B per steady frame (R-2084); invariant number output (R-4221). Build the affected TEST
project in a fix loop, never the solution; live runs in the background.

## 4. The report — `STATUS_Terrain_Closeout.md`
① per item: built / closed / stopped (the failed premise) and the design section folded · ② lane decisions with what would change each ·
③ the gate-report contract (rule 8): one row per gate, a `--no-build` column, reds proven pre-existing at a base sha · ④ ids allocated.
