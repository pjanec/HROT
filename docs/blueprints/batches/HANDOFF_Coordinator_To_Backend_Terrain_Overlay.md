<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Authoring.md — OWNS this work: §18 the compiled model (R-4204), §17 the review's gaps G-1…G-15, §2 the feature kinds
  - ../../DESIGN_Peek_And_Fire.md — §9 the static-obstacle bake this generalises (R-242…R-244)
  - ../../DESIGN_Terrain_Import_UTM.md — §8 destruction (breaches / collapses, R-4188, R-4196), §11 the importer and its folder writer, §12 the navmesh prefill
  - ../../DESIGN_Asset_Management.md — §4 / §5 the NAS → node sync and node → NAS publish the overlay rides
-->

# HANDOFF — coordinator → backend: the TERRAIN OVERLAY, compiled at save (terrain authoring's engine core)

**For:** backend. **From:** coordinator. **Dispatched at `01a3c26cb`**, scope frozen there; documents that change later are FYI only (one that
INVALIDATES an item ⇒ stop THAT item and report it). ⭐ **Start when your current batch (CE-4395, the brain's cost) is done.** Merge the lanes you
build on; push `chore: started HANDOFF_Coordinator_To_Backend_Terrain_Overlay at <sha>`; status file `STATUS_Backend_Terrain_Overlay.md`,
updated per item. ⛔⛔ **Autonomous — never stop for a question** (R-4156): a premise that fails stops THAT item only (R-106); a design choice
⇒ take the lean written here or your own, stated with its claim table in the status file.

**Why:** 🔒 user, `2026-11-11`: *"approved, compile at save. Orchestrator can make sure the compiled saved stuff gets distributed to nas and to other
nodes in the cluster"* · *"Yes backend can do it."* — terrain features placed in a scenario (houses, walls, woods, water, bridges, clutter, and
"blocks" from recipes) are compiled ONCE when the scenario is saved into a **terrain overlay**; every node, the Replay Browser and recordings
LOAD it, never expand it. The UI (wizard, picker, placement tools) is ui's, later; this batch is the engine underneath it.

**Design basis — read first:** [DESIGN_Terrain_Authoring.md](../../DESIGN_Terrain_Authoring.md) §18 (C-1…C-11, the sequence and class diagrams),
§17 (the gaps with their measured code lines), §2 (the feature kinds), §16 V-1 (the wizard's *Open in editor*). Rulings R-4198…R-4204.
⭐ **You design the details** (frame delegation): step 1 of each item that needs it is the UML in the owning design, then build, then fold the
as-built back (CLAUDE.md). A deviation from §18 is a finding argued in the design, not a silent choice.

## The overlay, as framed (§18; the format is yours to settle in O-1)

A FOLDER beside the scenario — lean: the terrain folder's OWN format, so the existing parser reads it:
the world file (`*.world.geojson`: buildings, walls, surfaces, walkables, prisms, trees, pads) in **canonical order** with a **stable key** per part
(`<feature entity guid>/<index in its expansion>`) · `buildings/<id>.building.json` for generated houses · `navtiles/` with ONLY the tiles the
additions changed (seeded from the base's) · a small header (its content hash, the base terrain's name + content hash, the scenario). ⛔ Never in git.

## Items, in order

| # | id | what | done when |
|---|---|---|---|
| **O-0** | CE-4419 | ⭐ a slow terrain bake must not destroy entities: the terrain participant's construction timeout is 18 000 FRAMES (`EntityCreationPack.cs:75`) — make it wall-clock or unbounded (the bake always acks) | a rail: a bake slower than the old frame budget keeps its entities |
| **O-1** | CE-4415 (design) | ⭐ **the overlay format + the UML** in Terrain Authoring §18 (class / sequence / module: who compiles, who publishes, who syncs, who loads — draw the dead edges); then MEASURE on scale-1 with a synthetic overlay of ~700 houses + woods + clutter: its size, the merge time at load, the navtiles it adds | the design marked READY-TO-BUILD with the numbers |
| **O-2** | CE-4416 | ⭐ **`TerrainWorldMerge`** — the general merge replacing `TerrainObstacles.With` for overlays (it appends prisms only and shares the base's surfaces, panels, buildings, doors, walkables, heights — `StaticObstacles.cs:~150-195`): buildings with panels and doors, walls, surfaces (forest, water + the ground cut), walkables (bridge decks, ramps), prisms, trees, height pads — in the overlay's order; door ENTITIES for overlay houses by stable key (today created only from the base at `ScenarioLoadStep.cs:264`); navmesh fine areas for them (`RecastNavmeshFactory.cs:144-156`); a bridge collapsible like a building | rails: a merged house is enterable, its door opens, a squad paths inside; a wood conceals; water is impassable (verify — §10 row); a bridge carries a unit and stops carrying it when collapsed |
| **O-3** | CE-4417 | ⭐ **stable keys** for breaches, collapses and door states — never a global panel / building index (`BuildingDamage.cs:21,57`) or the terrain's name (`:113`, `TerrainBuildingExpander.cs:299`) | a rail: the Muscle breaches an overlay house and every node draws the hole on the SAME house; saved door / damage states survive a re-save |
| **O-4** | CE-4415 (build) | ⭐ **the compiler + the load**: the recipe format and the pure, seeded `FeatureExpander` (§4; R-4199 B-1, B-3 fit stored at placement); the `OverlayCompiler` on scenario save; publish to the NAS with the scenario; the orchestrator's sync before a load (a new load part beside the terrain — Asset Management §4 / §5, `LoadPhaseContracts.cs`); `TerrainResidency` loads base + overlay; the hash check refuses a mismatched base and names both | rails: save → compile → a second process loads the overlay and gets byte-identical parts; a cluster integration run loads a scenario with an overlay on every node; a mismatched base refuses |
| **O-5** | CE-4422 | **bake into the terrain** (*Save terrain* / *Save terrain as*): base + overlay through the SAME merge, written by a writer of a `TerrainWorld` (rotation — hard-coded 0 today, `TerrainFolderWriter.cs:70-71` —, slabs, ramps, prisms, clutter); navtiles = base + overlay tiles copied (no cold prefill — `NavmeshPrefill.cs:22-28` reads only its own folder); keys rewritten overlay → terrain; a new terrain hash; the source scenario + ground base in `source/` (R-4201); for an imported terrain with no source, copy the original as the base on first edit (G-11) | a saved terrain loads with the features, no cold bake, door / damage states carried over |
| **O-6** | CE-4425 | generated content out of git: `assets/terrain/<name>/` for anything but the committed demos, overlays, `source/` — ignore or write to the NAS / content root (today only `assets/terrain/scale-*/` is ignored) | `git check-ignore` clean for a generated terrain and an overlay |
| **O-7** | CE-4418 | the editor's **live incremental bake** while authoring: only the changed area (touched tiles, index, cover; the 3-D chunks are ui's — expose which changed) | measured on scale-1: moving one feature in a town ≪ today's ≈ 17 s whole-world rebake |
| **O-8** | CE-4420 | the Replay Browser loads the recording's overlay (by hash) — and today's static obstacles, which it already misses (it mirrors the base world, no bake) | a replay of a scenario with an overlay shows its houses |

**Not in this batch:** roads and bridges in the road net (CE-4423), the missions check on a re-saved terrain (CE-4424), the external engine
(CE-4421 — needs the gateway session), the smaller gaps (CE-4426), the generator as a library and the UI (wizard, Add Entity groups, placement
tools, scatter — ui, later).

## Rules

- **Proof proportionate to risk** (CLAUDE.md): O-2, O-3, O-4 are sim / wire / cross-node — red-prove each new rail, gate the integration suites that
  exercise the bake and the load (name them, CLAUDE.md row 8). O-6 is a config change.
- **0 B garbage per steady frame** for anything per-frame (the load path is not per-frame; a per-frame overlay check must be).
- **Shared, never duplicated:** one merge (`TerrainWorldMerge`) for the load, the live bake and *Save terrain*; one expander; the runtime obstacle
  bake (`StaticObstacleBakeSystem`) stays for obstacles placed during a run (§18 C-10) — fold, do not fork.
- Each item folds its as-built into Terrain Authoring §18 (or the owning design it touched — Peek and Fire §9, UTM §8 / §11 / §12, Asset
  Management); new findings ⇒ rows in YOUR block.
- Gates per the gate-report contract (command · counts · `--no-build` · reds proven pre-existing at the base sha); `tracker-counts.py --check`.

**Done when:** every item is closed with its evidence, or names in the status file exactly what blocks it and what was measured.

---
_coordinator lane, 2026-11-11_
