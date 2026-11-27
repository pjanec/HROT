<!--STATUS
state: LIVE
build-state: BUILDING — S1, S3 built; S2 city-battle built, roadblock-blast open (§9) (rev 2 + the review's answers in §8, which SUPERSEDE §2 A-1, §5 D-2 / D-3 / D-4 where they differ)
updated: 2026-11-27 (rev 2: a placed wall is a terrain-feature WALL; rev 1's segment design is HISTORY)
current-answer: §8 the review's answers (supersede §2 A-1, §5 D-2/D-3/D-4) · §2 the decision · §3 the UML · §5 decisions · §6 acceptance · §9 stages
stale-below: ⛔ HISTORY (rev 1, segments) — must NOT be quoted as the design
known-rot: none
known-conflict: DESIGN_City_Battle_Showcase.md D-6 ("removal mandatory, even as disappearing") — still true for every non-wall obstacle; a WALL is breached in part (R-11022) · DESIGN_Terrain_Editor_UX.md TE-5 / R-4264 (drawn sandbag walls and jersey barriers are obstacle ENTITIES) — rev 2 proposes them as feature walls (D-1, the user's call)
related-designs:
  - DESIGN_Vehicle_Breaching.md — OWNS contact breaching of walls (ram, push, the dozer's blade, B-D3 / B-D11, D-1: one record for buildings and free walls); used unchanged
  - DESIGN_Terrain_Import_UTM.md §8 — OWNS the breach rule (BreachRule, breaches.json, Passage vs Hole); used unchanged
  - DESIGN_Terrain_Authoring.md §18 — OWNS terrain features (TerrainFeature kind Wall, the overlay compile, the live overlay); a placed wall IS one
  - docs/DESIGN_Ground_Patches_And_Road_Blockage.md — OWNS road closure (RoadBlockage, P-2); this adds free walls to its blockers
  - DESIGN_Terrain_Editor_UX.md §5 TE-5 — OWNS the obstacle tool (R-4264); D-1 changes what its wall types make
  - DESIGN_Peek_And_Fire.md §9.9 — OWNS obstacle destruction (CE-6003); unchanged for the non-wall obstacles
  - designs/group-maneuvers/DESIGN_City_Battle_Showcase.md D-6 — OWNS the roadblock drill; its roadblock content per D-4
  - SimNetGateway docs/design/DD-035 (Obstacle topic) and Unreal's TerrainFeature ask — the client side; a TerrainFeature topic is a separate gateway design (D-6)
-->
# Breachable walls — a placed wall is a terrain-feature WALL, breached in part like every other wall

🔒 **R-11022, user `2026-11-27`:** *"A wall is always breached. Only in part. Never the whole wall disappears."* · on rev 1's review: *"write a design
first and run an independent review if it will work as we expect"*.

## 1. INVENTORY — the queries run (`2026-11-27`)

| query | total | found |
|---|---|---|
| `search_graph name_pattern ".*Breach.*" label Class` | 17 | `BuildingDamage.WallBreach`, `BreachRule`, `BreachTable`, `ContactBreach(+Event, Table)`, the wire (`DdsWallBreach`, `ContactBreachTranslators`), behaviours — the ONE wall-breach mechanism (buildings + free walls, Vehicle Breaching D-1) |
| `… ".*Obstacle.*" label Class` | 25 | `StaticObstacle`, `ObstacleShape`, `ObstacleDamage`, `ObstacleRemovalSystem`, `StaticObstacleBakeSystem`, `ObstacleRowOptions`, `ClearObstacleBehavior` … |
| `… ".*WallGroup.*"` / `.*BladeCut.*` | 1 / 5 | `WallGroups` (buildings + free-wall lines); `BladeCut` (the dozer's cut of a wall at a point) |
| grep `RoadBlockage` | 1 caller | `BridgeClosureSystem.Recompute` (`:212`) — blockers: obstacle entities, craters, marked minefields |
| grep `TerrainFeature.Wall` | expansion | `FeatureKinds.OfKind` (`FeatureKinds.cs:34-35`): a `wall` line part, material = `Template`, height = `Pad`, label `<entity guid>/0` |
| grep `LiveOverlay` | 1 holder | the editor's bake (`EditorNodeBootstrapper.cs:396`); other hosts get the COMPILED overlay (`StorageProcessManager.CompileTerrainOverlay`, at save) |
| `git grep TerrainFeature / 8818 -- macros` | 0 | ⚠ no macro carries a terrain feature today |

⚠ `check_index_coverage` is not available through the CLI — the absence claims rest on the graph plus grep.

## 2. The decision

⭐ **A placed wall — a sandbag wall, a barrier line — is a terrain feature of kind `Wall`** (a polyline, material `sandbags` / `concrete`, its height),
not an obstacle entity. It is expanded into free-wall PANELS like any authored or imported wall, so **every existing wall breach applies unchanged**:

| what breaches it | the existing path | the gap |
|---|---|---|
| a charge, a shell, a bomb | `AreaEffectSystem.BreachWalls` → `BreachRule` (scaled distance, the material's resistance) | by the charge; ⭐ a cut to the ground on a low wall is a Passage (CE-4599, `BreachRule.Classify`) |
| a tank ramming / pushing | `ContactBreach` (B-D3: sandbags are a small barrier a heavy armoured hull drives through) | hull-wide (`CutAlong(… hull.Width + 2·CutMarginM)`) |
| the bulldozer | the point order's blade cut (B-4, `BladeCut`) | blade-wide |

The record is `BuildingDamage` of the wall's group (keyed by the feature's stable label, remapped by key across loads and re-sorts); the gateway
already sends it (`BuildingDamage`, `wall:<label>`). ⭐ **The whole wall never disappears** — by construction: walls are only ever breached.

What is ADDED (each small, each in an existing place):

| # | addition | where |
|---|---|---|
| A-1 | ⭐ **road closure counts free walls**: the world's free-wall prisms (after the breach re-cut) join the blockers; recomputed when the world changes (a bake, a breach) | `RoadBlockage` (a new `AddFreeWalls(TerrainWorld, …)`), `BridgeClosureSystem.Recompute` |
| A-2 | the obstacle tool's **wall types draw a feature wall** (a line), not N entities (D-1) | the terrain editor's obstacle tool (TE-5) |
| A-3 | the roadblock content: a barrier line becomes a feature wall (D-4) | `scenarios/showcases/city-battle/features/roadblock-blast`, `macros/city-battle/roadblock` (per D-4) |
| A-4 | Unreal draws the wall: the scenario's terrain features on DPS (D-6) | the gateway (its own design) |

## 3. UML

### 3a. Classes — existing (plain) and added (★)

```mermaid
classDiagram
  class TerrainFeature { Kind=Wall Template=material Pad=height }
  class EditablePolyline { Points }
  class FeatureKinds { OfKind() }
  class TerrainWorld { Panels Prisms }
  class BuildingDamage { group key breaches }
  class BreachRule
  class ContactBreach
  class BladeCut
  class RoadBlockage { Blockers() AddCraters() AddMinefields() ★AddFreeWalls() }
  class BridgeClosureSystem { Recompute() }
  TerrainFeature --> EditablePolyline
  FeatureKinds ..> TerrainFeature : expands to a wall line
  FeatureKinds ..> TerrainWorld : panels + prisms
  BreachRule ..> BuildingDamage
  ContactBreach ..> BuildingDamage
  BladeCut ..> BuildingDamage
  BuildingDamage ..> TerrainWorld : re-cut prisms
  BridgeClosureSystem ..> RoadBlockage
  RoadBlockage ..> TerrainWorld : ★ free-wall prisms
```

*What the picture shows that prose hides:* the only new arrow is road closure reading the wall prisms; every breach arrow already exists.

### 3b. A dozer cuts a sandbag wall across a road

```mermaid
sequenceDiagram
  participant D as Dozer (point order)
  participant BC as BladeCut
  participant BD as BuildingDamage
  participant W as TerrainWorld re-cut
  participant RC as BridgeClosureSystem
  participant G as Gateway
  D->>BC: cut at the wall point (blade width)
  BC->>BD: a Passage on the wall's panel
  BD->>W: the panel's prisms with the gap
  W->>RC: a new world - recompute
  RC->>RC: blockers incl. the free walls, the gap spans the road
  RC->>RC: the road re-opens (SUMO, the road graph)
  BD->>G: BuildingDamage wall-label (already sent)
```

### 3c. Who runs it

```mermaid
graph TD
  ED[Editor: LiveOverlay] -->|expands features live| TW[TerrainWorld]
  SAVE[Scenario save: OverlayCompiler] -->|compiled overlay| NODES[every other node: merged at load]
  NODES --> TW
  TW --> BCS[BridgeClosureSystem on every node with roads]
  BCS --> RB[RoadBlockage + ★ AddFreeWalls]
```

*Caption:* a node that has not been given the compiled overlay (a scenario never saved since its walls were drawn) has no wall — true for every
feature today (§7 row ②).

## 4. Rejected alternatives (one line each)

- **Segments** (rev 1): a dozer and a ram open ONE segment, orders name one entity, ids are wall-clock, placement leaves seams — rebuilding for obstacle
  rows what feature walls already do (review §HISTORY).
- **Obstacle walls as panels in `BuildingDamage`**: a dynamic panel set in a record indexed by panel; the key remap exists, but a feature wall already
  IS that, with authoring, save and the overlay solved.
- **One wall entity with a gap list**: a second breach mechanism (ruling 9).

## 5. Decisions — leans for the user's approval

| # | question | ⭐ lean | why / what would change it |
|---|---|---|---|
| **D-1** | what the obstacle tool's *sandbag wall* and *jersey barrier* make | ⭐ a **feature wall** line (material `sandbags` / `concrete`, height 1.0 / 0.8 m, the types' sizes); the single 3 m PIECE stays placeable as an obstacle entity (a lone piece, removed whole, as today) | ⚠ it changes R-4264 for these two types (drawn ⇒ a wall, not entities) — the user's call. Keeping both ⇒ a drawn row of pieces still vanishes piece by piece |
| **D-2** | road closure | ⭐ every FREE wall of the world (`Building == -1`), not only the overlay's | the rule is geometric (a wall across a road's full width); a base wall across a road is as closed |
| **D-3** | wall thickness | ⭐ the material's (sandbags 0.6 m via the feature's thickness, set by the tool) | ⚠ `FeatureKinds` passes thickness 0 for `Wall` (= the parser's default) — to set |
| **D-4** | the roadblock content | ⭐ `roadblock-blast`'s 6 × 1 × 1 m concrete barrier across the gate → a concrete feature wall; the city-battle macro's MG-nest sandbags (cover, off the road) → feature walls ONLY if macros can carry features (⚠ none does today — §8 to check) | the gate barrier is a barrier line (R-11022); MG-nest cover is a wall too |
| **D-5** | which non-wall obstacles stay entities | ⭐ car, concrete block (a lone 2 m block), crate, barrels, booth, wreck — removed whole (D-6 of the city battle) | they are objects, not walls |
| **D-6** | how Unreal sees the wall | ⭐ the scenario's terrain features on DPS — Unreal's open ask — as a gateway design of its own; the wall's label = the breach record's key, so the existing `BuildingDamage wall:<label>` cuts the drawn wall | without it Unreal sees the gaps of a wall it does not draw |

## 6. Acceptance — scenarios (R-4277)

| scenario | expect rows (`checks.json`) |
|---|---|
| ⭐ **`va-dozer-breach-wall`** (new, `demos/navigation-and-terrain/`, Unreal ask 4) — a 12 m sandbag feature wall across a road; a dozer on a point order; a car beyond | the wall gets a Passage at the cut (a `BuildingDamage` breach on its label); **the wall's other panels stand**; the dozer passes the wall line; the road was CLOSED before (a SUMO car stops) and OPENS after (the car passes) |
| `roadblock-blast` (D-4) | the charge breaches the concrete wall at the gate in part; the street opens for infantry; the rest of the wall stands |
| a tank rams a sandbag wall | a hull-wide Passage; the rest stands |

Rails: `RoadBlockage.AddFreeWalls` (a wall across a road closes it; a breach spanning the road's width re-opens it; a breach narrower than the road
keeps it closed); the closure recomputes on a breach (a new world instance).

## 7. Claim table

| the design rests on | code — how it IS | design basis — how it was MEANT |
|---|---|---|
| a wall feature expands to a free-wall line of its material and height | ✅ `FeatureKinds.cs:34-35` | ✅ Terrain Authoring §18 |
| free walls are breached in part by blast, ram, push and the blade | ✅ `BreachRule`, `ContactBreach` `:241, 308-320`, `BladeCut` | ✅ Vehicle Breaching D-1, B-D3, B-D7, B-4 |
| a low wall cut to the ground is passable | ✅ `BreachRule.Classify` (CE-4599) | ✅ Vehicle Breaching V-6 |
| feature-wall breach records survive loads / re-sorts by key | ✅ `RemapDamageEntities` (`TerrainResidency.cs:687-700`), `BuildingDamageSave.cs:85-161` (from rev 1's review) | ✅ CE-4427 R-4, CE-4599 |
| road closure does not count walls today | ✅ `RoadBlockage.cs:23-31`, `BridgeClosureSystem.cs:212-214` | ✅ Road Blockage P-2 (obstacles, craters, minefields) |
| a breach makes a NEW world whose free-wall prisms have the gap (what A-1 would read) | ⛔ **not measured** — sight and fire read breaches at QUERY time (B-D8); whether the prisms are re-cut, and when, decides how A-1 reads a gap | — ⇒ §8 to verify |
| the closure recomputes when the world changes | ⚠ it recomputes on the obstacle signature, craters, minefields, decks, and re-judges the collapse table on a new world (`BridgeClosureSystem.cs:150-200`) — ⛔ a breach's new world alone does not set `changed` today | — ⇒ A-1 must add that trigger |
| every node bakes the feature wall | ⚠ the editor live; others from the compiled overlay (at save) | ✅ Terrain Authoring §18c / O-8 |
| sandbags breach easily, concrete hard | ✅ `materials.json`: sandbags 100, concrete 1500 mm RHA / m | — |
| the gateway already sends free-wall breaches | ✅ `BuildingDamage` with `wall:<label>` (Unreal's roadblocks note §"Also") | ✅ DD-030 / T-075 |

## 8. Independent review of rev 2

Verdict (a fresh agent, the code, `2026-11-27`): *"the breach half works today for a scenario feature wall … the road-closure half (A-1) will not work
as written … the macro half of D-4 is not possible today."* ✅ Verified true: overlay free panels are indexed and breached (`BuildingPanelGrid.cs:77-81`,
`AreaEffectSystem.cs:582-599`); one wall line per feature with a stable key, remapped by key (`BuildingDamage.cs:242-266`, `TerrainResidency.cs:686-700`);
breaches replicated by key; a node without the overlay gets "no wall", nothing worse (`TerrainObjectRequests.cs:75-104`); the hull guard sees breaches at
query time (`TerrainWorld.cs:1004-1020`); the dozer cuts hull width + 1 m (`BladeCut.cs:107-122`).

| # | sev | finding (evidence; ✅ = re-checked by the main session) | ⭐ the design's answer |
|---|---|---|---|
| V-1 | ⛔ blocker | ✅ `RoadBlockage.Covered` closes a road only where ONE station's whole cross-line is inside a blocker, stations 0.5 m apart (`:77-100`); a 0.3 m wall is caught only by luck, a diagonal one never; SUMO samples ~1.5 m apart; and any ≥ 0.5 m gap re-opens — a 0.8 m infantry hole would open the road to cars | ⭐ **A-1 becomes a LINE-blocker rule** of its own: a free wall that crosses a lane from edge to edge closes it; it re-opens only where a Passage at least `minDriveThroughM` (4.2 m) wide lies inside that lane. Judged per lane (SUMO gets per-lane closures: a 4.7 m cut opens one lane of a 7 m road) |
| V-2 | ⛔ blocker | ✅ the panel is re-cut only by the bake, only for Passages, only on nodes with a navmesh (`TerrainResidency.cs:582 BakesDamage`), after a 0.5 s wall-clock debounce — `BridgeClosureSystem` runs on every node ⇒ nodes would disagree, and not deterministically | ⭐ **A-1 reads the free-wall PANELS + the replicated breach table** (`DoorStates.Of(repo, terrain).Damage`, its Passages) — as craters and minefields are read — never the re-cut prisms; the trigger is the table reference filtered by a hash of FREE-wall Passages (a house hole must not recompute; `ClosedEdges` allocates) |
| V-3 | ⛔ blocker | macros cannot carry a feature wall to the cluster: `OverlayCompiler` reads only the saved scenario's entities (`OverlayCompiler.cs:42-48`); macro members are transient | ⭐ D-4 revised: the roadblock's WALLS are placed in the SCENARIO itself (not in the macro), the macro keeps its non-wall members; teaching the compiler macros is rejected (a second expansion path) |
| V-4 | important | ✅ `TerrainFeature` has no thickness; a wall gets the parser's 0.3 m (`TerrainWorldParser.cs:206-207`); contact strength = resistance × thickness × height (`ContactBreach.cs:279-286`): sandbags at 0.3 m = 30 < a ramming car's 40 | ⭐ D-3 revised: a per-MATERIAL default thickness in `FeatureKinds` (sandbags 0.6 m, concrete 0.4 m) — no struct field (the recorded layout stays) |
| V-5 | important | the breach's `Panel` is the piece's rank within the line, and a sloped segment is split into pieces (CE-5015) — not the polyline segment | ⭐ D-6: the gateway tells Unreal to place a gap by `Centre`, `Normal` and `From` / `To` (T-075 already sends them), not by panel rank |
| V-6 | important | a feature wall placed DURING a run reaches other nodes only after a save; the SUMO body feed for a partly covered road counts entities only; a clear order names a point, never the wall | ⭐ walls are scenario CONTENT (placed in the editor before the run, as every terrain feature); placing one mid-run is out of scope — said in D-1 |
| V-7 | important | D-2 would make imported OSM walls / fences that cross roads (gates, driveways) start closing roads | ⭐ D-2 revised: **scenario feature walls only** (the overlay's), not base walls |
| V-8 | important | only a player-possessed hull or a `RamOrder` breaks (`HullBreakGate.cs:22-31`, B-D1) | the acceptance row says *player-driven or ram order* |
| V-9 | minor | the free-wall contact cut is the hull's footprint at its crossing angle + margins, ≥ 4.2 m (not hull width + margin); `ClearObstacle.Resolve` prefers an obstacle entity within the pick radius over the wall (`ClearObstacleBehavior.cs:101-111`) | §2 wording; the dozer scenario keeps cars out of the pick radius |

Could not verify: whether SUMO cars are stopped by a wall on an open lane; whether the vehicle navmesh passes a 4.7 m gap; how many imported walls cross
roads; nothing was run. ⇒ the acceptance scenario measures the first two.

## 9. Stages and tracked items (`2026-11-27`; S1–S4 handed to `terrain` — [HANDOFF_UiIntegration_To_Terrain_Breachable_Walls.md](../../blueprints/batches/HANDOFF_UiIntegration_To_Terrain_Breachable_Walls.md))

| stage | id | lane | what |
|---|---|---|---|
| S1 | CE-11069 | terrain | per-material default wall thickness (V-4) |
| S1 | CE-11070 | terrain | road closure by scenario feature walls — the line-blocker rule on the replicated breach table (V-1, V-2, V-7) |
| S2 | CE-11071 | terrain | roadblock content: `roadblock-blast`, city-battle walls in the scenario (V-3) |
| S3 | CE-11072 | terrain | acceptance scenario `va-dozer-breach-wall` (§6) |
| S4 | CE-11073 | terrain | ⏳ the obstacle tool's wall types draw a feature wall (D-1) — after the user's answer |
| S5 | CE-11074 | unreal-ig | scenario terrain features on DPS; Unreal draws the wall and places gaps by Centre / Normal / From–To (D-6, V-5) |

### 9a. As built — S1 (`terrain`, 2026-11-27)

| id | built | where | rails |
|---|---|---|---|
| CE-11069 | ✅ `FeatureExpander.WallThicknessOf(material)`: sandbags 0.6 m, concrete 0.4 m, others 0 (= the parser's 0.3 m) — a `Wall` feature's line part carries it | `FeatureKinds.cs` | `FeatureKindsTests.CE11069_*` — 0.6 / 0.4 / 0.3 m; a 1 m sandbag wall's strength 60 > the ramming car's 40, ≤ the IFV's 150 |
| CE-11070 | ✅ the LINE rule: `RoadBlockage.FeatureWallLines` (the scenario wall lines + their passable gaps: Passages merged where they touch, kept ≥ `minDriveThroughM`), `LaneClosedByWalls` (a wall segment crosses the lane's centreline, reaches BOTH lane edges, and the crossing is in no gap), `WallClosedSegments` (our graph: a segment closes when every lane does), `WallClosedEdges` (SUMO, per lane: an edge closes when every lane does), `WallSignature` (geometry + Passages only) | `RoadBlockage.cs`, `BridgeClosureSystem.Execute/Recompute` | `BridgeClosureTests.CE11070_*` (3) — closes; 0.8 m hole keeps it; a Hole does not recompute; a 4.7 m cut re-opens; two nodes agree; 0 B steady; a base wall and a half-across wall close nothing; SUMO: square, diagonal, one lane of a 7 m two-way road re-opens |

⭐ **"Scenario wall" = a free panel whose label is `<guid>/0`** (`IsScenarioWallLabel`): the feature's key is its entity guid on the live and the compiled overlay alike, while a
base / imported wall carries its own name — content-derived, so every node agrees without the feature entities. ⚠ It covers every scenario LINE kind (wall, fence, hedge): a
scenario fence across a road closes it too (none is placed across a road today — measured: only `Wall` features exist in `scenarios/`).

⚠ **Finding — a multi-lane one-way SUMO edge with ONE lane cut stays open** (`WallClosedEdges` reports the closed lane): SumoSharp closes EDGES (`Engine.CloseEdge`, routing only) —
a lane closure needs a SumoSharp lane permission at run time. A two-way road is two edges, so the design's "one lane of a 7 m road" case works.

### 9b. S3 measured — `va-dozer-breach-wall` (CE-11072) — ⭐ the first five runs; the car row is fixed by §9d

On `te-osm-bridge`: a 12 m sandbag wall (1 m) across the two-way road at x 50; a dozer on a point order at the eastbound lane's centre; a vehicle handed to SUMO driving east.
Measured over five runs (the new cold log lines in `BridgeClosureSystem` / `TrafficSession`):

| expectation (§6) | measured |
|---|---|
| the road is CLOSED before | ✅ `SUMO closed edges now [-west, west]` at load; SUMO REFUSES the car's hand-over (no open route) every 2 s; the car never passes the wall line |
| the dozer cuts a Passage, the rest stands | ✅ the dozer reaches the wall at t 24 s and cuts; ⚠ it drove through its cut in 4 of 5 runs (1 run it backed off west) |
| the road OPENS after | ✅ `closed edges now [-west]` right after the cut: the eastbound edge re-opens, the westbound (not cut) stays closed — the per-lane rule live |
| the car passes | ⛔ then: SUMO TOOK the car once the edge re-opened, but it stood — ⭐ cause found and fixed in §9d (its own gunner fed to SUMO as a pedestrian); ✅ after the fix the car passes the cut (t 47 s) and reaches the east bank (t 60 s) |

### 9c. Presence on feature walls, and the roadblock walls in the scenario (CE-11071; user `2026-11-27`: *"honour Presence on walls"*)

```mermaid
sequenceDiagram
  participant SAVE as Scenario save: OverlayCompiler
  participant OP as OverlayPresence
  participant LOAD as TerrainLoadStep (every node)
  participant RES as TerrainResidency.Prepare
  participant SV as ScenarioVariants
  SAVE->>OP: Stamp(entity, guid, parts) — presence + presenceKey on each part
  LOAD->>LOAD: variant = VariantOf(run args)
  LOAD->>RES: Prepare(terrain, overlay, variant)
  RES->>OP: AbsentKeys(world, variant) — part of "already resident?"
  RES->>OP: ReadOverlay(..., variant) → Filter
  OP->>SV: IsPresent(variant, scenario salt, presenceKey, p) — the ONE decision the scenario load makes
```

*What the picture shows:* the overlay is compiled once, before any variant exists, so the chance travels WITH the parts and each node drops the absent ones from the variant it
already reads — no second decision, no scenario file read at the terrain load.

| piece | as built |
|---|---|
| `"PresenceWith": "<entity guid>"` (`ScenarioVariants.PresenceWithKey`) | an entity rolls its `Presence` under ANOTHER entity's key ⇒ with the same chance the two stand or fall together; kept on save (`VariantForms.PresenceWith`), honoured by Preview (`PresetInPlace`); without `Presence` it is refused |
| `OverlayPresence` (Fdp.Toolkits Terrain) | `Stamp` at compile; `Keep` / `Filter` at read; `AbsentKeys` for the resident identity (an overlay with no chances: "" ⇒ unchanged behaviour) |
| `TerrainFolderWorld.ReadOverlay(..., variant)`, `TerrainResidency.Prepare(name, overlay, variant)` | the absent parts are removed before the parse; the resident terrain is "unchanged" only for the same overlay hash AND absent set |
| callers | `TerrainLoadStep` (the run's args — a replay's context too), `ReplayBrowserSubsystem` (the recording's `ScenarioArgs`) |
| the editor | its live overlay is built from the live entities, which the load already dropped — the same result |
| content | `city_battle_macros.py`: the roadblock macro keeps its cars, MG and AT men; `city_battle_scenario.py` `ROADBLOCK_WALLS`: per roadblock two concrete barriers (staggered, each closes one lane) and the two MG nests' sandbags as feature walls, `Presence 0.6, PresenceWith` the instance, added last (no other id moves) |
| rails | `FeatureKindsTests.CE11071_*` (the wall stands exactly when the roadblock's `Apply` keeps it, over 40 variants; a wall without Presence always); `CityBattleVariantsTests` (a roadblock = its instance; its 4 walls stand only with it) |

⚠ Not covered: a Presence-rolled BRIDGE feature keeps its road rows in the overlay header (`TerrainBridges`) even when its parts are dropped — no scenario does that today.

### 9d. Found by S3 — a rider was told to SUMO as a pedestrian (CE-11072)

📌 Measured on `va-dozer-breach-wall`: the APC (2001) handed to SUMO stood still 17 m after the hand-over, for good — with NO wall too. SumoSharp alone drove the same
hand-over to the end (a probe); the editor's SUMO held the shadow because its GUNNER (an enclosed seat, entity 2002) was fed as a PERSON at the APC's own place
(`TrafficBodyFeedSystem.Gather`): the shadow braked for its own crew. ⭐ Fix: a rider (`IsEmbarkedTag`, enclosed or exposed) is no body — its vehicle is
(`TrafficBodyFeedTests.CE11072_ARider_IsNoBody_OnlyItsVehicleIs`). Every vehicle with a crew handed to SUMO was affected (an APC, an IFV with its squad aboard).

⭐ **The dozer's exit (measured, 10 runs):** a blade cut is the hull's width + 1 m, so a DIAGONAL drive out of it clips the wall's edge — the move ended `FailedBlocked` at the
blade (2 of 7 runs, then 3 of 3 once the car drove). ⭐ A blade cut is the hull's width + 0.5 m a side, so the dozer fits through its OWN cut only centred to ±0.5 m — its exit ended
`FailedBlocked` straight or diagonal. The scenario cuts TWICE (the second point 4 m beside the first — ⚠ it must lie OUTSIDE the first cut: a point order is done once a
Passage covers its point, so a point inside it cuts nothing), then drives straight through and parks off the road: 3 of 3 runs pass, the same times
(cut 24 s, through 54 s, the car past the wall 57 s, east bank 72 s). ⚠ An order to cut a gap of a given WIDTH is not built (a point order cuts one blade).

 (stopped, R-106): the three `city-battle/roadblock` instances carry `Presence: 0.6` (each stands in 60 % of runs), and the
overlay compiler reads every saved feature entity without `Presence` (`OverlayCompiler.cs`) — moving the macro's walls into the scenario (V-3) would make them stand in EVERY run. ⇒ needs
a decision: Presence on feature walls (the compiler or the residency honouring it), or the walls always standing.

## ⛔ HISTORY — rev 1 (segments), withdrawn `2026-11-27`

Rev 1 made a wall a ROW of 1.5 m obstacle entities, each removed when destroyed. Its independent review found two blockers — a dozer and a ram open
only ONE segment (`ClearObstacleBehavior.cs:148-152`, `ContactBreach.cs:255, 421-427`) and the referenced review section was missing — and seven
important findings: the segment-Health rule was false (`ObstacleDamage.BlastDamage` ignores size), the content premise was wrong (the macro's
sandbags are MG cover; `roadblock-blast` holds a concrete barrier), the row tool spaces boxes and leaves bend wedges, `PlacementGroup` is not
replicated and its id is the wall clock, orders name one entity. It also showed that authored feature walls already breach in part — which rev 2 builds on.
