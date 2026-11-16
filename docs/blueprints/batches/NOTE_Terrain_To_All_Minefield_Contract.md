<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: the whole file — the numbers every lane binds to
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — the design: §3a ids, §6.1 the contract (this note gives the numbers it left to terrain)
  - HANDOFF_Coordinator_To_Terrain_Minefields.md — the dispatch (§1 asks for this note first)
-->

# NOTE — terrain → behaviors, ui (and everyone): the minefield contract, in numbers

From the `terrain` lane, the FIRST commit of CE-4624 (minefields). Every type below **compiles today**; behaviors and ui can merge `terrain` and
bind. The behaviour of the read API arrives slice by slice (MS-T1 … MS-T4) — the signatures do not change.

## Ids — ⛔ permanent

| what | value | where |
|---|---|---|
| component `Minefield` | **318** | `GlobalComponentIds.Minefield` |
| component `MinefieldStrikes` | **319** | `GlobalComponentIds.MinefieldStrikes` |
| component `MinefieldKnowledge` | **323** | `GlobalComponentIds.MinefieldKnowledge` |
| DDS `dtMinefield` · `dtMinefieldStrikes` · `dtMinefieldKnowledge` | **191 · 192 · 193** | `EDescriptorType` (NED); `Minefields.*DescriptorOrdinal` (toolkit) |
| events `MineStruck` · `MinefieldCellsSwept` · `MinefieldMarked` · `LaneCleared` | **5105 · 5106 · 5107 · 5108** | `TerrainEventIds` |
| TKB the field | **8830** | `TkbEntityTypes.Minefield`, `Minefields.TkbType` |
| TKB the knowledge record | **7206** | `MinefieldKnowledge.TkbType` |
| TKB the clearer | **116** | `TkbEntityTypes.Engineer_MineClearer`, `Minefields.ClearerTkbType` |
| munitions AP · AT · line charge | **6051 · 6052 · 6053** | `MunitionTkbCatalog.TkbMineAntiPersonnel …` |
| navmesh area | **5** (composites in MS-T3) | `Minefields.NavArea` |
| danger kind | `DangerAreaKind.Minefield = 8`; `DangerAreaKinds` is now **`ushort`**, `DangerAreaKinds.Minefield = 1 << 8` | `Squad/DangerArea` |

⚠ **Found on the way, fixed here:** `PlacementGroup` (terrain, CE-5008) and the animals' `Fear` (coordinator) both held component id **500** —
`PlacementGroup` moved to **309** (terrain block). 500–511 are full (animals 500–507, test-local types 508–511); the terrain block 300–319 had the room.

## Types — `Fdp.Toolkit.Terrain` (`FDP/Toolkits/Fdp.Toolkits/Terrain/Minefields/`)

| type | shape |
|---|---|
| `Minefield` (unmanaged) | `MineType Type` (1 AP, 2 AT, 3 mixed) · `byte AntiHandling` · `byte LaneCount` · `float SpacingM` · `float OrientationDeg` · `uint Seed` · `ushort KnownAtStartSides` (bit = ForceId) · lanes ≤ 8 via `Lane(i)` / `SetLane(i, MinefieldLane)` — `MinefieldLane(A, B, Width, ClearedBySidePlus1, KnownToSides)`. ⭐ The OUTLINE is the entity's ONE `EditablePolyline` |
| `MinefieldStrikes` (unmanaged, NoScenario) | `Guid Field` · `ushort Chunk` · `byte Count` · ≤ 96 × (`short Row`, `short Col`, `MineGone How` 1 struck / 2 cleared) |
| `PlaceKnowledgeHeader` | `SubjectKind` · `SidePlus1` · `PlaceKnowledgeKind Kind` (1 Minefield) · `State` · `KnowledgeSource Source` · `Marked` · `Chunk` · `Guid Subject` · `int ObjectIndex` · `SimInstant LearnedAt` |
| `MinefieldKnowledge` (unmanaged, NoScenario) | `Header` + a 2-bit grid 60 × 64: `Get(col, row)` / `Set` / `Raise` → `MineCellKnowledge` (0 unknown · 1 suspected · 2 known-clear · 3 known-mined) |
| `MineStruck` | `Entity Field, short Row, short Col, Entity By, float X, Y, SimInstant At` |
| `MinefieldCellsSwept` | `Entity Field, byte SidePlus1, byte Count, ≤ 16 × (Col, Row, Found), Entity By` — raised by the recon brain through the batch buffer |
| `MinefieldMarked` | `Entity Field, byte SidePlus1, Entity By` |
| `LaneCleared` | `Entity Field, byte Lane, byte SidePlus1` |

## Reads

| call | answers |
|---|---|
| `MinePattern.MinesIn(in Minefield, Vector2 anchor, IReadOnlyList<Vector2> outline, List<Mine> into)` | the derived mines (`Mine(Row, Col, At, Type)`), lanes excluded — ⭐ REAL now; the lattice is anchored at the field entity's position (a (row, col) survives an outline edit) |
| `MinePattern.At / LatticeUv / TypeAt / ToField / ToWorld / Inside / InLane` | the pattern's pieces (a mixed field: AP on even rows, AT on odd) |
| `MinefieldKnowledgeOps.TryOutline(world, field, into, out anchor)` | the outline in world points (the toolkit reads the `EditablePolyline` through `IAreaOutline`) |
| `MinefieldKnowledgeOps.Grid(in Minefield, anchor, outline)` → `KnowledgeGrid`; `CellOf(grid, point, out col, out row)`; `CentreOf` | where the knowledge cells lie (≥ 10 m, coarsened to fit 60 × 64) — ⭐ REAL now |
| `PlaceKnowledgeTable.Of(view).Knows(side, field, col, row)` · `.Marked(side, field)` | ⚠ STUB until MS-T2: answers Unknown / false |

## ⭐ Added since the first commit (`2026-11-16`)

| what | value / shape | for |
|---|---|---|
| component `MinefieldLanes` · `MineClearing` | **324 · 325**; DDS **194 · 195**; TKB **7208** (the lanes record) | MS-T4 |
| `MineClearing` (unmanaged, Brain group) | `byte Active` · `MineClearingTool Tool` (1 plough, 2 flail, 3 line charge) · `float WidthM` · `Vector3 LineTo` — ⭐ **the `ClearLane` brain sets it** (the bulldozer's `BladeCut` pattern); the Muscle clears | behaviors (MS-B3), ui (the kit) |
| `LaneCleared` | + `Ax, Ay, Bx, By, Width` (a runtime lane; `Lane = 255`) | the lanes record |
| `MinefieldLanes` / `MinefieldLanesSystem.LanesOf(view, fieldGuid, into)` | the lanes a node cleared (segment, width, the clearing side + 1) | ui (draw lanes), the navmesh |
| ⭐ **the side of a route** | rides in the route LAYER MASK, bits 14…18 = side + 1 (`NavLayerSelection.SideBits / SidePlus1Of`); `NavLayerSelection.For(repo, entity, …)` adds it from `EntityInfo.ForceId` — **every route request of an entity already carries it**; no pad byte (lane decision R-5047) | behaviors: nothing to do |
| ⭐ `PlaceKnowledgeTable` | REAL now: `DoorStates.Of(view, world).Knowledge` (or `PlaceKnowledgeTable.Of(view)`); `Knows(side, fieldEntity, col, row)`, `Marked(side, field)`, `KnowsAny(side, field)` | behaviors (recon), ui (the gizmo) |
| ⭐ the `Minefield` danger area | `DangerAlongRouteSolve` reports ONE area per field the unit's side knows on its route: `Center` = the entry, `NearSideHandle` = the last safe point (halt there). Ask with `DangerAreaKinds.Minefield` | behaviors (MS-B4) |
| writers | `PlaceKnowledgeSystem` on the SimHost AND the CGF node; raise `MinefieldCellsSwept` / `MinefieldMarked` from a brain through the batch buffer | behaviors (MS-B2) |

## What changes when

MS-T1 the field type, trigger, strikes, bursts, armour bottom · MS-T2 the knowledge writer + the real `PlaceKnowledgeTable` (on `DoorStates`) +
translators + save · MS-T3 the side in routes, the danger kind's solve · MS-T4 lanes and clearing. Progress: `STATUS_Terrain_Minefields.md`.
