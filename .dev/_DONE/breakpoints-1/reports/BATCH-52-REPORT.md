# BATCH-52 Report

**Scope:** P11T8, P11T10, P11T13
**Status:** COMPLETED

---

## Summary of Changes

### P11T8 — `StageMutation` size via ECS registry

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

- Added `using System.Runtime.CompilerServices;` to imports.
- Added static `_componentSizeCache = new Dictionary<Type, int>()` field.
- Added private static `GetEcsComponentSize(Type type)` helper that uses reflection to call `ComponentType<T>.Size` (= `Unsafe.SizeOf<T>()`) with a lock-protected cache.
- In `StageMutation`, replaced `Marshal.SizeOf(componentType)` with `GetEcsComponentSize(componentType)`.

The `using System.Runtime.InteropServices;` import was kept because `Marshal` is still used in `ReadPosition2D` (fallback path) and `ReadEntityName`.

### P11T10 — Compiled spatial position accessor

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

- Added `using System.Linq.Expressions;` to imports.
- Added `internal delegate Vector2 SpatialPositionDelegate<T>(ref T component) where T : unmanaged;` in the namespace (between `CompiledEventScanner` and `DataBreakpointManager`).
- Changed `_spatialTrackers` tuple type to include `Func<EntityRepository, Entity, Vector2>? posAccessor` as a 4th element.
- Added `CompileSpatialPositionAccessor(SpatialBoundingPredicateDto dto)` — dispatches to generic version via reflection; returns `null` if component is not an unmanaged value type or compilation fails (fallback to `ReadPosition2D`).
- Added `CompileSpatialPositionAccessorGeneric<T>(SpatialBoundingPredicateDto dto, int typeId) where T : unmanaged` — builds an expression tree `(ref T comp) => new Vector2(comp.X, comp.Y)`, compiles it to a `SpatialPositionDelegate<T>`, and wraps it in a `Func<EntityRepository, Entity, Vector2>` that guards with `HasComponentByTypeId` before calling `GetComponentRO<T>`. Pattern mirrors `PredicateCompiler.BuildUnmanagedMatcher<T>`.
- Updated `TryMountDelegate` spatial case to call `CompileSpatialPositionAccessor(spatialDto)` at mount time.
- Updated `EvaluateSpatialTrackers` to destructure the 4-element tuple and use `posAccessor(repo, entity)` when non-null, falling back to `ReadPosition2D` for managed components.
- `ReadPosition2D` and `ReadFloatField` retained as private fallback methods.

### P11T13 — `MatchesLifecycleCriteria` NetworkId throws

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

- Added XML doc `<exception cref="NotSupportedException">` to `MatchesLifecycleCriteria`.
- In the switch expression, replaced the silent `_ => false` for the NetworkId case with an explicit `EntityIdentifierType.NetworkId => throw new NotSupportedException(...)` arm.
- The trailing `_ => false` arm is kept for truly unknown enum values.

---

## New Tests Added

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/P11CorrectnessTests.cs`

### Class: `StageMutationSizeTests`
| Test | Description |
|------|-------------|
| `StageMutation_SimpleStruct_SizeMatchesUnsafeSizeOf` | Verifies `SizeBytes == Unsafe.SizeOf<TestHealth>()` |
| `StageMutation_StagedSize_EqualsManagedSize_NotInteropSize` | Documents the CLR-size contract |

### Class: `SpatialPositionAccessorTests`
| Test | Description |
|------|-------------|
| `SpatialTracker_CompiledAccessor_ReturnsCorrectPosition` | Entity at (3, 7.5) inside [0-10, 0-10] → `IsPaused = true` |
| `SpatialTracker_CompiledAccessor_DoesNotFireOutsideBounds` | Entity at (50, 50) outside [0-10, 0-10] → `IsPaused = false` |

### Class: `LifecycleNetworkIdTests`
| Test | Description |
|------|-------------|
| `Lifecycle_NetworkId_NoMapWired_ThrowsNotSupportedException` | `EvaluateStatefulBreakpoints` throws `NotSupportedException` for NetworkId |

**Total new tests: 5**

---

## Test Results

```
Hrot.Diagnostics.Breakpoints.Tests:
  Passed: 124, Failed: 0 (119 pre-existing + 5 new)

Hrot.ClusterRunner.Integration.Tests (BreakpointSubsystemWiring filter):
  Passed: 20, Failed: 0

Hrot.BTree.Editor.Tests:
  Passed: 167, Failed: 0

Hrot.Hsm.Editor.Tests:
  Passed: 192, Failed: 0
```

**Full solution build:** `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Deviations from Instructions

1. **`LifecyclePredicateDto.EventType` does not exist** — The batch instructions' test snippet used `EventType = LifecycleEventType.Spawned` which does not exist on `LifecyclePredicateDto` (the real API only has `IdentifierType`, `TargetValue`, `NameComponentType`, `NamePropertyPath`). The `EventType` and `LifecycleEventType.Spawned` properties were removed from the test. The test still correctly exercises the `NetworkId` throw path.

2. **`SpatialBoundingPredicateDto` field names differ from batch instructions** — The instructions showed `MinX/MaxX/MinY/MaxY` and `AuthorityRequirement`, but the real DTO uses `Bounds = new BoundingBox2D { Min, Max }` and `TriggerEvent`. Tests were written using the actual API.

3. **`CompileSpatialPositionAccessorGeneric<T>` not marked `unsafe`** — The method works without `unsafe` (mirrors `PredicateCompiler.BuildUnmanagedMatcher<T>` which is also not marked `unsafe`). Removed the `unsafe` modifier to avoid a potential compiler warning under `TreatWarningsAsErrors`.

---

## Developer Insights

**Issues encountered:**
- The batch instructions' test scaffolding referenced non-existent `LifecyclePredicateDto.EventType` and `LifecycleEventType.Spawned` properties — these don't exist in the actual DTO (it only tracks identity matching, not event type). Required checking the actual source before writing tests.
- `SpatialBoundingPredicateDto` uses `Bounds = new BoundingBox2D { Min, Max }` not separate `MinX/MaxX` properties as suggested in the instructions.

**Weak points spotted:**
- `GetEcsComponentSize` uses a lock per call. For the hot path (bulk ECB draining), the cache means only the first call per type pays the reflection cost. The lock is fine for this usage.
- The `CompileSpatialPositionAccessor` fallback silently returns `null` on any exception. This means a misconfigured `PositionXPath` (e.g., misspelled field name) falls back to the slow reflection path without warning. A debug log or `IBreakpointNotifier` callback might be useful in future.

**Design decisions:**
- Kept `ReadPosition2D` and `ReadFloatField` as private fallback methods (not deleted) per instructions, to support managed-component spatial predicates.
- The compiled accessor captures `typeId` at mount time. If the component registry is cleared and re-registered (test isolation via `ComponentTypeRegistry.Clear()`), the accessor becomes stale. This is acceptable since `TryMountDelegate` is always called after registration, and in production the registry is immutable after boot.
