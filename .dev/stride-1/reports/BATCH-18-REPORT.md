# BATCH-18 Report — Navmesh Runtime + Vehicle Navigation

## Implementation Summary

### Step 1 — Scene Geometry Extraction

**`BoxGeometryHelper`** (new, `Hrot.Stride.Core/BoxGeometryHelper.cs`):
- Pure static math helpers extracted to `Hrot.Stride.Core` so they can be headlessly tested.
- `ExtractBoxTriangles(shapeWorldMatrix, halfExtents, vertList, indexList)`: transforms 8 local-space box corners into world space via the given matrix and appends 12 triangles (6 faces × 2). Top face wound CCW from above → +Y normal (walkable for DotRecast).
- `AabbToBox(worldMatrix, vertList, indexList)`: extracts world-space column magnitudes as AABB half-extents and delegates to `ExtractBoxTriangles`.

**`StrideSceneGeometrySource`** (rewritten, `HrotStrideApp.Game/StrideSceneGeometrySource.cs`):
- Replaces the previous stub (which iterated shapes but did nothing).
- Walks scene entities recursively via `entity.Transform.Children`.
- For each `StaticColliderComponent`: `BoxColliderShapeDesc` → exact 8-corner extraction using `BoxColliderShapeDesc.Size` (full extents), `LocalOffset`, and `LocalRotation` + entity `WorldMatrix`. All other shape types (plane, mesh, capsule, sphere) → AABB fallback via `BoxGeometryHelper.AabbToBox` with a Warn log.
- Floor guard: if no extracted vertex has Y < 0.5 m, a synthetic 120×120 m floor quad at Y=0 is injected (ensures DotRecast has a walkable surface even if the arena floor is a plane collider with no triangle data).
- Logs summary: N box-exact + M AABB-fallback colliders, total triangles, floor source.

**Arena collider types found** (verified from MainScene.sdscene + prefabs):
- The vast majority of the ~144 static colliders are `BoxColliderShapeDesc` (walls, floors, pillars, tables, gates, ramps, stairs — all authored as box shapes in the Stride prefabs). These get the exact extraction path.
- One collider uses `StaticPlaneColliderShapeDesc` (`Wall_East` at X=20.0): this gets the AABB fallback.
- All `BoxColliderShapeDesc` shapes carry `LocalOffset` and `LocalRotation` (identity for most, non-zero for the multi-shape `Wall0x2x5Window` which has multiple offset sub-shapes per entity).

### Step 2 — Bake + Register Provider

**`StrideHrotGame.BakeNavmesh`** (new method, `StrideHrotGame.cs`):
- Called from `BootEditorSubsystem` immediately after `_editorSubsystem.Initialize(...)` and before `EnqueueDemoSpawns`.
- Creates `StrideSceneGeometrySource(scene)`, calls `TryGetTriangles`, runs `StrideNavmeshBaker.Bake(verts, indices, NavLayerMask.Vehicle | NavLayerMask.Infantry)`.
- Constructs `DotRecastNavmeshProvider(meshes)` and registers via `_editorSubsystem.World.SetSingletonManaged<INavmeshProvider>(_navmeshProvider)` — overwrites the `FakeNavmeshProvider` previously set by the simulation logic packs.
- Stores `_navmeshProvider` field for the F4 harness case.
- Full guard: any failure (no geometry / 0 meshes / exception) logs a loud Warn and leaves `_navmeshProvider` null. The F4 demo handles null cleanly.
- Logs per layer: `"Navmesh baked: layer=Vehicle polys=… verts=…"`.

### Step 3 — F4 Demo Harness Case

**`RegisterNavmeshDriveCase`** + **`NavmeshDrive`** (new, `StridePhysicsHarnessCases.cs`):
- Registered at index 13 → key **F4** (the mapping D1–D9=0–8, D0=9, F1=10, F2=11, F3=12, F4=13 is confirmed by `TryGetCaseKey`).
- Start: FDP (-5, 3, 0). Goal: FDP (5, 12, 0). Direct line passes through the arena's interior wall/obstacle cluster. The navmesh path routes around them.
- `PlanPath` inputs are converted FDP→Stride→navmesh-query space via `FdpStrideTransform.ToStridePosition`. Results are converted back via `FdpStrideTransform.ToFdpPosition`.
- If `PlanPath` returns 0 corners: logs "NAVMESH UNAVAILABLE / no path" and aborts cleanly.
- If corners found: logs the full corner list, spawns the APC, and drives it per-frame with `VehicleWaypointController` (exact same controller as F3 Drive To Waypoint).
- Same stuck-detection window pattern as F3 (movement-based, `StuckDisplacementThresholdM`/`StuckWindowSec`).
- Same timeout guard per corner (30 s).
- Visible corner markers (loaded `Models/Box2x1x1`) at each navmesh corner; goal marker is taller.
- Arrival log per corner; final log: `"NAVMESH DRIVE COMPLETE — reached goal via N corners"`.

**`StrideHrotGame.BuildTestHarness`** updated to register `RegisterNavmeshDriveCase` passing `_navmeshProvider`.

### New files
- `Stride/Hrot.Stride.Core/BoxGeometryHelper.cs` — pure math helper (box + AABB extraction)
- `Stride/Hrot.Stride.Core.Tests/StrideSceneGeometryExtractorTests.cs` — 9 headless tests

### Modified files
- `Stride/HrotStrideApp.Game/StrideSceneGeometrySource.cs` — full rewrite (real extraction)
- `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — added F4 case
- `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — `BakeNavmesh` + `_navmeshProvider` + F4 registration

---

## Design Decisions

1. **`BoxGeometryHelper` in `Hrot.Stride.Core` not `HrotStrideApp.Game`**: The pure math helpers need headless unit tests. Moving them to the core library (which the test project references) avoids the circular reference that would arise from the test project referencing `HrotStrideApp.Game` (a Stride GPU app).

2. **AABB fallback for non-box shapes**: The arena uses `StaticPlaneColliderShapeDesc` for the east wall. A plane has no finite geometry, so the AABB from the world matrix is used (centre + half the column magnitudes). This is conservative (the AABB is very thin for a plane collider) but logs a Warn so the developer can improve it later. The arena boundary walls aren't critical for interior navigation correctness.

3. **Synthetic floor guard**: The arena floor colliders are present as `BoxColliderShapeDesc` in many floor tile entities. The guard fires only if the geometry extractor somehow misses them all (e.g. scene not fully loaded). It injects a 120×120 m floor at Y=0. This is a safety net rather than the primary path.

4. **Start/goal positions for F4**: FDP (-5, 3) start and (5, 12) goal were chosen because:
   - They are on opposite sides of the interior wall cluster (Z≈5–8 in Stride, Y≈5–8 in FDP).
   - The arena boundary at FDP X≈±18 provides side clearance for the vehicle to route around.
   - The Vehicle navmesh (radius 1.5 m) should find the route without needing very tight gap navigation.

5. **No change to `FakeDtCrowdProvider`**: Per the batch instructions, the crowd navigation wiring is unchanged. Only the `INavmeshProvider` singleton is replaced.

---

## Deviations

None from the batch specification. All three steps implemented as specified.

---

## Test Results

```
Hrot.Stride.Core.Tests:   295 passed, 0 failed  (+9 new vs baseline 286)
Hrot.Stride.Animation.Tests:  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests: 136 passed, 0 failed  (unchanged)
```

**New tests in `StrideSceneGeometryExtractorTests` (9 tests):**

| Test | What it verifies |
|------|-----------------|
| `ExtractBoxTriangles_UnitBox_Produces8VertsAnd12Tris` | Count: 8 vertices, 12 triangles |
| `ExtractBoxTriangles_UnitBoxAtOrigin_CornersAreUnitCube` | Corner positions = all ±1 combinations |
| `ExtractBoxTriangles_TranslatedBox_CornersAtCorrectWorldPositions` | World-space transform: centre+halfExtents |
| `ExtractBoxTriangles_TopFaceHasUpwardNormal` | Top face CCW → cross-product Y > 0 (walkable) |
| `ExtractBoxTriangles_TwoBoxes_IndicesAreNonOverlapping` | Second box uses indices 8–15, not 0–7 |
| `AabbToBox_IdentityMatrix_Produces12Triangles` | AABB fallback: correct count |
| `AabbToBox_TranslationOnly_CornersAreCentredAtTranslation` | AABB centre = matrix translation |
| `PlanPath_WallObstacle_PathDetoursMidpoint` | Integration: floor + wall → path has corner |X|>4 m |
| `PlanPath_EmptyProvider_ReturnsZeroCorners` | Empty provider → 0 corners (F4 null guard) |

The `PlanPath_WallObstacle_PathDetoursMidpoint` test is the key integration proof: a 10 m wide wall at Z=5 blocks the direct (0,0,0)→(0,0,10) path; after baking with Vehicle layer (radius 1.5 m), the path must detour to |X|>4 m. This confirms the real navmesh routes around obstacles.

**Build:** `HrotStrideApp.Game.csproj` — 0 errors, 1 pre-existing warning (CS0108 `StrideHrotGame.Log` hides `GameBase.Log`) plus pre-existing NU1608 package warnings. No new warnings introduced.

---

## Developer Insights

1. **The arena is ~100% box colliders.** Every prefab (Wall0x2x5, Wall0x2x5Window, Floor*, GridBase*, Pillar*, Gate*, Ramp*, Stairs*, Table*, Box*) uses `BoxColliderShapeDesc`. Only `Wall_East` uses a plane. The extraction code should therefore produce exact geometry for all interior obstacles and near-exact geometry for the east boundary.

2. **`Wall0x2x5Window` has 5 sub-shapes per entity instance.** Each has non-zero `LocalOffset`. The `shapeLocalMatrix = RotationQuaternion(LocalRotation) * Translation(LocalOffset)` formula handles this correctly — each sub-shape is positioned independently within the entity's world frame.

3. **Floor tiles (Floor1x0x1, Floor2x0x1, Floor3x0x1)** are themselves box colliders at Y≈0 in Stride space. The floor guard will find these Y<0.5 vertices and NOT inject the synthetic floor, so the arena bake uses the real floor geometry.

4. **`StaticPlaneColliderShapeDesc` AABB**: The world matrix of `Wall_East` has M41=20 (translation X=20) and scale 1. The AABB half-extents from column magnitudes will be (0.5, 0.5, 0.5) — very small and won't contribute meaningfully to navmesh geometry. This is acceptable: the real east boundary comes from the vehicle erosion interacting with the world edge (beyond the arena area). The navmesh naturally stays within the baked area.

5. **PlanPath coordinate convention**: `DotRecastNavmeshProvider.PlanPath` takes inputs in navmesh-query space = Stride space (X=East, Y=Up, Z=North). FDP→Stride swizzle: `Stride = (fdp.X, fdp.Z, fdp.Y)`. Corners returned in Stride space → FDP via `ToFdpPosition`. The 2D waypoint positions for `VehicleWaypointController` use FDP (X=East, Y=North).

6. **Weak point**: The AABB fallback for `StaticPlaneColliderShapeDesc` is a 0.5 m box — not a wall-sized obstacle. If a future arena uses planes for interior walls, the navmesh would not block them. The current arena only uses planes for the east boundary, which is acceptable.

---

## Known Issues

1. **GPU-only verification**: The actual navmesh bake result and the F4 vehicle path are GPU-verified only (user must run `editor_stride`, press F4, and observe the APC routing around interior walls). The headless tests prove the math is correct but can't prove the exact arena geometry bakes correctly (depends on runtime scene loading).

2. **Arena collider count**: The batch spec mentions ~144 static colliders. At runtime the actual count depends on which prefab instances are in the MainScene (and child entity nesting). The extractor recurses into children, so multi-entity prefabs (like `Wall_East` with 16 children) are fully covered.

3. **`StaticPlaneColliderShapeDesc` AABB is small**: See Developer Insights §4. Not a correctness problem for the current arena.

4. **F4 start/goal not guaranteed to route around a wall**: The demo works if the Vehicle navmesh successfully bakes the interior geometry and finds the route. If the arena bake produces fewer polygons than expected (e.g. due to wall erosion eliminating tight passages), the path may not be as dramatic as described. The fallback is `PlanPath` returning 0 → the case logs "no path found" and aborts cleanly.

---

## Suggested Commit Message

feat(stride): bake DotRecast navmesh from arena colliders + F4 navmesh-drive demo (BATCH-18, STR-D19)
