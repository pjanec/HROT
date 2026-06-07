# BATCH-01 REPORT — Navigation Subsystem v2 Foundations

**Batch file:** `.dev/navig-2/batches/BATCH-01-INSTRUCTIONS.md`
**Status:** APPROVED — all tasks complete, build passes, all nav tests green.

---

## Task Results

### NAV-P0-T1 — Assembly Placement Documentation

**Status:** DONE

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContracts.cs`

**Description:**
Created a namespace organiser file with a `// NAV-P0-T1` comment block documenting the
assembly placement policy for Navigation Subsystem v2. Key decisions recorded:
- All new production code lives in the existing `Fdp.Toolkits` assembly (avoids circular deps).
- Namespace plan: `Fdp.Toolkit.Navigation` (interfaces/components), `.Fake` (test doubles),
  `.EngineBacked` (DotRecast-backed provider and module).
- UI/editor code stays in `Hrot.Editor.AiShared` / `Hrot.Editor`.

**Discrepancy resolved (DSC-5):** Design documents referenced non-existent `Hrot.Navigation.*`
assemblies. Using `Fdp.Toolkits` instead to avoid circular dependencies.

---

### NAV-P0-T2 — KinematicsMode Extension

**Status:** DONE

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/NavigationEnums.cs`

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationEnumsTests.cs`

**Description:**
Added three new `KinematicsMode` values:
```csharp
// Design's DirectPoint == existing Direct=4. Crowd/Naval/Flying start at 5.
Crowd = 5,    // Crowd-managed local avoidance agent (dtCrowd)
Naval = 6,    // Naval / surface watercraft pathfinding
Flying = 7,   // 3-D volumetric pathfinding for aircraft
```

**Discrepancy resolved (DSC-2):** Design proposed `Crowd=4` which collides with
`Direct=4`. Used `Crowd=5, Naval=6, Flying=7` instead. Comment documents the mapping.

**No switch-statement changes required:** `NavigationIntentBridgeSystem` and
`CarKinematicsSystem` both have `default:` clauses that safely cover the new values.

**Tests added (9 new, all passing):**
- `KinematicsMode_None_HasValueZero`
- `KinematicsMode_RoadGraph_HasValueOne`
- `KinematicsMode_CustomTrajectory_HasValueTwo`
- `KinematicsMode_Formation_HasValueThree`
- `KinematicsMode_Direct_HasValueFour`
- `KinematicsMode_Crowd_HasValueFive`
- `KinematicsMode_Naval_HasValueSix`
- `KinematicsMode_Flying_HasValueSeven`
- `KinematicsMode_Crowd_DoesNotEqualDirect`

---

### NAV-P0-T3 — INavmeshProvider Redefinition

**Status:** DONE

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/NavWaypoint.cs` — stub `readonly struct`
- `FDP/Toolkits/Fdp.Toolkits/Navigation/INavmeshProvider.cs` — new 7-method interface

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/INavmeshProvider.cs` — replaced with "moved" comment
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs` — reimplemented against new interface
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshReachableTest.cs` — migrated
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/PathCostScoreTest.cs` — migrated
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshSamplesGenerator.cs` — migrated
- `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshProviderTests.cs` — replaced old tests
- `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshTests.cs` — updated mocks
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsRoundTripTests.cs` — updated mock
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/PathCostInversionTests.cs` — updated mock
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs` — added using
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsContextSlotTests.cs` — added using

**New INavmeshProvider interface (7 methods):**
```csharp
bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF);
bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF);
int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF);
bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);
float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);
uint QueryVersion();
int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF);
```

**2D→3D mapping applied:** `new Vector3(x2D, 0f, y_north)` at all call sites.
Extract back: `PositionX = point.X, PositionY = point.Z`.

**TODO markers placed for NAV-P0-T5:** All three migrated EQS callers have
`// TODO NAV-P0-T5: use NavAgentProfile.PreferredLayerMask from ctx.Self` comments.

**Tests updated/replaced (all passing):**
- `StubNavmeshProvider_PathCost_ReturnsEuclideanDistance` (replaces T-NP1)
- `StubNavmeshProvider_PlanPath_ReturnsTwoWaypoints` (replaces T-NP2)
- All `NavmeshTests` inner mocks updated to new 7-method interface
- Integration mocks in `EqsRoundTripTests`, `PathCostInversionTests` updated
- `PathCostInversionTests` mock: `to.Y` → `to.Z` for position disambiguation

---

## Build / Test Results

| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| `dotnet build IOS-IG-SimHost.sln` | — | 0 errors | Clean build |
| `Fdp.Toolkits.Tests` (nav filter) | 23 | 0 | All nav tests green |
| `Hrot.ClusterRunner.Integration.Tests` (Eqs filter) | 62 | 0 | All EQS integration tests green |
| `Hrot.ClusterRunner.Integration.Tests` (AccurateLos filter) | 4 | 0 | LOS tests green |

Pre-existing test failures in `Fdp.Toolkits.Tests` (55 failures in
`GizmoSettingsPersistenceTests`, `IdAllocationTests`, `SimTransformBridgeSystemTests`,
`ReplayModuleTests`, `RecordingExportServiceTests`, etc.) are unrelated to navigation
and were already failing before this batch.

---

## Discrepancies Encountered

| ID | Description | Resolution |
|----|-------------|------------|
| DSC-2 | Design proposed `Crowd=4` colliding with `Direct=4` | Used `Crowd=5, Naval=6, Flying=7`; added comment |
| DSC-5 | Design referenced non-existent `Hrot.Navigation.*` assemblies | Used `Fdp.Toolkits` assembly; documented in T1 |
| DSC-6 | `EqsContextSlotTests.cs` also referenced old `INavmeshProvider` (not listed in batch) | Added `using Fdp.Toolkit.Navigation;` to fix compile error |
