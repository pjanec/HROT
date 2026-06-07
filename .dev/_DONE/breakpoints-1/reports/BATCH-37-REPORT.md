# BATCH-37 Report

**Workstream:** breakpoints-1  
**Batch:** BATCH-37  
**Status:** COMPLETE — all tasks implemented, all tests pass

---

## Task Summary

| Task              | Title                                                             | Status |
|-------------------|-------------------------------------------------------------------|--------|
| Corrective Task 0 | Replace `NoBreakpoints_DoesNoWork` with zero-allocation variant   | DONE   |
| UBP-P2T3          | Structural / Spatial / Lifecycle stateful breakpoint scanners     | DONE   |

---

## Corrective Task 0 — `NoBreakpoints_DoesNoWork_ZeroAllocations`

### What changed

Replaced `NoBreakpoints_DoesNoWork` in `DataBreakpointManagerTests.cs` with
`NoBreakpoints_DoesNoWork_ZeroAllocations`. The new test:

1. Warms up the JIT by calling `system.Execute(repo, 0f)` once.
2. Runs `GC.Collect() / WaitForPendingFinalizers / GC.Collect()` to stabilise the heap.
3. Records `GC.GetAllocatedBytesForCurrentThread()` as `before`.
4. Runs 10,000 iterations of `system.Execute(repo, 0f)` with no breakpoints mounted.
5. Records `after` and asserts `after - before == 0L`.

### Root-cause investigation

The test initially failed with `Actual: 240000` (24 bytes × 10,000 iterations) even
when the Execute body appeared to return immediately.

Disassembling the production assembly with ILDASM revealed the cause:

```
.locals init (class DataBreakpointSystem/'<>c__DisplayClass4_0' V_0, ...)
    IL_0000:  newobj instance void DataBreakpointSystem/'<>c__DisplayClass4_0'::.ctor()
```

The C# compiler emits a closure allocation (`<>c__DisplayClass4_0`, 24 bytes) for the
lambdas used in the `QueryDelta` callback and the `foreach` on `pendingHits`. In
debug IL, this closure object is allocated **at method entry (IL_0000)**, before any
conditional return. The early-out guard at `HasMountedDelegates` therefore never
prevented the allocation — the 24 bytes were always charged.

The same behaviour occurs in Release mode (JIT may not eliminate the closure even
with inlining because the closure captures a local `var pendingHits`).

### Fix — Execute / ExecuteCore split

`DataBreakpointSystem.Execute` was split into two methods:

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    // This method contains no lambdas, so the compiler emits no closure
    // allocations before this guard is reached.
    if (!_manager.HasMountedDelegates) return;
    ExecuteCore(view);
}

private void ExecuteCore(ISimulationView view)
{
    // All lambda / closure code lives here.
    ...
}
```

With this split:
- When `HasMountedDelegates == false`: `Execute` returns at the first instruction.
  No closures are allocated. `GetAllocatedBytesForCurrentThread` difference = 0.
- When there is work: `ExecuteCore` is called. Closures are allocated there, which
  is expected and acceptable.

---

## UBP-P2T3 — Structural / Spatial / Lifecycle Stateful Breakpoint Scanners

### IDataBreakpointManager additions

Added to `IDataBreakpointManager.cs`:

```csharp
bool HasStatefulTrackers { get; }
void EvaluateStatefulBreakpoints(EntityRepository repo);
```

### DataBreakpointManager additions

Three tracker dictionaries added as private fields:

```csharp
private readonly Dictionary<BreakpointId,
    (Breakpoint bp, StructuralPredicateDto dto, HashSet<Entity> knownSet)>
    _structuralTrackers = new();

private readonly Dictionary<BreakpointId,
    (Breakpoint bp, SpatialBoundingPredicateDto dto, HashSet<Entity> insideSet)>
    _spatialTrackers = new();

private readonly Dictionary<BreakpointId,
    (Breakpoint bp, LifecyclePredicateDto dto, HashSet<Entity> knownAlive)>
    _lifecycleTrackers = new();
```

`HasMountedDelegates` was extended to include stateful trackers:

```csharp
public bool HasMountedDelegates =>
    _componentPredicates.Count > 0 || _eventScanners.Count > 0 || HasStatefulTrackers;
```

`HasStatefulTrackers`:

```csharp
public bool HasStatefulTrackers =>
    _structuralTrackers.Count > 0 || _spatialTrackers.Count > 0 || _lifecycleTrackers.Count > 0;
```

`TryMountDelegate` was extended with three new cases. `UnmountDelegate` removes from
all three tracker dictionaries.

`EvaluateStatefulBreakpoints(EntityRepository repo)` delegates to three private helpers:
- `EvaluateStructuralTrackers` — diffs component presence vs. `knownSet` using
  `ComputeEffectivePresence` (mirrors `RecordingSearchService.RunStructuralFrame`).
- `EvaluateSpatialTrackers` — checks each entity's position against bounding-box
  bounds using `ReadPosition2D` / `IsInBounds`; fires on Entry / Exit / EntryOrExit
  as specified by `TriggerEvent`.
- `EvaluateLifecycleTrackers` — detects birth (new entities since last tick) and
  death (via `repo.GetDestructionLog()`); fires if `MatchesLifecycleCriteria` returns
  true (name match for Named entities, unconditional for any-entity).

Private helpers added:
- `ComputeEffectivePresence` — returns the set of entities that currently satisfy
  a structural predicate (component present + authority filter).
- `IsInBounds(Vector2, BoundingBox2D)` — axis-aligned box test.
- `ReadPosition2D` — resolves `PositionComponentType` + `PositionXPath`/`PositionYPath`
  via reflection to extract an (X, Y) pair from an entity's component.
- `ReadFloatField` / `ReadStringField` — reflection helpers for component field access.
- `MatchesLifecycleCriteria` — checks name or accepts any entity.
- `ReadEntityName` — reads entity name from the name component type via `NamePropertyPath`.

### DataBreakpointSystem additions

`DataBreakpointSystem.ExecuteCore` (the lambda-free inner method) calls:

```csharp
if (_manager.HasStatefulTrackers)
    _manager.EvaluateStatefulBreakpoints(repo);
```

### New test class — DataBreakpointSystemStatefulTests

New file: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointSystemStatefulTests.cs`

`[Collection("ComponentRegistry")]` applied so that `ComponentTypeRegistry.Clear()` in
other test classes does not corrupt this class during parallel xUnit execution.

Component fixtures registered:
- `[ComponentId(210)] internal struct WeaponState` — `int Ammo`
- `[ComponentId(211)] internal struct Position2D` — `float X`, `float Y`

Five tests, all passing:

| Test | Description |
|------|-------------|
| `StructuralPredicate_FiresOnComponentAdded` | Mount structural breakpoint with `ModificationType.Added`; add component; Execute; assert `IsPaused == true`. |
| `StructuralPredicate_DoesNotFireOnDwelling` | Same entity with component already present on second tick; assert `IsPaused == false` (no re-fire). |
| `SpatialPredicate_FiresOnEntry_NotOnDwelling` | Entity enters bounding box; asserts pause on tick 1; `RequestContinue`; tick 2 entity stays inside; asserts no re-pause. |
| `LifecyclePredicate_FiresOnBirth_AndOnDeath` | Spawn entity → fires on birth; `RequestContinue`; destroy entity → fires on death. |
| `AuthorityRequirement_RequireAuthority_FiltersGhostMutations` | Entity without authority does not trigger `RequireAuthority` structural breakpoint. |

---

## Test Results

```
Passed!  - Failed: 0, Passed: 32, Skipped: 0, Total: 32
```

All 32 tests in `Hrot.Diagnostics.Breakpoints.Tests` pass.
