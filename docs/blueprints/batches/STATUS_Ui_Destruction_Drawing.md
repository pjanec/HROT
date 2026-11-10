<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the table "Items", then "Gates"
related-designs:
  - docs/DESIGN_Map_3D_Mode.md — §6aj OWNS the drawing (UML, decisions X-1…X-7, as-built)
  - docs/DESIGN_Terrain_Import_UTM.md — §8 OWNS what the damage is and where it is decided (R-4196)
-->

# STATUS — building destruction drawn on the maps (ui; CE-4392)

Handoff [HANDOFF_Coordinator_To_Ui_Destruction_Drawing.md](HANDOFF_Coordinator_To_Ui_Destruction_Drawing.md), dispatched at `2011edb28`; started at `05862bbce`
(coordinator merged into ui at `df49e6920`). Taken over from the other ui session at the user's ask. Cross-lane edits, stated in the commits: `Fdp.Toolkits` terrain / world
(the coordinator's — `WithGaps` made internal, the render seam's opaque damage), `Fdp.Presentation` Vis3D.

| # | state | what |
|---|---|---|
| **D-1** breach in 3-D | ✅ | the breached wall re-cut through the parser's own expander (holes and passages); only the touched chunks' objects rebuilt, off the frame thread; + soot (X-7) |
| **D-2** collapse in 3-D | ✅ | the building's pieces gone; a rubble heap with debris (X-2) |
| **D-3** 2-D plan | ✅ | the damaged buildings hidden in the plan and drawn again, damaged, in an overlay — a breach is a gap (cut at 1 m), a collapse a rubble fill; soot at the gap |
| **D-4** every host | ✅ | the render source every host shares (`WorldQuery.RenderGeometryOf` → `ForView`) carries the damage; rails on a view that only RECEIVED the entity |

## Gates

| gate | result |
|---|---|
| `dotnet test Fdp.Toolkits.Tests --no-build --filter TerrainChunk\|Building` | 95 / 0 (incl. 5 `TerrainChunkDamageTests`) |
| `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter TheBuildingDamage` | 4 / 0 |
| `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build` (full) | run 1: 2 037 / 4 failed — the 3 pre-existing (`CE1039`, `CE1380`, `CE1227`, measured at `72aaefa03`) + ⚠ `TheProductionMap_DrawsTheBreachAndTheRubble_OnADisplayNode` once; run 2: that rail green. ⚠ It passes alone and beside every Map2D / Map3D rail — the cause is not identified (order-dependent, seen once in two full runs) |
| red-proofs (each compiles) | `Keeps` always true ⇒ 3 `TerrainChunkDamageTests` red · `SetDamage` disabled ⇒ both 3-D frame rails red · `ApplyDamage` never reached ⇒ the 2-D frame rail red · the scorch call dropped ⇒ the soot rail red |
