<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Authoring.md — §18c-1 (the data the mockups need), §18c-2 (as built)
-->

# NOTE — backend → ui: a compiled terrain overlay on the map vs mockup page 2

**From:** backend (terrain overlay O-4, CE-4415). The user asked that the visuals reasonably match the approved mockups. Backend rendered REAL frames of a
compiled overlay (a hamlet + a copse in test-town) through the production map layers (`TerrainOverlayFrameRail`, Hrot.Presentation.Tests, opt-in) and
compared them with [`terrain-authoring-2-blocks.svg`](../../UX/terrain-authoring-2-blocks.svg).

| | real frame | mockup | owner |
|---|---|---|---|
| 3-D houses | brick walls, grey roofs, grounded, the production template look | — (2-D only) | ✅ matches |
| woods | the terrain's forest fill (2-D) and canopy (3-D) | dark green wood | ✅ matches |
| per-house text | each house printed its stable KEY (`<guid>/<i>`) | none inside a block | ✅ backend fixed it: `TerrainBuilding.Name` (display), empty for block houses; `TerrainPlan2D` prints `Name` and skips an empty one (a two-line cross-lane edit in your file) |
| ⭐ house style in 2-D at overview zoom | a template house draws its FLOOR PLAN (blue-grey floors, red outline, wall strokes) — as every template house does today, demo-city's too | small TAN FILLED footprints, like solid buildings | **ui** — a level of detail for template houses (filled roof footprint below some px / metre)? It is your rendering decision and affects every template house |
| ⭐ the block OUTLINE, its title chip ("Hamlet"), the placing ghost, the block menu and Details | not drawn | dashed outline + chip; ghost; menu; Details (Recipe, Seed, Fit to the site, Contents) | **ui** — the data is ready: `<scenario>/terrain-overlay/overlay.json` `blocks[]` = key, recipe, seed, houses, woods, clutter, `outline` (hull, local metres); `TerrainBlock { Recipe, Seed, HeadingDeg, Fit }` (Fit bits: AlignRoad 1, AvoidWater 2, AvoidBuildings 4, LevelPads 8); the ghost = `FeatureExpander.Expand` (pure) on the cursor |
| "Align to the nearest road" | stored, ignored | checked by default | backend — CE-3431 |
