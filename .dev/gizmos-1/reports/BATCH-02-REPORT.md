# BATCH-02 Report

**Batch:** BATCH-02
**Tasks:** GZ004, GZ005, GZ006
**Status:** COMPLETE — all tests pass, zero compile errors

---

## Files Created

### Production code — Fdp.Toolkits

| File | Purpose |
|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs` | Gizmo lifecycle interface (`OnInitialize`, `UpdateAndDraw`, `OnTeardown`) |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoDefinition.cs` | Describes one type of entity-bound gizmo (`RequiredComponents`, `VisibilityPolicy`, `CreateInstance`) |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoVisibilityPolicy.cs` | Visibility-control interface + `AlwaysVisiblePolicy.Instance` and `NeverVisiblePolicy.Instance` singletons |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoRegistry.cs` | Startup registry; compiles `IGizmoDefinition` into `CompiledGizmoRule` (BitMask256 + index) |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | ECS system: manages lifecycle + drawing of entity-bound gizmos via `ConstructionOrder`/`DestructionOrder` events |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/IBehaviorGizmoFactory.cs` | Pool factory interface for behavior-bound gizmo instances |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/BehaviorGizmoRegistry.cs` | Maps behavior name -> `IBehaviorGizmoFactory` |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/BehaviorGizmoManagerSystem.cs` | ECS system: manages lifecycle + drawing of behavior-bound gizmos via `AssignBehaviorEvent`/`ClearBehaviorEvent`/`DestructionOrder` |

### Test code — Fdp.Toolkits.Tests

| File | Purpose |
|------|---------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs` | 74 tests covering GZ004, GZ005, GZ006 scenarios |

---

## Test Results

```
Passed: 74 / 74   (Gizmos filter)
Failed:  0
Skipped: 0
```

Test classes and scenario coverage:

| Class | Scenarios | Count |
|-------|-----------|-------|
| `GizmoRegistryTests` | SC-GZ004-1 through SC-GZ004-6 | 6 |
| `DataDrivenGizmoSystemTests` | SC-GZ005-1 through SC-GZ005-8 + extras | ~40 |
| `BehaviorGizmoManagerSystemTests` | SC-GZ006-1 through SC-GZ006-6 + extras | ~28 |

Pre-existing failures in unrelated areas (Navigation, Replication, Behavior) are unchanged; no regressions introduced.

---

## Design Deviations

### 1. Selection predicate instead of SelectionState ECS query

**Reason:** `Hrot.IG.Components.SelectionState` lives in a project that `Fdp.Toolkits` does not reference. Adding a project reference would create a layering violation.

**Solution:** Both `DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` accept an optional `Func<ISimulationView, Entity, bool>? isSelectedPredicate` constructor parameter.

- `null` (default): every active gizmo is drawn unconditionally (equivalent to "global force visible").
- Non-null: the predicate is invoked per-entity; only entities for which it returns `true` are drawn.

The game host layer that wires the system is responsible for supplying a predicate that queries `SelectionState` (or any other selection mechanism it sees fit).

### 2. GlobalDebugSettings deferred

**Reason:** `GlobalDebugSettings` is not yet defined in the codebase (scheduled for GZ015).

**Solution:** The system treats a `null` predicate as the "always draw" mode, which subsumes what `GlobalDebugSettings.ForceAllGizmosVisible` would do. This will be revisited when GZ015 lands.

### 3. GizmoRegistry.Rules is `internal`, not `public`

**Reason:** `CompiledGizmoRule` is an `internal struct`. C# (CS0053) forbids a `public` property whose return type is less accessible than the property itself.

**Solution:** `Rules` is declared `internal IReadOnlyList<CompiledGizmoRule>`. `DataDrivenGizmoSystem` (same assembly) can access it directly; tests access it via `[assembly: InternalsVisibleTo("Fdp.Toolkits.Tests")]`.

### 4. GizmoSelectedTag is a presence tag, not a bool-field struct

**Reason:** The ECS layout validator (`ComponentTypeRegistry.ValidateUnmanagedLayout`) rejects unmanaged structs that contain `bool` fields without `[MarshalAs(UnmanagedType.I1)]`.

**Solution:** `GizmoSelectedTag` is an empty struct (presence = selected, absence = not selected). Test predicate checks `repo.HasComponent<GizmoSelectedTag>(entity)`.

---

## Known Issues / Follow-ups

- None. All acceptance criteria from the batch instructions are satisfied.
- GZ015 (`GlobalDebugSettings`) will refine the always-draw behaviour introduced by `isSelectedPredicate = null`.
