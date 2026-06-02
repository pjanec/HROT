# BATCH-07: StrideNavmeshBaker + DotRecastNavmeshProvider (Phase 2 start)
**Tasks:** STR-P2-T1, STR-P2-T2   **Phase:** P2 (Navigation)   **Est:** ~10–12h
**Dependencies:** Phase 0/1 complete. `Hrot.Stride.Core` already references `DotRecast.Recast`/`DotRecast.Detour`/`DotRecast.Detour.Crowd` 2026.1.3 (BATCH-01).

Goal: real DotRecast navigation. (T1) `StrideNavmeshBaker` bakes per-`NavLayerMask` DotRecast navmeshes from scene collision triangles; (T2) `DotRecastNavmeshProvider : INavmeshProvider` wraps the baked mesh as the managed singleton — a **drop-in replacement for `FakeNavmeshProvider`**. **Unlike P0/P1, DotRecast is pure managed .NET — so both tasks are fully validatable headlessly with real geometry** (bake a synthetic triangle soup → query/plan over it). Only the *scene-triangle extraction* from the live Stride `MainScene` needs the engine, so it goes behind a small seam.

No Corrective Task 0 (BATCH-06 approved). This batch begins retiring the "everything is seam-tested" risk: the navmesh logic gets **real** test coverage.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §10.1 (navmesh mode — the spec), §4 (coordinate seam).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P2-T1, STR-P2-T2.
4. `.dev/navig-2/Navigation_Design_v2_0.md` §8.1 (the `INavmeshProvider` contract rationale) — context for the provider semantics.
5. `reviews/BATCH-06-REVIEW.md` + `DEBT-TRACKER.md`.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **The contract to implement** = `INavmeshProvider` ([INavmeshProvider.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/INavmeshProvider.cs)): `IsWalkable`, `ProjectToNavmesh`, `SampleNavmeshPoints`, `PathExists`, `PathCost`, `QueryVersion`, `PlanPath` (writes `NavWaypoint`s). `[ComponentId(GlobalComponentIds.INavmeshProvider)]` → it's registered as a managed ECS singleton.
- **⚠ COORDINATE CONVENTION — the #1 risk.** `INavmeshProvider`'s doc says coordinates are 3-D world space with the 2D flat-earth mapping `(x_east, 0, y_north)` → i.e. **Y-up, Z-north** (NOT FDP's X-east/Y-north/Z-up). `DotRecastNavmeshProvider` **must use the exact same convention as `FakeNavmeshProvider`** to be a true drop-in. **[VERIFY]** the precise in/out convention by reading [FakeNavmeshProvider.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs) and its tests + a real caller (e.g. `NavmeshReachableTest`/EQS). Whatever the fake does, match it. Reconcile this with design §10.1's "baked geometry in FDP coordinates" — resolve the actual convention from source and **document it explicitly** (this likely means: the baker swizzles scene geometry into the navmesh-query space the callers use; that space may differ from raw FDP — pin it down with a test against a known feature).
- **Drop-in target** = `FakeNavmeshProvider` ([FakeNavmeshProvider.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs)) and `NavigationFakesModule` ([NavigationFakesModule.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavigationFakesModule.cs)). The singleton-registration pattern is shown by `EngineBackedNavigationModuleTests.RegisterProviders_SetsINavmeshProviderSingleton` — [VERIFY] the exact singleton set call (`World.SetSingletonManaged<INavmeshProvider>(...)` or similar).
- **`NavLayerMask`** enum: [NavLayerMask.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/NavLayerMask.cs) — [VERIFY] its members; bake one navmesh per layer with per-layer params (Infantry: agent radius 0.3 m / max slope 60°; Vehicle: radius 1.5 m / slope 20° / step 0.1 m).
- **DotRecast API [VERIFY]** (2026.1.3): the build pipeline (`RcConfig`/`RcBuilder`/`RcBuilderConfig` → heightfield → compact → contours → poly mesh → detail mesh → `DtNavMeshCreateParams` → `DtNavMesh`) and the query side (`DtNavMeshQuery`, `FindNearestPoly`, `FindPath`, `FindStraightPath`, filters). Confirm the exact namespaces/types in the installed package; do not guess.
- `FdpStrideTransform` (BATCH-01) for any Stride↔FDP conversion in the baker.

**Complete tasks in sequence (T1 → T2); do NOT start T2 until T1 is implemented, tested, and ALL tests (incl. prior batches') pass.** Work autonomously. Only stop on a genuine breaking design flaw or unrecoverable blocker (e.g. the DotRecast build API differs irreconcilably from the design — document it).

---

## Task 1: `StrideNavmeshBaker` (STR-P2-T1)
**Files:** `Stride/Hrot.Stride.Core/StrideNavmeshBaker.cs` (NEW) + a scene-geometry seam. Spec: design §10.1.
The baker turns a **triangle soup** (vertices + indices, in the navmesh-query coordinate space — apply the swizzle from the convention you pinned down) into a per-`NavLayerMask` DotRecast `DtNavMesh`, using per-layer `RcConfig` params. The *source* of the triangles is the live Stride `MainScene`'s collision/terrain geometry (`StaticColliderComponent`s) — extracting that needs a running game, so put it behind a seam: define `ISceneGeometrySource` (e.g. `bool TryGetTriangles(out float[] verts, out int[] indices)`), with the concrete Stride implementation (walks `MainScene` static colliders, swizzles via `FdpStrideTransform`) in `HrotStrideApp.Game` (it can stay thin/untested-headlessly), and the baker tested headlessly against a synthetic soup.

**Tests required** (headless, real DotRecast over a synthetic triangle soup):
- Bake a known flat ground quad (e.g. 20×20 m) → a **non-empty** `DtNavMesh` (assert it has ≥1 tile / ≥1 polygon — a real bake, not a stub).
- Per-layer params applied: bake the same soup for Infantry vs Vehicle and assert the configs differ as specified (e.g. assert the `RcConfig` agent radius/slope used per layer; and/or that a 0.4 m-wide gap is walkable for Infantry (0.3 m) but not Vehicle (1.5 m) — a behavioral consequence of the radius).
- Coordinate fidelity: a triangle at a known FDP/world feature bakes to a navmesh polygon whose query coordinates match the documented convention (assert a point known to be on the ground projects onto the mesh).

## Task 2: `DotRecastNavmeshProvider` (STR-P2-T2)
**File:** `Stride/Hrot.Stride.Core/DotRecastNavmeshProvider.cs` (NEW). Spec: design §10.1.
`DotRecastNavmeshProvider : INavmeshProvider` wrapping the baked `DtNavMesh`(es) + a `DtNavMeshQuery`, implementing the **full** contract over the baked mesh, honoring `layerMask` to select the per-layer mesh. Register it as the `INavmeshProvider` managed singleton (drop-in for the fake). `QueryVersion()` increments on rebake.

**Tests required** (headless, over a real baked mesh from a synthetic soup):
- `IsWalkable` true for a point on the ground quad, false for a point off it / over a hole.
- `ProjectToNavmesh` snaps an above-ground point down onto the mesh (assert the snapped coordinate is on the surface, correct value).
- `PathExists`/`PlanPath` over the baked mesh: a clear path returns waypoints from start to end (assert ≥2 waypoints, first≈from, last≈to); a path blocked by a baked wall/gap returns no path (or routes around it — assert the path length is longer than the straight-line distance when an obstacle is present).
- `PathCost` returns finite for a reachable pair and `float.MaxValue` for an unreachable one.
- `QueryVersion` increments after a rebake.
- **Contract parity:** a small suite mirroring the key `FakeNavmeshProvider` behaviors confirms the DotRecast provider answers the same questions the same way (drop-in). Register it as the singleton and assert `World.GetSingletonManaged<INavmeshProvider>()` returns the DotRecast instance (replacing the fake).

---

## Success Criteria
- [ ] STR-P2-T1: `StrideNavmeshBaker` bakes a non-empty per-layer `DtNavMesh` from a triangle soup with the correct per-layer params; scene extraction is behind `ISceneGeometrySource`; coordinate convention pinned + documented.
- [ ] STR-P2-T2: `DotRecastNavmeshProvider` implements the full `INavmeshProvider` contract over the baked mesh, registered as the managed singleton (drop-in for the fake); path/reachability/projection answered over real geometry.
- [ ] Full test suite green (all prior batches + this); Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-07-REPORT.md`)
Answer: **the exact navmesh-query coordinate convention** you pinned down (from `FakeNavmeshProvider` + a real caller) and how the baker swizzles into it — reconcile with design §10.1 (this is the headline; a wrong convention silently breaks all navigation); the DotRecast 2026.1.3 build + query API you used (real type/method names); the per-layer `RcConfig` params and how the Infantry-vs-Vehicle difference is tested behaviorally; the `ISceneGeometrySource` seam + what the concrete Stride extractor does (and that it's the only headless-untested piece); the singleton registration call; weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
