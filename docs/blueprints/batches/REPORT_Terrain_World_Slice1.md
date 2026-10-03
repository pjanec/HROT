<!--STATUS
state: LIVE
updated: 2026-10-03
current-answer: whole file — the as-built report of terrain-world slice 1 (steps 0–3).
stale-below: nothing
related-designs:
  - docs/DESIGN_Terrain_World.md — the owning design; its §7 rows and §8 carry the as-built notes this report points at.
-->
# REPORT — terrain world, slice 1 *(backend lane, `2026-10-03`)*

Design: [`DESIGN_Terrain_World.md`](../../DESIGN_Terrain_World.md) — approved Q81 (`R-181`), W2–W8 ruled (`R-182`), W1/W9/W10/W11 approved (`R-183`: *"Approved. Document and go autonomously."*).

## What was built

| step | commit | built |
|---|---|---|
| 0 | `bf5c0b017` | `CE-3013` live route axis swap · `CE-3015` saves stamp the resident terrain |
| 1a | `1b5f679dd` | `TerrainWorld` + GeoJSON parser · `TerrainCatalog` + folder staging (`BP-557`) · universal terrain load part · `CE-3012` · File/Terrain… picker (W13) · shipped `test-town`, `basic-desert` · `CE-3016` engine singleton cap |
| 1b | `ca42c5aff` | `FilledTriangle` primitive · `TerrainWorldGizmo` on every map · `CE-3014` |
| 2 | `399e6ffd3` | W8 — `CarKinematicsSystem` takes Z from `TerrainWorld.SurfaceZ` |
| 2 | `8febfb726` | W1 — DotRecast code moved to `Fdp.Toolkits.Navigation.Recast` (worktree agent) |
| 2 | `ca1325385` | W7 / `CE-3011` — every navigation API Z-up (worktree agent) |
| 2 + 3 | closing commit | W6 — `SwitchableNavmeshProvider`, `INavmeshFactory`, `RecastNavmeshFactory` + `TerrainWorldGeometrySource` + `TerrainWorldMesh`, bake in `TerrainResidency.Prepare`, publish at commit, `NavigationSolverModule` composed on SimHost (`CE-3006`) · W9 grid-coverage warning · step 3: `ILosStrategy` (planar default = the old sweep), `TerrainWorldLosStrategy` on SimHost/editor/Stride, `SensorMount` (id 305), `PhysicsCollider.Height`, `SensorCapabilitiesDto.EyeHeight*`, shared `PhysicsColliderReaders` |

## Deviations — each folded into the design (§7 "As built" notes, §5, §8)

| | deviation | why | where now |
|---|---|---|---|
| W8 | no `GroundFollow` / `LinearKinematics` Z | measured: no ground mover runs `LinearKinematics` (SimHost infantry carry `VehicleState`, W10) | design §7.1 W8 |
| W9 | check only, no resize | both grids are value types allocated at composition, before any terrain | `CE-3018` |
| W11 | stance runtime not composed | `StanceTransitionSystem` needs `IAnimationBackend` + the animation pipeline no host composes; the LOS stance reader is null ⇒ Standing | `CE-3010` |
| W6 | editor/CGF solver not composed | its role DECLARES `NavigationSolver` but its plan has no capability for it; adding one is a plan change under the order rails | `CE-3017` |
| LOS | world via `Func`, not the view | perception ticks in a background scoped view with no singleton API | design §4.3 |

## Gates *(T-1 feature suites first; build the TEST project, then `--no-build`)*

| gate | result | note |
|---|---|---|
| `Fdp.Toolkits.Tests` — whole suite | 2538 / 1 → **157/157** on the touched areas after the fix | the 1 was `PhysicsCollider_IsUnmanagedValueType` pinning `sizeof == 8`; updated to 12 as a deliberate layout change (no wire topic carries it; scenario JSON names fields) |
| `Hrot.SimHost.Tests` | 1057 / 3 / 3 skip | ⭐ all 3 pass in isolation (10/10): `TheHaltCannotStopTheClock` was a source scan over the agent's leftover worktree (removed); `EcsRecordReplayController…` = known `BP-534` flake; `FullBranchPipeline…` passes alone |
| `Hrot.Editor.Tests` | 455 / 2 / 2 skip | ⭐ both were source-scan rails over the leftover worktree — 6/6 after removing it |
| `Hrot.IG.Tests` | 457 / 0 / 1 skip | — |
| `HrotStrideApp.Game` build | ✅ | ⚠ Stride tests cannot run on Linux (no Windows desktop runtime); Stride Z-up edits compile-verified only |
| `rulings-check` · `design-digest --check` · `mermaid-check` | ✅ 72/72 · ✅ · 5/5 | |
| `tracker-counts --check` | ✅ | ids allocated: `CE-3017`, `CE-3018` (next free `CE-3019`) |

New rails: `CarKinematicsSystemTests.Vehicle_OnARamp_TakesItsZFromTheTerrainWorld_W8` · `RecastNavmeshFactoryTests` (3: Y-up re-wound soup, path AROUND a building in Z-up, switchable provider) · `TerrainLoadStepTests.AnAttachedNavmesh_IsBakedAtPrepare_AndPublishedAtCommit_W6` · `TerrainWorldTests` mesh ×3 + `GridCoverage_…_W9` · `LosRequestBatchingSystemTests` ×5 (building blocks; standing over a 0.5 m wall, prone not; `SensorMount` override; collider height; translator projection) · from the agent: `PathfindingSolverBackendSelectionTests.Navmesh_ZUpRequest_…`, `DotRecastNavmeshProviderTests.PlanPath_NorthSouth_ReturnsZUpWaypoints`.

⚠ **Not run:** a live cluster (`--mode all`) MoveTo around a building — acceptance is proven by the rails above, not by the product.
