<!--STATUS
state: LIVE — dispatched, not started (RE-STAMPED 2026-11-10, rule 1a: ui may DESIGN the 3-D look of the NEW things — R-4195; first stamp d34e3a3e4 never reached ui)
updated: 2026-11-10
current-answer: the whole file
-->

# HANDOFF — coordinator → ui: drawing the LARGE terrain (3-D and 2-D), and the importer in the wizard

**For:** ui. **From:** coordinator. **Dispatched at `e0865ce9e`** (re-stamped; first `d34e3a3e4`), scope frozen there. Merge the other lanes first (⭐ `coordinator` after every
item — the importer lands there); push `chore: started HANDOFF_Coordinator_To_Ui_Large_Terrain_Drawing at <sha>`; status file
`STATUS_Ui_Large_Terrain_Drawing.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md). ⛔⛔ **Never stop for a question** (R-4156):
every decision is made below; a premise that fails stops THAT item only (R-106).

**Why:** the large-terrain programme ([PLAN_Large_Terrain.md](../../PLAN_Large_Terrain.md) U-1 / U-2) — backend made the 5.5 km terrain load
in budget on the SIM side ([DESIGN_Scale_Rig.md](../../DESIGN_Scale_Rig.md) §5); the EDITOR's drawing of it is untouched: 📌 **CE-3423** —
`TerrainWorldMesh.Build` at 1 m on scale-1 = **61.4 M triangles, 2.2 GB, peak 7.0 GB, 9.4 s**, all for the 3-D view and its pick grid.

**The terrain to work on:** `scripts/terrain-scale.py --seed 1 --trees` → `assets/terrain/scale-1/` (gitignored, regenerated in ≈ 30 s; 1 651
enterable houses, the 1 m height store, woods + lone trees). Small terrains (demo-city, test-town) are the look reference.

**Budgets (R-4192):** **3-D ≥ 30 fps** on the editor machine · 0 B garbage per steady frame (CLAUDE.md hot-path rule) · ≤ 4 GB per process.
⚠ This VM draws with software GL on Xvfb (Scale Rig §C-1: it takes ~3 cores) ⇒ its fps is NOT the budget number — measure the CPU side here
(mesh build seconds, triangles and draw calls per frame, memory, garbage); the fps number is taken on the user's Windows machine later.

**⭐ What may look different — and what may not** (🔒 user, `2026-11-10`, R-4195: *"if there is new stuff like trees or different roofs the ui
lane is free to design their 3d look with respect to performance"*):

| | |
|---|---|
| ⭐ **YOURS TO DESIGN** — the 3-D look of what is NEW with the large terrain: **trees** (lone trunks, tree rows, the woods' canopy / edge), **pitched roofs** (today a flat roof + a solid cap prism to the ridge, UTM §11.3 I-h — you may draw a real gable / hip over it), and the **imported houses' facades** (openings on unique footprints) | design it for PERFORMANCE first (instancing, few draw calls, level of detail by distance) and to read well in the editor; the colours stay in the house's one colour language (R-4139, `TerrainLayer3D.ColourOf` / the material library). ⭐ **Make a mockup or a rendered still of the chosen look and put it in the design** (Map 3D Mode, R-4143), then build to it; the before / after frames go to the chat as an FYI, never a gate (R-4146) |
| ⛔ **must NOT change** — everything that exists today: the ground, the existing buildings and templates, walls, slabs, roads, water, the colours of materials | near the camera they look as today; far detail may simplify (that is what level of detail means). **Proof:** before / after frames at the same cameras on demo-city AND scale-1 (near, mid, far), side by side to the scratchpad and the chat (⛔ never git); every visible difference listed in the status file with why |
| ⚠ the look of a 3-D roof must not change the SIMULATION | sight and fire keep using the world's prisms (the cap); a drawn gable is presentation only |

**📐 = DESIGN FIRST** in the named section (diagram first: class + sequence + module graph with WHO CALLS IT EACH FRAME, an `INVENTORY` of the
graph queries for the reused parts), then build.

| # | id | what | 📐 | done when |
|---|---|---|---|---|
| **D-1** | CE-3423 | ⭐ **the 3-D ground by level of detail**: the ground in square chunks (e.g. 256 m), each at a resolution chosen by its distance to the camera (1 m near … 8–16 m far), built OFF the frame thread from the 1 m height store (`TerrainHeightGrid`), cached per chunk + level, cracks between levels closed (skirts or stitched edges); buildings, walls, slabs and roofs still come from the world's prisms but per chunk too, so nothing builds the whole-terrain soup; the PICK grid (`TerrainWorldQuery`) built per chunk on demand, not from the soup. ⛔ Never block a frame; ⛔ no allocation per steady frame | 📐 [DESIGN_Map_3D_Mode.md](../../DESIGN_Map_3D_Mode.md) (a new section; link Scale Rig §5.2 N-5 both ways) | scale-1 opens in the 3-D view with no whole-soup build; numbers before / after (build s, peak memory, triangles drawn at the three cameras); demo-city unchanged (frames); the existing `Map3D*` frame rails green under `xvfb-run -a` |
| **D-2** | CE-4376 | **the 2-D map at 5.5 km**: the terrain plan (`TerrainLayer2D` / `TerrainPlan2D`: buildings, woods, water, walls, roads) and the height shading drawn within budget on scale-1 — retained meshes per chunk where it draws per frame today; the shading from the height store at the zoom's resolution | 📐 the 2-D layer's design section (find it via `TerrainLayer2D`; link both ways) | numbers before / after on scale-1 (frame CPU ms, garbage, memory) at the whole-terrain zoom and close in; small terrains unchanged (frames) |
| **D-3** | CE-4377 | **buildings and trees at scale**: 1 651 houses (≈ 85 000 prisms) and the woods + ≈ 11 000 lone trunks drawn in 3-D and 2-D in budget — trees, tree rows and the woods' canopy in a look you design (R-4195; R-4190 L-5's cone / ball is the baseline, not a limit); pitched roofs drawn as you design them over the cap; instanced / batched draws, culled by chunk | 📐 with D-1 | numbers on scale-1 with `--trees`; the frames |
| **D-4** | CE-4378 | **the importer in the wizard**: when the coordinator's importer (`TerrainImporter : ITerrainImporter`, CE-4368, project `Hrot.Terrain.Import`) is on `origin/coordinator`, compose it in the editor (the wizard's last stage "Import into HROT", then "Open in New Scenario" opening the new terrain in the New Scenario picker; Replace lists `ScenariosUsing`) — exactly the mockup's step 4 (C-14). ⛔ If it is not there when you reach D-4, mark D-4 blocked in the status file and finish the batch | — (OSM §6b already designs it) | a headless rail: the fake pipeline + the REAL importer on a synthetic set → the terrain folder exists and the picker lists it; the C-14 frame rail still green |

**Order:** D-1 → D-3 → D-2 → D-4 (D-4 whenever the importer is there). Gates: the touched test classes + each new rail red-proved once; display
rails under `xvfb-run -a` (a skipped display rail is not a pass); `tracker-counts.py --check`; Mermaid parses.

---
_coordinator lane, 2026-11-10_
