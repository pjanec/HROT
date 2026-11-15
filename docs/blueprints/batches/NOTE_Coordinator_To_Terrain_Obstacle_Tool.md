<!--STATUS
state: LIVE — a ruling for the terrain lane's terrain-editor design (CE-4594, TE-5)
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Editor_UX.md — TE-5 (the Obstacle family) this ruling settles; fold it in
  - ../../DESIGN_Terrain_Authoring.md — L-6 and the compiled model's clutter row ("a live obstacle stays the entity path") — the basis
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — RM-5 (a ram breaches a weak barrier) needs breakable obstacles
  - ../../DESIGN_Vehicle_Breaching.md — B-3 / B-4: crushing and the bulldozer act on obstacle entities
-->

# NOTE — coordinator → `terrain`: the Obstacle tool places OBSTACLE ENTITIES (R-4264, option a)

🔒 User, `2026-11-15`: *"Approved, option a, record it and tell terrain, with reasoning"* — your question: should a drawn sandbag / block row be
baked clutter that the road blockage learns to see (b), or real obstacle entities (a)?

## The ruling
**(a)** — the Obstacle tool (TE-5) places real obstacle entities. ⭐ **A drawn ROW is ONE undo step and selects as ONE group**, so the user handles
it like one feature. Generated street clutter (the scatter, blocks, towns) stays baked, inert scenery. Both share ONE size / material table
(`ClutterTypes` ⇄ the obstacle TKB shapes — never two definitions of a concrete block).

## Why — the claim table it rests on

| claim | code | design |
|---|---|---|
| the design already splits this: a LIVE obstacle is an entity, baked clutter is indestructible scenery | ✅ `RoadBlockage.Blockers` collects obstacle entities + craters only (`RoadBlockage.cs:19-20`) | ✅ Terrain Authoring L-6 (*"clutter is written into the terrain when generated … in a scenario it is an obstacle entity like today"*) and its compiled-model clutter row (*"⛔ baked clutter is not — a live obstacle stays the entity path"*) |
| obstacle entities already block roads, are crushed, bulldozed, and re-open the road when removed — no engine change | ✅ CE-4590 (road blockage), CE-4600 (a heavy hull crushes obstacles), CE-4601 (the bulldozer's order) | ✅ Breaching B-3 / B-4 |
| drawn obstacles MUST be destructible | — | ✅ Checkpoint & Ramming RM-5 (R-4260): a ramming vehicle breaches a weak barrier through the obstacle path; the checkpoint's serpentine is drawn obstacles |
| (b) would put a second policy over the same pieces | ⚠ inferred: the street-clutter scatter lays parked cars along roads, so a road-blocking tag must exclude by type | — |

## What the entity cost really is
A checkpoint is ~10–40 pieces; a 50 m sandbag line ~17 walls (3 m each). The "thousands" case is street clutter, which stays baked. ⭐ The cost
that matters is the USER's — one row, one undo, one selection — and the group handles it.

## Rejected (one line each)
- **(b) alone** — roads correct, but obstacles indestructible: ramming, crushing and the bulldozer cannot remove them.
- **(b) + clutter destructibility later** — rebuilds in the terrain what obstacle entities already do (ruling 9: one implementation per concept).

## For you
Fold this into `DESIGN_Terrain_Editor_UX.md` TE-5 (and its review) as the as-ruled decision; the row's group and single undo step are part of TE-5's
acceptance. No engine work is asked of you by this note.
