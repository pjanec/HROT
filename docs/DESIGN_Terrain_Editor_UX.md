<!--STATUS
state: LIVE
build-state: READY-TO-BUILD — ✅ APPROVED by the user 2026-11-16 (R-5034: every lean of §5, + TE-15); ruled earlier: TE-1 (R-5032), TE-5 (R-4264), TE-6 (R-5033); nothing built
updated: 2026-11-16 (TE-16: 3-D is the PRIMARY view, R-5035) · 2026-11-16 (✅ APPROVED R-5034; TE-15 move / rotate / reshape added) · 2026-11-16 (TE-6 ruled R-5033) · 2026-11-15 (REVISED to §8 + R-5032 + R-4264; the first §3 / §5 / §6 / §7 moved to HISTORY) · 2026-11-15 (R-4264 folded: TE-5 obstacles are entities) · 2026-11-15 (first version — terrain lane, close-out item 4)
current-answer: §5 the decisions (revised) · §3 the diagrams · §7 acceptance · §8 the review they answer · §2 the friction measured
stale-below: ## ⛔ HISTORY (the first version's §3 / §5 / §6 / §7 — superseded by the review, kept for the reasoning; ⛔ not to be built)
known-rot: none in the live sections; §8's findings are answered in §5 (each row names its R-n)
known-conflict: none — TE-6's snap ON for terrain LINE tools deviates from Map Interaction §2.5's "off by default" (written for units) BY RULING (R-5033)
related-designs:
  - DESIGN_Ground_Patches_And_Road_Blockage.md — §4d BINDS this design (G-30 one feature family, G-31 one patch type, G-32 the stack, G-33 expand on the patched ground); OWNS the Earthworks tools (P-5, P-6) this design only re-homes on the toolbar
  - DESIGN_Terrain_Authoring.md — OWNS the feature machinery this design gives a UX to: the picks and the draw panel (§15a U-1, U-2, U-7, §15a-2 C), the live bake (§18e), the scatter (§18g T-2), blocks (§11, §18h)
  - UX/DESIGN_Map_Interaction.md — OWNS drawing / shape editing (§2.5 D11: finish / cancel, insert, centre-drag, delete), snapping (the row TE-6 is the FIRST build of — unbuilt there), always-multi placement (D5) and multi-select
  - UX/UX_Feature_Tool_Model.md — OWNS the tool arbiter (ToolController) and "the toolbar marks the armed tool" (step 5) the Terrain row rides on
  - UX/UX_Feature_Multi_Select.md — OWNS selection; ⚠ its NOT-BUILT status is stale: Map Interaction P5 built Shift / Ctrl / band / Alt-click (2026-11-08)
  - UX/DESIGN_Editing_UX.md — §3 places the tool palette in the shell toolbar region; ⭐ for TERRAIN tools the user ruled the Editor Toolbar window instead (R-5032: "There is not enough place") — the snap toggles of §3 are what TE-6 builds the terrain half of
  - DESIGN_Vehicle_Breaching.md — B-6 OWNS what a lone tree does (felled, blocks); B-2 / B-3 what a wall does — the Contents lines of TE-8 quote them
  - blueprints/batches/NOTE_Coordinator_To_Terrain_Obstacle_Tool.md — R-4264: TE-5 RULED, drawn obstacles are obstacle entities (option a)
  - designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — RM-5 (R-4260): a ram breaches a drawn barrier, so drawn obstacles must be breakable entities
  - DESIGN_Add_Entity_Picker.md — OWNS the picker the terrain groups live in; stays the search / gallery door
-->

# DESIGN — the terrain editor, the user-friendly way (CE-4594)

🔒 User, `2026-11-14`: *"we might need some tool to define where the forest area is … create walls along a defined line … plant trees and various
obstacles … These deserve a new design with a lot of thinking what would be the user-friendly way how to do that"*. Row:
[CE-4594](blueprints/Blueprint_Issues_Tracker.md). Mockup: [terrain-editor.svg](UX/terrain-editor.svg) (generator `scripts/gen-terrain-editor-mockup.py`).

⭐ **The machinery exists** (Terrain Authoring T-2 / U-7, P-5, P-6: every kind, the draw panel, the live bake). **This design is only the
WAY a user reaches and repeats it.** It adds no storage: every change below is a `TerrainFeature` (G-30).

## 1. INVENTORY — measured `2026-11-15`

| query (graph via `scripts/cbm.sh`, then grep) | total | what it says |
|---|---|---|
| `search_graph name_pattern=".*Terrain.*(Pick\|Draw\|Ghost\|Birth\|Placement\|Details\|Gizmo\|Ops\|Palette\|Tool).*" label=Class` | **23** (has_more false) | `TerrainPickCatalog`, `TerrainDrawPanel` / `TerrainDrawOptions`, `TerrainGhost*`, `TerrainBirth`, `TerrainPlacement`, `TerrainBlockGizmo`, `TerrainZoneGizmo`, `TerrainBlockOps`, `TerrainBlockDetailsView` — ⛔ **no palette, no terrain-FEATURE gizmo, no feature Details** (grep adds `GroundFeatureDetailsView`, Ground kind only) |
| `search_graph ".*(Toolbar\|ToolController\|ToolDescriptor\|ShapeEditSession\|AreaAuthoring\|PointSequence\|Snap).*" Class` | 116 (76 read; the rest `*Snapshot`) | `EditorToolbarPanel` (+ `EditorToolbarWindow`), `MainToolbarManager` (the shell's command toolbar), `AreaAuthoringArm` / `AreaAuthoringRequest`, `PointSequenceGizmo`, `ShapeEditSession` — ⛔ **no snap class** |
| `search_graph ".*(Scatter\|FeatureExpander\|OverlayExpansion\|ClutterType).*" Class` | 7 | `FeatureExpander` (3 partials), `OverlayExpansion`, `ClutterTypes`, `TerrainRecipe.ScatterItem` |
| `search_graph ".*(Snap\|Terrain\|Tool).*" Interface` | 13 | `IToolController`, `ITerrainDrawSource`, the terrain services — ⛔ no snap seam |
| `search_graph ".*(Brush\|Eyedropper\|Stamp\|Gate).*" Class` | 83 | none for terrain (gates are flow gates) |
| ⭐ added by the review (§8 R-3) — grep `FeatureElements.cs` | — | the recipe elements `group` (items along a line / in an area, `count`, `facing`), `enclosure` (a closed wall with a gate gap), `fields` (a hedge round a gap), `orchard` (trees on a grid) — `FeatureElements.cs:242-335` |
| ⭐ added — `search_graph ".*(MacroInstance\|EntityGroup\|GroupMember).*" Class` | 7 | `MacroInstance` (+ its translators and `MacroInstanceDetailsView`) — the only parent-with-member-entities mechanism (TE-5's group candidate) |
| ⭐ added — grep `RegisterObstacle` | — | the obstacle TKB types are built FROM `ClutterTypes` (`NedTkbCatalog.cs:1362-1370`): Car, Sandbag wall, Concrete block, Crate — one table already (R-4264); Barrel, Jersey barrier, Guard booth are clutter only |

⚠ `check_index_coverage` is not available through the CLI — the absences above are "not in the graph or in grep", not proven.

## 2. The friction, measured — what a user meets today

| the job | today, measured | file:line |
|---|---|---|
| reach a terrain tool | right-click ▸ Add Entity ▸ group ▸ tile ▸ *Place on map*: a modal gallery per item | `TerrainPicks.cs:79-108`, §15a U-1 |
| draw the 2nd, 3rd hedge | ⛔ a line / area tool **removes itself after Finish** — back to the picker each time; point placement stays armed (D5) | `AreaAuthoringArm.cs:119-160` (`onRemove`) vs `EntityPlacementGizmo.cs:392` |
| plant trees | one click per tree, or a WOOD (a canopy mass); ⛔ no row, no small group | picks `TerrainPicks.cs:89` |
| a row of obstacles | one click per obstacle (Add Entity's obstacle types, always-multi) — or a whole shipped block (*roadblock*, *checkpoint*, *defensive position* place rows in one click) | `TerrainPicks.cs:105-106`, `assets/terrain-recipes/roadblock.recipe.json` |
| street clutter | ⛔ the tile is greyed: no scatter recipe ships | `TerrainPicks.cs:95`, `assets/terrain-recipes/` (11, none with `scatter`) |
| a wall along a road, onto a house | free-hand clicks; ⛔ no snap of any kind | INVENTORY rows 2, 4 |
| a wall round a plot, a gate | a line is open; ⛔ no gate | `TerrainPicks.cs:90-92` |
| change a hedge into a wall, its height | only the raw component in *Properties…* (`TerrainFeature.Pad`); no friendly tab for a non-ground feature (Ground and Block only) | `EditorSubsystem.cs:3975-3977`, `ScenarioComponentsView.cs:128` |
| find / pick a placed tree or item | ⚠ only the generic entity symbol (`EntityPresentationGizmo`, TKB 8818 "a map graphic, no body") — no terrain look; **not run in a frame — S-6 measures it first** | `EntityPresentationGizmo.cs:37`, `NedTkbCatalog.cs:1234-1238` |
| see what a line will make while drawing | the generic polyline look (U-7's own deviation) | §15a U-7 |
| edit a drawn shape | ✅ Edit Shape (E): vertex drag, insert on a segment, centre-drag, Delete — a feature with a polyline is an Area target | `MapMenuActions.cs:119`, `:265-274`; Map Interaction §2.5 |
| undo | ✅ one edit = one step by construction | Terrain Authoring §18h |
| select many | ✅ built — Shift / Ctrl / band / Alt-click (cycle) and multi-delete (Map Interaction P5, 2026-11-08) | `MapGestures.cs:27` |

## 3. The diagrams

### 3.1 Classes — existing (`<<existing>>`) and what this adds (`<<new>>`)

```mermaid
classDiagram
  class EditorToolbarPanel { <<existing>> DrawContent() ActiveTool }
  class TerrainToolRow { <<new TE-1>> family buttons, wraps to the window width }
  class TerrainPickCatalog { <<existing>> FeaturePicks() Build() }
  class PlacementToolRegistry { <<existing>> ArmTerrain(spawn, pick) }
  class ScenarioSpawnAdapter { <<existing>> StartAreaAuthoringMode() StartPlacementMode() }
  class ArmedTerrain { <<new TE-1>> the armed pick's spec, read by the row }
  class AreaAuthoringArm { <<existing>> Arm(request) }
  class AreaAuthoringRequest { <<existing>> Closed Born Underlay }
  class EntityPlacementGizmo { <<existing>> always-multi, Renew() }
  class PointSequenceGizmo { <<existing>> Underlay }
  class ShapeEditSession { <<existing>> insert / delete / centre-drag, Menu }
  class TerrainDrawOptions { <<existing>> Kind Height Seed Born }
  class TerrainGhostContext { <<existing>> Base Assets Roads, cached }
  class BuildingPanelGrid { <<existing>> Near() }
  class TerrainSnap { <<new TE-6>> Snap(point) road edge / wall / line end }
  class LineEndIndex { <<new TE-6>> feature line ends, rebuilt on edit }
  class FeatureElements { <<existing>> group / enclosure / orchard placement }
  class LinePlaces { <<new TE-4/5>> the group element's placement, extracted, shared }
  class FeatureKinds { <<existing>> OfKind() + Row 14, Group 15 }
  class ObstacleRowBirth { <<new TE-5>> N obstacle entities, one EditSteps bracket }
  class MacroInstance { <<existing>> parent of member entities }
  class TerrainFeatureDetailsView { <<new TE-8>> }
  class TerrainFeatureGizmo { <<new TE-9>> on the TerrainBlockGizmo template }
  class DrawGhost { <<new TE-10>> 0 B geometry, recomputed on a committed point }

  EditorToolbarPanel *-- TerrainToolRow
  TerrainToolRow --> TerrainPickCatalog : the SAME picks
  TerrainToolRow --> PlacementToolRegistry : ArmTerrain
  TerrainToolRow ..> ArmedTerrain : lights the family
  PlacementToolRegistry --> ScenarioSpawnAdapter
  ScenarioSpawnAdapter --> AreaAuthoringArm : line / area
  ScenarioSpawnAdapter --> EntityPlacementGizmo : point
  ScenarioSpawnAdapter --> ArmedTerrain
  AreaAuthoringArm --> AreaAuthoringRequest
  AreaAuthoringRequest : +Repeat (new TE-2)
  AreaAuthoringArm --> PointSequenceGizmo
  PointSequenceGizmo --> ShapeEditSession
  PointSequenceGizmo ..> TerrainSnap : each placed point
  PointSequenceGizmo ..> DrawGhost : Underlay
  TerrainSnap --> TerrainGhostContext
  TerrainSnap --> BuildingPanelGrid
  TerrainSnap --> LineEndIndex
  FeatureElements --> LinePlaces
  FeatureKinds --> LinePlaces : Row / Group trees
  ObstacleRowBirth --> LinePlaces : where each obstacle goes
  ObstacleRowBirth ..> MacroInstance : the group (to measure, S-3)
  AreaAuthoringArm --> TerrainDrawOptions
```
*What the picture shows that prose hid: the row arms through the door that ALREADY arms every terrain pick (`ArmTerrain`), and reads the armed
pick back to light itself — no per-pick tool ids. Trees in a row and obstacles in a row share ONE placement (`LinePlaces`, extracted from the
recipe `group` element) but are born differently: trees as one baked feature, obstacles as entities (R-4264).*

### 3.2 Sequence — three hedges, then a row of concrete blocks

```mermaid
sequenceDiagram
  actor U as user
  participant TB as Editor Toolbar (Terrain row)
  participant PR as PlacementToolRegistry
  participant SA as ScenarioSpawnAdapter
  participant G as PointSequenceGizmo
  participant SN as TerrainSnap
  participant OB as ObstacleRowBirth
  participant B as live bake / road blockage
  U->>TB: Walls (last: Hedge)
  TB->>PR: ArmTerrain(hedge pick)
  PR->>SA: StartAreaAuthoringMode(spec, Repeat)
  loop each hedge
    U->>G: click points
    G->>SN: Snap(point)
    SN-->>G: line end of Hedge 2
    U->>G: Enter
    G->>SA: one SpawnEntityCommand (one undo)
    SA->>SA: Repeat: new seed, re-arm
  end
  U->>G: Esc (drops the shape in progress)
  U->>G: Esc (disarms, the row unlights)
  U->>TB: Obstacles (Concrete block)
  U->>G: drag a line across the road, Enter
  G->>OB: the line
  OB->>OB: LinePlaces: 4 positions
  OB->>SA: 4 obstacle entities in ONE EditSteps bracket
  SA->>B: StaticObstacle x4 -> the road closes (SUMO + our graph)
```

### 3.3 Modules — who registers it, who calls it each frame

```mermaid
graph TD
  ED[EditorSubsystem] -->|registers| TW[EditorToolbarWindow - Scenario perspective, open by default]
  TW -->|draws each frame| TP[EditorToolbarPanel + TerrainToolRow]
  ED -->|registers DetailsViews| DV[TerrainFeatureDetailsView]
  TP -->|ArmTerrain| SA[ScenarioSpawnAdapter]
  SA --> PG[PointSequenceGizmo]
  PG -->|frame thread, per placed point, 0 B| SN[TerrainSnap on TerrainGhostContext]
  MIP[MapInteractionPack: every map host] -->|gizmo projectors, reflection| FG[TerrainFeatureGizmo]
  FG -.->|draws only where LiveTerrainBakeInfo exists: Player / SimHost / Replay have none| X[other hosts]
  OB[ObstacleRowBirth] -->|StaticObstacle entities| RB[BridgeClosureSystem -> RoadBlockage.Blockers]
  SA --> OB
  style X fill:#555,stroke:#999
```
*What it shows: the row and the Details tab are editor-only by registration; the gizmo is in the shared pack but gated on the editor's live
bake info (the `TerrainBlockGizmo` precedent); an obstacle row reaches road blockage through the entity path it already reads — no engine change.*

## 4. The proven solution, and what we take from it

Map editors that do this well (game level editors, GIS editing) share four habits: **a persistent tool palette** (not a modal per object) ·
**sticky tools** (stay armed until Esc) · **snapping to the things that matter** (edges, ends, angles) · **objects along a path / in an area**
(one object, many instances, spacing). Sculpt brushes and per-instance painting are the fifth habit — ⛔ **rejected here**: our ground is
parametric patches re-applied on every node (G-31, G-32) and every placed thing is a replicated entity, so a brush stroke of hundreds of
entities breaks the entity budget (B-2: one entity per block).

## 5. Decisions — revised `2026-11-15` (§8 answered; ✅ = ruled by the user)

| # | question | ⭐ decision / lean | why · blast radius |
|---|---|---|---|
| **TE-1** | where do the terrain tools live? | ✅ **RULED R-5032** — a **Terrain row in the Editor Toolbar window** (`EditorToolbarPanel`), ⛔ not the main toolbar. Seven FAMILY buttons — *Wood · Trees · Walls · Obstacles · Earthworks · Water · Scatter* — each opens its variants; the label is the family (the last variant is the tooltip and the hint line, R-12 of the review); the row WRAPS to the window's width. A button calls `PlacementToolRegistry.ArmTerrain(pick)` — the door every terrain pick already uses — and lights from the ARMED pick the adapter holds (`ArmedTerrain`), not from a tool id (§8 R-7). Add Entity keeps the gallery and search | 🔒 user: *"Terrain tools can live in the editor toolbar, not in the main menu. There is not enough place."* The window is open by default in the Scenario perspective. Blast: editor UI only |
| **TE-2** | after Finish? | ⭐ a terrain LINE / AREA tool **stays armed** (`AreaAuthoringRequest.Repeat`, terrain requests only) with a **new seed per shape** (the placement gizmo's `Renew` precedent); **Esc twice**: the first drops the shape in progress, the second disarms (through the central Esc); the scatter panel's *Done* ends the SHAPE, not the tool | §8 R-8 |
| **TE-3** | planting trees | ⭐ one *Trees* family: **One** (click — today's Tree) · **Row** (a line, spacing) · **Group** (an area, a count); lone trunks, ⛔ never a wood | the user means trunks they can see and fell (B-6) |
| **TE-4** | a row / a group of trees in data | ⭐ two NEW `TerrainFeature` kinds, **Row = 14, Group = 15** (`Template` = the item — `tree` in v1; `Pad` = spacing on a Row, the count in a Group; `Seed`); an older build SKIPS an unknown kind (`IsKnown`) instead of throwing; Scatter keeps "an area + a recipe". The placement is the recipe `group` element's, **extracted into one shared `LinePlaces`** (ruling 9), not a second algorithm. ⛔ Species / size: no field in v1 (cut from the mockup) | §8 R-3, R-4. Blast: `FeatureKinds`, the compile, the parser; saved scenarios unaffected |
| **TE-5** | obstacles | ✅ **RULED R-4264 (option a)** — the *Obstacles* family places **real obstacle ENTITIES**: click = one (as today), drag a line = a row facing along or across, positions from `LinePlaces`; ⭐ **a row is ONE undo step (one `EditSteps` bracket, the Unpack precedent) and selects as ONE group** — the group's mechanism is MEASURED first (candidate `MacroInstance`), never invented beside it. The types are the ONE `ClutterTypes` table (already: `NedTkbCatalog.cs:1362-1370`); ⭐ Barrel, Jersey barrier and Guard booth become obstacle types too (new TKB ids — allocated by the lane that owns `TkbEntityTypes`) | obstacle entities already block roads, are crushed, bulldozed and re-open the road (CE-4590, CE-4600, CE-4601); RM-5 needs them breakable. Cost: a checkpoint ≈ 10–40 entities |
| **TE-6** | snapping | ✅ **RULED R-5033** (user `2026-11-16`: *"Snapping on by default, yes"*) — the FIRST snap (Map Interaction's row is unbuilt — §8 R-10): **road edges** (+ an offset, from `TerrainRoads.Corridors` half-widths), **building walls** (`BuildingPanelGrid.Near`), **line ends** (a new index of feature line ends, rebuilt on edit) — inputs from the cached `TerrainGhostContext`; ⭐ ON for LINE tools only (walls, rows, ditches), off for areas; **Ctrl** suspends it for one point, **Shift** = 15° steps (keymap rows, never literal keys — the modifier table below); the active target named on the hint line; 0 B per moved point | the jobs are "along this road", "continue that hedge". ON by default deviates from Map Interaction's off-default (units) — ruled |
| **TE-7** | a wall round a plot; a gate | ⭐ the `enclosure` precedent: *Wall around a plot* stores a closed line (the first point repeated); **Add gate here** (Edit Shape's segment menu) ROTATES the line's start into a 3 m gap — one entity, one label, one undo; a breach recorded in Live has its ranks re-mapped (said in the slice). ⭐ One gate per wall in v1 (a second gate = a second wall) | §8 R-5: a split would mint a new key and orphan B-2 breaches |
| **TE-8** | changing what was placed | ⭐ ONE **Terrain** Details tab for every non-ground feature: Kind (only kinds of the SAME shape — a point never becomes a line, §8 R-16), material, height, spacing / count + seed dice, a Contents line of what it does; beside the Ground and Block tabs. Obstacle entities keep their entity Details | *Properties…* shows only the raw component today |
| **TE-9** | finding what was placed | ⭐ `TerrainFeatureGizmo` on the `TerrainBlockGizmo` template (pick segments, chip, 0 B steady), gated on `LiveTerrainBakeInfo`: a point feature a handle dot, a line / area its outline, selected as ONE; bulk delete / Kind change through the BUILT multi-select. ⚠ S-6 first measures what the generic entity symbol shows for TKB 8818 today | §8 R-12 |
| **TE-10** | seeing it before and while it is made | ⭐ the draw ghost has its OWN 0 B geometry (reused buffers, the item resolved at arm), recomputed only when a COMMITTED point changes, the cursor segment drawn plain; ⭐ after Finish the ghost STAYS until the bake holds that feature (`LiveTerrainBakeInfo`), so ten repeated hedges never vanish; the count line says when a Row / Group hit its cap | §8 R-9, R-14 |
| ~~TE-11~~ | "another one like that" | ⛔ **dropped**: Copy / Paste / Duplicate (Ctrl+C / V / D) already exist for any entity (`MapMenuActions.cs:115-117`); Alt-click is *select the next overlapped unit* | §8 R-6 |
| **TE-12** | a berm | ⭐ kept, re-costed: a ground mode + the parser + a TERRAIN-class line + its slot in the G-32 stack — a small backend item, not a preset | §8 R-11 |
| **TE-13** | the greyed *Street clutter* | ⭐ ship two scatter recipes: *Street clutter* and *Undergrowth* (baked, inert — R-4264 keeps generated clutter scenery) | data only |
| **TE-14** | what v1 leaves OUT, said | erasing everything in an area · a clearing inside a wood · deleting part of a line (Edit Shape deletes points) · single-letter shortcuts (`W` is *move forward* in 3-D — `EmbodiedKeys.cs:134`) | §8 R-15: each is a later item if asked for |

| **TE-15** | moving, turning and reshaping what was placed — in 2-D AND 3-D | ✅ **RULED R-5034** — 🔒 user `2026-11-16`: *"Line can be of course.bettwr to be editable by dragging endpoinys, no wxtra rotation needed tgen"*. ⭐ A LINE (wall, hedge, tree Row, ditch) is moved by Edit Shape's centre handle and reshaped — "turned" included — by dragging its points (P6, built: drag a point, insert on a segment, Delete); ⛔ **no rotate for lines**. ⭐ An OBSTACLE ROW (entities) moves and turns as a group with the built group move / rotate (P5 `GroupTransform`: about the group's centre, or each piece about its own; one undo) once it selects as one group (TE-5). An AREA (wood, Group, ground shape) the same as a line. A block keeps its own Move / Rotate (U-3). ⭐ 3-D: the same handles — the 3-D view turns the mouse ray into a ground point and runs the 2-D hit test (P7: handles draped and pickable), band select works in 3-D (CE-1136); ⚠ not yet proven for terrain features in a 3-D frame — acceptance 9 | measured: the Rotate action is units-only (`MapMenuActions.cs:118`) and a line's shape is its points, not the entity's rotation (`OverlayCompiler.cs:95-99`) — so dragging points is the ONE way, no second mechanism |

| **TE-16** | where is terrain edited? | ✅ **RULED R-5035** — 🔒 user `2026-11-16`: *"Will all the terrain enditing work in 3d which is the primary place for that?"* ⭐ **3-D is the PRIMARY authoring view; 2-D must work too.** Measured: the 3-D view already draws and picks what the tools use — points picked on ground / roof / wall top (`Picking.cs`, Map 3D M17 / M22), lines, filled triangles and boxes DRAPED on the relief (`GizmoRenderer3D.cs:133, :229-232, :270-281`), handles hit-tested at the ray's ground point (Map Interaction P7), band select (CE-1136), chunks rebuilt by area after a bake (U-6); snapping works on the picked XY, so it is free in 3-D. ⚠ **Two gaps the slices close:** ① **picking a placed TREE / tree Row in 3-D** — the picker holds the terrain mesh and entity boxes, not trees, so a click on a canopy lands on the ground BEHIND it (metres off from an oblique view): the feature gizmo (TE-9) must test the feature's own volume (a trunk + canopy cylinder, a wall's height band) along the RAY, not only the ground point; ② **right-click = the drawing menu vs right-drag = look** (`MapCamera3D.cs:344-378`) — prove a right CLICK opens the menu while a terrain tool is armed | the user's primary view; every slice's frame rails run in a 3-D frame FIRST, then 2-D |

**Modifier keys while a terrain tool is armed** (checked against Map Interaction §2.5 and `MapGestures`):

| key | does | already bound to |
|---|---|---|
| Enter · the ✓ · menu *Finish* | finish the shape | D11 — unchanged |
| Esc | 1st: drop the shape in progress · 2nd: disarm | D11's cancel, made two-stage (TE-2) |
| Backspace · Delete | remove the last / the selected point | D11 — unchanged |
| Ctrl (held) | no snap for this point | a NEW keymap row; Ctrl-click (toggle select) does not apply while drawing |
| Shift (held) | 15° steps | Map Interaction's snap row (designed, unbuilt) |
| Alt-click | — not used by terrain tools | *select the next overlapped unit* (P5) |

**Rejected — one line each:** a separate terrain-editor mode or app (a second storage) · the main toolbar (R-5032: no room) · sculpt brushes (the
ground is parametric patches, G-31) · paint-brush trees (hundreds of entities a stroke) · per-tree editing in a wood (a seeded mass) · obstacles
as baked clutter the road step learns to see (R-4264: indestructible, a second policy) · a gate by splitting the wall (new key, orphaned breaches)
· Row / Group as new meanings on Scatter (ambiguous data; older builds throw) · an eyedropper (Copy / Paste exist).

**Design docs checked:** Ground §4d — binds it (one family, one patch type, the stack). · Terrain Authoring §11, §15a, §18e, §18g, §18i — owns
the machinery and the recipe elements TE-4 / TE-5 reuse. · Map Interaction §2.5 — finish / cancel and shape editing settled; its snap row is
designed, unbuilt (TE-6 builds the terrain half). · Editing UX §3 — the palette's region; overridden for terrain tools by R-5032. · Tool Model —
the toolbar marks the armed tool; the terrain row reads the armed PICK instead of a tool id. · Multi-select — built (P5). · R-4264 note —
settles TE-5. · Checkpoint & Ramming RM-5 — why obstacles must be breakable. · Breaching B-2, B-3, B-4, B-6 — what walls, obstacles and trees do.

## 6. Slices *(proposed; each mockup-checked, R-4146)*

| slice | builds | lane |
|---|---|---|
| S-1 | TE-1 the Terrain row (families, wrap, lit from the armed pick) + TE-2 repeat, new seed, two-stage Esc | ui |
| S-2 | TE-4 `LinePlaces` extracted from the `group` element; kinds Row / Group; TE-13 the two recipes | backend (terrain) |
| S-3 | TE-5 the obstacle row (entities, one bracket, the group — `MacroInstance` measured first) + the three missing obstacle types | ui + backend |
| S-4 | TE-6 snapping + the modifier rows | ui + backend |
| S-5 | TE-8 the Terrain Details tab | ui |
| S-6 | TE-9 (measure the generic symbol, then the gizmo — with its 3-D volume pick, TE-16 ①) + TE-10 the ghost and its stay-until-baked | ui |
| S-7 | TE-7 the closed wall and the gate rotation; TE-12 the berm | backend + ui |

## 7. Acceptance

1. Three hedges are drawn after ONE click on the row's *Walls*; each has its own seed; one Esc drops a half-drawn hedge, a second disarms; the row lights *Walls* while armed.
2. A Row of 16 trees along a road is ONE feature, one undo; each tree fells like a lone tree (B-6); an older build loading the scenario skips the Row and does not throw.
3. Four concrete blocks dragged across a road are four obstacle ENTITIES (R-4264), one undo step, selected as one group; the road closes for SUMO and our road graph (P-2) and re-opens when the row is deleted; a heavy hull crushes one (B-3), the bulldozer clears one (B-4).
4. A wall drawn near a road snaps to its edge + 1 m; Ctrl draws one point free; a hedge started at another's end joins it; drawing a WOOD does not snap.
5. A closed wall round a house, then *Add gate here*: still ONE feature with a 3 m gap, one undo; a breach made before the gate survives it.
6. Selecting a placed wall shows the Terrain tab; Kind → Hedge changes it after one bake; Kind offers no point kinds.
7. While drawing and until its bake lands, the ghost shows what the bake will make; the frames match [the mockup](UX/terrain-editor.svg) (R-4146).
8. ⭐ (TE-16) Items 1–7 are proven in a 3-D frame first: points picked on the relief, the ghost and handles draped, a tree in a Row picked by a click on its CANOPY from an oblique view, the drawing menu opened by a right click while right-drag still looks around.
8b. A steady drawing frame allocates 0 B (snap, ghost — R-2084); every sim-touching rail runs under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`.
9. In a 3-D frame FIRST, then a 2-D one (`xvfb-run -a`, TE-16): a wall moved by its centre handle and one end dragged to a new point; a tree Row's end dragged; an obstacle row moved and turned about its centre as one group — each one undo step, and the bake follows (TE-15).

## 8. ⛔ Critical review `2026-11-15` — three independent reviewers *(engine / data · the author's workflow · claims vs code; every headline finding re-measured)*

⭐ **Verdict: the direction holds (one family, no new storage, a toolbar door, sticky tools, rows, Details, a ghost); seven rows of §5 were built
on a wrong or missing measurement.** Rows marked ✔ were re-measured by the lane after the review.

| # | finding | evidence | ⭐ revised lean |
|---|---|---|---|
| **R-1** ✔ ✅ R-5032 | TE-1 rejects the shell's toolbar, but the APPROVED Editing UX §3 puts *"the tool palette (select, place, route, area, draw, rotate)"* in the toolbar region | `UX/DESIGN_Editing_UX.md:274` (D2–D9 approved 2026-11-06) | the Terrain row follows Editing UX §3: it joins the tool palette wherever that palette lands (today the editor's *Editor Toolbar* window); ⚠ **the user rules** whether the palette moves to the shell toolbar first |
| **R-2** ✔ | BLOCKER — a clutter row does NOT close a road: P-2 reads only `StaticObstacle` entities; a clutter / scatter feature is an overlay prism, not crushable, not clearable | `RoadBlockage.cs:18-31`, `FeatureKinds.cs:138-147` | ✅ **RULED R-4264 (option a, user `2026-11-15`)** — the Obstacle tool places obstacle entities; folded into TE-5 and §7 item 3. ⛔ the lane's lean (b) is rejected: indestructible, and a second policy over the same pieces |
| **R-3** ✔ | the line / grid placement ALREADY EXISTS (⚠ for obstacles now only the GEOMETRY is reused — where along the line each entity is spawned — R-4264): recipe `group` (items along a line, `count`, `facing: along \| across`, or `area{count}`), `orchard` (trees on a grid), `enclosure` (a closed wall with a gate gap), `fields` (a hedge split round a gap) — missed by §1 | `FeatureElements.cs:242-335` | Row / Group / obstacle rows CALL these element routines (ruling 9); §1's query gains `Group\|Enclosure\|Orchard\|Element` |
| **R-4** | TE-4's data is ambiguous: a polyline has no open / closed flag, so a 3-point Scatter cannot say Row or Group; `Pad` would carry three meanings; an older build reads `item:tree` as a recipe id and THROWS | `EditablePolyline` (Points, Version only), `TerrainRecipe.Find`, `FeatureKinds.cs:66` | ⭐ two NEW kinds, **Row = 14, Group = 15** (Template = the item, Pad = spacing / count) — older builds skip an unknown kind (`IsKnown`) instead of throwing; Scatter keeps "area + recipe" |
| **R-5** | TE-7 split is wrong: splitting a ring gives ONE open line, not two; the second half's new guid changes its wall key and segment ranks (B-2), orphaning a breach made in Live (Edit Shape is enabled in Live) | `FeatureKinds.cs:74-83`, `MapMenuActions.cs:119`, Breaching §7.9 | ⭐ the `enclosure` precedent: a closed wall repeats its first point; a gate ROTATES the polyline's start into the gap — one entity, one label; ranks re-mapped (said) for an edit in Live; one undo bracket (the Unpack precedent) |
| **R-6** ✔ | Alt is taken: Alt-click = *select the next overlapped unit* (P5, built); TE-6 (Alt = no snap) and TE-11 (Alt-click = copy) collide with it and each other | `MapGestures.cs:27, :83` | TE-6: hold **Ctrl** for no snap (a keymap row, never a literal); TE-11 dropped — Copy / Paste / Duplicate (Ctrl+C / V / D) already exist (`MapMenuActions.cs:115-117`); a §5 table of every modifier, checked against Map Interaction §2.5 |
| **R-7** ✔ | the toolbar cannot light the right terrain button: every terrain line / area tool arms ONE id (`PlaceArea`), points arm *Place Entity*; the real arming path is `PlacementToolRegistry.ArmTerrain` → `ScenarioSpawnAdapter.ArmAreaAuthoring`, not `ToolController.Activate(terrain.hedge)` (§3 is wrong there) | `ScenarioSpawnAdapter.cs:125-129, :394-435`, `PlacementToolRegistry.cs:124-137` | the row reads the ARMED PICK (the adapter's current spec) to light its button; §3's diagrams redrawn on `ArmTerrain` + the adapter |
| **R-8** ✔ | TE-2: one Esc on a half-drawn wall drops the wall AND the tool; and `TerrainDrawOptions.From` rolls its seed ONCE, so every repeated group / row gets the same pattern | `ShapeEditSession.cs:337`, `TerrainDrawPanel.cs:28`, `EntityPlacementGizmo.cs:392` (`Renew`) | two-stage Esc (the shape, then the tool) through the central Esc; a new seed per repeat (the placement gizmo's `Renew` precedent) |
| **R-9** | TE-10's ghost cannot be 0 B through `FeatureExpander` (JsonObject trees, LINQ, the recipe re-read from disk, throws under 2–3 points) | `FeatureKinds.cs:76, :121, :194`, `TerrainRecipe.Find` | the ghost has its own allocation-free geometry (reused buffers, the item resolved at arm), recomputed only when a COMMITTED point changes; the cursor segment drawn plain |
| **R-10** ✔ | TE-6 does not EXTEND a snap: Map Interaction's snap row is unbuilt (no snap type exists); but the queries exist — `TerrainRoads.Corridors` (half-width), `TryNearestNode`, `TryAlign`, `BuildingPanelGrid.Near`, all cached in `TerrainGhostContext`; bridge ends already snap to road nodes within 25 m | `DESIGN_Map_Interaction.md:47`, `TerrainGhost.cs:7-21`, `GroundPatches.cs:153`, Terrain Authoring §18k | TE-6 BUILDS the first snap, on `TerrainGhostContext` + an index of feature line ends; road edges + line ends ON only for LINE tools (not areas); the active snap target on the hint line |
| **R-11** | TE-12 is not "no new code path": a ground LINE is honoured only for ditch presets; a berm needs a mode, the parser, a TERRAIN-class line and a stack slot | `GroundPatches.cs:420-425`, `TerrainWorldParser.cs:294` | kept, re-costed: a small backend item, not a preset |
| **R-12** ✔ | TE-9: multi-select IS built (P5, 2026-11-08); what blocks bulk edit is only that features are not pickable. The gizmo's gate should be `LiveTerrainBakeInfo` (the `TerrainBlockGizmo` precedent), not "Edit mode" | `DESIGN_Map_Interaction.md:315`, `TerrainBlockGizmo.cs:40` | TE-9 on the `TerrainBlockGizmo` template; bulk delete / kind change in the same slice |
| **R-13** ✔ | the mockup breaks R-4151: the existing toolbar drawn with icons, an amber armed style and the title "Tools" (real: text buttons, teal `ArmedColour`, *Editor Toolbar*, a *Reload BTrees* button); the obstacle list invents types (real: Car, Sandbag wall, Concrete block, Crate, Barrel, Jersey barrier, Guard booth); the draw panel drawn 420 px (real 260 px); Species / Size / "2 m off the road" have nowhere to live in data | `EditorToolbarPanel.cs:36-66`, `EditorWindows.cs:42`, `ClutterTypes.cs:20-27`, `TerrainDrawPanel.cs:205` | redraw the context from the real frame; cut Species / Size until a field is designed (or encode them in R-4's Template); the real obstacle list — and per R-4264 the Obstacle panel spawns ENTITIES |
| **R-14** | the pending bake (≈ 5 s warm) has no state, worst with sticky tools; no error states (MaxScatterItems = 2000 cap, a missing recipe, a greyed reason) | Terrain Authoring §18e-1, `FeatureKinds.cs:18` | the ghost stays until its bake lands; the count line says when a row is capped |
| **R-15** | missing jobs: erase in an area, a clearing in a wood, delete part of a line, 3-D / roof drawing, keyboard; the mockup's `W` for Wall is *move forward* in 3-D | `EmbodiedKeys.cs:134` | each gets a decision or an explicit out-of-scope row in the revision; no single-letter shortcuts without the keymap check |
| R-16 | TE-8: changing Kind across shapes (a Tree point ↔ a Row line) must add / remove the polyline | `NeedsShape` | the Kind list offers only kinds of the same shape |
| R-17 | a generic Components view exists (*Properties…*) and U-7 says `TerrainFeature.Pad` is editable there — TE-8 is a friendlier tab, not the first editor | `ScenarioComponentsView.cs:128`, Terrain Authoring §15a U-7 | §2's row corrected to "only the raw component" |

⭐ **Sound as written (all three agree or none objected):** TE-3 (trunks, not a wood), the one-family / no-new-storage frame, TE-8's single tab,
the G-33 / G-34 ordering (rows expand on the patched ground; felling keys on position, so label shifts do not break B-6), the rejected sculpt and
paint brushes, shipping the two scatter recipes.

⇒ ✅ **Answered `2026-11-15`:** R-1 ruled (R-5032 — the Editor Toolbar window), R-2 ruled (R-4264); §3, §5, §6, §7 and the mockup are revised to this table; the first version's sections are under HISTORY below.

## ⛔ HISTORY — the first version's §3, §5, §6, §7 *(superseded `2026-11-15` by the review and R-5032 / R-4264; ⛔ not to be built — kept for the reasoning)*

### (was) 3. The diagrams

#### (was) 3.1 Classes — existing (grey) and what this adds (green)

```text
classDiagram
  class EditorToolbarPanel { <<existing>> DrawContent() ActiveTool }
  class ToolController { <<existing>> Activate(id) ActiveModal }
  class TerrainPickCatalog { <<existing>> FeaturePicks() Build() }
  class TerrainDrawOptions { <<existing>> Kind Height Born }
  class TerrainDrawPanel { <<existing>> rows per pick }
  class AreaAuthoringArm { <<existing>> Arm(request) }
  class AreaAuthoringRequest { <<existing>> Closed Born Underlay }
  class PointSequenceGizmo { <<existing>> Underlay }
  class ShapeEditSession { <<existing>> insert / delete / centre-drag }
  class FeatureExpander { <<existing>> Expand() Scatter() }
  class TerrainFeature { <<existing>> Kind Template Pad Seed HeadingDeg }
  class GroundFeatureDetailsView { <<existing>> }
  class TerrainBlockDetailsView { <<existing>> }

  class TerrainToolRow { <<new TE-1>> buttons = picks, last variant per family }
  class TerrainSnap { <<new TE-6>> Snap(point, targets) RoadEdge BuildingWall LineEnd }
  class TerrainFeatureDetailsView { <<new TE-8>> Kind Material Height Spacing Seed Contents }
  class TerrainFeatureGizmo { <<new TE-9>> handle per feature, Edit mode }
  class GateSplit { <<new TE-7>> Split(feature, segment, at) }

  EditorToolbarPanel *-- TerrainToolRow
  TerrainToolRow --> TerrainPickCatalog : lists the SAME picks
  TerrainToolRow --> ToolController : arms through
  ToolController --> AreaAuthoringArm
  AreaAuthoringArm --> AreaAuthoringRequest
  AreaAuthoringRequest : +Repeat (new TE-2)
  AreaAuthoringArm --> PointSequenceGizmo
  PointSequenceGizmo --> ShapeEditSession
  PointSequenceGizmo ..> TerrainSnap : each point (new TE-6)
  PointSequenceGizmo ..> FeatureExpander : the ghost, Underlay (TE-10)
  TerrainDrawPanel --> TerrainDrawOptions
  TerrainDrawOptions --> TerrainFeature : Born
  FeatureExpander --> TerrainFeature : Scatter gains a LINE + one item (TE-4)
  TerrainFeatureDetailsView ..> TerrainFeature
  TerrainFeatureGizmo ..> TerrainFeature
  GateSplit ..> TerrainFeature
```
*What the picture shows that prose hid: every new box hangs off an existing seam — the row off the Tools toolbar, the snap off the point gizmo,
the Details beside the two existing tabs; the only DATA change is the scatter's line shape (TE-4), on the record that already exists.*

#### (was) 3.2 Sequence — three hedges along a road (TE-1, TE-2, TE-6, TE-10)

```text
sequenceDiagram
  actor U as user
  participant TB as Tools toolbar (Terrain row)
  participant TC as ToolController
  participant AA as AreaAuthoringArm
  participant G as PointSequenceGizmo
  participant S as TerrainSnap
  participant E as FeatureExpander
  participant B as live bake (§18e)
  U->>TB: click Hedge
  TB->>TC: Activate(terrain.hedge)
  TC->>AA: Arm(request with Repeat)
  loop each hedge
    U->>G: click points
    G->>S: Snap(point)
    S-->>G: road edge + 1 m
    G->>E: Expand(points so far) for the ghost
    U->>G: Enter
    G->>AA: onFinish(points)
    AA->>B: SpawnEntityCommand (one edit, one undo)
    AA->>AA: Repeat: re-arm, same options
  end
  U->>TC: Esc or Select
  TC->>AA: cancel, the toolbar unlights
```

#### (was) 3.3 Modules — who registers it, who calls it each frame

```text
graph TD
  ED[EditorSubsystem] -->|registers| TW[EditorToolbarWindow]
  TW -->|draws each frame| TP[EditorToolbarPanel + TerrainToolRow]
  ED -->|registers DetailsViews| DV[TerrainFeatureDetailsView]
  MIP[MapInteractionPack: every map host] -->|ToolController| TC[ToolController]
  TP --> TC
  MIP -->|gizmo projectors each frame| FG[TerrainFeatureGizmo]
  FG -.->|Edit mode only: Player / SimHost / Replay draw nothing| X[other hosts]
  TC --> AA[AreaAuthoringArm]
  AA -->|frame thread| PG[PointSequenceGizmo]
  PG -->|frame thread, cached per point| SN[TerrainSnap: road graph + TerrainWorld]
  BK[StaticObstacleBakeSystem] -->|after a commit| LB[live bake]
  style X fill:#555,stroke:#999
```
*What it shows: the row and the Details tab are editor-only (the editor registers them); the gizmo is in the shared pack but draws only while
authoring; snap runs on the frame thread on cached inputs — it must allocate 0 B per moved point (R-2084).*

### (was) 5. Decisions — a lean for each

| # | question | ⭐ lean | why · blast radius |
|---|---|---|---|
| **TE-1** | where do the terrain tools live? | ⭐ a **Terrain row on the editor's Tools toolbar**: Wood · Tree · Wall-family · Obstacle · Earthworks · Scatter; each button shows its last variant, its arrow opens the family; the buttons ARE `TerrainPickCatalog` picks armed through `ToolController`; Add Entity keeps the gallery and search | one door, one click; ⭐ the Tools window already marks the armed tool (Tool Model step 5). ⛔ not the shell's `MainToolbarManager` — that holds commands, not map tools. Blast: editor UI only |
| **TE-2** | after Finish? | ⭐ a terrain **line / area tool stays armed** with the same options until Esc or another tool (`AreaAuthoringRequest.Repeat`, terrain requests only) | point placement already is always-multi (D5); drawing ten hedges should not be ten trips. Other area tools unchanged |
| **TE-3** | how are trees planted? | ⭐ one Tree tool, three gestures: **One** (click) · **Row** (a line, spacing) · **Group** (an area, a count) — lone trunks, ⛔ never a wood | the user means trunks they can see and fell (B-6); a wood is a canopy mass |
| **TE-4** | what is a row / a group in data? | ⭐ the existing **Scatter** feature gains a **line shape** and a **single-item** mode (`Template = item:<tree \| clutter type>`, `Pad` = spacing on a line / count in an area); the expander already scatters lone trees (`FeatureKinds.cs:216`) | ⭐ one mechanism for tree rows, obstacle rows and street clutter (shared, not three kinds); ⚠ a data change on a recorded component — a NEW meaning of existing fields, no new field. Blast: `FeatureKinds.Scatter`, the compile, saved scenarios (old ones unchanged: an area + recipe id) |
| **TE-5** | obstacles? | ✅ **RULED `2026-11-15` (R-4264, option a; [note](blueprints/batches/NOTE_Coordinator_To_Terrain_Obstacle_Tool.md))**: the Obstacle tool places **real obstacle ENTITIES** (concrete block, jersey barrier, sandbag wall …) — click = one, drag a line = a row facing along or across; ⭐ **a drawn row is ONE undo step and selects as ONE group**. Generated street clutter (scatter, blocks, towns) stays baked, inert scenery. ONE size / material table for both (`ClutterTypes` ⇄ the obstacle TKB shapes — never two definitions of a concrete block). ⛔ The row is NOT a TE-4 feature | 🔒 user: *"Option A"*. Obstacle entities already block roads (SUMO + ours, CE-4590), are crushed by a heavy hull (CE-4600), cleared by the bulldozer (CE-4601) and re-open the road when removed — no engine change; Checkpoint & Ramming RM-5 (R-4260) needs them breakable. Cost: a checkpoint ≈ 10–40 entities, a 50 m sandbag line ≈ 17. ⚠ The GROUP mechanism is open: the candidate is the macro instance (`MacroInstance`, a parent with member entities and its own Details view) — the slice measures whether it fits before adding anything |
| **TE-6** | snapping? | ⭐ extend Map Interaction's snap row with three terrain targets: **road edges** (+ an offset), **building walls**, **line ends**; ON by default for terrain lines, Alt suspends for one point, Shift = 15° | the jobs are "along this road", "up to this house", "continue that hedge". ⚠ deviates from "off by default" (units) — **the user rules** |
| **TE-7** | a wall round a plot; a gate? | ⭐ *Wall around a plot* = a CLOSED line; a gate = **Add gate here** on a segment in Edit Shape, which SPLITS the feature into two with a 3 m gap (one undo) | no new data (a gate is the absence of wall, G-30); a breach stays a runtime record (B-2) |
| **TE-8** | changing what was placed? | ⭐ ONE **Terrain** Details tab for every non-ground feature — Kind within its family, material, height, spacing / count, species / size, seed + dice, a Contents line of what it does — beside the Ground and Block tabs | the Details seam exists (`DetailsViewDescriptor`); one tab, not one per kind |
| **TE-9** | finding what was placed | ⭐ a `TerrainFeatureGizmo` draws each feature's handle in Edit mode (a tree: a dot; a line / area: its outline, selected as ONE) — usable before the bake lands; bulk delete / kind change waits on multi-select | ⚠ **measure first (S-6):** what the generic entity symbol shows for TKB 8818 today |
| **TE-10** | seeing it before it is made | ⭐ the drawing **ghost** is `FeatureExpander` on the points so far (a wall in its colour, tree dots, items) + a count line (*Row of 16 trees · 124 m*) — the placement ghost's pattern (U-2) | closes U-7's own deviation; the same pure expansion as the compile, so the ghost cannot lie |
| **TE-11** | "another one like that" | Alt-click a placed feature while a terrain tool is armed copies its kind and settings into the tool | cheap; ⚠ optional — build only if wanted |
| **TE-12** | a berm (raised line) | ⭐ the Earthworks family gains **Berm** = a Line · Raise ground shape | G-30 names it; one preset, no new code path |
| + | ship the first scatter recipes | ⭐ *Street clutter* and *Undergrowth* | the tile is greyed today only for want of data |

**Rejected — one line each:**
- a separate terrain-editor mode or app — the compile and the live bake already run in the scenario editor (R-4204); a second editor is a second storage.
- sculpt brushes — the ground is parametric patches re-applied on every node (G-31); a painted raster has no place in the stack (G-32).
- paint-brush tree placement — hundreds of entities per stroke; Row / Group are one entity each.
- per-tree editing inside a wood — a wood is a seeded mass of thousands; Unpack is for blocks.
- Add Entity as the only door — the measured friction of §2 rows 1–2.
- the shell's main toolbar — commands, perspective-filtered, not map tools (`MainToolbarManager`).

**Design docs checked:** Ground §4d — binds it (one family, one patch type, the stack). · Terrain Authoring §11, §15a, §18e, §18g — owns
the machinery; settles every "what is built". · Map Interaction §2.5 — settles finish / cancel and shape editing; its snap row is extended,
not replaced. · Tool Model — settles how a button arms a tool. · Multi-select — not built, so bulk edit (TE-9) waits. · Breaching B-2, B-3,
B-6 — settle what a wall and a lone tree do (the Contents lines). · Add Entity Picker — stays the gallery; does not settle a palette.

### (was) 6. Slices *(proposed; each mockup-checked, R-4146)*

| slice | builds | lane |
|---|---|---|
| S-1 | TE-1 the Terrain row + TE-2 sticky line / area tools | ui |
| S-2 | TE-4 the scatter's line + single item, the two recipes | backend (terrain) |
| S-3 | TE-3 / TE-5 the Tree and Obstacle draw-panel rows on S-2 | ui |
| S-4 | TE-6 snapping (road edges from the road graph, walls from `TerrainWorld`, line ends from the features) | ui + backend |
| S-5 | TE-8 the Terrain Details tab | ui |
| S-6 | TE-9 measure the generic symbol, then the feature gizmo; TE-10 the ghost | ui |
| S-7 | TE-7 the gate split, TE-12 the berm | backend + ui |

### (was) 7. Acceptance

1. Three hedges are drawn with ONE click on the toolbar; Esc stops the tool; the toolbar lights the armed one.
2. A row of 16 trees along a road is one entity, one undo; each tree fells like a lone tree (B-6); 2 m off the road edge.
3. Four concrete blocks dragged across a road are four obstacle ENTITIES (R-4264), one undo step, selected as one group; the road closes for SUMO and our road graph (P-2) and re-opens when the row is deleted; a heavy hull crushes one (B-3), the bulldozer clears one (B-4).
4. A wall drawn near a road snaps to its edge + 1 m; Alt draws free; a hedge started at another's end joins it.
5. A closed wall round a house, then *Add gate here*: two features, a 3 m gap, one undo.
6. Selecting a placed wall shows the Terrain tab; Kind → Hedge changes it after one bake.
7. While drawing, the ghost shows what the bake will make, and the frames match [the mockup](UX/terrain-editor.svg).
8. Steady drawing frames allocate 0 B (snap, ghost — R-2084); every rail runs under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1` where it touches the sim.

