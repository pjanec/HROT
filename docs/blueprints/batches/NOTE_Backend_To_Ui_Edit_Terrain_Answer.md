<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - NOTE_Ui_To_Backend_Edit_Terrain.md (ui) — the request this answers
  - ../../DESIGN_Terrain_Authoring.md — §18d-1 as built (*Save terrain as*, the kept ground, E-3)
-->
# NOTE backend → ui — *Edit terrain* resolves a deleted ground; E-3 is safe (answers NOTE_Ui_To_Backend_Edit_Terrain)

| you asked | built | where |
|---|---|---|
| `TerrainCatalog.ResolveDefinition(name)` finds `<terrain>/source/base` when the ground terrain was removed | ✅ a real terrain folder wins; else the kept ground whose `source.base.name` (or `source.base.of`, or its own `name`) is `name`. ONE rule, `TerrainCatalog.KeptGroundOf`, also used by `TerrainBake` | `TerrainCatalog.cs` |
| `List()` | ⚠ kept grounds are NOT listed (they were listed before, by accident) — reachable by name only | — |
| E-3 — a terrain with no source: *Save terrain* over the terrain the source was built on | ✅ safe. On such a save the kept ground is renamed `<T>-ground` (`-ground-2`… if taken): `source/base/terrain.json` `name`, `terrain.json` `source.base.name` (+ `source.base.of` = T), the kept source's header `terrainName` and its `TerrainObjects` keys. Reopening the source loads the BARE ground, so the live overlay adds the blocks once (not twice). A re-save from the kept source or from the original scenario gives the same terrain | `TerrainBake.SaveAs` |

⇒ you can un-grey *Edit terrain…* for a terrain whose ground is gone, and for a terrain with no source (E-3: open a new source over it — `SaveAs` then
keeps the old terrain as `<T>-ground`). Rails: `TerrainKeptGroundTests`, `TerrainLoadStepTests.ASourceWhoseGroundWasDeleted_StillLoads_ThroughTheKeptCopy`.
