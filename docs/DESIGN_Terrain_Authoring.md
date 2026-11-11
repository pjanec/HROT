<!--STATUS
state: LIVE
build-state: DESIGN — R-4198…R-4201 ruled; ⭐ the S-0 MOCKUPS ✅ approved (§15, R-4203); L-1…L-7 ✅ approved (R-4202, L-2 amended); §16 the wizard → editor step and cluster sync ✅ (R-4203)
updated: 2026-11-11
current-answer: ⭐⭐⭐ §18 THE COMPILED MODEL (R-4204 — supersedes §5 and §16 V-2…V-6) · §17 the critical review (15 gaps → tracker CE-4415…CE-4426) · §16 wizard → editor, cluster sync (leans) · §15 the mockups (S-0) · §14 editing a baked terrain again, non-terrain entities in a source (leans E-1…E-6) · §13 the workflow (✅ R-4200) · §12 the random terrain = an empty surface + placed blocks (✅ R-4200) · §11 the user's view (blocks as RECIPE assets through Add Entity — ✅ APPROVED R-4199) · §2 the model (one feature vocabulary, three uses) · §3 decisions · §4–§6 diagrams · §7 street clutter · §8 rivers and bridges · §9 slices · §10 not yet measured
stale-below: §5's sequences and §16's V-2…V-6 (expand on every node at load) are SUPERSEDED by §18 (R-4204, compile at save) — kept for the reasoning, ⛔ not to be built
related-designs:
  - DESIGN_Peek_And_Fire.md — §9 P-7a OWNS the mechanism this extends: a scenario entity that IS terrain, baked on every node, lifecycle-gated (R-242, R-243, R-244)
  - DESIGN_Terrain_Import_UTM.md — §8 destruction (breaches and collapses ride the same bake), §10 the input set, §11 the importer and its terrain folder writer, CE-4403 flattening under buildings
  - DESIGN_Terrain_Synth.md — hrot-terrain-synth, the generator the random terrain grows from
  - DESIGN_Terrain_From_OSM.md — the OSM wizard this one mirrors (§6 frame, preview); Q-O5 "one shot, no terrain editor" — superseded for authoring by R-4198, not for OSM import
  - DESIGN_Terrain_World.md — the terrain world model the features become (prisms, walkables, surfaces, walls)
  - DESIGN_Map_3D_Mode.md — §6ah / §6ai how terrain, trees and roofs are drawn
  - DESIGN_Add_Entity_Picker.md — how a scenario entity is picked and placed today (obstacles included)
  - PLAN_Large_Terrain.md — budgets R-4192
-->

# DESIGN — terrain authoring: a random terrain, a layout of blocks, and terrain features in a scenario

## 0. The request

🔒 User, `2026-11-11`: *"couldn't we have an option to create a random terrain … not too much tuning knobs. Rather to have a few presets"* ·
*"the user could optionally move the blocks, like the city center or village … Maybe it could include also the wood drawer by enabling some polygon
footprint editor"* · *"Sometimes we need a scenario which modifies something a little bit on an existing terrain … place a house here, or some fences
or bushes here and there, or tree areas … saved only in the scenario … applied at the beginning by baking into the terrain"* · *"I rather specify it now
when we design the feature so we can share it a lot."* · *"When we create a city, it's usually empty … there are parked cars. There are piles of
rubbish."* — and the answers in §3 (R-4198).

## 1. INVENTORY — measured `2026-11-11`

Queries: `scripts/cbm.sh search_graph` name patterns `.*(Obstacle|Fortification|Berm|Fence|PlacedBuilding|TerrainFeature|Barrier|Sandbag|Prop).*`
(80, paged once), `(Terrain(Tree|Wood|Building|Premise|Concealment|Vegetation|Scrub).*|.*Premises.*|.*Woods.*)` (3), `.*(Synth|TerrainGen|Generator).*`;
grep for `bridge`, `water`, `TerrainSurfaceType`, `StaticObstacleBake`; the design corpus for *terrain overlay / edit / delta*. ⚠ Coverage could not be
checked (`check_index_coverage` is not in the CLI).

| what | exists | where |
|---|---|---|
| ⭐ a scenario entity that IS terrain, baked on every node off the frame, all at once at load, the sim held until the bake commits, only touched navmesh tiles rebaked | ✅ boxes only | `StaticObstacle`, `ObstacleShape` (`Fdp.Toolkits/Terrain/StaticObstacles.cs`); `StaticObstacleBakeSystem` (`Hrot.Core/Services/StaticObstacleBake.cs`); `TerrainResidency.BakeObstacles`; Peek and Fire §9 |
| obstacle types | ✅ `Car` 8806 · `Sandbag wall` 8807 · `Concrete block` 8808 · `Crate` 8809 | `TkbEntityTypes.cs:108-114`, `NedTkbCatalog.cs:1176` |
| placing them in the editor | ✅ | `ObstaclePlacementGizmo` |
| the same bake follows destruction (breaches, collapses) and replay keyframes | ✅ | UTM §8.5; `BuildingDamageBakeTests.cs:170` |
| buildings from templates, woods (area + density), lone trees, walls / fences / hedges, surfaces open / forest / water, walkable slabs and ramps | ✅ in the TERRAIN files | `TerrainBuildingExpander`, `TerrainWoods`, `TerrainWorld.cs:13,17`, `TerrainFolderWriter.cs:8` (`WorldWall`) |
| a writer of a whole terrain folder | ✅ | `Hrot.Terrain.Import/TerrainFolderWriter.cs` |
| a generator of heights, villages, roads, trees, a pond, a river strip, scrub | ✅ a CLI | `Hrot/Tools/Hrot.Tools.TerrainSynth` (`SynthProfile`, `RoadNetwork`, `Extras.cs:83`) |
| water cuts the ground mesh | ✅ | `TerrainWorldMesh.cs:369` |
| ⛔ a town layout (blocks, 3–6 storeys) · a bridge · street clutter · a feature kind other than a box in a scenario · "Save terrain as" | none (grep) | NEW |

## 2. The model — ONE feature vocabulary, THREE uses

```mermaid
graph LR
  subgraph Uses
    G[Random terrain wizard]
    L[Block layout editor]
    S[Scenario editor - terrain features]
  end
  V[Terrain feature vocabulary]
  G --> L
  L --> V
  S --> V
  V --> TF[(terrain folder - baked once)]
  V --> SC[(scenario entities - baked at load)]
  SC -->|Save terrain as| TF
```

*What the picture shows that prose hid:* the random generator and the scenario tools are NOT two editors — the generator's **blocks** expand into the
**same features** a scenario places, and the only difference is **where they are saved**. "Save terrain as" is the one arrow between the two stores.

| feature kind | becomes in the terrain world | movement | sight / fire | first slice |
|---|---|---|---|---|
| **house** (a template, or a generated unique one) | a building (walls, openings, doors, floors) — destructible | doors, stairs | walls, windows | ✅ |
| **fence / wall / hedge** (a polyline) | `WorldWall` segments, by material | blocks (hedge: per material) | per material | ✅ |
| **wood** (a polygon + density) | a forest surface + trees | slows (when CE movement costs land) | conceals | ✅ |
| **lone tree · bush / scrub patch** | a tree / a scrub area | trunk only / slows | conceals | ✅ |
| **clutter** (§7) — parked car, skip, rubbish pile, bench … | a small prism of a material (the obstacle bake, as today) | blocks | cover by material | ✅ |
| **water** (a polygon: lake, pond, river) | a water surface (cuts the ground) | impassable | — | S-3 |
| **bridge** (§8) | a walkable deck + ramps over water, a road edge on the net | crosses | deck | S-3 |
| **block** — town centre · village · wood · open field | expands to the features above (generator only) | — | — | S-2 |
| *ground shaping — flatten pad, berm* | the height store patched | — | — | ⏭ later (counted: every house carries its pad, §3 TA-6) |

## 3. Decisions

**Settled by the user — R-4198** (`2026-11-11`):

| # | ruling |
|---|---|
| TA-1 | features edited in the scenario editor are saved **in the scenario by default**; ⭐ an option **"Save terrain as…"** writes a NEW terrain that includes them — *"a kind of a terrain editor hidden in the scenario editor"* |
| TA-2 | the random generator can produce **plain terrain** — no houses, nothing — to start from and add features to |
| TA-3 | scenario-placed houses are **destructible** like every other |
| TA-4 | **flattening the ground under houses is required** — not in slice 1, but the design must count with it |
| TA-5 | **rivers and bridges** are in scope — *"they make the movement behavior more difficult"* |
| TA-6 | the random generator **populates** what it builds — parked cars, rubbish piles and the like (§7) |

**Leans — ✅ approved `2026-11-11` (R-4202; L-2 amended by the user: the generator becomes a library at the START, so generated unique houses are available from slice 1, beside the templates):**

| # | lean | why | rejected |
|---|---|---|---|
| L-1 | ⭐ a scenario feature is **an entity** of a feature kind, and the existing **obstacle bake learns the new kinds** | R-243 already gives load-time bake, determinism, replay rebake, navmesh tiles — for free | a second "scenario overlay" path beside it — two implementations of one concept |
| L-2 | a house is placed from the **building templates** and gets a **generated unique** option later | templates exist and are what the scale terrain uses (R-4192 §7) | generating per placement in slice 1 — the generator is not a library yet |
| L-3 | **"Save terrain as"** writes through the importer's `TerrainFolderWriter` (base terrain + baked features) | one writer of the terrain folder | a second writer in the editor |
| L-4 | the block layout is **only in the random wizard** — click a block, drag it, draw a wood's outline; **Shuffle** re-rolls what is not pinned | the user's "optionally move the blocks" | a free-form terrain painter — out of scope |
| L-5 | each house feature carries a **ground pad** field from day one (the height it stands at); slice 1 writes it and ignores it, the flattening slice uses it | TA-4 counts with it without a format change later | adding it later — every saved scenario would need migrating |
| L-6 | clutter is **written into the terrain** when generated (thousands, no entity each); in a scenario it is an obstacle entity like today | 5 000 parked cars as entities would cost the entity budget and the wire | entities everywhere |
| L-7 | the external visual engine learns scenario features through a **wire descriptor per feature kind**, like `EntityObstacleShape` | the other engine must draw a scenario's house | relying on the terrain files — they do not hold scenario features |

## 4. Classes

```mermaid
classDiagram
  class TerrainFeature {
    <<component, recorded, unmanaged>>
    Kind
    Material
    Pad
  }
  class FeatureShape {
    <<component, recorded, unmanaged>>
    fixed polyline or polygon
  }
  class StaticObstacle {
    <<existing>>
  }
  class ObstacleShape {
    <<existing>>
  }
  class StaticObstacleBakeSystem {
    <<existing, extended>>
    Execute()
  }
  class TerrainResidency {
    <<existing>>
    BakeObstacles(prisms, damage)
  }
  class FeatureExpander {
    <<new>>
    Expand(feature) TerrainWorld parts
  }
  class TerrainFolderWriter {
    <<existing>>
  }
  class TerrainSaveAs {
    <<new>>
    Write(baseTerrain, features, name)
  }
  class TerrainGenerator {
    <<new library, from hrot-terrain-synth>>
    Generate(preset, size, relief, vegetation, seed, layout)
  }
  class BlockLayout {
    <<new>>
    blocks
    pinned
  }
  StaticObstacleBakeSystem --> TerrainResidency
  StaticObstacleBakeSystem ..> TerrainFeature : reads
  StaticObstacleBakeSystem ..> StaticObstacle : reads
  TerrainResidency --> FeatureExpander
  FeatureExpander ..> FeatureShape
  TerrainSaveAs --> FeatureExpander
  TerrainSaveAs --> TerrainFolderWriter
  TerrainGenerator --> BlockLayout
  TerrainGenerator --> FeatureExpander
  TerrainGenerator --> TerrainFolderWriter
```

*What it shows:* three new classes and one new library; the bake, the residency and the folder writer are the existing ones. `FeatureExpander` is the
**one** place a feature becomes terrain-world parts — the bake, "Save terrain as" and the generator all call it.
⚠ The shape component's fixed capacity (vertices per fence / wood) is a §10 measurement — a recorded component is unmanaged (CLAUDE.md).

## 5. Sequences *(⛔ SUPERSEDED by §18, R-4204 — the bake no longer expands blocks on every node; kept for the reasoning)*

**A scenario with features loads (and a replay starts)** — the obstacle bake as built, with the expander added:

```mermaid
sequenceDiagram
  participant SC as Scenario load or replay keyframe
  participant B as StaticObstacleBakeSystem
  participant R as TerrainResidency
  participant X as FeatureExpander
  participant N as Navmesh tiles
  SC->>B: feature entities exist, held in Constructing
  B->>R: bake all features at once, off the frame
  R->>X: expand each feature
  X-->>R: prisms, walls, surfaces, buildings
  R->>N: rebake only the touched tiles
  R-->>B: new terrain world, cover, navmesh
  B->>SC: commit, ack the lifecycle, features go live
```

**Save terrain as:**

```mermaid
sequenceDiagram
  actor U as User
  participant E as Scenario editor
  participant S as TerrainSaveAs
  participant X as FeatureExpander
  participant W as TerrainFolderWriter
  U->>E: Save terrain as, a new name
  E->>S: base terrain plus the scenario features
  S->>X: expand each feature
  S->>W: write the new terrain folder
  E->>E: optionally re-point the scenario at the new terrain and drop the features it now holds
```

## 6. Who runs it

```mermaid
graph TD
  CGF[CGF node] --> BS[StaticObstacleBakeSystem]
  SIM[SimHost node] --> BS
  ED[Editor - edit and preview] --> BS
  RB[Replay Browser - O-8: shared collector, per seek] --> BS
  BS --> RES[TerrainResidency]
  WIZ[Random terrain wizard] --> GEN[TerrainGenerator]
  GEN --> IMP[Importer - TerrainFolderWriter, navmesh prefill]
  classDef unknown stroke-dasharray: 5 5
```

*What it shows:* the bake already runs on every simulating host and the editor (rails `CgfSubsystemTests.cs:123`,
`TheEditorRunsTheOrchestratorCoreTests.cs:278`); ⭐ the separate Replay Browser (O-8, CE-4420) reads the same collector
(`StaticObstacleBakeSystem.CollectPrisms`) and bakes the replayed obstacles into the displayed world on each load or seek — world only, no navmesh or cover. The generator ends in the **existing
importer path**, so a random terrain gets the navmesh prefill and the folder layout every imported terrain has.

## 7. Street clutter — what the generator scatters

⭐ Every item is a small box of a material — the obstacle bake's own shape — placed by a rule, with a density per block type (town high, village low,
open field none). Lean: these, and nothing that needs its own model in slice 1:

| item | where | material | why it matters in play |
|---|---|---|---|
| parked car (the existing `Car` type) | kerbside, both sides of town streets, in yards | `car-body` | cover, blocks lanes, hides men |
| skip / large bin | street corners, yards | steel | cover |
| rubbish pile | alleys, beside skips | soft (sight 0, low protection) | concealment, not protection |
| bench · bus shelter | squares, main streets | wood / glass | low obstacle |
| lamp post · traffic sign | along streets | steel, thin | almost nothing — sight lines read as real |
| low garden wall · hedge | around village houses | brick / hedge | cover, channels movement |
| construction fence · barrier | one or two sites per town | steel mesh | blocks movement, not sight |
| market stalls | the town square | wood | concealment |
| burnt-out car | rare | steel | cover — a battlefield look |
| woodpile · hay bales | villages, field edges | wood / straw | cover / concealment |

⚠ Thousands of these in a town: the draw (ui, instanced like trees) and the terrain index must be measured (§10).

## 8. Rivers and bridges

- **A river** is a water polygon along a path (width by preset), cutting the ground — impassable to men and ground vehicles; the generator routes it
  through the low ground and roads cross it only at bridges.
- **A bridge** = a walkable **deck** (a slab, `TerrainWalkableKind.Slab`) with **ramps** at each end over the water, plus the road edge across it on the
  road net (SUMO already models bridges, `DESIGN_Terrain_From_OSM.md` §2) — a **chokepoint** the AI must find by pathing.
- ⭐ In a scenario the user can place a bridge over an existing river (a feature) — and destroy it later (a collapse, like a building: R-4188 D-7).
- ⚠ Not measured: does the navmesh treat water as impassable (the cut suggests yes), and does a vehicle's road path cross a deck. §10.

## 9. Slices

| slice | what | done when |
|---|---|---|
| **S-0** | ⭐ the mockups (R-4143): the random wizard (preset, size, relief, vegetation, seed, layout with draggable blocks and a polygon drawer) and the scenario editor's terrain-feature tools; ui | the user approves the pictures |
| **S-1** | scenario terrain features: house (template), fence / wall / hedge, wood, lone tree, bush, clutter items — entities, the bake extended, saved, recorded, replayed, destructible houses; the wire descriptors (L-7) | rails: each kind blocks / conceals as its row says, a replay rebuilds it, a scenario round-trips |
| **S-2** | the random terrain: the generator as a library, presets incl. **plain terrain**, the town layout (new), clutter, the wizard with Shuffle and the block layout | a 3 km "Town with suburbs" generates, imports and loads in budget; plain terrain is empty |
| **S-3** | rivers and bridges: in the generator and as scenario features | a river cuts the map, units path over the bridges, a destroyed bridge cuts the route |
| **S-4** | "Save terrain as" | the saved terrain loads with the features baked in, the scenario re-pointed |
| **S-5** | flattening under houses (TA-4, CE-4403) — the pad (L-5) applied to the height store | no house buried on a slope; every door reachable |

## 10. Not yet measured — before S-1 is READY-TO-BUILD

| question | why it matters |
|---|---|
| load time of a scenario with ~500 features (the one bake) | R-4192 budgets |
| the fixed capacity of a feature's shape (vertices) | unmanaged recorded components |
| does the separate Replay Browser run the bake | replays of edited terrains |
| water impassable in the navmesh; a vehicle over a deck | rivers and bridges are pointless otherwise |
| the external gateway: what it needs to draw a scenario feature | L-7 |
| how many clutter boxes a town holds, and their cost in the draw and the terrain index | §7 |

## 11. The user's view — what makes it practical *(✅ APPROVED `2026-11-11`, R-4199 — B-1…B-8 and the leans of B-Q1…B-Q3)*

🔒 User, `2026-11-11`: *"the blocks that we are able to put into the scenario, that needs to be kind of an asset, and it needs to be addable by the same
mechanism like the entities. Add entity, and it will open the picker dialog with nice thumbnails"* · *"this could be also some kind of a random asset
… So we do not need to store all predefined blocks as files on the disk"* · *"What makes sense for the user? What would make it practical? Not just
blindly add features."*

**What a scenario author actually wants to do** — four jobs, and the one tool each needs:

| job | example | the tool |
|---|---|---|
| ① furnish an empty spot | "a farmstead here", "a roadblock on this road", "a hamlet by the crossroads" | ⭐ place a **block** from Add Entity — one click, it fits itself to the site |
| ② add one thing | "a wall here", "a bridge over this river", "one more house" | place a single **feature** from Add Entity (lines and areas through the existing area / route tools) |
| ③ make a place look lived-in | "this town is empty — fill its streets" | ⭐ an **area scatter**: draw an area, pick *Street clutter* / *Undergrowth*, a density |
| ④ adjust what was placed | "another layout", "not that house" | **Re-roll** (new seed) · **Unpack** into single features to delete / move one |

**Leans:**

| # | lean | why |
|---|---|---|
| B-1 | ⭐ a block is a **RECIPE asset** (small data: which features, how many, spacing, rules) **+ a seed per placed instance** — the user's "random asset". A few recipes on disk, endless variants | the user: *"we do not need to store all predefined blocks"* |
| B-2 | ⭐ **a placed block is ONE scenario entity** (recipe id, seed, footprint, heading); it expands to features **at bake time**, deterministically (same recipe + seed + site ⇒ same houses on every node and in replay) | one entity per block, not 40 — the entity budget and the wire stay small; the bake of §5 already runs it |
| B-3 | ⭐ **it fits the site**: aligns to the nearest road, keeps out of water and existing buildings, sits its houses on pads (TA-4) | a block that ignores the terrain is the weird-looking result the user saw on the slope; fitting is what makes one click enough |
| B-4 | ⭐ **the same recipes are the random terrain's blocks** — the generator lays out recipes; the scenario places recipes | one catalogue, two uses (CLAUDE.md shared and unified) |
| B-5 | the Add Entity picker gets **terrain groups**: *Blocks* (farmstead, hamlet, village street, town block, checkpoint, roadblock, defensive position, copse, orchard, hedgerow fields) · *Buildings* · *Vegetation* · *Walls and fences* · *Clutter* · *Water and bridges*; ⭐ a recipe's thumbnail is rendered from its default seed by the existing atlas pipeline (§2j of the picker design) | the user's "same mechanism like the entities" |
| B-6 | one block = **one undo step**; moving / rotating moves the whole block; Unpack is itself undoable | the editor's undo works per edit |
| B-7 | the external visual engine receives the **expanded** features, never the recipe | it cannot run our recipes |
| B-8 | ⭐ **a small curated catalogue first** — ~10 blocks, ~15 single features, 2 scatters — grown from what scenarios ask for | the user's "not just blindly add features" |

**Open — needs a decision:**

| # | question | lean |
|---|---|---|
| B-Q1 | a recipe is edited later — do scenarios that placed it change? | ⭐ yes, like a building template today (a recipe is content); the editor shows which scenarios use it before saving the recipe; Unpack freezes an instance for good |
| B-Q2 | who authors recipes — the user in the editor, or we ship them as data? | ⭐ we ship them as data files in slice 1; a recipe editor only if users ask |
| B-Q3 | can a scatter be destroyed / felled? | a scattered car or bin is an obstacle like today — yes; trees with CE-3426 (felling) |

## 12. The random terrain IS an empty terrain plus placed blocks *(✅ AGREED `2026-11-11`, R-4200)*

🔒 User, `2026-11-11`: *"the random terrain creation tool might be basically the same like creating an empty scenario, having just these blocks in it
with some kind of variant number per each … what we need to actually really generate at the very beginning is just the terrain surface, because
everything else can be added later by editing. Am I right? And terrain surface is basically a height map."*

⭐ **Right, with one correction: the surface is MORE than the height map — it is everything a block FITS ITSELF TO** (B-3), which is the part a
block cannot add later:

| generated with the SURFACE (once) | why it cannot be a block |
|---|---|
| ⭐ **the height map** (relief preset) | everything stands on it |
| ⭐ **the road network** (a SUMO net) | blocks align to roads (B-3); traffic and vehicle routes need ONE connected net, built by netconvert; a road is drawn later as a feature only for small additions |
| ⭐ **rivers and lakes** | they follow the low ground of the height map and cut it; bridges sit where roads meet them |
| ground cover (fields, meadow, bare) | a surface colour / class under everything — cheap, terrain-wide |

```mermaid
graph LR
  W[Random terrain wizard] --> SF[Surface generator - heights, roads, rivers, ground cover]
  SF --> T[(new terrain - the surface only)]
  W --> LY[Layout - which recipes where, each with a seed]
  LY --> SC[(new scenario - one entity per block)]
  T --> SC
  SC -->|optional Save terrain as| T2[(a terrain with the blocks baked in)]
```

*What the picture shows:* the wizard makes **two** things — a surface terrain and a scenario of placed blocks — and the blocks are **ordinary
scenario entities** (B-2), editable, re-rollable and unpackable like any the user places by hand. ⇒ the generator writes **no houses at all**;
"plain terrain" (TA-2) is simply the layout step skipped.

| lean | why | rejected |
|---|---|---|
| R-1 ⭐ the wizard = **surface generator + an automatic layout of recipe blocks into a new scenario** | one house-making path (the recipes), shared by hand-placing and generating | the synth's own house code as a second path |
| R-2 ⭐ **"Save terrain as" is the step that makes a large town FAST** — the blocks bake once into the terrain, with the navmesh prefilled | a 5.5 km town of ~1 800 houses re-expanded at every load would cost load time (§10, not yet measured); a baked terrain does not | always expanding at load |
| R-3 the synth's house, tree and road code is not thrown away: its roads and heights become the **surface generator**; its villages become **recipes** | reuse | rewriting |

## 13. The workflow — generate and shuffle, then edit, then bake *(✅ AGREED `2026-11-11`, R-4200; W-1…W-4 ✅ approved the same day)*

🔒 User, `2026-11-11`: *"the generator will generate something initially, but stores it as a scenario, which is editable later. So the user is then
able to move the stuff around and in the end save it as a terrain, which will trigger the normal terrain generation stuff, including the navmesh
pre-generation."*

```mermaid
stateDiagram-v2
  [*] --> Wizard
  Wizard --> Shuffling: Generate from a preset
  Shuffling --> Shuffling: Shuffle layout or Shuffle ground
  Shuffling --> Editing: Open in the scenario editor
  Editing --> Editing: move, re-roll, unpack, add or delete blocks and features
  Editing --> Baked: Save terrain as
  Baked --> Editing: reopen the source scenario and save again
  Editing --> [*]: Save the scenario only
```

*What the picture shows:* ONE path, not two modes — the wizard's result is already the editable scenario, so a user who is happy after
shuffling just saves, and one who is not keeps editing the SAME thing. *Save terrain as* is the one exit into the normal terrain pipeline
(the importer's folder writer, the navmesh prefill).

| # | lean | why |
|---|---|---|
| W-1 | ⭐ **two shuffles**: *Shuffle layout* (new seeds for the blocks — seconds) and *Shuffle ground* (new heights, roads, rivers — slower: netconvert) | re-rolling the town should not wait on the roads; keeping the ground while trying layouts is the common case |
| W-2 | the wizard's preview is the **2-D plan** (the OSM wizard's), with the blocks drawn as their footprints | the existing preview, and fast enough to shuffle |
| W-3 | ⭐ **Save terrain as keeps the block scenario as the terrain's SOURCE**: it writes the new terrain AND a new scenario that uses it (empty of the baked blocks); the source scenario stays, so the terrain can be edited and saved again | a baked terrain is no longer editable as blocks; without the source the user's work would be one-way |
| W-4 | the source scenario remembers which terrain it produced; saving again **overwrites that terrain** after a confirmation naming the scenarios that use it | the same "who uses this" check as a changed recipe (B-Q1) |

## 14. Editing a baked terrain again — the source travels with the terrain *(✅ user `2026-11-11`: "yes, add section 14"; E-1…E-6 ✅ agreed, R-4201)*

🔒 User, `2026-11-11`: *"Will there be any way how to open an existing terrain as a scenario so I can keep editing it? … even if we bake everything
into a terrain, we might want to keep the original scenario together with it somehow."* · *"What if the scenario we are editing, the terrain one,
will get some non-terrain entities? Is it harmless? Or will we need to somehow filter those out?"*

```mermaid
graph TD
  BT[(baked terrain folder)] --> TJ[terrain.json - names its source and its base]
  BT --> SRC[(source - the editable block scenario)]
  SRC --> BASE[(base terrain - the ground only, heights, roads, rivers)]
  ED[Edit terrain - from the terrain picker or Open Asset] --> SRC
  SRC -->|Save terrain - bakes only terrain entities| BT
```

*What the picture shows:* the chain ground → blocks → baked result is stored **inside the terrain**, so copying, syncing or sharing the terrain
carries what is needed to edit it again; *Edit terrain* always reopens the whole chain.

| # | lean | why |
|---|---|---|
| E-1 | ⭐ the source scenario lives in the baked terrain's folder (`source/`), and `terrain.json` names it and the base terrain | the terrain and its source can never be separated (copies, the NAS sync, sharing) |
| E-2 | **Edit terrain…** in the terrain picker and Open Asset opens the source in the editor; the editor shows it is a **terrain source** (the title) | the user always knows whether a save changes a terrain or a mission |
| E-3 | a terrain with **no source** (imported, or older) gets one on the first *Edit terrain*: a new source that holds only additions on top of it | every terrain is editable the same way; its existing houses are not blocks and stay baked |
| E-4 | ⭐ **non-terrain entities in a source are harmless and kept**: *Save terrain* bakes ONLY terrain entities — 📐 the bake already selects by component (`StaticObstacleBake.cs:187`, `With<StaticObstacle>().With<ObstacleShape>()`), the new kinds join that query; units, AI, triggers and areas are not terrain and are never baked. ⭐ They are useful there: place a squad to try whether the town is passable, run it, look along a street | filtering by KIND is what the bake does by construction; there is nothing to strip |
| E-5 | the summary of *Save terrain* names what was **not** baked ("12 units and 2 areas stay in the source only"), and the new scenario *Save terrain as* writes (W-3) does **not** copy them — a mission on the terrain is its own scenario | a terrain source should not quietly turn into a mission; the user sees the rule each time |
| E-6 | ⛔ **damage is not baked**: a breach or collapse made while trying the source (a run) stays scenario state; *Save terrain* bakes the intact geometry and says so. A *pre-damaged town* ("a ruined village") is a later option — a block variant, or an explicit "bake damage" tick | a test shot should not quietly change the terrain |
| E-Q | ⏭ **removing a baked house** (an imported one, to make room for a block) = a new "remove" feature saved in the source; designed now, built only if needed early | the user's call when it is needed |

## 15. The mockups — S-0 *(drawn `2026-11-11`; ✅ approved the same day, R-4203; generator `scripts/gen-terrain-authoring-mockups.py` on the one kit, R-4143)*

| page | shows | basis |
|---|---|---|
| [1 — the workflow and the wizard](UX/terrain-authoring-1-wizard.svg) | the workflow strip (wizard → shuffle → edit → save terrain → edit terrain…); step ① six preset pictures + Size / Relief / Vegetation / Water + seed; step ② the 2-D preview with the blocks outlined, what was generated, the budget verdict, *Shuffle layout* / *Shuffle ground*, *Open in editor* | §12, §13, W-1, W-2, TA-2 |
| [2 — blocks in the editor](UX/terrain-authoring-2-blocks.svg) | the TERRAIN SOURCE title chip; a placed block (outline, menu: Re-roll · Rotate · Move · Unpack · Change recipe · Delete; Details: recipe, seed, *Fit to the site*); a block being placed, fitting itself to the track; Add Entity with the six terrain groups and the Blocks tiles | §11 B-1…B-8, E-2 |
| [3 — single features and the scatter](UX/terrain-authoring-3-features.svg) | ① a hedge / wall / fence line; ② a bridge over the river on the road; ③ the area scatter (Street clutter, density, seed); the Clutter tiles | §2, §7, §8, §11 job ③ |
| [4 — saving, and editing again](UX/terrain-authoring-4-save.svg) | the File menu; *Save terrain as* with its summary (baked · stays in the source · damage not baked · mesh pre-built · a new scenario); the overwrite confirmation naming the users; the terrain picker with SOURCE and *Edit terrain…* | §13 W-3, W-4, §14 E-1…E-6 |

⚠ Scope (R-4151): the editor, the menu bar and the picker's existing groups are drawn as dimmed or plain CONTEXT and are unchanged. Clutter tiles
show glyphs where the real tiles will be model renders (the thumbnail atlas).

## 16. From the wizard to the editor, and the same terrain on every node *(✅ V-1…V-6 approved `2026-11-11`, R-4203 — ⛔ V-2…V-6 SUPERSEDED the same day by §18, R-4204: compile at save; V-1 stands)*

🔒 User, `2026-11-11`: *"How will we get from the wizard to the editor? … In a cluster, when the scenario contains this terrain stuff, how will we
make sure that the others will receive the same stuff so they can bake the terrain features into their own 3D map and terrain representations?"*

**Wizard → editor (lean V-1):** *Open in editor* is the wizard's last step and does it all: it writes the ground terrain, **saves** the block
scenario at once (nothing is lost if the editor is closed), and **opens it** in the editor — the same as the OSM wizard's *Open in New Scenario*
(`OsmTerrainWizardWindow.cs:645`, through `Services.OpenNewScenario`). An open scenario with unsaved changes gets the editor's usual save prompt first.

**The same features on every node:**

```mermaid
sequenceDiagram
  participant A as Authoring node - editor
  participant W as Wire
  participant N as Every other node
  participant X as FeatureExpander on each node
  A->>A: place a block - fit to the site ONCE, store the result in the entity
  A->>W: the block entity - recipe id and content hash, seed, position, heading, fitted placement
  W->>N: the same small descriptor, held in Constructing until it arrives
  N->>X: expand recipe plus seed plus placement on the same terrain
  X-->>N: identical houses, walls, trees, clutter
  N->>N: bake into its own terrain world, navmesh and 3-D chunks, then ack
  N->>A: lifecycle ack - the block goes live everywhere at once
```

*What the picture shows:* the wire carries a **small descriptor**, never the houses; each node **expands it itself** and gets the same result
because every input is the same — the mechanism static obstacles already use (`ObstacleShape` is `[PerInstanceValue]`, the creator publishes it and
a node does not promote the ghost before it arrives, R-136; the bake's lifecycle gate, R-243).

| # | lean | why |
|---|---|---|
| V-1 | *Open in editor* saves the scenario and opens it automatically | one click, nothing to find; the OSM wizard's precedent |
| V-2 | ⭐ **fit to the site happens ONCE, at placement, on the authoring node; the result is stored in the entity** | the bake on every node then depends only on recipe + seed + stored placement + the terrain — never on the order other blocks arrive in, so no node can expand differently |
| V-3 | ⭐ the descriptor carries the **recipe's content hash**; recipes are content assets synced to every node before a load (asset management) — a node whose recipe differs **refuses the load and names it**, never expands a different block | the one way two nodes could still disagree |
| V-4 | the expander is **pure and integer-seeded** (one RNG, no wall clock, no dictionary order) and has a rail that expands the same block twice in two processes and compares the result byte for byte | determinism stated as a test, not a hope |
| V-5 | the external visual engine gets the **expanded** features (L-7, B-7) from the node that owns the entity | it cannot run our recipes |
| V-6 | a replay records the **descriptor** (unmanaged, tiny) and expands it again on load | the recording stays small; the bake follows keyframes as today |

## 17. ⛔ Critical review `2026-11-11` — what the design gets wrong or leaves out *(before S-1; three independent reviews + the coordinator, every load-bearing claim re-checked in code)*

🔒 User, `2026-11-11`: *"take a new critical look on the whole terrain editing feature from different perspectives and find flaws and gaps"*.
Reviews: simulation / cluster / replay · scale / data / content pipeline · the author's workflow. ✔ = re-verified by the coordinator.

### 17.1 The gaps, most severe first

| # | gap | evidence | consequence |
|---|---|---|---|
| G-1 | ⛔ **damage and door state are addressed by INDEX and by terrain NAME** | ✔ `WallBreach.Panel` is the GLOBAL panel index, `BuildingDamage.Building` an int (`BuildingDamage.cs:21,57`); keys `"<terrain>/<label>"` (`:113`), doors `DoorKey(terrain, label, id)` (`TerrainBuildingExpander.cs:299`), state keyed so (`TerrainObjectsSection.cs`) | houses appended in each node's query order get different indices ⇒ the Muscle breaches house N, another node draws the hole in a different house; re-roll / unpack / *Save terrain as* orphan every saved door and damage |
| G-2 | ⛔ **the bake merges BOXES only** — L-1's "the bake learns the new kinds" is a rewrite | ✔ `BakeObstacles(IReadOnlyList<TerrainPrism>)` → `TerrainObstacles.With` appends prisms and SHARES the base's surfaces, panels, buildings, doors, walkables and heights (`TerrainResidency.cs:505-517`, `StaticObstacles.cs:~150-195`) | a house's floors and doors, a wood's concealment, water's cut, a bridge deck, a pad — none can be added; collapse works only on buildings, so a bridge cannot fall |
| G-3 | ⛔ **added houses get no door entities** | ✔ door entities are created once, at scenario load, from the base world (`ScenarioLoadStep.cs:264`, `TerrainObjectRequests.ForDoors`); the navmesh's fine areas read `world.Buildings` / `world.Doors` (`RecastNavmeshFactory.cs:144-156`) | doors that cannot open, no building clearing, coarse navmesh inside, door state not saved |
| G-4 | ⛔ **every change rebuilds the WHOLE terrain on every node** | ✔ new world + whole index + whole cover + every navmesh tile re-keyed (`TerrainResidency.cs:510-513`); ≈ 16 s navmesh + 1 s index/cover on scale-1 (`DESIGN_Scale_Rig.md:140-142`); re-fires 0.5 s after any change; the 3-D view rebuilds every chunk on a new world (`TerrainLayer3D.cs:48-57`) | moving one hedge in a town ≈ 17 s and a full 3-D rebuild; a generated town as blocks = a COLD navmesh bake on every node at first load (scale-1 cold 536 s) — far over R-4192's 30 s |
| G-5 | ✅ **FIXED `2026-11-11` (CE-4419, backend): the terrain module waits without a timeout (`EntityLifecycleModule.WaitWithoutTimeout`)** — was: ⛔ **a slow bake DESTROYS the entities** | ✔ construction timeout 18 000 FRAMES (`EntityCreationPack.cs:75`); a timeout destroys the entity (Peek and Fire §9 O2) | a big town on a fast headless node can exceed it ⇒ its blocks vanish on that node only |
| G-6 | ⛔ **recipes and terrains are identified by NAME, and B-Q1 / V-3 / V-6 contradict** | B-Q1 "a changed recipe changes every scenario" vs V-3 "a hash mismatch refuses"; `RecordingMetadata.TerrainName` is a name (`RecordingMetadata.cs:79`); asset sync compares length + mtime (Asset Management) | after any recipe edit or terrain re-save, old scenarios refuse or change, and old RECORDINGS replay different houses (paths through walls) |
| G-7 | ✅ **FIXED `2026-11-11` (CE-4420, backend): the Replay Browser loads the recording's overlay by hash and bakes its obstacles per seek (§18a modules)** — was: **the Replay Browser does not bake** | ✔ no `StaticObstacleBakeSystem` in `ReplayBrowserSubsystem` (0 references); it mirrors the base world only | scenario houses, walls, woods — and today's obstacles — are invisible in replays there |
| G-8 | **the external engine: the design contradicts itself, and a baked terrain is lost to it** | V-2 "never the houses" vs V-5 / B-7 "the expanded features"; one block = ~40 houses with panels in one descriptor (DIS / HLA has no building type); after *Save terrain* the houses live only in our files | the other engine draws nothing scenario-added, or nothing baked |
| G-9 | **"Save terrain as" has no writer that can write it** | ✔ `TerrainFolderWriter` writes template houses with `rotation` 0 (`:70-71`), has no slab / ramp / obstacle prism, takes importer inputs, not a `TerrainWorld`; the parser has no clutter kind | rotated houses saved wrong, bridges and clutter dropped; E-3 (add to an imported terrain) has no write path; and §5 (re-point THIS scenario) contradicts W-3 / E-5 (a NEW scenario) |
| G-10 | **saving to a new folder re-bakes every navmesh tile** | ✔ prefill reads only the new folder (`NavmeshPrefill.cs:22-28`), never the base's tiles | each *Save terrain as* ≈ 9 min cold at 5.5 km |
| G-11 | **E-3 bakes twice** | a terrain with no source has no separate ground base | the second *Save terrain* stacks the additions onto a terrain that already holds them |
| G-12 | **roads and traffic never change** | the bake never touches the road net (`RoadNetworkHolder`); no road feature kind in §2 | a placed bridge carries no vehicles; a destroyed bridge is still driven over by SUMO traffic |
| G-13 | **re-saving a terrain breaks the missions on it** | W-4 only NAMES the users; scenarios name their terrain by string | a new house stands on a squad, a hedge cuts a route — found at run time |
| G-14 | **storage** — ✅ the git half FIXED `2026-11-11` (CE-4425): `assets/terrain/*` is ignored except the named demos, plus `*/source/` and `**/terrain-overlay/` | ✔ only `assets/terrain/scale-*/` is gitignored — a generated `assets/terrain/<name>/` (heights, navtiles, source, base: hundreds of MB) is committable; the source + base inside the terrain double what every node downloads; publish never deletes stale navtiles | repo bloat; slow syncs |
| G-15 | smaller | shapes: an unmanaged fixed list vs the 1 024-byte component cap (✔ `EntityCommandBuffer.cs:37`) vs the managed `EditablePolyline` the area tools already use · a big feature would get ONE movement collider of half its diagonal (`ObstacleTkbTranslator`) · clutter destructible (B-Q3) vs written into the terrain (L-6) · replica float noise (✔ the bake already rounds to 1 cm) and templates / materials not in the recipe hash · per-house edits inside a block (only Re-roll / Unpack) · multi-select · two authors on one source · a bake is not undoable · "terrain out of date" never shown · the Add Entity picker's groups are DIS masks and its pictures an atlas built into the executable — a recipe fits neither · blocks on dense OSM towns · water in the navmesh unverified · the per-frame collection allocates | each a row for S-1…S-4 |

### 17.2 ⭐ The fork these point to — **F-1: expand at LOAD on every node, or COMPILE at SAVE?**

G-1, G-4, G-6, G-7, G-8 and G-10 share one root: the approved model (R-4203 V-2…V-6) EXPANDS blocks on every node at every load and
BAKES them into a world rebuilt from scratch. The alternative:

```mermaid
graph LR
  ED[Editor - live edits] -->|small incremental bake, this node only| EV[the author's view]
  ED -->|Save scenario| CO[Compile once - expand blocks, canonical order, stable keys]
  CO --> OV[(terrain OVERLAY beside the scenario - world parts, stable keys, its navmesh tiles seeded from the base)]
  OV --> N[every node loads base terrain + overlay - no expansion, no cold bake]
  OV --> RB[Replay Browser and recordings - the overlay's hash]
  OV --> EX[external engine - one file format]
  OV -->|Save terrain as| T[(a terrain = base + overlay merged)]
```

| | expand at load (approved) | ⭐ compile at save (proposed) |
|---|---|---|
| identity (G-1) | per-node order, must be made canonical everywhere | fixed once in the file, stable keys |
| load time (G-4, G-10) | cold navmesh for every block tile, every node | tiles baked at save, seeded from the base's |
| determinism (G-6, V-4) | every node must expand byte-identically | expanded once; nodes load bytes |
| recordings (G-6, G-7) | old replays re-expand from today's recipe | the recording names the overlay's hash |
| external engine (G-8) | a descriptor per block it cannot expand | the same parts file |
| save terrain (G-9) | a second writer | the overlay IS the terrain's own format; *Save terrain as* = base + overlay |
| cost | — | an overlay file per scenario (binary — ⛔ not in git; NAS / content root, like real terrains); the editor still needs a small LIVE bake for edits; runtime-placed obstacles keep today's entity bake |

**F-1 ✅ DECIDED `2026-11-11`: compile at save** (R-4204) — see §18.
Either way G-2, G-3, G-5, G-12, G-13, G-14 need their own fixes (a general world merge with doors; a wall-clock timeout; a road-net
patch; a conflict check against the missions on a re-saved terrain; content outside git).

## 18. ⭐ The compiled model — build once at save, load everywhere *(✅ R-4204 `2026-11-11`; supersedes §5 and §16 V-2…V-6)*

🔒 User, `2026-11-11`: *"approved, compile at save. Orchestrator can make sure the compiled saved stuff gets distributed to nas and to other nodes in
the cluster"*.

```mermaid
sequenceDiagram
  actor U as Author
  participant E as Editor
  participant C as OverlayCompiler
  participant P as Asset publish
  participant NAS as NAS
  participant O as Orchestrator
  participant N as Every node
  U->>E: Save scenario
  E->>C: blocks, features, scatters of this scenario
  C->>C: expand once, canonical order, stable keys, merge parts, bake the touched navmesh tiles seeded from the base
  C-->>E: overlay folder beside the scenario, with its content hash
  E->>P: publish the scenario and its overlay
  P->>NAS: upload
  U->>O: load the scenario on the cluster
  O->>N: sync the terrain, the scenario and its overlay from the NAS before the load
  N->>N: load base terrain plus overlay - no expansion, no cold bake
  N->>N: create door entities for the overlay houses, by stable key
```

*What the picture shows:* the expensive and non-deterministic work (expanding recipes, fitting, merging, navmesh) happens ONCE, on the authoring
node, at save; every other consumer only LOADS bytes. Distribution is the existing asset path (Asset Management §4 NAS → node before a load,
§5 node → NAS publish), now carrying one more part.

```mermaid
classDiagram
  class OverlayCompiler {
    <<new, editor side>>
    Compile(scenario, baseTerrain) Overlay
  }
  class FeatureExpander {
    <<new, pure>>
    Expand(feature) parts
  }
  class TerrainOverlay {
    <<new, a folder beside the scenario>>
    parts in canonical order
    stable keys
    navtiles
    contentHash
    baseTerrainHash
  }
  class TerrainWorldMerge {
    <<new, replaces TerrainObstacles.With for overlays>>
    Merge(base, overlay) TerrainWorld
  }
  class TerrainResidency {
    <<existing, extended>>
    LoadOverlay(overlay)
  }
  class LiveEditBake {
    <<new, editor only>>
    rebakes the changed area while authoring
  }
  class StaticObstacleBakeSystem {
    <<existing, unchanged>>
    runtime-placed obstacles
  }
  OverlayCompiler --> FeatureExpander
  OverlayCompiler --> TerrainOverlay
  TerrainResidency --> TerrainWorldMerge
  TerrainWorldMerge ..> TerrainOverlay
  LiveEditBake --> FeatureExpander
  LiveEditBake --> TerrainWorldMerge
```

| # | rule | closes |
|---|---|---|
| C-1 | ⭐ **the overlay is compiled when the scenario is SAVED** (and by *Open in editor* from the wizard); it lives beside the scenario as a folder: the merged parts (buildings with panels and doors, walls, surfaces, walkables, prisms, trees, pads) in **canonical order**, each with a **stable key** (`<feature entity guid>/<index in its expansion>`), its **navmesh tiles** (seeded from the base terrain's tiles — only the touched ones are new) and a **content hash** + the **base terrain's hash** | G-1, G-4, G-6, G-10 |
| C-2 | ⭐ **the orchestrator distributes it**: the editor PUBLISHES the overlay to the NAS with the scenario; before a load the orchestrator SYNCS it to every node, exactly as it does the terrain folder (Asset Management §4 / §5; a new load part beside the terrain) | the user's ruling |
| C-3 | **every node LOADS, never expands**: `TerrainResidency` merges base + overlay (`TerrainWorldMerge` — the general merge G-2 needs: surfaces, panels, buildings, doors, walkables, heights, in the overlay's order) and creates the overlay houses' door entities by stable key | G-2, G-3, determinism |
| C-4 | **damage and door state are keyed by the stable key**, never by index or terrain name; a replicated breach names `<key>` + panel-in-building | G-1 |
| C-5 | **a load checks the hashes**: the overlay's base-terrain hash must match the terrain on the node, else the load refuses and names both; a recording stores the overlay's hash and the browser loads the same overlay | G-6, G-7 |
| C-6 | **the scenario keeps its blocks and features as ENTITIES for editing** (recipe, seed, fitted placement — R-4199 / R-4203 V-1 unchanged); on a node they are inert data — the overlay, not the entities, is the terrain. A recipe changed later reaches a scenario only when it is compiled again (saved), so old recordings and loads are unaffected | B-Q1 vs V-3 contradiction |
| C-7 | **while editing, a LIVE bake on the editor node only** rebuilds the changed area (incremental: the touched tiles, the touched 3-D chunks), off the frame | G-4 in the editor |
| C-8 | the external engine reads the **overlay's parts** (the same format as a terrain's world) | G-8 |
| C-9 | ***Save terrain as* = base + overlay merged by the same `TerrainWorldMerge`** and written by a writer of a `TerrainWorld` (rotation, slabs, ramps, prisms — G-9); its navmesh is the base's tiles + the overlay's, no cold bake | G-9, G-10 |
| C-10 | obstacles placed DURING a running exercise keep today's entity bake (`StaticObstacleBakeSystem`), with a wall-clock timeout (G-5) | runtime |
| C-11 | ⛔ the overlay is binary content — **never in git**; it lives on the NAS / content root like real terrains (G-14) | storage |

Still open in their own rows: roads and bridges in the road net (G-12, CE-4423), the check of missions on a re-saved terrain (G-13, CE-4424),
the smaller items (G-15, CE-4426).

## 18a. The overlay as built — format, classes, the load, the distribution *(O-1, backend `2026-11-11`; build-state: READY-TO-BUILD — numbers in §18b)*

**INVENTORY — the queries run** (`scripts/cbm.sh search_graph`, `label: Class`, + grep): `.*Terrain(Residency|Obstacles|FolderWriter|Parser|Loader|World)$` → 4
(`TerrainResidency`, `TerrainObstacles`, `TerrainWorld`, `SumoNetTerrainLoader`) · `.*Terrain.*(Writer|Parser|Reader|Folder|Catalog|Manifest|Hash).*` → 3
(`TerrainCatalog`, `TerrainDefinitionParser`, `TerrainWorldParser`; grep adds `TerrainFolderWorld`, `TerrainFolderWriter`) · `.*(Navtile|NavmeshPrefill|…).*` →
`NavTileCache`, `NavTileKey` · `.*(AssetSync|LoadPhase|…).*` → `AssetSyncService`, `AssetTreeSync`, `LoadPhaseChain`, `LoadPhaseContext`,
`AssetInventoryProcessManager` · `.*(BuildingDamage|WallBreach|DoorKey|DoorState|TerrainObject).*` → `DoorState(s)`, `TerrainObjectKey`, `TerrainObjectRef`,
`TerrainObjectRequests`, `TerrainObjectsSection`, `EntityDoorState` translators (+ `BuildingDamage`, `WallBreach` by grep). ⚠ coverage could not be checked
(`check_index_coverage` is not in the CLI). ⇒ ⛔ no overlay, merge, content hash or overlay sync exists; every piece below EXTENDS one of these.

### The format — a terrain folder in miniature *(the lean taken: the terrain's own format, so the one parser reads it)*

```
<scenario folder>/terrain-overlay/          (⛔ never in git — .gitignore **/terrain-overlay/, O-6)
  overlay.json                 header: format 1 · contentHash · base { name, contentHash } · scenario id · world file name · part counts
  overlay.world.geojson        the SAME schema as a terrain's world file; features in CANONICAL order (ordinal sort of the stable key);
                               every building / wall / surface / walkable carries "label": "<feature entity guid>/<index in its expansion>"
  buildings/<t>.building.json  generated templates only (the shared assets/buildings/ ones are referenced, not copied)
  navtiles/<x>_<z>_<hash>.navtile   ONLY the tiles whose key differs from the base's (NavTileKey — the same file format)
```

| rule | why — the measured fact it rests on |
|---|---|
| the overlay world is PARSED by `TerrainWorldParser` into its own `TerrainWorld` (indices from 0), then MERGED | ⭐ one parser, one building expander (`TerrainBuildingExpander`: panels, openings, doors, storeys, walkables) — ⛔ a second reader drifts (CE-4393's own rule, `TerrainFolderWorld.cs:7-9`) |
| the merge APPENDS: base parts keep their indices; overlay part i lands at `base.Count + i`, and every cross-index is offset — `TerrainPrism.Panel / .Building`, `TerrainWalkable.Building`, `TerrainWallPanel.Building`, `TerrainDoorDef.Panel` (`TerrainWorld.cs:42,46,64`, `TerrainBuildings.cs:49,61`) | the base's indices — and every saved / replicated state keyed by them today — are untouched; the overlay's are the same on every node because its order is in the FILE (G-1's "per-node query order" cannot arise) |
| building key = its LABEL (globally unique: base labels as today, overlay labels `<guid>/<i>`); door key = `<building key>/<door id>`; ⛔ no terrain-name prefix for new keys (O-3) | `BuildingDamages.KeyOf` and `DoorKey` prefix the terrain NAME (`BuildingDamage.cs:113`, `TerrainBuildingExpander.cs:299`) ⇒ *Save terrain as* (a new name) orphans every saved state (G-1). A legacy key `"<terrain>/<label>"` is still READ (prefix stripped when it names the resident terrain) |
| content hash = SHA-256 over `overlay.json`'s parts and the world file (not navtiles); the base's hash = SHA-256 over `terrain.json` + the files it names, cached in `terrain.hash` keyed by each file's (length, mtime) | a NAME identifies nothing (`RecordingMetadata.TerrainName`, G-6); the cache keeps a 127 MB height store from being re-hashed per load |
| navtiles: the merged world's bake seeds from the base's `navtiles/` THEN the overlay's (`RecastNavmeshFactory.cs:115-120` reads ONE `SourceFolder` today ⇒ it takes a list) | the overlay's houses touch few tiles — every untouched tile is the base's file (G-4, G-10) |
| ⛔ NOT in v1: height pads (no type exists — `grep Pad` in the world types: none) | a finding row in this lane's block, not a silent drop |

### Classes

```mermaid
classDiagram
  class TerrainWorldParser { <<existing>> Parse(geojson, name, assets) TerrainWorld }
  class TerrainFolderWorld { <<existing, extended>> ReadWorld(definitionPath, def, name) · ReadOverlay(overlayFolder) TerrainOverlay }
  class TerrainOverlay { <<new>> Header · World(TerrainWorld, indices from 0) · Folder (its navtiles) }
  class TerrainOverlayHeader { <<new, overlay.json>> Format · ContentHash · BaseName · BaseHash · ScenarioId · World }
  class TerrainWorldMerge { <<new, FDP Terrain>> Merge(base, overlay) TerrainWorld }
  class TerrainObstacles { <<existing, unchanged>> With(world, prisms, damage) — runtime obstacles over the MERGED world }
  class TerrainContentHash { <<new>> Of(definitionPath) · Of(overlayFolder) }
  class TerrainResidency { <<existing, extended>> Prepare(terrainName, overlayFolder?) }
  class TerrainLoadStep { <<existing, extended>> reads the staged header's OverlayHash }
  class OverlayCompiler { <<new, editor side — O-4>> Compile(scenario, base) → folder }
  class FeatureExpander { <<new, pure, seeded — O-4>> }
  class StorageGatewayModule { <<existing, extended>> StageNamedArtifacts: + the overlay folder }
  class RecastNavmeshFactory { <<existing, extended>> seeds from world.SeedFolders (base, overlay) }
  TerrainFolderWorld --> TerrainWorldParser
  TerrainFolderWorld --> TerrainOverlay
  TerrainOverlay --> TerrainOverlayHeader
  TerrainResidency --> TerrainFolderWorld
  TerrainResidency --> TerrainWorldMerge
  TerrainResidency --> TerrainContentHash : refuses a base mismatch
  TerrainResidency --> TerrainObstacles : _baseWorld = the MERGED world
  TerrainLoadStep --> TerrainResidency
  OverlayCompiler --> FeatureExpander
  OverlayCompiler --> TerrainWorldMerge : the compile bakes the merged world's touched tiles
  StorageGatewayModule ..> TerrainOverlay : mirrors the folder (AssetTreeSync)
  RecastNavmeshFactory ..> TerrainOverlay : reads its navtiles
```
*What it shows that prose hid:* the merged world becomes `_baseWorld` (`TerrainResidency.cs:323`), so the runtime obstacle bake (C-10) and building damage
lay over base + overlay with NO change to `TerrainObstacles.With` — the overlay is not a second bake path, it is a bigger base.

### The load — every node

```mermaid
sequenceDiagram
  participant O as Orchestrator (StorageGatewayModule)
  participant D as Node disk
  participant L as TerrainLoadStep
  participant R as TerrainResidency
  participant M as TerrainWorldMerge
  O->>D: mirror terrain/<name>/ (existing) + overlays/<contentHash>/ (new) · staged header + OverlayHash
  L->>R: Prepare(terrainName, overlays/<hash>)
  R->>R: base = ReadWorld · overlay = ReadOverlay
  R->>R: TerrainContentHash.Of(base) == overlay.BaseHash ? else THROW naming both
  R->>M: Merge(base, overlay)
  M-->>R: one TerrainWorld (base indices kept, overlay appended)
  R->>R: navmesh (seeds: base navtiles, overlay navtiles) · cover · index
  L->>R: Commit — the merged world is the base the obstacle bake builds on
  Note over L: ScenarioLoadStep creates door entities from world.Doors (ScenarioLoadStep.cs:264) — overlay doors included, keyed by stable key
```

### The modules — who calls what each load *(⭐ O-8 `2026-11-11`: the two former dead edges are live)*

```mermaid
graph TD
  ED[Editor save] -->|O-4| OC[OverlayCompiler]
  OC --> FS[(scenario folder/terrain-overlay)]
  FS -->|publish with the scenario, Asset Mgmt §5| NAS[(NAS)]
  NAS -->|StageNamedArtifacts, every prefetch| ND[(node overlays/hash)]
  ND --> TLS[TerrainLoadStep - Terrain load part, MuscleGround + Brain]
  TLS --> RES[TerrainResidency.Prepare]
  OC -->|O-8: also kept by hash, nas/assets/terrain/overlays/hash| NASO[(NAS overlays/hash)]
  REC[Recording metadata - TerrainName, OverlayHash, TerrainHash] --> RB[Replay Browser - ApplyRecordingTerrain]
  RB -->|TerrainCatalog.ResolveOverlay: node staging, else NAS| NASO
  RB --> RES
  RB -->|on a load / a jump only: SignatureOfWorld, then CollectPrisms| OBS[TerrainObstacles.With - displayed world]
```
*What it shows:* the cluster nodes reach the overlay through the ONE prefetch the terrain already rides. The Replay Browser reaches it by the HASH the
recording stamped (`RecorderTickSystem.StampWorld`), from the copy the master keeps by hash on the NAS — so a recording still finds its additions after
the scenario was edited again. A missing or mismatched overlay opens the base with `TerrainWarning`, never a crash. Its obstacles go through the node's own
collector; the allocation-free `SignatureOfWorld` guards it, so a steady or fast-playback frame allocates 0 B (rail `O8_BakeRecordedObstacles_AllocatesNothing_*`).
⚠ It bakes on a jump, not every frame: an obstacle created during uninterrupted 1× playback appears at the next seek (CE-3434).

| claim the design rests on | code — how it IS | design basis |
|---|---|---|
| the prefetch mirrors the terrain folder to every target before the load | ✅ `StorageGatewayModule.cs:557-563` (`AssetTreeSync().Sync`) | ✅ Asset Management §4 |
| the terrain step runs only after distribution is acked | ✅ `TerrainLoadStep.cs:82-86` (L8 comment) | ✅ Cluster Load Phase §7.3 ④ |
| the obstacle bake builds on `_baseWorld` | ✅ `TerrainResidency.cs:323,508-512` | ✅ Peek and Fire §9 |
| door entities come from `world.Doors` at scenario load | ✅ `ScenarioLoadStep.cs:264` → `TerrainObjectRequests.ForDoors` | ✅ §17 G-3 |
| fine navmesh areas read `world.Buildings` / `world.Doors` | ✅ `RecastNavmeshFactory.cs:142-155` | ✅ §17 G-3 |
| keys carry the terrain name | ✅ `BuildingDamage.cs:113`, `TerrainBuildingExpander.cs:299` | ✅ §17 G-1 |

### 18a-3. Stable keys — O-3 as built *(CE-4417, backend `2026-11-11`)*

| decision | why — measured |
|---|---|
| ⭐ a replicated breach names the house by KEY and the wall by its index WITHIN the house; the receiver resolves both against ITS world (`BuildingDamages.IndexOfKey`, `BuildingPanelIndex.ToGlobal`) — the global indices never cross the wire | the sample carried the sender's global `Building` and `Panel` and the receiver applied them as they were (`EntityBuildingDamageTranslators.cs:77,179`) ⇒ two worlds assembled differently breach different houses (G-1). The SAVE already used key + panel-in-building (`BuildingDamageSave.cs:42-62,149`) — the wire now speaks the same language |
| a key resolves with or without the terrain-name prefix (`"<terrain>/<label>"` and `"<label>"`) | after *Save terrain as* (a new name) an old key still finds its house |
| ⚠ **DEVIATION from §18a's first table — the terrain-name PREFIX is NOT dropped from the keys written today; it moves to O-5** | 📐 measured: six shipped scenarios (`bt-doors`, `bd-destruction`, `bc-*`, `ua-civ-shelter`) save door state under `"<terrain>/<building>/<door>"`, AND behaviour params name doors that way (`DoorLocksmith`: `"door":"bt-range/House A/front"`), compared as strings in places not yet enumerated; recordings hold them in `TerrainObjectKey`. Dropping the prefix now touches all three; O-5 (*Save terrain as*) rewrites keys anyway, so it is done there, with the legacy read already in place here. Overlay labels are `<guid>/<i>` — stable across re-saves whatever the prefix |

### 18a-2. The merge — O-2 as built *(CE-4416, backend `2026-11-11`)*

| as built | where |
|---|---|
| `TerrainWorldMerge.Merge(base, overlay)` — appends; offsets `TerrainPrism.Panel/.Building`, `TerrainWalkable.Building`, `TerrainWallPanel.Building`, `TerrainDoorDef.Panel` (`Opening` panel-local, not offset), `BakedCollapsed`; every field of every part copied (checked against the types); a duplicate building label or door key THROWS naming both; bounds = union; Name, SourceFolder, Height, GroundZ, Materials, Breaches (a material rule set — no per-panel index), Beyond = the base's | `FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainWorldMerge.cs` |
| `TerrainWorld.SeedFolders` (default `[SourceFolder]`); the merge lists base then overlay; `NavTileCache.ForSeeds(folders)` reads them in order; `TerrainObstacles.With` keeps them | `TerrainWorld.cs`, `NavTileCache.cs`, `RecastNavmeshFactory.cs`, `StaticObstacles.cs` |
| `TerrainOverlay` + `TerrainOverlayHeader` (`overlay.json`, format 1); `TerrainFolderWorld.ReadOverlay(folder, terrainName, sharedBuildingsRoot)` — ⚠ DEVIATION: a third parameter, because an overlay beside a scenario cannot find the shared `assets/buildings` from its own folder; `TerrainResidency` passes `TerrainAssets.RecipesSibling(baseFolder)` | `TerrainOverlay.cs`, `TerrainFolderWorld.cs` |
| `TerrainResidency.Prepare(terrainName, overlayFolder = null)` — the merged world is indexed and becomes `_baseWorld`; the "already resident" skip also compares the overlay (else a changed overlay was skipped) | `TerrainResidency.cs` |
| a bridge: slab / ramp / polygon-building features with `"structure": "<key>"` form ONE `TerrainBuilding` (label = key, no panels, solid; footprint = the part, or the box of several; storey from the lowest ground under it to the deck) ⇒ the existing collapse path drops its deck (`TerrainObstacles.With` removes walkables of a collapsed building) | `TerrainWorldParser.cs` |
| 📐 water is ALREADY impassable — the ground mesh cuts water polygons out (no fix; kept as a rail) | `RecastNavmeshFactoryTests.O2_WaterFromAMergedOverlay_IsImpassable…` |

Rails (each red-proved by a compiling mutation): `TerrainWorldMergeTests` (indices, duplicate keys, seed folders, the merged bake seeds from both folders, `ReadOverlay` resolves shared templates) · `RecastNavmeshFactoryTests.O2_*` (an overlay house is enterable through its offset door, a locked door blocks; water impassable) · `TerrainWoodsTests.O2_AForestFromAnOverlay_ConcealsLikeTheBasesWoods` · `BuildingOverlayTests.O2_AStructureBridge_Carries…AndStopsCarrying…` · `TerrainLoadStepTests.Prepare_WithAnOverlayFolder_StagesTheMergedWorld_O2`.

### 18b. The numbers — scale-1 with a 700-house overlay *(O-1, measured `2026-11-11`, this cloud VM: 4 cores, Debug build)*

Input: scale-1 (5.5 km, 1 651 houses) split by `scripts/terrain-overlay-measure.py` into a 951-house BASE and an OVERLAY of 700 of its houses (labels
`<guid>/<i>`) + 40 woods of 60 m, canonical order. Harness: `TerrainWorldMergeTests.O1_Measure_AScaleOneOverlay` (opt-in, `HROT_MEASURE_OVERLAY=1`).

| measure | value | reads as |
|---|---|---|
| overlay folder size (no navtiles) | **153 KB** (one world file) | 0.2 KB per house — the parts are template placements, expanded by the parser |
| overlay read + expand (700 houses → 36 001 prisms, 8 130 panels, 3 002 doors, 4 527 walkables) | **0.63 s** | the cost a node pays per load, beside the base read (951 houses: 1.66 s, with the 127 MB height store) |
| the merge | **11 ms** | negligible |
| ✅ merged prisms | **84 607 = the unsplit scale-1's** (`DESIGN_Scale_Rig.md` §5.1) | base + overlay rebuilds the original terrain exactly |
| navmesh tiles the overlay touches | **2 006 of 52 900 per layer** (3.8 %; ×2 layers ≈ 4 012 tiles) | the tiles a COMPILE bakes; every other tile is the base's file |
| ⚠ the compile's tile bake | **≈ 20 s estimated, not measured** — 4 012 tiles × 5.1 ms (the cold rate: 536 s / 105 800 tiles on 4 cores, §5.1) | once, at SAVE, on the authoring node; a loading node bakes NOTHING cold (it reads the overlay's tiles) |
| ⚠ the overlay's `navtiles/` size | **not measured** — needs O-4's compile | |

⇒ **READY-TO-BUILD.** A node's load grows by ≈ 0.64 s for a 700-house overlay; the save carries the tile bake.

### 18c. The compile — O-4 part 2 *(CE-4415, backend `2026-11-11`; build-state: READY-TO-BUILD)*

```mermaid
sequenceDiagram
  participant N as Nodes (their slices)
  participant M as Master - StorageProcessManager
  participant C as OverlayCompiler
  participant X as FeatureExpander
  participant NAS as NAS scenario folder
  N->>M: scenario slices (save)
  M->>M: ScenarioMergeCore.Merge -> canonical scenario.json
  M->>C: Compile(canonical dom, scenario dir, base terrain definition)
  C->>C: entities with TerrainBlock / TerrainFeature (guid = stable key prefix)
  C->>X: Expand(recipe, seed, site, base world) per block - pure, seeded
  X-->>C: features labelled guid/i
  C->>C: canonical order, write overlay.world.geojson + overlay.json (hashes)
  C->>C: bake the merged world's navmesh: write ONLY tiles the base's seed lacks
  C->>NAS: scenario dir/terrain-overlay - already on the NAS: published by construction
  Note over C: no TerrainBlock / TerrainFeature entity => no overlay folder (a stale one is deleted)
```
*What it shows:* the compile runs where the CANONICAL scenario is born — the master's merge writes it straight to the NAS — so "publish the overlay"
is not a step. The editor's own save (`EditorScenarioSession.SaveTo`, one node, no cluster) calls the SAME `OverlayCompiler.Compile`.

| element | decision | why |
|---|---|---|
| `TerrainBlock { FixedString32 Recipe; uint Seed; float HeadingDeg }` + `SimTransform` | an unmanaged, saved component — one entity per placed block (B-2); its guid prefixes every part's key | recorded ⇒ unmanaged (CLAUDE.md); one entity per block keeps the wire small |
| `TerrainFeature { byte Kind; FixedString32 Template; float HeadingDeg; float Pad }` + `SimTransform` | a single placed house (Kind = House) in v1; `Pad` written, not yet used (L-5) | the smallest kind that proves the path; lines / areas need the shape capacity measurement (§10) |
| recipe = `assets/terrain-recipes/<id>.recipe.json`: houses (templates, count range, radius, spacing) + woods (count, size) | shipped data (B-Q2); the content hash covers the EXPANDED world, so a recipe edit reaches a scenario only when it is compiled again (C-6) | B-1 |
| `FeatureExpander.Expand` — pure; its own seeded generator (xorshift, never `System.Random`); keeps out of the base's building footprints and water surfaces (B-3 part) | identical bytes for the same input on every machine | ⚠ road alignment (B-3) needs the road net — a finding row, not v1 |
| navtiles: the compile bakes the merged world in Prefill mode seeded from the base; a seed hit is NOT written | the overlay's `navtiles/` holds only the touched tiles (§18b: 3.8 %) | G-4, G-10 |
| ⛔ the TKB type + the Add Entity groups for blocks / features | ui's (B-5), later; the engine reads the components from the scenario JSON whatever placed them | the handoff's "UI is ui's, later" |

#### 18c-1. What the approved mockups need from the engine *(user `2026-11-11`: "make sure the visuals reasonably match the mockup design")*

The screens are ui's; ⭐ the DATA each screen shows is this batch's, so ui can draw the mockup as drawn — nothing invented, nothing missing:

| mockup | it shows | the engine provides | item |
|---|---|---|---|
| [2 — blocks](UX/terrain-authoring-2-blocks.svg), Details | *Fit to the site*: Align to the nearest road · Keep out of water · Keep out of buildings · Level houses on pads | `TerrainBlock.Fit` flags (four bits, all on by default), honoured by `FeatureExpander` (road alignment ⚠ needs the road net — a row; pads written, flattening later S-5) | O-4 |
| 2, Details | *Contents: 28 houses · 64 clutter items* | `overlay.json` `blocks[]`: per block key, recipe, seed, house / clutter / wood counts | O-4 |
| 2, the canvas | the block OUTLINE and the placing ghost | `blocks[].outline` (the expanded parts' hull, local metres); `FeatureExpander` is pure, so the ghost is the same call on the editor | O-4 (ghost: ui) |
| 2, the canvas | the houses drawn like the terrain's own | the merged world is the terrain world ⇒ the 2-D / 3-D terrain layers draw overlay houses with no change — ⭐ VERIFIED by rendering a frame (O-4 gate) | O-2 / O-4 |
| [4 — save](UX/terrain-authoring-4-save.svg), *Save terrain as* | *Baked: 23 blocks → 684 houses, 41 farm buildings · 14 woods, 3 scatters, 220 trees · 6.1 km of walls, fences and hedges · 3 bridges* | `TerrainSaveSummary` — counts per kind + wall length (km) + bridges | O-5 |
| 4 | *Stays in the source only: 12 units and 2 areas — not terrain (E-4) · damage to 3 houses from a test run — not baked (E-6)* | the summary's `NotBaked` (units, areas, damaged houses) | O-5 |
| 4 | *Pre-build the walkable mesh ≈ 2 min* | the summary's tile count + the estimate (the cold rate, §18b) | O-5 |
| 4, overwrite | *Used by 3 scenarios: …* | `TerrainUsers.Of(terrainName)` — the scenarios naming it | O-5 |

### 18a-4. Distribution and load — O-4 part 1 as built *(CE-4415, backend `2026-11-11`)*

| as built | where |
|---|---|
| `TerrainContentHash.OfTerrain(definitionPath)` — SHA-256 over every file of the folder but `navtiles/`, `source/`, `terrain.hash` (ordinal path order); cached in `terrain.hash` by (length, mtime) — 📐 scale-1: **203 ms cold, 5 ms cached** | `TerrainContentHash.cs` |
| `TerrainContentHash.OfOverlay(folder, baseHash)` — over the world file + `buildings/*` AND the base's hash: the same parts over another base are another overlay (different navtiles), and the staged folder is named by it — ⚠ found in review, added with a rail | `TerrainContentHash.cs` |
| the prefetch mirrors `<scenario>/terrain-overlay/` to `{node}/Terrain/overlays/<contentHash>/` (`AssetTreeSync`, the terrain's nodeRoot) and writes `OverlayHash` into the staged header; no overlay ⇒ no field | `StorageGatewayModule.cs` |
| `TerrainCatalog.ResolveOverlay(hash)` searches the terrain roots (staging, NAS); `TerrainLoadStep` reads `OverlayHash` and calls `Prepare(name, folder)`; a named hash with no folder THROWS | `TerrainCatalog.cs`, `TerrainLoadStep.cs`, `ScenarioTerrainName.cs` |
| `Prepare` refuses a base mismatch (names both hashes, the terrain, the folder — C-5) and a torn overlay (its files ≠ its header's hash) | `TerrainResidency.cs` |
| ⚠ not pruned: overlay folders are content-addressed and accumulate on a node — a row for the cache rule (the recording-archive cache rule is the model) | — |

### 18c-2. The compile — O-4 part 2 as built *(CE-4415, backend `2026-11-11`)*

| as built | where | deviation from §18c |
|---|---|---|
| `TerrainBlock` (494) / `TerrainFeature` (495) — unmanaged, saved, registered in `HrotSharedComponentRegistry` | `TerrainBlock.cs`, `GlobalComponentIds.cs` | ⚠ ids from the TOP of the coordinator block (backend's 434–449 is full) — `NOTE_Backend_To_Coordinator_Component_Ids_494_495.md` |
| `TerrainRecipe` + three shipped recipes (`hamlet`, `farmstead`, `copse`) in `assets/terrain-recipes/` | `TerrainRecipe.cs` | — |
| `FeatureExpander.Expand(...)` → `FeatureExpansion` (features, house / wood counts, outline); own xorshift64*, coordinates rounded to mm; AvoidBuildings + AvoidWater honoured; LevelPads writes `pad`; AlignRoad turns the block to the nearest road edge in reach (T-5, §18h); every house carries an explicit `baseZ` (the overlay world has no height grid — without it a house on a hill sits at 0) | `FeatureExpander.cs` | returns a record, not a bare list; takes `TerrainAssets` (template footprints) |
| `OverlayCompiler.Compile(dom, scenarioDir, resolveDef, bakeNavtiles, recipeRoots)` — reads `header`/`Header` and `entities`/`Entities` (the master's canonical file is PascalCase); sorted by label; written into `terrain-overlay.part/` then swapped; `overlay.json` with `blocks[]` (key, recipe, seed, houses, woods, clutter 0, outline hull); no blocks ⇒ the stale folder deleted | `OverlayCompiler.cs` (in the Recast project — it bakes navtiles) | lives in Recast, not FDP Terrain |
| navtiles: `RecastNavmeshFactory.SeedPrefill` — a prefill bake also reads the base's seeds and writes only the misses. 📐 test base: 576 tiles, 542 seed hits, **34 written**; two compiles byte-identical INCLUDING navtiles (578 files) | `RecastNavmeshFactory.cs` | ⭐ found while building: the overlay world must declare the BASE's bounds (grown only for a feature outside them) — the features' own margin re-keyed every tile (0 of 610 seed hits) |
| the master's hook: `StorageProcessManager.CompileTerrainOverlay` right after the canonical `scenario.json` is written (a thread-pool continuation, not the frame thread); a failure is LOGGED, the save stands | `StorageProcessManager.cs`; `Hrot.Orchestrator` → Recast reference | ⚠ the compile delays the "saved" answer and bakes on every core (CE-3433) |
| ⛔ the editor's own `EditorScenarioSession.SaveTo` hook NOT built | — | 📐 it has no production caller (its own remarks) and throws on a world-less host; every real save goes to the master ⇒ the master hook covers them |

Rails (each red-proved): `FeatureExpanderTests` (determinism, fit vs buildings + water over 8 seeds, the unfitted control, pads, AlignRoad without a road net changes nothing, T-5 alignment, shipped recipes parse) · `OverlayCompilerTests` (hashes, `blocks[]`, outline, canonical order, byte-identical recompile, stale folder deleted, failure leaves no `.part`, navtiles only the seed's misses) · `TerrainLoadStepTests` (compile → fresh residency loads; a changed base refused) · `StorageProcessManagerTests` (the merge writes `overlay.json`; a bad recipe still saves) · `TerrainOverlayClusterLoadTests` (the real in-process cluster loads it: SimHost's resident world holds the house and its door — ⚠ the harness's nodes share ONE staging root, so distribution itself is covered by `StorageGatewayTests`; CGF keeps no resident world in that harness).

#### 18c-3. Visual check against mockup page 2 *(user `2026-11-11`: "make sure the visuals reasonably match the mockup design")*

Real frames through the production layers (`TerrainOverlayFrameRail`, opt-in, `HROT_RAIL_SHOTS`): a compiled hamlet + copse in test-town, 2-D and 3-D,
each beside the bare terrain, and demo-city's own template houses at the same 2-D zoom. ✅ 3-D houses grounded, the production template look; woods the
terrain's own fill and canopy; overlay houses identical to the terrain's template houses. ⚠ Found and FIXED: each overlay house printed its stable KEY on
the 2-D map — `TerrainBuilding.Name` (display; defaults to the label, so every existing terrain draws as before; empty for block houses, as the mockup
shows none) and `TerrainPlan2D` prints `Name`, nothing when empty (rail `TerrainLayer2DTests.O4_AnOverlayHouse_DrawsNoText_…`, red-proved).
⏭ ui's (NOTE_Backend_To_Ui_Terrain_Overlay_Visuals.md): template houses drawn as floor plans where the mockup shows tan filled footprints (every
template house, a level-of-detail decision), the interior slab labels' clutter, and the block outline / chip / ghost / menu / Details — data ready.

### 18d. *Save terrain as* — O-5 *(CE-4422, backend `2026-11-11`; build-state: READY-TO-BUILD)*

```mermaid
classDiagram
  class TerrainBake {
    <<new, Recast project beside OverlayCompiler>>
    SaveAs(request) TerrainSaveResult
    Summarise(request) TerrainSaveSummary
  }
  class OverlayCompiler {
    <<existing>>
    Compile(scenario, dir, resolve) overlay folder
  }
  class TerrainWorldParser {
    <<existing - THE reader>>
  }
  class TerrainWorldMerge {
    <<existing - the oracle of the rail>>
  }
  class TerrainSaveSummary {
    <<new record>>
    houses, woods, trees, wallKm, bridges per kind
    NotBaked units, areas, damaged houses
    tiles, tilesToBake, estimate
  }
  class TerrainUsers {
    <<new>>
    Of(terrainName, scenarioRoots) scenarios
  }
  class TerrainObjectsSection {
    <<existing - door states in a scenario>>
  }
  class TerrainFolderWriter {
    <<existing - NOT used: importer inputs only, rotation 0 - G-9>>
  }
  TerrainBake --> OverlayCompiler : the source compiled first
  TerrainBake ..> TerrainWorldParser : the saved folder is read by it
  TerrainBake --> TerrainSaveSummary
  TerrainBake --> TerrainUsers
  TerrainBake ..> TerrainObjectsSection : door keys re-prefixed
```
*What it shows:* *Save terrain as* writes no new serialiser. The terrain's world file and the overlay's are ONE format read by ONE parser, so the bake
APPENDS the overlay's features to the base's feature list (bounds = the union) and copies every other base file byte for byte. ⭐ G-9 closes by construction:
nothing is re-serialised, so rotation, slabs, ramps, structures and clutter land exactly as authored. `TerrainWorldMerge` stays the definition of "base +
overlay", and the rail holds the saved folder to it.

```mermaid
sequenceDiagram
  actor U as Author
  participant B as TerrainBake
  participant C as OverlayCompiler
  participant F as new terrain folder (.part, then swapped)
  U->>B: SaveAs(source scenario dir, new name N)
  B->>C: Compile the source - its overlay is current
  B->>F: copy the base folder (not source/, not terrain.hash)
  B->>F: world file = base features + overlay features, bounds union
  B->>F: navtiles = base tiles + overlay tiles
  B->>F: terrain.json name N + source {scenario, base name + hash}
  B->>F: source/scenario (the block scenario, no terrain-overlay) + source/base (the ground terrain)
  B->>F: hash, then swap .part into place
  B-->>U: a new scenario on N - door keys re-prefixed, no blocks, no units (E-5) + the summary
```
*What it shows:* the saved terrain carries its own source (E-1), so re-saving (W-4) and *Edit terrain* (E-2) need nothing outside the folder.

```mermaid
graph TD
  UI[ui: Save terrain as dialog, mockup 4] -->|later: ui's| B[TerrainBake.SaveAs]
  B --> OC[OverlayCompiler.Compile]
  B --> FS[(assets/terrain/N on the NAS)]
  FS -->|existing prefetch| ND[(every node)]
  ND --> RES[TerrainResidency.Prepare N - no overlay, no cold bake]
```
*What it shows:* a saved terrain is an ordinary terrain to every node; only the authoring call is new.

| decision | lean | rejected — one line each |
|---|---|---|
| D-1 the world | append the overlay's features to the base's world file (JSON level), bounds = union | a `TerrainWorld` writer — a second serialiser that must track every parser field (G-9 is exactly that failure) · extending `TerrainFolderWriter` — it takes importer inputs, not a world |
| D-2 navtiles | base tiles + overlay tiles copied; rail: loading the saved terrain bakes 0 tiles | a fresh prefill — 9 min cold at 5.5 km (G-10) |
| D-3 keys | door keys in the carried scenario re-prefixed `<base>/` → `<N>/`; building keys already resolve by label (`BuildingDamages.IndexOfKey`) | dropping the terrain prefix everywhere (O-3's deferred item) — touches six shipped scenarios and behaviour params for no gain here |
| D-4 the source | `source/scenario/` + `source/base/` (the ground terrain, its navtiles kept as seeds); `terrain.json` names both (E-1) | a reference to the base by name — a copy or a rename separates them (E-1's reason) |
| D-5 the new scenario | header on N + the door states, re-keyed; no blocks, features, units or areas (W-3, E-5); damage stays in the source (E-6) | copying the source's entities — a terrain source would turn into a mission (E-5) |
| D-6 the summary | from `overlay.json` `blocks[]` + the overlay world's parts + the source's non-terrain entities; estimate = tiles to bake × the cold rate (§18b) | — |

⏭ Not in O-5: *Edit terrain* resolving `source/base` (E-2, ui + backend), the ui dialog (mockup 4), an HTTP route — rows when they are picked up.

#### 18d-1. As built *(CE-4422, backend `2026-11-11`)*

`TerrainBake.SaveAs(sourceScenarioDir, target, newName, resolve, recipeRoots, bakeNavtiles)` → `TerrainSaveResult(Folder, ContentHash, Summary, Scenario)`
(`FDP/Toolkits/Fdp.Toolkits.Navigation.Recast/TerrainBake.cs`); `TerrainUsers.Of(name, roots)`. Built to D-1…D-6, with these deviations:

| as built | why |
|---|---|
| the world file is parsed and written back as JSON (features appended, `hrot.bounds` = the union) — the base's features are not re-modelled, only re-printed | the one parser reads it; rail (a) holds it element-wise to `TerrainWorldMerge` |
| the templates the overlay places are copied into the saved folder's `buildings/` | the shared `assets/buildings` is found only from under an `assets/terrain/` tree — the saved terrain must be self-contained wherever it lands |
| swap: target → `.old`, `.part` → target, delete `.old` (restored if the move fails) | a re-save (W-4) over a live target; a failure leaves it as it was |
| W-4 with the base no longer resolvable: the target's own `source/base` stands in, read before the swap | the source carries its ground (E-1) |
| the new scenario is RETURNED as a DOM (header on N, door states re-prefixed, no entities, no damage); the caller writes it | where it goes is the dialog's choice (mockup 4) |
| *area* = an entity with a component named `*Area` / `Zone*`; every other non-terrain entity counts as a unit | the scenario format has no area marker |
| not baked at save ⇒ `TilesTotal` is a bounds estimate, `TilesToBake` / `EstimateSeconds` null; baked ⇒ exact (0 to bake) | an exact count means baking every tile |
| ⚠ a base tile the overlay re-baked stays in `navtiles/`, unused (its hash differs) | harmless; a cleanup row (CE-3435) |

Rails (each red-proved, `TerrainBakeTests`): parse(saved) ≡ merge(base, overlay) · loading the saved terrain bakes 0 tiles · its hash differs from the base and `source/` does not move it · authored door states resolve through the new scenario · a re-save works, also with the base deleted · a failed save leaves the target and no `.part` / `.old` · the summary's counts and `TerrainUsers.Of`.

### 18e. The live incremental bake — O-7 *(CE-4418, backend `2026-11-11`; build-state: part A BUILT, part B DESIGN)*

📐 **Measured on scale-1** (105 800 tiles, 4 cores, Debug; `O7_Measure_ALiveEditOnScaleOne`, opt-in): a warm rebake after moving a hamlet 20 m bakes
**24** tiles and still takes **≈ 26–28 s**, and an all-hit rebake takes the same. Split (`BakeStats` stage timers): **gather 77 % + hash 21 %** of
the parallel section's thread time, cache read 0.7 %, baking the misses 0.2 %; the serial `AddTile` assembly 0.5 s. Index + cover ≈ 0.6–1.0 s,
both whole-only (`BepuTerrainIndex.cs:17-64`, `TerrainCoverProvider.cs:47-69`). ⇒ ⛔ §17 G-4's "≈ 16 s" was low; the cost is re-reading
UNCHANGED tiles to learn they did not change.

```mermaid
sequenceDiagram
  participant R as TerrainResidency.BakeObstacles (off-thread)
  participant F as RecastNavmeshFactory
  participant B as RecastNavmeshBaker
  participant C as NavTileCache (bytes)
  R->>R: dirty = bounds of the parts that differ from the last bake (prisms added / removed / moved)
  R->>F: Rebake(world, dirty)
  F->>B: Bake(..., Previous = last bake's tile keys, Dirty = dirty)
  loop every tile
    alt tile outside every dirty rect (with the gather margin) and it has a previous key
      B->>C: TryGet(previous key) - a fresh copy, no gather, no hash
    else
      B->>B: gather, hash, cache or bake - as today
    end
  end
  B-->>F: meshes + the new key map
```
*What it shows:* the change already knows where it happened, so the baker stops proving that 105 776 tiles did not change. A tile whose previous
copy is not in the cache falls back to today's path, and `dirty = null` (unknown) is today's full rebake — so a wrong caller is slow, never wrong.

| decision | lean | rejected — one line each |
|---|---|---|
| A-1 reuse | the previous tile by its KEY through the cache (fresh bytes → fresh `DtMeshData`) | the previous `DtMeshData` objects — `AddTile` / `ConnectIntLinks` write into them (`DtPoly.firstLink`), and the old mesh is still queried on another thread |
| A-2 dirty | computed by the residency from the prism sets it bakes (symmetric difference → bounds); damage changes add their building's bounds; a base change ⇒ `null` (full) | the caller passing rects — every caller would have to remember; the residency is the one place every bake passes (by construction) |
| A-3 the index and cover | whole, unchanged (≈ 1 s) | incremental now — no add/remove API in either; a row if 1 s proves too slow |
| A-4 scope | every node's obstacle bake, not only the editor — a hedge placed at run time pays the same 26 s today | an editor-only path — a second bake (ruling 9) |
| B (DESIGN) | the editor's live overlay: an editor-only system expands changed blocks off-thread, merges, and feeds the same residency bake with the changed parts' bounds; 3-D chunks are ui's (the dirty rects exposed) | — |

Acceptance A: a dirty bake's tile keys equal a full bake's for a moved wall and a moved block (rail); a too-small dirty rect is caught by that rail
(red-proof); scale-1 warm move measured ≪ 26 s.

#### 18e-1. Part A as built *(backend `2026-11-11`)*

📐 **Scale-1, warm, navmesh wall time: a wall moved 2 m 27.9 s → 4.2 s; a hamlet moved 20 m 28.9 s → 4.4 s** (105 756 of 105 800 tiles reused by
key). What remains: reading ~105 800 tiles back from the cache (fresh copies, A-1) and the serial assembly (~0.85 s); index + cover ~0.7 s ⇒ a warm block
move ≈ 5 s end to end.

| as built | why |
|---|---|
| `RecastNavmeshBaker.Previous` / `Dirty` / `LastKeys`, `BakeStats.Reused`; a tile is clear when its reach `max(margin + 3, FineMarginMetres)` meets no dirty rect | a little wider than the gather margin, so a doorway or fine area at the edge is covered |
| `IIncrementalNavmeshFactory` (`Build(world, dirty)`, `BakeSerial`) in `Fdp.Toolkits/Navigation` | ⚠ deviation: `Hrot.Core` does not reference Recast, so the residency calls it through an interface |
| the residency passes a region only when the factory's `BakeSerial` is the one it recorded at its last commit; the load's bake is the baseline (an empty obstacle set) | ⚠ deviation: an abandoned staged bake or another terrain's bake would leave the factory's key map describing a different world — any mismatch ⇒ full bake |
| dirty = bounds of the prism symmetric difference (footprint + heights at 1 cm + material) + the footprint of every building whose damage changed; a base change ⇒ null | A-2 |
| ⚠ woods are not dirty-tracked — only prisms and damage change without a base change | an overlay change is a base change (part B decides how it passes its bounds) |

Rails (`DirtyRegionBakeTests`, `BuildingDamageBakeTests.O7A_*`; each red-proved): a dirty bake's keys equal a full bake's for a moved wall and a moved
hamlet · a region missing the old place makes them differ · null = today's bake · changed bounds ignore a region · the residency's region covers old and new,
is empty for an unchanged set, null on a stale base or a fresh residency.


#### 18e-2. Part B — the editor's live block expansion *(design `2026-11-11`)*

```mermaid
sequenceDiagram
  participant S as StaticObstacleBakeSystem (editor)
  participant L as LiveOverlay (editor only, new)
  participant X as FeatureExpander (pure)
  participant R as TerrainResidency.BakeObstacles (off-thread)
  S->>L: signature of TerrainBlock + TerrainFeature entities (allocation-free)
  alt changed and quiet for the debounce
    S->>L: snapshot the changed entities
    L->>X: expand each CHANGED block (an unchanged one keeps its cached parts, by guid + params)
    L-->>S: the overlay parts, keyed guid/i
    S->>R: BakeObstacles(prisms, damage, overlay parts)
    R->>R: world = With(Merge(base, overlay), prisms), dirty adds the overlay parts that differ by key + geometry
  end
```
*What it shows:* a block edit rides the bake every node already runs; the stable keys (`<guid>/<i>`) make "which parts changed" a key diff, so a moved
block dirties only its old and new place — woods included.

| decision | lean | rejected — one line each |
|---|---|---|
| B-1 where | the obstacle bake gains the overlay parts as a third input; the editor's `StaticObstacleBakeSystem` is given a `LiveOverlay` (null on every other host — blocks are inert data there, C-6) | an overlay as a BASE change — a base change is a full 28 s bake (A-2) · a second bake system — two bakes over one world (ruling 9) |
| B-2 expansion | per entity, cached by guid + (recipe, seed, heading, fit, position); only changed entities re-expand | expanding everything each time — fine at 23 blocks, wasteful at hundreds |
| B-3 the editor's load | the editor must NOT also merge the compiled overlay at load, or each house appears twice — ⚠ to be measured first (STOP if it does) | — |
| B-4 forwarding | a rail on the CONSTRUCTED editor system that its `LiveOverlay` is set (the silent-default rule) | — |
| B-5 3-D chunks | the dirty rects of each committed bake are exposed (`ObstacleBake.Dirty`) for ui's chunk rebuild | ui deriving them again — a second diff |

**As built (O-7 B, `2026-11-11`)** — what the build did against the table above:

| as built | note |
|---|---|
| B-3 measured first: the editor loads BASE ONLY | its staging root has no gateway-written `ScenarioHeader.json`, so `TerrainLoadStep` reads no `OverlayHash` (`ScenarioTerrainName.ReadOverlayHash`, `TerrainLoadStep.cs:87`); the only writer of that field is `StorageGatewayModule` (orchestrator). A block is therefore never drawn twice |
| the per-entity expansion is `OverlayExpansion` (`Fdp.Toolkits/Terrain`) — `Block`, `House`, `WorldDocument`, `SortByLabel`, `Hull` — called by `OverlayCompiler.Compile` AND `LiveOverlay` | ⚠ deviation: factored out into `Fdp.Toolkits`, not left in the Recast project, because `Hrot.Core` does not reference Recast; the compiler's JSON reading stays in the compiler |
| `LiveOverlay` (`Hrot.Core/Services/LiveOverlay.cs`): `SignatureOfWorld` (allocation-free, cached queries) · `Snapshot` (frame thread, only on change) · `Expand` (bake task; cache by key + params at 1 cm, dropped on a new base; a source that fails is logged and skipped) | parts = `OverlayPart(Key, GeometryHash, Min, Max)` |
| `StaticObstacleBakeSystem.LiveOverlay` (settable, null by default); its signature gains the overlay hash; the expansion runs inside the bake task | ⚠ deviation: wired by ASSIGNMENT on the constructed system in `EditorNodeBootstrapper.RegisterSpawningPipeline`, not a pack parameter — no other host can get one by forgetting a field |
| `TerrainResidency.BakeObstacles(prisms, damage, overlay)`: `With(Merge(base, overlay), …)`; the overlay carries the base it was expanded for (and that base's version), so a base that moved meanwhile makes the commit refuse the bake; `ExpansionBase()` = (base, version, terrain folder) | dirty = prism diff + damage diff + the parts that differ by key + geometry hash (old and new) |
| `ObstacleBake.Dirty` (null = unknown = everything), mirrored by `StaticObstacleBakeSystem.LastDirty` and `BakedParts` | B-5; set on every bake, navmesh or not |

Rails: `LiveOverlayBakeTests` (a block in the baked world · a moved block dirties exactly its old and new parts, an unchanged block is not re-expanded · steady frames allocate nothing beyond a plain system),
`EntityCreationPackRails.OnlyTheEditorRoot_GivesTheObstacleBakeALiveOverlay` / `APackBuiltObstacleBake_HasNoLiveOverlayByDefault`, `TheEditorShowsItsTerrainBlocksLiveTests` (B-4).

### 18f. The generator library and the random-terrain wizard's engine — T-1 *(CE-4428, backend `2026-11-11`; build-state: READY-TO-BUILD)*

📐 **INVENTORY** (grep + reads, ⚠ graph not ready this session — re-run before the as-built): the synth lives in a CONSOLE tool,
`Hrot/Tools/Hrot.Tools.TerrainSynth` (≈ 3 000 lines: `HeightField`, `RoadNetwork` with `Villages`, `ExtrasGenerator` water / scrub / woods, `TreeGenerator`,
`BuildingGenerator`, `SumoWriter` + netconvert, `ManifestWriter`; 87 tests) writing the §10 input set; ⭐ the importer is a library
(`Hrot.Terrain.Import.TerrainImporter : ITerrainImporter`, navmesh prefill via `ITerrainNavmeshPrefill`); ⭐ **the OSM wizard is the precedent to
mirror**: an engine pipeline `Hrot.Terrain.Osm.IOsmTerrainPipeline` (`PreviewAsync` → `OsmPreview` with `PreviewRoad / PreviewArea / PreviewLine`,
`OsmCounts`, `OsmBudget.Evaluate` → `BudgetVerdict`; `WriteInputSetAsync`) composed in `EditorSubsystem.cs:3632-3660` with the importer.

```mermaid
classDiagram
  class HrotTerrainSynth {
    <<new library - the synth's code MOVED, namespace kept>>
    HeightField, RoadNetwork, ExtrasGenerator, TreeGenerator, BuildingGenerator, SumoWriter
    TerrainSynthesizer.Run(options)
  }
  class hrot_terrain_synth {
    <<existing CLI - now a thin shell: Program + SynthOptions.Parse>>
  }
  class RandomTerrainPipeline {
    <<new - mirrors OsmTerrainPipeline>>
    Preview(options) RandomTerrainPreview
    ShuffleLayout(preview, seed) RandomTerrainPreview
    OpenInEditor(preview, name) terrain + scenario
  }
  class RandomTerrainOptions {
    <<new record>>
    Preset, Size, Relief, Vegetation, Water, GroundSeed, LayoutSeed
  }
  class BlockLayout {
    <<new, pure>>
    Place(preset, villages, roads, water, seed) TerrainBlock entities
  }
  class OsmPreview {
    <<existing - the SAME preview records, so the one wizard frame draws both>>
  }
  class OsmBudget {
    <<existing - the same verdict>>
  }
  class TerrainImporter {
    <<existing - ground input set to terrain folder + prefill>>
  }
  class FeatureExpander {
    <<existing - counts and outlines for the preview>>
  }
  hrot_terrain_synth --> HrotTerrainSynth
  RandomTerrainPipeline --> HrotTerrainSynth : ground only - no buildings
  RandomTerrainPipeline --> BlockLayout
  RandomTerrainPipeline --> FeatureExpander
  RandomTerrainPipeline --> OsmPreview
  RandomTerrainPipeline --> OsmBudget
  RandomTerrainPipeline --> TerrainImporter
```
*What it shows:* nothing is written twice — the ground is the synth (moved into a library), the import is the importer, the houses are the recipe
blocks a user also places (R-1 of §12), and the preview / budget are the OSM wizard's records, so ui's U-8 reuses its OSM frame.

```mermaid
sequenceDiagram
  participant W as ui wizard (U-8)
  participant P as RandomTerrainPipeline
  participant S as Synth library
  participant L as BlockLayout
  participant I as TerrainImporter
  W->>P: Preview(preset, size, relief, vegetation, water, seeds)
  P->>S: heights, roads (+ netconvert), water, woods, scrub - NO buildings, NO village houses
  P->>L: blocks at the villages along the roads (preset density), each a seed
  P-->>W: OsmPreview (relief, roads, water, woods, block outlines) + counts + verdict
  W->>P: ShuffleLayout(new layout seed) - the ground is kept in memory, seconds
  W->>P: OpenInEditor(name)
  P->>I: the ground input set to assets/terrain/name, prefill included
  P-->>W: terrain folder + scenario folder (header on name, the TerrainBlock entities) - V-1
```
*What it shows:* *Shuffle layout* re-runs only the pure layout over the ground already in memory; *Shuffle ground* is a new `Preview`.

| decision | lean | rejected — one line each |
|---|---|---|
| F-1 library | a new `Hrot/Engine/Hrot.Terrain.Synth` library holding the moved files (namespace kept — no rename blast radius); the CLI keeps `Program` + option parsing; the tests point at the library | a reference from the editor to the CLI exe — a tool is not a library · copying the generators — R-4202 "moved, not copied" |
| F-2 the ground | the synth with buildings and village houses OFF; woods / scrub / water ON per Vegetation / Water; Relief scales the height range | a separate surface generator — a second heights / roads path |
| F-3 the layout | `BlockLayout`: at each synth `Village`, N blocks from the preset's recipe mix, placed along the nearest roads, kept off roads, water and each other; guids derived from the layout seed (deterministic) | the synth's `BuildingGenerator` houses — a second house path (§12 R-1) |
| F-4 presets | Plain terrain (no layout) · Open countryside (farmsteads, copses) · Villages (hamlets) · Small town · Town with suburbs · Mixed — recipes as they exist; T-3's richer recipes join by name | — |
| F-5 preview | `OsmPreview` records + `OsmCounts` (houses from the blocks' expansion counts) + `OsmBudget.Evaluate` | new preview records — the one frame would need two drawers |
| F-6 Open in editor | import the ground (`TerrainImporter`, prefill), write the scenario (`scenario.json` with `TerrainBlock` + `SimTransform` entities); the editor opens it and its live overlay expands the blocks (O-7 B) | baking houses into the terrain here — *Save terrain as* does that (§13 W-3) |

⚠ The netconvert dependency stays the synth's (a warning and plain files without SUMO; the importer then has no road net) — the wizard shows it as
a note. ⏭ Acceptance (handoff T-1): same inputs → byte-identical ground files and layout DOM; *Shuffle layout* keeps the ground byte-identical; a
3 km *Town with suburbs* generates, imports and loads within R-4192's 30 s warm.

**As built (T-1, `2026-11-11`)** — what the build did against the table above:

| as built | note |
|---|---|
| **F-1** `Hrot/Engine/Hrot.Terrain.Synth` (net8.0, warnings as errors, refs `Hrot.Terrain.InputSet`): the generators `git mv`-ed, namespace `Hrot.Tools.TerrainSynth` kept; the CLI (`Hrot.Tools.TerrainSynth`) is `Program.cs` over it; the tool's 87 tests, `Import.Tests` (47) and `InputSet.Tests` (13) point at the library, unchanged and green (`CleanPinTests` byte-identical); both in `HROT.sln` | `SynthOptions` (record + `Parse`) moved with the library: `Import.Tests` / `InputSet.Tests` call `SynthOptions.Parse` |
| `TerrainSynthesizer.Generate(options)` → `SynthGround` (in memory) and `Write(ground, options, log)`; `Run` = both | the generation order is unchanged (the random streams are forked by name); the log still has one line per stage |
| **F-2** options, all default-off so a default run is byte-for-byte what it was: `GroundOnly` (no buildings, no nodata holes; `Buildings` then only sets how many villages — `BuildingCount / 60`) · `ZRange` · `WaterMode` none / ponds / river · `RoadScale` · `WoodsShare` | ⚠ the synth reads a woods share of 0 as "woods everywhere", so *Vegetation: none* strips the woods, lone trees and scrub from the ground in the pipeline instead |
| 📐 **measured: trees outside woods** (1 500 m, seed 11): light 300, heavy 858 against ≈ 100 ha of woods — **7 %** of the trees (the profile's `LoneShare`) | ⇒ they stay (field trees and rows along minor roads); *none* has none |
| ⚠ **deviation — where the pipeline went:** a small project **`Hrot/Engine/Hrot.Terrain.Random`** (+ `.Tests`), not the synth library | it needs the importer (and so Recast), the OSM contract records and Fdp.Toolkits' expander; the 87 generator tests should not drag those in. No cycle: nothing below references it |
| **F-3** `BlockLayout.Place` (pure) + `ToScenarioDom` | per village N blocks from the preset's mix (Open countryside, Mixed also place some along any road); each along one of the 8 nearest roads, centre `Clearance(radius) = 0.3·radius + 3 m` off every road's edge, off water (centre + 8 points at 0.6·radius), blocks ≥ 0.8·(r₁ + r₂) apart; guids from `Rng(layoutSeed)`; positions in the terrain's local metres (SW corner = 0,0). ⚠ the disc's centre keeps off roads, a house inside it may still meet a village's own street (T-5 `AlignRoad` now turns the block along the nearest road in reach; the preview passes the generator's roads) |
| **F-4** presets: villages per km² and road scale per preset (`RandomTerrainOptions.GroundOf`), blocks per village and recipe mix per preset (`BlockLayout.MixOf`): Open countryside 1–3 farmstead / copse + 2 per km² along roads · Villages 2–4 hamlet / copse · Small town 4–7 hamlet · Town with suburbs 7–11 hamlet + farmstead · Mixed 3–6 + 1 per km² | T-3's richer recipes join by name |
| **F-5** `RandomTerrainPipeline`: `Preview` → `RandomTerrainPreview` (the OSM `OsmPreview`: roads, water / woods / scrub / **block outline** areas, the blocks' houses as `PreviewBuilding`, `OsmCounts` from the blocks' expansion — doors and windows from the profile's per-house means —, `OsmBudget.Evaluate`; plus `ReliefRange` / `ReliefGrid(n)` and the layout), `ShuffleLayout(preview, seed)` (the ground stays in memory), `WriteGroundInputSet`, `OpenInEditorAsync(preview, name, scenariosRoot, terrainsRoot, navmeshPrefill)` | a netconvert that is missing is a Warning note in the preview, as in the OSM wizard |
| **F-6** `OpenInEditorAsync`: input set → temp folder → `TerrainImporter` (prefill from the caller) → `scenarios/<name>/scenario.json` (`$meta`, `header.terrainName`, the `TerrainBlock` entities); refuses an existing scenario or terrain | |

📐 **Measured, 3 km *Town with suburbs*, warm** (`TownBudgetTests`, opt-in `HROT_RUN_TOWN_BUDGET=1`; 133 blocks, 756 houses, 179 road edges, verdict Ok): preview 2.1 s (ground 1.0 · layout 0.1 · records 1.0) · write the ground 2.0 s · import 85.8 s, of which the **navmesh prefill 85.0 s** (heights 0.5 · roads, buildings, vegetation, write ≈ 0) · **load (Prepare 8.1 s + Commit 0.04 s) = 8.2 s**. R-4192's budget is *load ≤ 30 s warm, navmesh from the cache*: **met, 8.2 s**; generate + import is 90 s and is dominated by the one-time prefill, which a cold import pays once.

### 18g. More feature kinds and the area scatter — T-2 *(CE-4429, backend `2026-11-11`; build-state: READY-TO-BUILD)*

📐 **INVENTORY** (grep + reads; ⚠ graph not ready — re-run before the as-built): `TerrainFeature { byte Kind; FixedString32 Template; float HeadingDeg;
float Pad }` with one kind (`House = 1`, `TerrainBlock.cs:35`); the world parser knows `building`, `wall` / `fence`, `tree`, `slab` / `ramp`,
`surface` (`TerrainWorldParser.cs:141-265`) — ⚠ **no `prism` kind** (G-9), so clutter cannot be written into an overlay today; the shape store the
area tools already use is the managed immutable `EditablePolyline` (`Hrot.Core/Components/Map/EditablePolyline.cs:20`, an `IAreaOutline`, with its
scenario translator `EditablePolylineTranslator`); `FeatureExpander` already writes woods (`surface: forest` + trees) and houses; bridges come from O-2's
`"structure"` grouping.

```mermaid
classDiagram
  class TerrainFeature {
    <<existing unmanaged, saved - Kind widened>>
    byte Kind
    FixedString32 Template - house template, clutter type, recipe, material
    float HeadingDeg
    float Pad - density, width or height by kind
    uint Seed - new, for wood, scrub, scatter
  }
  class EditablePolyline {
    <<existing managed immutable record - the shape of line and area kinds>>
  }
  class OverlayExpansion {
    <<existing - ONE expansion, compiler and live overlay>>
    Feature(kind, shape, params) parts
  }
  class FeatureExpander {
    <<existing - gains Line, Area, Point, Scatter>>
  }
  class TerrainWorldParser {
    <<existing - gains the prism kind>>
  }
  TerrainFeature "1" --> "0..1" EditablePolyline : line and area kinds
  OverlayExpansion --> FeatureExpander
  FeatureExpander ..> TerrainWorldParser : writes features it reads
```
*What it shows:* every kind is ONE more case in the one expander, written as a part the one parser already reads — except clutter, which needs the
parser's missing `prism` kind (then the overlay, *Save terrain as* and the live overlay all carry it for free).

| kind | shape | becomes | destructible |
|---|---|---|---|
| Wall · Fence · Hedge | polyline | `wall` / `fence` features, material from `Template` (brick, steel mesh, hedge) | as today's walls (breach by material) |
| Wood | polygon + density + seed | `surface: forest` + trees (the expander's woods) | no |
| Tree | point | `tree` | no |
| Scrub | polygon | `surface: scrub` | no |
| Clutter | point + heading | `prism` (new parser kind): size and material from the §7 table, reusing the obstacle TKB type's shape where one exists (`Car`) | ⛔ baked clutter is not (G-15) — a live obstacle stays the entity path |
| Water | polygon | `surface: water` (cuts the ground — O-2 measured impassable) | no |
| Bridge | polyline (two ends) + width | a deck `slab` + two `ramp`s grouped by `"structure"` (O-2) | collapses as a structure (O-2) |
| Scatter | polygon + recipe + seed + density | ONE entity (R-4199 B-2): the expander places the recipe's items (clutter, trees, bushes) inside, deterministic | per item kind |

| decision | lean | rejected — one line each |
|---|---|---|
| G-1 shape store | `EditablePolyline` beside `TerrainFeature` (a rarely changed managed record — the CLAUDE.md exception) | a fixed point list in the unmanaged component — the 1 024-byte cap (G-15) |
| G-2 seed | `TerrainFeature` gains `uint Seed` (wood, scrub, scatter) | reusing `Pad` bits — unreadable |
| G-3 clutter | a `prism` parser kind (footprint, baseZ, topZ, material, label) | clutter as live obstacle entities — thousands of entities per town, the bake per item |
| G-4 scatter | an area + recipe + seed + density expands to items like a block; capacity measured (§7 warns of thousands) | one entity per item — the B-2 ruling |

Acceptance (handoff T-2): rails — a hedge blocks movement, a wood conceals, water is impassable, a unit crosses a bridge, a scatter of 140 items
compiles deterministically; the compiler and the live overlay give the same parts for every kind.

**As built (T-2, `2026-11-11`)** — what the build did against the kinds table and G-1…G-4:

| as built | note |
|---|---|
| `TerrainFeature` (unmanaged, saved, recorded): `Kind` widened — House 1 · Wall 2 · Fence 3 · Hedge 4 · Wood 5 · Tree 6 · Scrub 7 · Clutter 8 · Water 9 · Bridge 10 · Scatter 11 — and `uint Seed`; `Template` / `Pad` mean what the kind needs (material, clutter type or recipe; height, width, density) | the component stays tiny (≈ 48 B ≪ 1 024); it round-trips through the scenario serializer with `EditablePolyline` (`EditablePolylineTranslatorTests.T2_*`). ⚠ `Seed` changes the layout of a recording made with the O-4 component |
| **G-1 shape:** line and area kinds carry the managed `EditablePolyline` (points RELATIVE to the entity); the compiler reads it from the scenario JSON in the `EditablePolylineTranslator`'s shape, the live overlay from the component — both build an `OverlayExpansion.FeatureSpec` and call `OverlayExpansion.Feature` → `FeatureExpander.OfKind` (the ONE entry) | `FeatureExpander` is `partial`; the kinds live in `FeatureKinds.cs` |
| **G-3 parser:** new `prism` kind (Polygon, `height`, `baseZ`, `material`, `label` → a `TerrainPrism` of kind `Wall`, what a static obstacle is) and new surface `scrub` (`TerrainSurfaceType.Scrub = 4`: drawn and walkable, no concealment yet) | `TerrainWorldMerge` already copied `Prisms` and offset `Panel` / `Building`; a prism has neither |
| per kind: Wall / Fence → `wall` / `fence` (material from `Template`, default concrete / fence-wood; height from `Pad`) · **Hedge = a `wall` of the existing `hedge` material** (sight 0.3), 1.5 m, 0.8 m thick · Wood → `surface: forest` (density from `Pad`) · Tree → `tree` · Scrub → `surface: scrub` · Water → `surface: water` · Clutter → `prism` from `ClutterTypes` | |
| **Bridge** = a deck `slab` 1.5 m above the ground under it + a `ramp` at each end (6 m, the foot buried) under ONE `"structure": "<guid>"` (a collapsible building, no panels; O-2); width from `Pad` (4 m) | measured: units cross it (navmesh) |
| **G-3 clutter table** `ClutterTypes` (Fdp.Toolkits): Car 4.5 × 1.8 × 1.5 car-body · Sandbag wall · Concrete block · Crate (the four obstacle TKB types — `NedTkbCatalog.RegisterObstacles` now READS its sizes from the table: one table) + Barrel · Jersey barrier | baked clutter is not destructible (G-15); a live obstacle stays the entity path |
| **G-4 scatter:** one entity (area + recipe id in `Template` + `Seed` + density in `Pad`) → `area / 100 · density` items (≤ 2 000), drawn by weight from the recipe's new `scatter` section (`{ "density": 2, "items": [ { "kind": "clutter", "type": "car", "weight": 1 }, { "kind": "tree" }, { "kind": "bush" } ] }`; a bush is a small scrub octagon) at deterministic places inside the area and out of the base's water | 140 items compile byte-identically |

⚠ **Measured finding — a collapsed bridge still carries a unit on its rubble.** Collapse removes the deck and both ramps (`SurfaceZ` at deck height falls to the ground) but CE-4391 S-2's rubble mound — a 0.6 m solid over the structure's whole footprint — is a walkable roof to the navmesh, so the path from bank to bank across the river still exists (two waypoints, over the mound). The rail asserts what O-2 asserts (the deck is gone) and records this; a rule that leaves no mound where a structure stands over water is for the destruction design to decide.

Rails (`FeatureKindsTests`, each red-proved): a hedge blocks movement (the path goes round) · a wood conceals · water is impassable · a unit crosses a bridge (deck) and the collapsed bridge no longer carries at deck height · a scatter of 140 compiles byte-identically · clutter round-trips through the overlay file; `LiveOverlayBakeTests.T2_TheCompilerAndTheLiveOverlay_GiveTheSamePartsForEveryKind`.

### 18h. Block operations and road alignment — T-4, T-5 *(CE-4431, CE-3431, backend `2026-11-11`; build-state: BUILT — see "As built" at the end of this section)*

📐 **INVENTORY** (graph `search_graph` `.*(Undo|EditCommand|CommandStack).*` Interface + `.*(UndoStack|UndoManager|EditTransaction).*` Class, and
reads): the editor's ONE undo is `EditHistory` with `WorldUndoRecorder` (`Hrot.Presentation/Edits/WorldUndoRecorder.cs:8-16`): ⭐ **every accepted
authoring edit submitted through `Edits.Submit` (`Fdp.Toolkits/Replication/Edits/Edits.cs:23`) is ONE step by construction** — the step snapshots the
world before and after; `EditOp` has `Write` (attribute changes), `Create`, a `PatchJson`. `GizmoUndoStack` (Player) is a separate gizmo-only stack, not
this. ⇒ a block operation needs no undo code of its own.

```mermaid
sequenceDiagram
  actor U as Author
  participant UI as ui (U-3 menu)
  participant B as BlockOperations (new, engine)
  participant E as Edits.Submit - one step
  participant L as LiveOverlay (O-7 B)
  U->>UI: Re-roll / Change recipe / Unpack
  UI->>B: the operation on the block entity
  B->>E: ONE EditOp bracket (Write Seed / Recipe, or destroy the block + create its features)
  E-->>L: the world changed - the live bake re-expands only what changed
  U->>UI: Undo
  UI->>E: EditHistory.Undo - the snapshot restores the block
```
*What it shows:* the operations are ordinary authoring edits; undo, the live view and the save all follow from the paths that already exist.

| decision | lean | rejected — one line each |
|---|---|---|
| H-1 Re-roll | `Write(TerrainBlock.Seed = next)` — the next seed is derived from the current one (deterministic, so a redo is the same layout) | a random seed — redo would differ from the first do |
| H-2 Change recipe | `Write(TerrainBlock.Recipe)`; place and heading kept | — |
| H-3 Unpack | ONE bracket: create one `TerrainFeature` (+ `EditablePolyline` for line / area kinds, T-2) per expanded part, then destroy the block. ⭐ "same parts" = the same geometry, materials and order; the part KEYS change (`<feature guid>/0`), so the bracket also re-keys every door entity of the block's houses (`<terrain>/<block>/<i>/<door>` → the new key) | keeping the block's keys on the features — a guid plus `/i` overflows `FixedString32`, and a key override field is a second identity |
| H-4 where | `BlockOperations` in `Hrot.Core` beside `LiveOverlay`, using the ONE `OverlayExpansion` for Unpack | ui building the features itself — a second expansion |
| H-5 T-5 road alignment | `FeatureExpander` with `AlignRoad`: the block's heading turns to the nearest road edge's direction within its radius (the road net's lanes, `RoadNetworkHolder`); no road in reach ⇒ the heading as placed | snapping position too — moves a block the user placed |

Acceptance: unpack → recompile gives the same geometry as before the unpack (keys aside), and the door states survive; undo restores the block in
one step; a farmstead placed near a track turns to face it (T-5).

#### As built (backend `2026-11-11`) — what differs from the lean above, and why

| | as built | prior state |
|---|---|---|
| **one bracket** | `EditSteps.Begin/End` (`EditOp.cs:141`) — the OUTER pair `Edits.Submit` nests into. `BlockOperations` (`Hrot.Core/Services/BlockOperations.cs`, `Hrot.Map.Common.Services`) brackets its own change, so Re-roll / Change recipe / Unpack are each one `EditHistory` step by construction. Refused (nothing changed) while a recording replays or when the station's `IEditGate` says no — the two checks `Submit` makes | H-1…H-4: "`Edits.Submit` with a `Write`" — a `TerrainBlock` field is not an attribute-shaped write; `SetComponent` inside the bracket is what `MacroArgs.Write` does for the same reason |
| **entities** | written DIRECTLY and synchronously (`CreateFeature`, the one place): the creation-request path completes after the bracket closed and would not be part of the step. ⚠ no network identity — when the placing UI gives `TerrainFeature` a TKB type (U-2 / B-5) switch `CreateFeature` | — |
| **keys** | the new feature's guid = the block's guid with its last 32 bits `= index + 1`, the index in the SORTED part-label order ⇒ the overlay's order is the block's (a part label is `<guid>/0`, the guids rise), and an unpack undone and done again makes the same keys (`PartGuid`). Doors: every entity whose `TerrainObjectKey` starts `<terrain>/<old part label>/` is re-keyed to `<terrain>/<new guid>/0/…` in the bracket, its `DoorState` untouched | H-3 "re-keys the door entities" — kept. ⚠ a damaged house's `BuildingDamage` entity is NOT re-keyed (it follows its building by the key the OLD world gave its index, `BuildingDamages.Remap`): damage on an unpacked house is lost |
| **seed** | `NextSeed` = a PCG step (`seed·747796405 + 2891336453`, full period, never the same seed back) | H-1 |
| **undo's restore** | ⭐ the undo keyframe (`CaptureOptions.Undo`, "authored types") now carries `EntityGuid` beside the replication identity (`IsReplicationIdentity`, CE-4411): an undone unpack restored the block WITHOUT its scenario key (`NoScenario`), i.e. another block (new part keys, door and damage keys hanging on the old one). One line in `WorldKeyframes.cs`, railed | not in H-3 — found by the undo rail |
| **⚠ finding, not fixed** | ANY undo strips `DoorState` + `TerrainObjectKey` from door entities (both `NoScenario`, so the authored-only restore removes them) — measured with a plain re-roll. After an undone unpack the doors are gone until the scenario is reloaded | → tracker |
| **T-5 roads (H-5)** | `TerrainRoads` (`Fdp.Toolkits/Terrain`): the terrain folder's FIRST declared road network — the file `TerrainResidency.Prepare` publishes — read from the JSON and cut into straight edges (8 per Hermite segment). ⭐ ONE reader for both callers: `OverlayCompiler` (`TerrainRoads.Load(definitionPath, definition)`) and `LiveOverlay` (`TerrainRoads.ForFolder(folder)`, once per base/folder change) ⇒ the same roads by construction; `OverlayExpansion.Block` / `FeatureExpander.Expand` take it as an optional last argument | H-5 said "`RoadNetworkHolder`" for the live overlay: the holder's blob is native memory held under leases for the main thread's readers, and a second reader of the same file is the only way the two callers see IDENTICAL geometry |
| **direction** | the nearest edge within the recipe's radius (centre to edge); ⭐ of its two directions (it and its opposite) the one nearer the PLACED heading, so a block turns ≤ 90°; the result is rounded to 1 mm of a degree. No edge in reach, the bit off, or no road net ⇒ the heading as placed. Ties between equally near edges: the last in the file | "the direction of the nearest edge" — which of the two was open |
| **callers without roads** | `RandomTerrainPipeline`'s preview passes the generator's road edges (the preview shows what the compile will do) | — |

Rails: `FeatureExpanderTests.T5_*` (3: turns to the track within 1° · the nearer of the two directions, ≤ 90° · no road in reach / bit off / empty net ⇒ the bytes of the plain block), `AlignRoadAgreementTests` (SimHost: the compiler's and the live overlay's parts, labels and boxes agree, every house on the track's direction), `BlockOperationsTests` (Presentation: unpack = same shape in the same order + the door follows its house · undo = one step, same block and key, redo = the same keys · re-roll one step, undo/redo/again the same seeds · change recipe keeps place and heading · a refusing gate changes nothing). Each red-proved by a compiling mutation (see the batch report).

### 18i. The recipe vocabulary — the ten blocks of the mockup — T-3 *(CE-4430, backend `2026-11-11`; build-state: BUILT)*

📐 **INVENTORY** (graph `search_graph` `.*(Recipe|Expander|Element|BuildingGenerator|BuildingTemplate).*` Class; reads): the one expander `FeatureExpander` (`Fdp.Toolkits/Terrain`, houses + woods + T-2's kinds), the recipe `TerrainRecipe` (`houses`, `woods`, `scatter`),
the synth's `BuildingGenerator` (`Hrot.Terrain.Synth`, needs `SynthProfile` + `HeightField` + `RoadNetwork`) and the importer's `BuildingTemplateGenerator` (`Hrot.Terrain.Import`, writes template FILES into a terrain folder). Nothing else makes block contents.

```mermaid
flowchart LR
  R["recipe.json<br/>houses · woods · scatter<br/>+ elements[ ]"] --> P[TerrainRecipe.Parse<br/>validates each element]
  P --> E[FeatureExpander.Expand<br/>ONE seeded stream]
  E -->|houses, woods| H[building point · forest area]
  E -->|ExpandElement| L["lane → open surface<br/>row / grid / cluster → building points<br/>enclosure / fields / garden → wall · hedge · fence lines<br/>group → clutter prisms<br/>orchard → trees"]
  H --> O[OverlayExpansion → compiler + live overlay]
  L --> O
  O -->|Unpack, T-4| F["TerrainFeature kinds<br/>House · Tree · Clutter · Wall / Fence / Hedge · Wood / Scrub / Water / Lane"]
```
*What it shows:* a new element is a new producer of the same part kinds T-2 already writes — the compiler, the live overlay and Unpack need no new path, only the part-to-feature map (`BlockOperations.TryRead`).

| decision | as built | rejected |
|---|---|---|
| I-1 format | `recipe.elements[]`, additive: a recipe without it expands exactly as before; each element draws from the stream the houses and woods left, in order; coordinates are block-LOCAL metres (x along the heading — the road side once AlignRoad faces it, y to the left); `radius` at the top level is the block's own (road reach, layout clearance). A bad element (unknown type, missing field, unknown clutter) is a parse error | a second recipe file per kind · world coordinates (a block turns) |
| I-2 elements | `lane` (strip) · `row` (houses along a line, both sides or one, fronts to the line, optional hedge garden) · `grid` (lanes each way + a `row` along every side, off the cross streets) · `cluster` (houses / barns in a disc) · `enclosure` (hedge / fence / wall rectangle, gate gap) · `group` (clutter prisms at explicit places, along a line or scattered in a rectangle) · `orchard` (trees on a grid) · `fields` (hedge edges of field polygons, gates by chance) | one element per tile (ten special cases) |
| I-3 lanes | ⭐ **no road surface exists** — `surface: road` is retired (R-231: the road is the road graph) — so a lane / street / trench is an **`open` surface** strip: a new feature kind `TerrainFeature.Lane` (12), drawn grey by the 2-D plan (`TerrainPlan2D`: open surfaces were skipped, scrub fell into the water colour — both drawn now). It carries no gameplay (open ground is the default) | a thin walkable slab (it would change the navmesh) · a road in the graph (a block is not a terrain edit) |
| I-4 prisms | the T-2 clutter table gains `Guard booth` (checkpoint); a clutter part now carries its type and turn (`clutter`, `heading` properties the parser ignores) so Unpack makes a `Clutter` feature of the same type. A "trench" is a narrow lane between two sandbag lines | prism groups as scatter (placement is designed, not random) |
| I-5 ⛔ unique houses | **STOPPED.** The street grid uses the shipped templates (`house-3f`, `office-4f`: 3 and 5 storeys incl. the roof), not unique generated ones. `BuildingGenerator` is in `Hrot.Terrain.Synth` and `BuildingTemplateGenerator` in `Hrot.Terrain.Import`; `FeatureExpander` lives in `Fdp.Toolkits`, which neither may be referenced from (FDP is below Hrot), and both are tied to a whole generated ground (`SynthProfile`, `HeightField`, `RoadNetwork`) and write template FILES | calling them from the expander (a reference cycle) · a second building generator (ruling 9) |
| I-6 seam proposed for I-5 | `IUniqueBuildings` **in `Fdp.Toolkits`**: `Make(rect in block-local metres, storeys range, seed) → (template JSON by name, placements)`; `Hrot.Terrain.Random` implements it with `BuildingGenerator` + `BuildingTemplateGenerator` (that is where the dependencies already are) and passes it to `OverlayExpansion.Block`; the generated templates are written to the overlay folder's `buildings/` (O-2 already resolves a template from there). A `grid` element with `"unique": true` calls it; without an implementation it falls back to the pool. Needs a user decision (it makes the compile depend on the generator's output) | |
| I-7 presets | `BlockLayout` mixes use the ten: countryside = farmstead · hedgerow-fields · orchard · copse; villages = hamlet · village-street · copse; small town = village-street · hamlet · town-block · farmstead; town with suburbs = town-block · village-street · hamlet · farmstead; mixed = all but the military three | — |
| I-8 Unpack | covers every part kind the elements make (house, tree, clutter, wall / fence / hedge, wood / scrub / water / lane) — a block of every element kind unpacks to the same shape in the same order | refusing a block of new parts |

**The ten** (`assets/terrain-recipes/`, new templates `barn`, `shed`): farmstead (cluster of house + 2–3 barns / sheds, hedge yard with a gate, track in, crates and barrels) · hamlet (a bent lane, 5–8 houses both sides with hedge gardens, a copse) · village street (a bent lane, 10–12 houses, gardens, cars) · town block (2–3 × 2 streets, 3–5-storey houses both sides, cars) · checkpoint (staggered jersey barriers, a booth, sandbags, a car — across the road its heading faces) · roadblock (cars and concrete blocks across the road, two angled barriers) · defensive position (two sandbag lines, a trench, crates) · copse (3–5 woods) · orchard (5–6 × 6–8 trees) · hedgerow fields (hedge-edged fields in 2–3 × 2–3, gates).

**Rails:** `BlockRecipeTests` (Toolkits: each recipe byte-identical twice, another seed another layout, the tile's kinds and counts, a turned block keeps its part counts, every recipe parses into a world and names real templates, a bad element is a parse error) · `BlockOperationsTests.T3_ABlockOfEveryElementKind_UnpacksToTheSameShape` · `RandomTerrainTests` (every preset lays out the shipped recipes and makes houses) · `TerrainOverlayFrameRail.T3_TheTenBlocks_AreDrawnIn2D` (opt-in, `HROT_RAIL_SHOTS`; ten PNGs).

**Frames vs the mockup tiles** (read by the builder, R-4146): farmstead — match (house + barns, hedge yard with a gate, track) · hamlet — match (6 houses on a bent lane, gardens) · village street — match (houses both sides, gardens) · town block — match (a street grid, houses along it; the 3-D interiors' labels crowd the plan, not this change's) · checkpoint — match in contents, deviation: no road drawn (the block sits on the road graph, which this frame's layer does not draw) · roadblock — match (cars and blocks across, barriers) · defensive position — deviation: the trench is a thin grey strip, the mockup's is a dashed brown line · copse — match (a wood of several blobs) · orchard — match after a fix (a lone tree is now drawn as its canopy disc in the 2-D plan, as the mockup; it was a 2-px trunk) · hedgerow fields — match (hedge-edged fields with gates).

### 18j. The missions check for a re-saved terrain — T-8 *(CE-4424, backend `2026-11-11`; build-state: READY-TO-BUILD)*

📐 **INVENTORY** (graph `search_graph` `.*(Waypoint|Route|PatrolPath|Area)$` in `*Components*`, + reads): routes are `RoutePlan` (an immutable
managed record of `RouteWaypoint`s, `Hrot.Core/Components/Map/RoutePlan.cs:70`); areas are `EditablePolyline`, readable from toolkit code through
`IAreaOutline` (points relative to `SimTransform.Position`, `Fdp.Toolkits/Terrain/IAreaOutline.cs:13`); a unit is any entity with a `SimTransform`;
door states are keyed `<terrain>/<label>/<door>` in the scenario's `TerrainObjects` section; who uses a terrain is `TerrainUsers.Of` (O-5).

```mermaid
sequenceDiagram
  participant UI as ui overwrite dialog (W-4, mockup 4)
  participant B as TerrainBake
  participant C as TerrainChangeCheck (new, pure)
  UI->>B: SaveAs over an existing terrain - first the check
  B->>B: old world = the target as it is, new world = base + overlay (the merge SaveAs writes)
  B->>C: Check(old, new, TerrainUsers.Of(name) scenario files)
  C->>C: parts diff by key + geometry - added, removed, moved
  C-->>UI: per scenario - a unit standing in a new part, a route leg crossing one, an area it overlaps, a key that no longer resolves
```
*What it shows:* the check reads the scenario FILES (JSON), so it needs no loaded world and runs before anything is overwritten; the part keys
(O-3) make "what changed" a diff, not a geometric guess.

| finding kind | rule |
|---|---|
| StandsIn | a `SimTransform` position inside an ADDED or MOVED blocking part (building footprint, wall / hedge / fence buffered by its thickness, prism, water) |
| RouteCrosses | a `RoutePlan` leg (consecutive waypoints, from the entity's position) intersecting such a part |
| AreaOverlaps | an `EditablePolyline` area intersecting an added, removed or moved part (informational) |
| KeyLost | a door state or a behaviour parameter naming a part key (`TerrainObjects`, any string property shaped `<terrain>/<label>…`) that the new world does not resolve |

| decision | lean | rejected — one line each |
|---|---|---|
| J-1 where | `TerrainChangeCheck` in `Fdp.Toolkits/Terrain` (pure: two worlds + scenario JSON → findings); `TerrainBake` gains `CheckOverwrite(...)` that builds the two worlds | loading each scenario into a world — seconds per scenario and a whole node's setup for a list |
| J-2 diff | by part key; a part whose key is unchanged but whose geometry moved > 1 cm counts as moved | a geometric diff of whole worlds — no identity, every re-roll looks like "everything changed" |
| J-3 output | a flat list (scenario, entity guid + name, kind, part key, the point or leg) the dialog groups | — |

Acceptance (handoff T-8): a rail lists the squad a new house would stand on — and the route leg a new hedge cuts, and the door state whose house
was removed.

### 18k. Roads and bridges in the road net — T-7 *(CE-4423 + CE-3438, backend `2026-11-11`; build-state: DESIGN — step 1 measures)*

📐 **INVENTORY** (graph `search_graph` `.*(RoadNetwork|LaneClos|EdgeClos|RoadBlock|Closure).*` Class + `search_code` for closure calls, + reads):
**TWO road systems** run off one terrain — ① the vehicle kinematics' `RoadNetworkBlob` (from the terrain's `roadNetworks[0]` JSON,
`TerrainResidency.cs:276-289`), held by `RoadNetworkHolder` with generation-safe `Publish` (`CarKinem/Road/RoadNetworkHolder.cs:109`) — a swap at run
time is already supported; ② SUMO traffic (`Hrot.Traffic`, the terrain's `sumoNet` `.net.xml`, stepped off-thread by `TrafficSteppingModule`), whose engine
has reroute (`SumoSharp Sim.Core/Engine.cs:75-102`) but ⚠ **no edge-closure call found** (search_code: none). ⇒ a bridge must reach BOTH, and so must its fall.

```mermaid
graph TD
  BF[Bridge feature - T-2] -->|compile at save| OC[OverlayCompiler]
  OC --> RJ[overlay roads - the bridge edges joined to the nearest road ends]
  OC -->|netconvert -s base.net.xml + the bridge edg / nod| SN[overlay net.xml]
  RJ --> RES[TerrainResidency - roads = base + overlay roads]
  SN --> TR[Hrot.Traffic - the overlay net when present]
  COL[structure collapsed - O-2] -->|close the bridge edges| RES
  COL -->|close + reroute - a closure call to add| TR
```
*What it shows:* the bridge becomes road at SAVE (one netconvert, the handoff's "netconvert on Save terrain"), and its fall closes the same edges in
both systems at run time — the vehicles' net by a republish the holder already supports, SUMO by a closure the engine still lacks.

| decision | lean | rejected — one line each |
|---|---|---|
| K-1 where the bridge's road is made | the compile: a Bridge feature's two ends snap to the nearest road nodes within 25 m; the edges go into the overlay's roads JSON and, via `netconvert -s <base net> -e/-n <bridge plain files>`, an overlay `.net.xml` | a live netconvert per edit — seconds per change; the editor shows the deck, traffic follows at save |
| K-2 the fall, vehicles | collapse ⇒ republish the `RoadNetworkBlob` without the bridge edges (`RoadNetworkHolder.Publish`) | a cost penalty — a vehicle would still drive into the river |
| K-3 the fall, SUMO | add an edge-closure command to SumoSharp (disallow all vClasses on the edge's lanes + reroute the vehicles whose route crosses it) — ⚠ an ExtDeps change, measured first | despawning traffic on the bridge — the cars behind it would still route over |
| K-4 CE-3438 | no rubble mound over a water surface (clip it to land) | — |

⏭ **Step 1 (measure before building):** does `netconvert -s` + extra edges keep every base edge id (traffic routes and recordings name them); how
the SUMO engine stores lane permissions (is a closure a lane flag or a route cost); how a structure collapse is observed today (the event the
closure would hang on). Acceptance (handoff T-7): a vehicle routes over a new bridge and around a fallen one.
