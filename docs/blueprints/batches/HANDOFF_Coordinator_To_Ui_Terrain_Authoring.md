<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Authoring.md — OWNS this work: §11 (blocks through Add Entity, R-4199), §13 (the workflow, R-4200), §14 (Edit terrain, R-4201), §15 the approved MOCKUPS (R-4203), §18 the compiled overlay as built by backend (R-4204)
  - ../../DESIGN_Add_Entity_Picker.md — §2f the gallery, §2j the thumbnail atlas: the picker the terrain groups join
  - NOTE_Backend_To_Ui_Terrain_Overlay_Visuals.md · NOTE_Backend_To_Ui_Save_Terrain_As.md — backend's notes: what the engine already gives you
  - HANDOFF_Coordinator_To_Backend_Terrain_Authoring_2.md — backend's parallel batch (more feature kinds, richer recipes, the generator library) that U-5 / U-6 build on
-->

# HANDOFF — coordinator → ui: terrain authoring in the editor (the approved mockups, on backend's compiled overlay)

**For:** ui. **From:** coordinator. **Dispatched at `2235ff0c2`**, scope frozen there; later documents are FYI (one that INVALIDATES an item ⇒ stop
THAT item and report it). Start when your current work allows; merge `coordinator` and `backend` first; push
`chore: started HANDOFF_Coordinator_To_Ui_Terrain_Authoring at <sha>`; status file `STATUS_Ui_Terrain_Authoring.md`, per item.
⛔⛔ **Autonomous — never stop for a question** (R-4156); a failed premise stops THAT item only (R-106); a design choice ⇒ take the lean here or
your own, with its claim table in the status file.

**Why:** 🔒 user, `2026-11-11`: *"Lets proceed to designing the mockups so I can imagine the whole workflow"* · *"approved, mockups too"* ·
*"approved, compile at save"*. Backend has built the engine (O-0…O-8, [STATUS](STATUS_Backend_Terrain_Overlay.md)): `TerrainBlock` /
`TerrainFeature` entities, recipes in `assets/terrain-recipes/`, the overlay compiled when the master saves, loaded on every node, *Save terrain as*
in `TerrainBake.SaveAs`, a live editor bake. ⭐ **This batch is the user-facing half — and the approved mockups are the acceptance spec.**

**Fidelity (R-4138 / R-4146 / R-4151):** each item names its mockup page; render the real frame (`HROT_RAIL_SHOTS`, `xvfb-run -a`), compare it
with the mockup yourself, fix every deviation, and put the side-by-side in the status file and the chat (⛔ never in git). What a mockup draws as
dimmed CONTEXT is out of scope and unchanged. A mockup element the engine cannot back yet (a feature kind backend has not built) ⇒ build the
surface against backend's published interface, grey it with its reason, and say so — ⛔ never invent engine behaviour.

## Items, in order

| # | what | mockup | engine it uses | done when |
|---|---|---|---|---|
| **U-1** | ⭐ **Add Entity ▸ the six terrain groups** (Blocks · Buildings · Vegetation · Walls & fences · Clutter · Water & bridges, B-5): a NEW picker SOURCE for recipes and features (a recipe is not a DIS type — the groups today are `DisTypeMask` lists, G-15); tiles with pictures (a recipe at its default seed through the thumbnail pipeline §2j — a stand-in tile until the atlas carries it); the Side row greyed while a terrain group is open; the side pane | page 2 (bottom), page 3 (bottom) | recipes: `TerrainRecipe.Find`, `assets/terrain-recipes/*.recipe.json`; templates: `assets/buildings/` | a rail opens Blocks and lists every shipped recipe; the frame matches page 2 |
| **U-2** | ⭐ **placing a block / a house**: the ghost that fits itself while you move it (the expander's fit — avoid water and buildings — previewed live; road alignment greyed until backend's CE-3431), R to rotate, click places ONE `TerrainBlock` (or `TerrainFeature`) entity; one undo step | page 2 (the ghost and its two-line hint) | `FeatureExpander.Expand` (pure — safe to call for the preview), `TerrainBlock` / `TerrainFeature` | a rail places a hamlet and a house; undo removes each in one step |
| **U-3** | ⭐ **a placed block on the map**: the dashed outline + its chip (recipe · seed), selection, the block menu (Re-roll · Rotate · Move · Unpack · Change recipe · Delete) and its Details (Recipe, Seed + dice, Fit to the site, Contents); the TERRAIN SOURCE title chip when the scenario is a terrain source (E-2). Unpack needs backend's T-4 — grey it until then | page 2 | `<scenario>/terrain-overlay/overlay.json` `blocks[]` (key, recipe, seed, counts, outline hull); the live bake (§18e-2) redraws after each change | rails: Re-roll changes the seed in one undo step and the outline follows; the frame matches page 2 |
| **U-4** | the **map look of template houses at overview zoom** — a filled footprint (the mockup's tan) instead of the floor plan, and a zoom threshold for the interior slab labels (`TerrainPlan2D.cs:93`); it applies to EVERY template house (demo-city's too) | page 2 | — (backend's visuals note) | the frame matches page 2 at overview zoom; close in, the floor plan returns |
| **U-5** | **Save terrain / Save terrain as… / Edit terrain…**: the File menu entries; the *Save terrain as* dialog with its summary (baked · stays in the source · damage not baked · pre-build the walkable mesh with its estimate · a new scenario on it); the overwrite confirmation naming the terrain's users (W-4); the terrain picker's SOURCE badge and right-click *Edit terrain…* (an imported terrain: *creates a source*) | page 4 | `TerrainBake.SaveAs`, `TerrainSaveSummary`, `TerrainUsers.Of` (backend's Save terrain note) — you write the returned scenario DOM; *Edit terrain* opens `<terrain>/source/scenario` | a rail saves a source as a terrain, loads the new scenario, finds the houses; the frames match page 4 |
| **U-6** | the **3-D view follows the live bake by area**: rebuild only the chunks under the dirty rectangles backend exposes, not every chunk on a new world (`TerrainLayer3D.cs:48-57`) | — | the dirty rects of §18e | measured on scale-1: one feature moved rebuilds the touched chunks only |
| **U-7** | the **single-feature tools and the area scatter**: a line tool for walls / fences / hedges (kind, height), a bridge across water (span, joins the banks), *Scatter in this area* (what, density, seed, Done) — on backend's T-2 feature kinds; ⚠ if those have not landed (check ancestry), build the tools against the interface backend's design publishes and grey what cannot run | page 3 | backend T-2 | rails per tool; frames match page 3 |
| **U-8** | the **random-terrain wizard** (File ▸ New ▸ Random terrain…): ① six preset pictures + Size · Relief · Vegetation · Water · Seed, ② the 2-D preview with the blocks outlined, *what was generated*, the budget verdict, *Shuffle layout* / *Shuffle ground*, ③ *Open in editor* (saves and opens, V-1) — the same frame as the OSM wizard; ⚠ needs backend's T-1 generator library — same rule as U-7 if it has not landed | page 1 | backend T-1 | rails: generate, shuffle, open; frames match page 1 |

**Not in this batch:** the engine behind U-7 / U-8 (backend's parallel batch), roads in the road net (CE-4423), the missions check on a re-saved
terrain (CE-4424), the external engine (CE-4421).

## Rules

- **Proof proportionate to risk:** U-1…U-5, U-7, U-8 are UI — one key rail red-proved per item + the frame check; U-6 touches the per-frame 3-D path —
  a 0 B garbage rail and the measured chunk count.
- **0 B garbage per steady frame** on every per-frame path (outlines, chips, the ghost, the 3-D chunks).
- **Shared, never duplicated:** the existing gallery, placement tool, point-sequence tool, menus and wizard frame — extended, never copied.
- Fold each item's as-built into Terrain Authoring (a §15 / §18 sub-section) or the design it touched (Add Entity Picker §2f / §2j, Map 3D §6ah).
- Gates per the gate-report contract; display rails under `xvfb-run -a` (a skip is not a pass); `tracker-counts.py --check`.

**Done when:** every item closed with its evidence and its frame beside its mockup, or the status file names exactly what blocks it.

---
_coordinator lane, 2026-11-11_
