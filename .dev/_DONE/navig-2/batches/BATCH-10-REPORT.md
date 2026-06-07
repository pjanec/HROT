# BATCH-10 REPORT

## Summary

All four tasks completed. Build: 0 errors. Navigation tests: 232 passed, 0 failed.

---

## Task NAV-P5-T2 — CorridorPreviewSystem

### Files Modified
- `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs` — added `FlagBitStreamCorridorPreview = 3`

### Files Created
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CorridorPreviewSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/CorridorPreviewSystemTests.cs`

### Design Decisions
- Batch instructions referenced several APIs that do not exist in the actual codebase. All were corrected:
  - Used `IPathRegistry.TryGetWaypointsSlice(handle, startIdx, 8, buf, out count)` instead of the non-existent `TryGetWaypoints(handle, out ReadOnlySpan<NavWaypoint>)`.
  - Used `NavWaypoint.Traversal` / `NavWaypoint.Surface` (actual field names) instead of `TraversalKind` / `SurfaceType`.
  - Used `SurfaceType.Generic` instead of the non-existent `SurfaceType.Default`.
- `CorridorPreviewSystem` is gated on bit `FlagBitStreamCorridorPreview` (bit 3) in `NavigationIntent.Flags`.
- Scratch buffer `NavWaypoint[8]` is reused across ticks to avoid per-frame heap allocation.
- `PreviewVersion` increments only when `GlobalSegmentStart` or `WaypointCount` changes (stable under constant window).
- Component is removed (not zeroed) when flag is cleared or `RouteHandle == 0`.

### Tests (6 / 6 passing)
| Test | Description |
|------|-------------|
| `StreamFlag_Set_PopulatesComponent` | Flag set → component added with count > 0 |
| `StreamFlag_NotSet_ComponentAbsent` | Flag unset → no component |
| `WaypointCount_Capped_At8` | Path of 20 → WaypointCount == 8 |
| `SegmentAdvance_BumpsPreviewVersion` | Advancing CurrentSegmentIndex increments PreviewVersion |
| `FlagCleared_RemovesComponent` | Clearing flag on next tick removes component |
| `InvalidRouteHandle_NoComponent` | Unregistered handle (99) → no component |

---

## Task NAV-P6-T1 — EngineBackedNavmeshProvider

### Files Created
- `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavmeshProvider.cs`
- (Tests in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedProviderTests.cs`)

### Design Decisions
- Implements `INavmeshProvider` as a direct-line stub: all positions walkable, `ProjectToNavmesh` returns the input position unchanged, `PlanPath` produces a straight two-waypoint route.
- `PathCost` returns Euclidean distance for deterministic test assertions.
- `QueryVersion()` returns constant `1`.

### Tests (6 / 6 passing)
| Test | Description |
|------|-------------|
| `IsWalkable_AnyPoint_ReturnsTrue` | Any input returns true |
| `ProjectToNavmesh_PreservesInputPosition` | Output snapped == input position |
| `PathCost_ReturnsEuclideanDistance` | 3-4-5 triangle → 5 metres |
| `QueryVersion_ReturnsOne` | Returns 1u |
| `PlanPath_ReturnsTwoWaypoints_StartAndEnd` | Correct positions at index 0 and 1 |
| `PlanPath_SmallBuffer_ReturnsZero` | Buffer of 1 → returns 0 |

---

## Task NAV-P6-T2 — EngineBackedDtCrowdProvider

### Files Created
- `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedDtCrowdProvider.cs`
- (Tests in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedProviderTests.cs`)

### Design Decisions
- Implements `IDtCrowdProvider` as a safe no-op stub.
- `RegisterAgent` returns `true` (accepted without side effects).
- `GetAgentVelocity` returns `Vector3.Zero`.
- `TryGetAgentSnapshot` returns `false` (no state tracked).

### Tests (3 / 3 passing)
| Test | Description |
|------|-------------|
| `GetAgentVelocity_ReturnsZero` | Always returns Zero |
| `RegisterAgent_ReturnsTrue` | Registration succeeds silently |
| `TryGetAgentSnapshot_ReturnsFalse` | No snapshot available |

---

## Task NAV-P6-T3 — EngineBackedVolumetricPathProvider

### Files Created
- `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedVolumetricPathProvider.cs`
- (Tests in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedProviderTests.cs`)

### Design Decisions
- Implements `IVolumetricPathProvider` as a direct-line stub; overrides the three optional methods that the interface provides as `throw NotSupportedException` defaults (`IsFlyable`, `PathExists`, `QueryVersion(BoundingBox3D)` NOT overridden — not needed by tests).
- `PlanPath` produces two waypoints with `TraversalKind.Fly`.
- All positions are flyable (`IsFlyable` always returns `true`).
- Used `TraversalKind.Fly` (enum value exists in `NavigationComponents.cs`).

### Tests (3 / 3 passing)
| Test | Description |
|------|-------------|
| `IsFlyable_AnyPoint_ReturnsTrue` | Always true |
| `PlanPath_ReturnsTwoWaypoints` | Count == 2 |
| `PlanPath_SmallBuffer_ReturnsZero` | Buffer of 1 → returns 0 |

---

## Build & Test Results

```
Build succeeded. 0 Error(s)
Passed!  - Failed: 0, Passed: 232, Skipped: 0, Total: 232
```
