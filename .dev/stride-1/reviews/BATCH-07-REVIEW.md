# BATCH-07 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`StrideNavmeshBaker` (per-layer DotRecast bake from a triangle soup, scene extraction seamed) + `DotRecastNavmeshProvider : INavmeshProvider` (drop-in for the fake, registered as the managed singleton). **First phase with genuine engine-backed validation** — all navmesh tests run real DotRecast bake+query over synthetic geometry. Verified: read the provider + tests, ran the suite (Core 153, +37 = 190 green).

## Verification performed
- `DotRecastNavmeshProvider`: real per-layer `DtNavMesh`+`DtNavMeshQuery`+`DtQueryDefaultFilter`; `FindNearestPoly`/`FindPath`/`FindStraightPath`; `layerMask` filtering; `QueryVersion` increments on `Rebake`. Full `INavmeshProvider` contract, no stubs.
- **Coordinate convention pinned correctly:** navmesh-query space = Stride world (X=East, Y=Up, Z=North), confirmed from `FakeNavmeshProvider.PointInPolygon(pos.X, pos.Z)` + the `INavmeshProvider` `(x,0,y_north)` doc. The provider does no internal swizzle (callers pass navmesh-space, same as the fake) — a true drop-in. Reconciled with §10.1 in the report.
- Tests are real behavior: `PlanPath` asserts ≥2 waypoints with first≈start(-8,0,0)/last≈end(8,0,0); `PathCost` finite-for-reachable / exact `float.MaxValue` for unreachable; `QueryVersion` v2>v1 after rebake; `SlopeObstacle_InfantryBakesRampPolys_VehicleDoesNot` proves the per-layer slope difference behaviorally; singleton via `GetSingletonManaged<INavmeshProvider>()`.
- The coder found two real DotRecast bugs under validation (CCW winding `0,2,1`; `polyFlags=1` so `DtQueryDefaultFilter` includes polys) — these only surface with real bake+query, confirming the tests aren't hollow.
- Ran the suite myself; counts match.

## Issues Found
No blocking issues. The only headless-untested piece is `StrideSceneGeometrySource` (the concrete MainScene triangle extractor) — its mesh-walk body is a stub pending GPU bring-up (folds into STR-D11). The baker logic itself is fully tested against synthetic soups.

## Verdict
APPROVED. Proceed to BATCH-08: STR-P2-T3 (`DotRecastDtCrowdProvider`), STR-P2-T4 (`CrowdAgentUpdateSystem` velocity-only refactor — resolves STR-D12), STR-P2-T5 (road-graph mode + `Auto` selection).

## Commit Message
```
feat(stride): StrideNavmeshBaker + DotRecastNavmeshProvider — real DotRecast navigation (BATCH-07)

Completes STR-P2-T1, STR-P2-T2
- StrideNavmeshBaker: per-NavLayerMask DtNavMesh bake from a triangle soup (DotRecast 2026.1.3),
  per-layer RcConfig (Infantry 0.3m/60deg, Vehicle 1.5m/20deg/0.1m step); scene-triangle extraction
  behind ISceneGeometrySource (concrete Stride extractor stubbed pending GPU bring-up)
- DotRecastNavmeshProvider: full INavmeshProvider over baked meshes (IsWalkable/ProjectToNavmesh/
  SampleNavmeshPoints/PathExists/PathCost/PlanPath/QueryVersion); navmesh-query space = Stride
  (X=East,Y=Up,Z=North), drop-in for FakeNavmeshProvider; registered as managed singleton
- Fixed two real DotRecast bake bugs found under validation: CCW triangle winding; polyFlags=1
Tests: 190 (153 Core incl. 37 new real-DotRecast bake+query, 4 Animation, 33 Game).
```
