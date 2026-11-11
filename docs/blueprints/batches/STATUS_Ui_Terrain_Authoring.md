<!--STATUS
state: LIVE
updated: 2026-11-11 (follow-up 3: Unpack and road alignment wired to backend's T-4 / T-5)
current-answer: the table, then 'Follow-up 3' (newest), 'Follow-up 2'
related-designs:
  - HANDOFF_Coordinator_To_Ui_Terrain_Authoring.md — the frame (dispatched at 2235ff0c2; scope frozen there)
  - ../../DESIGN_Terrain_Authoring.md — the owning design; §15a holds the as-built of each item
  - STATUS_Backend_Terrain_Authoring_2.md — backend's parallel batch (T-1 and T-2 landed during this run and are merged; T-3 recipes, T-4 Unpack, T-5 road alignment are NOT built)
-->

# STATUS — ui: terrain authoring in the editor ([handoff](HANDOFF_Coordinator_To_Ui_Terrain_Authoring.md))

Started at `c8dc93df4` (merged `coordinator` + `backend@80051b2f0`); `backend@dc680a442` (T-1, T-2) merged before U-7. Tracker ids from the ui block: CE-1481 … CE-1490 (CE-1488 follow-up fonts/icons/hint · CE-1489 block Details form · CE-1490 Edit terrain…). Frames: rendered by the rails to the session scratchpad (`HROT_RAIL_SHOTS`), ⛔ never committed.

| # | id | state | note |
|---|---|---|---|
| U-1 | CE-1481 | ✅ DONE | six terrain groups in Add Entity (`TerrainPickCatalog`, `GalleryGroups` always appends them), tiles with a generated picture of the recipe at its default seed; rails `TerrainAuthoringPickerTests.U1_*`, `TerrainAddEntityFrameRail` |
| U-2 | CE-1481 | ✅ DONE | the fitted placing ghost (pure `FeatureExpander`, cached, 0 B steady), R rotates, one `TerrainBlock` / `TerrainFeature` per click; rails `U2_*` |
| U-3 | CE-1482, CE-1489 | ✅ DONE (follow-up 2026-11-11: Details form built, menu icons, hint fix) | outline + chip + selection + block menu (Re-roll · Rotate · Change recipe… one undo step each; **Unpack live since follow-up 3**); `TerrainBlockGizmoTests`, `TerrainBlockFrameRail` |
| U-4 | CE-1483 | ✅ DONE | template houses are tan footprints below 6 px/m, floor plan above; `TerrainBlockFrameRail` |
| U-5 | CE-1484, CE-1490 | ◐ PART | Save terrain as… ✅; **follow-up 2026-11-11: Edit terrain… ✅ (staged to `opened/terrain/<t>`), SOURCE chip + right-click ✅, File ▸ Edit Terrain… / Save Terrain ✅, `TERRAIN SOURCE ·` title ✅**; ⏭ greyed: a terrain whose ground left the catalogue ([NOTE](NOTE_Ui_To_Backend_Edit_Terrain.md)), a terrain with no source (E-3) |
| U-6 | CE-1485 | ✅ DONE | 3-D chunks adopt a new world and rebuild only those under the dirty rectangles: demo-city 16 chunks, 48 builds at load, one moved rectangle ⇒ 3 builds |
| U-7 | CE-1486, CE-1492 | ◐ PART | lines / areas / points through the existing tools on T-2; **follow-up 2: the options panel (kind · height · bridge span · scatter recipe / density / seed), per-type coloured clutter glyphs**; **the area scatter is greyed (no recipe carries a `scatter` section yet)** |
| U-8 | CE-1487, CE-1491 | ✅ DONE (follow-up 2: the plan with relief, chips, tooltip, zoom; the preset tiles generated; the mockup's rows) | the three-step wizard over T-1; deviations in §15a-2 (the engine's option labels, no clutter / bridge rows) |

## What is greyed, and why

| surface | reason |
|---|---|
| ~~block menu ▸ *Unpack*~~ · ~~the placing hint's road alignment~~ · ~~Align to the nearest road (Details)~~ | ✅ **all three live since follow-up 3** (below) |
| *Street clutter* (the area scatter) in Clutter | backend's T-3 part 1 shipped the recipe vocabulary but no recipe with a `scatter` section; the tile stays greyed — it lights by itself when one lands |
| the bridge panel's *road edge at save* | backend's T-7 (CE-4423); the panel says so |

## Follow-up 3 (2026-11-11) — Unpack and road alignment wired (CE-1482)

| item | state | rail (red-proved by a compiling mutation) | note |
|---|---|---|---|
| Unpack | ✅ | `BlockOperationsTests.U3_TheMenusUnpackAction_IsEnabled_UnpacksInOneStep_AndOneUndoRestoresTheBlock` (handler calling Re-roll) · `TerrainBlockGizmoTests.U3_TheBlockHasItsOwnMenu_…` (no greyed tooltip) · `TerrainBlockMenuFrameRail` | `TerrainBlockActions.Unpack` → `BlockOperations.Unpack` (its own bracket ⇒ ONE step; undo restores the block, same key); the editor passes `BlockExpansionContext.Of(TerrainResidency)`; a refusal is logged and changes nothing. Frame `u3-block-menu.png`: the row is teal and enabled |
| Align to the nearest road (Details) | ✅ | `TerrainBlockDetailsViewTests.U3_TheForm_…` (row drawn disabled ⇒ the click changes nothing ⇒ red) | the checkbox toggles the `AlignRoad` bit, one undo step. Frame `u3-block-details.png` |
| ghost previews the alignment | ✅ | `TerrainAuthoringPickerTests.U2_TheGhostAlignsToTheRoadLive_…` (+ **0 B** steady cursor beside the road) · `TerrainBlockFrameRail.TheGhostAtTheTrack_…` (no road net to the expander ⇒ both red) | `TerrainGhostContext.Roads` → `Expand(…, roads)`; hint *aligned to the track (R to rotate)* when `TryAlign` turns it. Frame `t5-ghost-at-the-track.png` |

⚠ **Deviation from mockup 2:** the ghost's hint is the shared two-line backdrop text (white line, green verdict) — the mockup's warning / tick glyphs and the blue bordered panel are not drawn (as before; U-2). ⚠ **Finding, not fixed:** `TerrainBlockOps.Reroll` / `ChangeRecipe` duplicate backend's `BlockOperations` with a different seed rule — see the CE-1482 note; unify once the Details seed field can hold a full `uint`.

## Follow-up 2 (2026-11-11) — gaps measured against mockups 1 and 3

Merged `backend@78eb37d3d` (T-3 part 1, T-4, T-5) into `ui` first (`f011b98ea`); the one real conflict was `FeatureExpander` (my `Shapes` for the ghost on top of backend's `ElementState` refactor — folded in) and three recipe files (my `"ui"` object re-added).

| gap | state | rail (red-proved) | note |
|---|---|---|---|
| A the preview plan | ✅ | `RandomTerrainWizardTests.U8_ThePlanGroupsTouchingBlocksIntoOneChip_…` (GroupGap mutation), `…U8_ThePlanFrame_…` (relief mutation) | the "one strip" was the rail's fake service, not a drawing defect; real blocks were drawn unlabelled. Per-block labels, relief shading, caption, tooltip, zoom, rows, Back to the preset built. |
| B step ① | ✅ | `U8_TheWizardsFrames_…` (picture mutation) | pills + dashes + generated preset pictures; the workflow strip is a grey annotation (not drawn by the app) |
| C page 3 | ◐ | `TerrainDrawPanelFrameRail` (kind-click mutation), `U7_TheDrawingPanelsChoices_…` (Born mutation), `TheGalleryOpenedOnClutter_…` (glyph-feed mutation), `U7_EveryClutterTypeHasItsOwnGlyph_…` (key mutation) | panel for hedge / bridge / scatter, per-type glyph tiles, scatter pick lights with a recipe. ⏭ greyed: Street clutter (no scatter recipe), the bridge's road edge (T-7) |

Ids: CE-1491 (A + B), CE-1492 (C). Frames this run (⛔ not in git): `u8-wizard-1-preset.png`, `u8-wizard-2-preview.png`, `u8-wizard-2b-zoom.png`, `u7-draw-panel-{hedge,bridge,scatter}.png`, `u7-add-entity-clutter.png` under the session scratchpad `terrain-shots/`.

**Not done, honestly:** the placed wall / bridge on the map was not rendered beside the mockup's (only the panel and the tiles were): the in-progress line is the shared drawing look and was not changed; *Unpack* and the ghost's road alignment were not rewired then — done in follow-up 3; the gallery has no *Scatter in an area…* header button; no 3-D look at any of it.

## Premises that failed (R-106 — that item only, the rest continued)

| item part | premise | measured |
|---|---|---|
| ~~U-5 *Edit terrain…* / SOURCE badge / *Save terrain* / TERRAIN SOURCE chip~~ (**SUPERSEDED 2026-11-11 — built; the claim below was too strong: the door opens by name, and `opened/<area>/<key>` carries a staged copy**) | "open `<terrain>/source/scenario`" is one call | the scenario door (`EditorScenarioSession.LoadScenario(name)`) loads by NAME from the catalogue, and the terrain catalogue does not resolve `<terrain>/source/base` — backend's own note lists *Edit terrain* as "not built, ui + backend" (E-2). Needs a load-by-folder door and a catalogue root; row CE-1484 stays open for it |
| ~~U-3 Details form (Recipe dropdown, Seed + dice, Fit checkboxes, Contents)~~ (**SUPERSEDED 2026-11-11 — built as the Block tab**) | a form in the Details panel | was not built: the generic component fields (Recipe, Seed, HeadingDeg, Fit) are shown by the ordinary Details panel; a custom form is its own slice |

## Claim table (decisions taken, with their basis)

| decision | code | design |
|---|---|---|
| the terrain groups are intrinsic (appended by `GalleryGroups`), not a `groups.json` edit | ✅ `groups.json` is seeded to the NAS and an older copy would hide them | ✅ §2f G1 (a recipe is not a DIS type, G-15) |
| a block / feature is a NEW TKB type pair 8817 / 8818 | ✅ measured free on every remote branch (`TkbEntityTypes.cs:147` was the last 88xx) | ✅ §18 "the TKB type + the Add Entity groups — ui's (B-5)" |
| the ghost expands with the BASE world, not the merged one | ✅ `TerrainResidency.ExpansionBase()` is what `LiveOverlay.Expand` uses | ✅ §18e-2 B-2 |
| tile pictures generated from the recipe (cached by content under `%TEMP%`), not the §2j atlas | ✅ the atlas holds model renders of TKB types; a recipe has no model | ⚠ B-5 says "through the atlas pipeline" — deviation until that can render a recipe |
| `LiveTerrainBakeInfo` (ComponentId 496) published by `StaticObstacleBakeSystem` is the ONE source for outlines (U-3) and dirty rects (U-6) | ✅ `BakedParts` / `LastDirty` were already on the system; the views are in other assemblies | ✅ §18e-2 B-5 "the dirty rects are exposed — ui deriving them again is a second diff" |
| the block menu is a new `ActionTarget.TerrainBlock` in the catalogue, not a gizmo-bound menu | ✅ the map resolves a right-click menu through `MapMenuActions.JsonFor` | ✅ B-11 A1 (the catalogue composes every menu) |
| re-roll / rotate / change recipe are direct component writes inside an `EditSteps` bracket | ✅ the editor's undo snapshots the world around a bracket (`WorldUndoRecorder`) | ✅ §18h H-1: *"a block operation needs no undo code of its own"* — replaced by backend's `BlockOperations` when T-4 lands |
| Save terrain as reads the scenario AS SAVED | ✅ `TerrainBake.SaveAs` compiles from `scenario.json` on disk | ⚠ the dialog does not save first (asynchronous through the storage gateway) — say so, Save scenario first |

## Cross-lane edits (named, additive)

`FDP/Toolkits/.../FeatureExpander.cs` (`FeatureExpansion.Shapes`, `HouseFootprint` — the ghost) · `NodeEditor.UI` `PickerRequest.InitialGroup` · `Hrot.Core` `StaticObstacleBakeSystem` (publishes `LiveTerrainBakeInfo`) · `Hrot.Common` (`ActionTarget.TerrainBlock`, action ids 2430–2433, the block menu) · `FDP/Engine/Fdp.Presentation` `TerrainChunks3D` / `TerrainLayer3D` (U-6) · `GlobalComponentIds.LiveTerrainBakeInfo = 496` · `TkbEntityTypes` 8817 / 8818 · the editor's File menu (`FileMenuLayout`, `ScenarioMenuCommands`) · `assets/terrain-recipes/*.recipe.json` gained an optional `"ui"` object the engine ignores.

## Gates

See the final report of the batch (commands, counts, the pre-existing reds: `Map3DFrameRail.CE1227_V_DemoCity`, `FilmFrameRail.CE1380`, `Map3DFrameRail.CE4388` fail identically at `c8dc93df4`, the base — measured in a clean worktree).
