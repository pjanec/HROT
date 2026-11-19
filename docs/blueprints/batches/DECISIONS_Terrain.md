<!--STATUS
state: LIVE — the terrain lane's decisions (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-19
current-answer: the whole file
-->
# DECISIONS — terrain

## (open) · CE-5039 — may a run start before the scenario's own terrain overlay is baked?
**OPEN `2026-11-19` — for the user (it touches the editor's load / run gate: ui · backend).** Measured: the load reports done ~0.6 s before the live
overlay's first bake; a unit whose target only the overlay makes reachable (inside a building made enterable) plans on the base mesh, fails, and never
retries (`MaxReplans 0`) — `te-make-enterable` is red by it; after the bake the same unit walks in. ⭐ Lean: the run gate stays closed until the live
overlay's FIRST commit for the loaded scenario (the overlay is the scenario's terrain; the map already draws it). Independent hardening, the navigation
owners' call: a PathToPoint that failed for an off-mesh target retries when the navmesh version changes. What would change the lean: a measured load-time
cost of waiting that is too high on a large overlay (then: retry-on-new-mesh alone).

## R-5068 · CE-5026 — glass windows: D-1…D-10
**DECIDED `2026-11-18` (user: "approved"; D-10 the user's own: "building break which removes the wall should of course remove the window glass").** Glazed
windows get a pane piece; hits / cracks / broken in a separate `WindowDamage` on the building's damage entity (SimHost decides, every map draws it), no navmesh
rebake; broken by the 3rd hit, a man firing out, a blast's pressure (∛kg) or nearby fragments; sound through the AQ106 portal graph (an intact pane = a closed
window portal); a breach or collapse that removes the wall removes the glass. 📄 DESIGN_Glass_Windows.md §3.

## R-5067 · CE-5024 / CE-5025 — editing baked-in buildings: D-1…D-8
**DECIDED `2026-11-18` (user: "approved").** An edit is a `TerrainFeature` kind `Edit` keyed by the building's label; `Merge(base, overlay, removed)` compacts
the base; a removed footprint is levelled (bare flat ground); its doors and damage go in the same commit (G-3 closed); *Make enterable* / *Add door* reuse the
importer's generator with a ≥ 1 door rule the importer adopts; "Save terrain as" deletes the removed features. 📄 DESIGN_Terrain_Base_Edits.md §3.

## R-5066 · CE-4662 ② — the smoke cloud carries "blocks infrared" as a FIELD, not by its type *(the lane's own ruling, for review)*
**DECIDED (lane): `AreaEffectCloud.BlocksInfrared` (byte, appended to `dtAreaEffectCloud`), copied from the munition's `WarheadDto.SmokeBlocksInfrared` when
the burst asks for the cloud.** Why: every cloud is the one bare TKB type 7202, so its type cannot tell a phosphorus screen from a signal smoke. Starter
values: the 60 / 81 / 120 mm mortar smoke bombs block infrared; the hand smoke grenade does not. Revisit if a cloud should get its own TKB type per munition.
📄 DESIGN_Thermal_Drivers §4c.

## R-5065 · CE-4662 ① — the ground SURFACES get thermal classes too *(the lane's own ruling, for review)*
**DECIDED (lane): `materials.json` gains a `surfaceThermalClasses` map beside the materials** — ground soil-dry · open grass · forest dense-vegetation ·
water water · scrub grass; a terrain folder may override it. Why: TD-9 named materials only, but most of what an image generator paints (fields, forest,
water) is a surface with no material. Revisit: wet soil after rain (soil-dry → soil-wet) belongs to the generator's solve from the weather, not to us.
📄 DESIGN_Thermal_Drivers §4c.

## R-5064 · CE-5019 — a burst that breaches a wall within 0.5 m makes no crater *(the lane's own ruling, for review)*
**DECIDED `2026-11-18` (user, via coordinator): KEPT as built. Revisit only if a big bomb landing at a wall should still crater (then scale the skip by kg).**
