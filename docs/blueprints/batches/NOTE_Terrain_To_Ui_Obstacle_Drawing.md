<!--STATUS
state: LIVE — FYI + one question for ui
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../DESIGN_Map_3D_Mode.md — the CE-4656 paragraph beside CE-1966 (§6ai)
-->

# NOTE — terrain → ui: a baked static obstacle has ONE drawer (CE-4656)

| what | your side |
|---|---|
| the obstacle bake makes every static obstacle a terrain prism; a **platform-typed** obstacle (DIS kind 1 — the parked car, TKB 8806) now carries `TerrainPrism.DrawnByEntity` and `TerrainWorldMesh` neither draws it nor cuts the ground under it — `EntityBodyLayer3D` draws the car. Before: a grey `car-body` box over the car once the bake committed (user observation). Rail `StaticObstacleBakeTests.CE4656_*` | nothing to change |
| ⚠ **question (not measured):** the seven cultural-feature obstacles (kind 5: 8807 sandbags, block, crate, barrel, jersey barrier, guard booth, wreck) resolve to `VisualFamily.Unknown` in `VisualFamilies.Of`, and `EntityBodyLayer3D.Draw` skips only `Unit` — so it may ALSO draw them (an unknown box) under / over their baked material box. If a frame shows two, the lean: the terrain box is their look (the baked clutter of the same name is the same box, T-2 G-3) ⇒ put the obstacle types on `LookOf`'s no-body list, as CE-1966 did for terrain features | a frame rail (`xvfb-run -a`) with a placed sandbag wall in 3-D |
