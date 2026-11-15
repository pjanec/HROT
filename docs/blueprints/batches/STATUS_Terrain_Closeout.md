<!--STATUS
state: LIVE
updated: 2026-11-15
current-answer: §1 the items, §2 lane decisions, §3 gates, §4 ids
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Terrain_Closeout.md — the dispatch (scope frozen at 1344f276e)
  - ../STATUS_Terrain.md — the first batch's report (P-0…P-6, B-1…B-6)
  - ../../PLAN_Dynamic_Terrain.md — the programme (now build-state BUILT)
-->

# STATUS — `terrain` close-out (started at `d8ddb1e16`, merged `coordinator@cf0fb9ea6`)

⭐ **In one line:** items 1–4 done; none of the three red rail sets was a code defect (each pinned a superseded design); the programme is
BUILT with six named remainders; the CE-4594 design is written and awaits the user's leans.

## 1. The items

| # | item | state | cause / evidence | design folded |
|---|---|---|---|---|
| **1** | close out dynamic terrain ([CE-4604](../Blueprint_Issues_Tracker.md)) | ✅ | **CE-4403, CE-4592, CE-4593, CE-4600 closed**, each with its acceptance and §3's runs; every task row of the plan closed; `PLAN_Dynamic_Terrain.md` → BUILT; `CAPABILITIES.md` §2b → ✅ with the remainders 📐; `USAGES.md` U-20. ⭐ **Nothing genuinely left to BUILD inside the four rows** — measured: every V-row of their slices is built (Ground §9.7–§9.17, Breaching §7.10, §7.14–§7.17); what remains is blocked on the user, on missing inputs or on another lane — see the remainders | PLAN status block; CAPABILITIES §2b |
| | **remainders (named)** | 📋 | **CE-5004** D-4 — a re-import as a new terrain version: awaits [Architect Question 112](../Architect_Question_112_Terrain_Versions_On_Reimport.md) (large blast radius ⇒ the user's) · **CE-4402** the low door on scale-1 — needs the scale terrains re-imported (input sets not in this checkout) · **CE-5002** the cold first bake (~21 ms + ~22 ms once per session — profile first) · **CE-5000** part 3 (debt) · **V-29** a squad taking its foxhole posts — behaviors lane (R-5031) · the bulldozer's map *clear here* gesture | |
| **2** | [CE-4588](../Blueprint_Issues_Tracker.md) — three 2-D terrain rails | ✅ all green under `xvfb-run -a` | ① `PresentationGizmoTests…LabelsBuildings`: an `open` surface is drawn now (Terrain Authoring §18i I-3) ⇒ 6 triangles, colours asserted. ② `FilmFrameRail.CE1380_…`: the fixture's unit had no `EntityInfo` ⇒ the empty-scenario hint (L-12 / CE-4311) covered its symbol; the film was right. ③ `Map3DFrameRail.CE1227_V_DemoCity…`: the same hint (≈ 4 300 samples) + a reference built without U-4's overview tops (≈ 1 400, every one a house's wall edge) + the close-up read on a CE-4559 cache-hit frame (0 strokes, true of that frame) | `DESIGN_Terrain_World.md` §10 rails row |
| **3** | the stale geo rail → [CE-5003](../Blueprint_Issues_Tracker.md) | ✅ | the rail pinned the deleted tangent plane's `(0, 0)`; re-pointed to UTM everywhere's local-only frame (geodetic refused, then `UseOrigin` gives a frame). Production untouched. Red-proved: the default transform made a UTM frame at (0, 0) ⇒ red; reverted ⇒ green | none needed — `DESIGN_Geo_Origin.md` §6 UE-2 / UE-a says it |
| **4** | optional [CE-4594](../Blueprint_Issues_Tracker.md) design — the terrain editor's user-friendly way | ✅ DESIGNED (nothing built) | [DESIGN_Terrain_Editor_UX.md](../../DESIGN_Terrain_Editor_UX.md): INVENTORY (graph + grep), the friction measured, UML (3 diagrams, all parse), [mockup](../../UX/terrain-editor.svg); leans TE-1…TE-12 for the user — ⚠ TE-6 (snapping ON for terrain lines) deviates from Map Interaction's off-default and is the user's call | reciprocal links in Ground §4d's doc, Terrain Authoring, Map Interaction |

**Finding (not in scope, filed):** [CE-5005](../Blueprint_Issues_Tracker.md) — the empty-scenario hint draws `?` for `—` and `▸` (raylib's default
font); the ui lane's file, not edited.

## 2. Lane decisions

None new. The rails were re-pointed to designs already ruled (I-3, L-12, U-4, CE-4559, UE-2) — no design choice was made here. The first
batch's R-5000…R-5031 still await the user's review (`STATUS_Terrain.md` §1).

## 3. Gates (rule 8)

| gate | command | `--no-build` | result |
|---|---|---|---|
| T-1 geo | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build --filter HrotEnvironmentTests` | yes (built after the edit) | 6 / 0 |
| T-1 2-D plan | `dotnet test Hrot/Subsystems/Hrot.Player.Tests --no-build --filter PresentationGizmoTests` | yes | 22 / 0 |
| T-1 display rails (Xvfb) | `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build --filter FilmFrameRail\|Map3DFrameRail\|TerrainLayer2DTests\|EmptyScenarioHintLayerTests` | yes | 65 / 1 / 1 skipped — the 1 is `Map3DFrameRail.CE4388_TheCachedWireSphere_…AndFaster` (a timing rail), pre-existing: in `STATUS_Terrain.md` §3's set failing identically at `ab59df05` |
| dynamic terrain, toolkit | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter CE4403\|CE4592\|CE4593\|CE4600\|CE4597\|TerrainWorldMergeTests\|GroundEffectTests\|CarKinematicsSystemTests\|CraterTests\|BuildingBreachTests\|TreeFellingTests\|HeightStoreTests\|ImporterTests` | yes | 186 / 0 |
| terrain import | `dotnet test Hrot/Engine/Hrot.Terrain.Import.Tests --no-build` | yes | 53 / 0 |
| SimHost bakes / roads / breaches | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter CE4600\|CE4597\|StaticObstacleBakeTests\|BuildingDamageBakeTests\|LiveOverlayBakeTests\|CraterBakeTests\|BridgeClosureTests\|TreeFellingBakeTests\|BreachEntryTests\|TerrainBakeTests\|EntityGroundDamageTranslatorTests` | yes | 47 / 0 |
| presentation, dynamic terrain (Xvfb) | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 xvfb-run -a … --filter CE4592\|CE4600\|CE4593\|GroundFeatureDetailsViewTests\|TerrainDrawPanelFrameRail\|TheDynamicTerrainLooksTests\|TheBuildingDamagePlanTests\|GroundEffect` | yes | 14 / 0 |
| stepped hash (live) | `HROT_PARALLEL_SIM=1 python3 scripts/stepped-hash.py --threads 1,4,8 --scenario roadblock-blast` · `… bt-mortar-roof` (Release runner) | built Release once | **STATE EQUAL** at 1 / 4 / 8 on both (0 types differ). ⚠ ~2 s of sim each — whether a crater / breach forms inside the window was not measured (as in the first batch) |
| red-proofs | CE-5003: production mutation (default = UTM at 0, 0) ⇒ red, reverted ⇒ green. CE-4588 ①–③: the pre-fix rails were red at this base (measured, messages above) | | |
| ledger | `python3 scripts/rulings-check.py` | | 666 / 666 |
| tracker | `python3 scripts/tracker-counts.py --check` | | OK |
| design digest | `python3 scripts/design-digest.py --check` | | red, pre-existing: old architect questions with no INVENTORY (AQ5, AQ7, AQ8, AQ9, …) — none touched |
| working tree | `git status` after every run | | clean |

⚠ Not run: the full suites of the four projects (only the feature filters above), the full-solution build. Each red named above is
pre-existing; no new skip.

## 4. Ids

- **CE-5003** (the stale geo rail, ✅), **CE-5004** (D-4, split off CE-4403), **CE-5005** (the hint's glyphs, ui lane). Next free: `CE-5006`.
- No ruling ids (TE-1…TE-12 are leans, not rulings, until the user approves).
- Cross-lane edits: none in production. The tests re-pointed live in the ui / presentation suites (`FilmFrameRail`, `Map3DFrameRail`,
  `PresentationGizmoTests`) — said here; the CAPABILITIES / USAGES / tracker rows are shared files.
