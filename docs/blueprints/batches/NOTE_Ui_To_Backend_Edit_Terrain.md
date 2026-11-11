<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the whole file (one missing call)
related-designs:
  - docs/DESIGN_Terrain_Authoring.md — §14 E-1/E-2 own Edit terrain; §15a U-5 the ui half
  - docs/blueprints/batches/STATUS_Ui_Terrain_Authoring.md — the ui lane's status
-->
# NOTE ui → backend — Edit terrain… needs the catalogue to resolve `<terrain>/source/base`

**What the ui lane built (no backend change):** *Edit terrain…* (the terrain picker's right-click, `File ▸ Edit Terrain…`) copies `<terrain>/source/scenario` to the NAS `opened/terrain/<terrain>` (the generic `opened/<area>/<key>` area — `ContentLayout.OpenedCopyName`) and opens it through the scenario door by NAME (the door has no folder open: `ContentLayout.ResolveScenarioDirectory` is the one resolver and knows saved · `recipes/scenarios` · `opened/` · `macros`). The title says `TERRAIN SOURCE · <terrain>`; *Save terrain* bakes the staged copy back over the terrain (`TerrainBake.SaveAs`).

**What is missing (the only thing):** the staged source's header names the ground it was built on (`terrainName` = `source.base.name`, e.g. `riverside-3-ground`). The editor loads that ground through `TerrainCatalog`. While the ground is still in the catalogue it works; **when the ground terrain has been removed** the source cannot open, although the terrain keeps a copy of it in `<terrain>/source/base` (E-1's reason for keeping it).

**The exact call:** `TerrainCatalog.ResolveDefinition(name)` (and `List()` / `ForNode`) should, for a name that has no terrain folder of its own, find `<other terrain>/source/base/terrain.json` whose `source.base.name` (or its own `name`) equals `name` — the same fallback `TerrainBake.SaveAs` already has in its private `Resolve` (the kept ground "after the original is gone"). Until then the ui greys *Edit terrain…* for such a terrain with: *"Its ground '<base>' is no longer in the catalogue … (backend — see NOTE_Ui_To_Backend_Edit_Terrain)"*.

**Also not built (design, not backend):** E-3 — *Edit terrain…* on a terrain with NO source (an import, an older terrain) makes a source over it; it is greyed with that reason. It needs `SaveAs` to accept the terrain it overwrites as its own base (today the base would be replaced while it is read).
