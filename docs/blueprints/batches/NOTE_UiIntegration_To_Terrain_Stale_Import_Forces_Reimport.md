# Note: a stale imported terrain in the repository forces a full re-import on EVERY load (CE-11061)

| | |
|---|---|
| **From** | the HROT-integration session (`unreal-ig`), 2026-11-26 |
| **For** | the `terrain` lane |
| **Found by** | `unreal-ig`: [NOTE_UnrealIg_To_UiIntegration_Stechovice_Reimports_Every_Load.md](NOTE_UnrealIg_To_UiIntegration_Stechovice_Reimports_Every_Load.md) |
| **User** | *"yes file it but write a note to terrain lane, do not do it yourself"* |
| **Id** | CE-11061 (the `unreal-ig` integration block; the work is yours) |

## What happened (measured by unreal-ig, on Windows, HROT `ui` `ebfff3644`)

Every live load of `showcases/stechovice-battle` ran the full Stechovice import TWICE and re-baked the whole navmesh (59 834 tiles, "0 from the cache"):
~7 min instead of ~20 s. The input had not changed (its manifest sha matched both outputs).

| | |
|---|---|
| the trigger | a stale copy at `<repo>/assets/terrain/Stechovice` (git-ignored, `.gitignore:77`), imported 2026-11-23 — BEFORE `importerRevision` was recorded: its `terrain.json` has the input sha but no revision |
| why it loops | `SourceImport.Current` (`Hrot.Terrain.Import/SourceImport.cs:27`, CE-5056) needs the sha AND `importerRevision == TerrainImporter.Revision` (2); an absent revision reads as 1 ⇒ never current ⇒ re-import (into the node's folder and the shared root) — and the next load resolves the repository copy FIRST again (`TerrainCatalog`, the first-use import order of CE-11037 / CE-11039: the dev checkout's `assets/terrain`, else the shared root) ⇒ the same stale copy, the same verdict, every load |
| confirmed | moving the stale folder aside: the next load had no `[TerrainImport]` line, the navmesh 59 834 tiles from the cache in 6.9 s, the scenario up ~73 s after start |

## The defect (yours to decide the fix)

A terrain that is found but NOT current is neither replaced nor reported — the import writes somewhere else and the stale one keeps winning. Any machine with an
import older than an importer revision bump hits it silently. Leans (pick and argue):

1. ⭐ re-import INTO the folder that was found stale (so it becomes current and wins next time), or
2. prefer a CURRENT copy over a stale one wherever it is, and log the stale one by path once (*"stale import at … (revision 1 < 2) — ignored"*).

Rail: a stale `terrain.json` (sha matches, no revision) in the first-searched root ⇒ ONE import, and the second load imports nothing.
