<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Import_UTM.md — §8 (R-4188), ⭐ §8.2 B-9 / B-12 (R-4196), §8.3–§8.4 as built: what the damage IS and where it lives
  - ../../DESIGN_Map_3D_Mode.md — §6ah the chunked 3-D terrain the breaches and rubble are drawn into; the look is recorded here (R-4195)
  - ../../DESIGN_Terrain_World.md — §10a the 2-D plan
-->

# HANDOFF — coordinator → ui: draw building destruction on the 2-D and 3-D maps (CE-4392)

**For:** ui. **From:** coordinator. **Dispatched at `2011edb28`**. ⭐ Scope FROZEN there. Queue it after the render-under-load handoff you are on.
Merge `coordinator` first, push `chore: started HANDOFF_Coordinator_To_Ui_Destruction_Drawing at <sha>`, and keep a status file
(`STATUS_Ui_Destruction_Drawing.md`).

🔒 User, `2026-11-10`: *"destruction should be replicated from muscle to other nodes so they see it on their 3d maps"* · *"i need the destructions to
happen on muscle role node (other has terrain just for displaying their map, no querying or modification allowed)"* (R-4196) · *"if there is new
stuff … the ui lane is free to design their 3d look with respect to performance"* (R-4195).

## What exists (built by the coordinator, CE-4391 S-1 / S-2)

| piece | where |
|---|---|
| ⭐ the damage arrives on EVERY node: `BuildingDamage` entities, owned by the muscle that decided them, ingressed everywhere else (`SharedTranslatorPack` — every role) | `FDP/Toolkits/Fdp.Toolkits/Terrain/BuildingDamage.cs`, `Hrot.Network.NED/Replication/Map/EntityBuildingDamageTranslators.cs` |
| ⭐ the ONE read: `BuildingDamageTable.Of(view, world)` — null when nothing is damaged; cached per thread, a new instance only when the damage changes (compare the reference to know when to rebuild a mesh) | `Terrain/BuildingDamageTable.cs:148` |
| a wall's breaches: `BreachesOf(panel)` → `WallBreach { Panel, From..To (m along the panel from A), Bottom..Top (absolute Z), Kind: Hole / Passage }` | `:120` |
| a collapse: `Collapsed(building)`; its mound: `Rubble` (one solid `TerrainPrism` per collapsed building: the footprint, ⅓ of its height, material `rubble`); its doors: `DoorDestroyed(doorIndex)` | `:56`, `:64`, `:68` |
| every piece of a building knows it: `TerrainPrism.Building`, `TerrainWalkable.Building` (−1 = none) | `Terrain/TerrainWorld.cs` |
| ⛔ today NOTHING draws it — the damage entity is explicitly not drawn as a unit box (`EntityBodyLayer3D.cs:1712`) | — |

## The items

| # | what | done when |
|---|---|---|
| **D-1** | **3-D: a breach is drawn as an opening in its wall** — that one building's wall mesh rebuilt with the breach as a gap (the way windows / doorways already are), only when the table's instance changes; ⭐ performance first: rebuild only the touched building / chunk, never the terrain | a frame rail on the REAL host (R-4146): a wall before and after a breach, the gap visible from both sides; a rail that an unchanged table rebuilds nothing |
| **D-2** | **3-D: a collapsed building disappears and its mound appears** — the building's walls, slabs, roof and door leaves hidden; the `Rubble` prism drawn with a look you design (R-4195 — record it in Map 3D §6ah, a rendered still) | a frame rail: the house, then the mound |
| **D-3** | **2-D plan: the same** — a breach as a gap in the wall line; a collapsed building drawn as rubble (fill / hatch of your choice) | a frame rail |
| **D-4** | the editor, the Player and every observer show it — they all read the same table | one rail on a non-muscle host (a received `BuildingDamage` entity, no local decision) |

## Fences

- ⛔⛔ **R-4196: draw from the replicated entities (`BuildingDamageTable`), never by changing the display node's `TerrainWorld`.**
- ⚠ **On a muscle / all-in-one node the resident terrain is REBAKED after a passage or a collapse** (S-3, in flight: the derived world gets the
  passage openings and the rubble prism). ⇒ draw the rubble from `BuildingDamageTable.Rubble` ONLY and skip a resident prism whose material is
  `rubble`; a breach already present as a gap in a rebaked wall must not be cut twice (idempotent is fine). Ask the coordinator if the S-3 shape
  makes this awkward — it can still change.
- ⛔ No allocation per frame in the draw path (R-4192: 0 B per steady frame); mesh rebuilds on change only.
- The EXTERNAL visual engine draws its own (D-8) — out of scope.
