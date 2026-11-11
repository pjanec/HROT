<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the table
related-designs:
  - ../../DESIGN_Terrain_Authoring.md — §18d / §18d-1 Save terrain as (engine, as built); §13 W-3/W-4, §14 E-1…E-6; mockup 4
  - NOTE_Backend_To_Ui_Terrain_Overlay_Visuals.md — the earlier overlay visuals note
-->

# NOTE — backend → ui: *Save terrain as* is ready in the engine (CE-4422)

The engine side of mockup 4 ([terrain-authoring-4-save.svg](../../UX/terrain-authoring-4-save.svg)) is built: `TerrainBake.SaveAs` in
`FDP/Toolkits/Fdp.Toolkits.Navigation.Recast/TerrainBake.cs`. The dialog and its entry points are yours. Nothing on your side is blocked.

| mockup 4 shows | call |
|---|---|
| *Baked: N blocks → houses · woods, trees · km of walls · bridges* | `TerrainSaveSummary` (Houses, Woods, Trees, WallKm, Bridges, block count) |
| *Stays in the source only: units, areas, damaged houses* | the summary's NotBaked fields |
| *Pre-build the walkable mesh ≈ N min* | `TilesToBake` / `EstimateSeconds` (null when the save did not bake navtiles) |
| *Used by N scenarios* (overwrite, W-4) | `TerrainUsers.Of(terrainName, scenarioRoots)` |
| the save itself | `SaveAs(sourceScenarioDir, targetFolder, newName, resolveTerrainDefinition)` → a new terrain folder + the new scenario as a DOM (W-3: header on the new terrain, door states re-keyed, no entities) — **you write that scenario** where the dialog says |

⏭ Not built, yours or shared when you pick them up: *Edit terrain* (E-2 — open `<terrain>/source/scenario`, whose base is `<terrain>/source/base`), the
terrain picker's entry, an HTTP route if the dialog runs off the master.
