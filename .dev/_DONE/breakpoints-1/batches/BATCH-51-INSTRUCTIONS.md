# BATCH-51 Instructions

**Scope:** P11T1 (zero-allocation Execute), P11T2 (chunk-version-aware QueryDelta), P11T9 (eliminate MountedAccessor allocations)

**Design reference:** [DESIGN.md](../DESIGN.md) §6.3, §6.7; [TASK-DETAIL.md](../TASK-DETAIL.md) #ubp-p11t1, #ubp-p11t2, #ubp-p11t9

---

## Context

`DataBreakpointSystem.Execute` has three performance problems that violate DESIGN §6.7 (zero steady-state allocation when breakpoints are armed but the simulation is running normally):

1. **P11T1** — `var pendingHits = new List<Entity>()` is created per breakpoint per tick, plus the `Action<Entity>` lambda closure captured over `bp`, `compiled`, `repo`, `pendingHits` is re-created every tick.
2. **P11T2** — `sinceVersion = 0u` is passed to `QueryDelta`, so the entire entity table is scanned every tick even if nothing changed.
3. **P11T9** — `MountedComponentPredicates` and `MountedEventScanners` property getters create a fresh `new List<(Breakpoint, CompiledComponentPredicate)>` (or Scanner) on every call. `Execute` calls these every tick.

These three tasks are tightly coupled and should be implemented together.

---

## Task 1 — P11T1: Zero-allocation `DataBreakpointSystem.Execute`

### Files to change
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs`

### What to change

The `EntityRepository` already has a zero-allocation `foreach`-compatible overload:

```csharp
// EntityRepository.DeltaQuery.cs
public DeltaQueryEnumerable QueryDelta(EntityQuery query, uint sinceVersion)
    // returns a ref struct DeltaQueryEnumerable — zero allocation, supports foreach
```

**Step 1:** Add a reusable pending-hits buffer field to `DataBreakpointSystem`:

```csharp
private readonly List<Entity> _pendingHitsBuffer = new();
```

**Step 2:** In `ExecuteCore`, within the component-data path loop, replace:

```csharp
var pendingHits = new List<Entity>();

// sinceVersion = 0 scans all entities every tick.
repo.QueryDelta(query, 0u, entity =>
{
    if (bp.FilterEntity is { } filterEntity && filterEntity != entity) return;
    if (!compiled.Delegate(repo, entity)) return;
    pendingHits.Add(entity);
});

foreach (var hitEntity in pendingHits)
    _manager.OnHit(bp, hitEntity);
```

with:

```csharp
_pendingHitsBuffer.Clear();

foreach (var entity in repo.QueryDelta(query, compiled.LastScanVersion))   // P11T2: use per-predicate version
{
    if (bp.FilterEntity is { } filterEntity && filterEntity != entity) continue;
    if (!compiled.Delegate(repo, entity)) continue;
    _pendingHitsBuffer.Add(entity);
}
compiled.LastScanVersion = repo.GlobalVersion;   // P11T2: advance to current version

foreach (var hitEntity in _pendingHitsBuffer)
    _manager.OnHit(bp, hitEntity);
```

Note: `compiled.LastScanVersion` is defined in P11T2 below. Both tasks are implemented together since the `foreach` loop replaces the lambda, and `sinceVersion` replaces `0u`.

The `foreach (var entity in repo.QueryDelta(query, sinceVersion))` iterates a `DeltaQueryEnumerable` ref struct — zero allocation. No `Action<Entity>` lambda is created. No `List<Entity>` is created per tick.

**Remove** the `// sinceVersion = 0 scans all entities every tick.` TODO comment; replace with:

```csharp
// Uses per-predicate LastScanVersion so unchanged chunks are skipped (P11T2).
```

---

## Task 2 — P11T2: Chunk-version-aware `QueryDelta`

### Files to change
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` — add `LastScanVersion` property to `CompiledComponentPredicate`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs` — use it (already shown in P11T1 above)

### What to change

**In `DataBreakpointManager.cs`:** The `CompiledComponentPredicate` record already exists at the top of the file:

```csharp
public sealed record CompiledComponentPredicate(
    Func<EntityRepository, Entity, bool> Delegate,
    IReadOnlyList<Type> MandatoryComponents);
```

Add a mutable `LastScanVersion` property to track the last-scanned repo version:

```csharp
public sealed record CompiledComponentPredicate(
    Func<EntityRepository, Entity, bool> Delegate,
    IReadOnlyList<Type> MandatoryComponents)
{
    /// <summary>
    /// The last <see cref="EntityRepository.GlobalVersion"/> at which this predicate was evaluated.
    /// Passed to <see cref="EntityRepository.QueryDelta"/> as <c>sinceVersion</c> to skip unchanged
    /// entity chunks. Defaults to 0 (scan everything on first evaluation). Reset to 0 automatically
    /// when the predicate is re-compiled (hot-reload) because <see cref="DataBreakpointManager.TryMountDelegate"/>
    /// creates a new <see cref="CompiledComponentPredicate"/> instance.
    /// </summary>
    public uint LastScanVersion { get; set; } = 0u;
}
```

**Hot-reload reset:** `TryMountDelegate` already calls `_componentPredicates[id] = new CompiledComponentPredicate(del, mandatory)`. This creates a NEW instance with `LastScanVersion = 0u`. No extra code needed.

**In `DataBreakpointSystem.cs`:** The `foreach` loop from P11T1 uses `compiled.LastScanVersion` as `sinceVersion` and updates it after the scan (already shown above).

---

## Task 3 — P11T9: Eliminate `Mounted*` accessor allocations

### Files to change
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

### What to change

Both `MountedComponentPredicates` and `MountedEventScanners` currently allocate on every call. Replace with a cached pattern — invalidate whenever the underlying dictionaries change.

**Step 1:** Add two nullable cached-list fields (place them near the other private fields):

```csharp
private List<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)>? _cachedComponentPredicates;
private List<(Breakpoint Breakpoint, CompiledEventScanner Scanner)>? _cachedEventScanners;
```

**Step 2:** Replace the `MountedComponentPredicates` getter:

```csharp
public IReadOnlyList<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)> MountedComponentPredicates
{
    get
    {
        if (_cachedComponentPredicates != null) return _cachedComponentPredicates;
        _cachedComponentPredicates = new List<(Breakpoint, CompiledComponentPredicate)>(_componentPredicates.Count);
        foreach (var (id, compiled) in _componentPredicates)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                _cachedComponentPredicates.Add((bp, compiled));
        }
        return _cachedComponentPredicates;
    }
}
```

**Step 3:** Replace the `MountedEventScanners` getter:

```csharp
public IReadOnlyList<(Breakpoint Breakpoint, CompiledEventScanner Scanner)> MountedEventScanners
{
    get
    {
        if (_cachedEventScanners != null) return _cachedEventScanners;
        _cachedEventScanners = new List<(Breakpoint, CompiledEventScanner)>(_eventScanners.Count);
        foreach (var (id, scanner) in _eventScanners)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                _cachedEventScanners.Add((bp, scanner));
        }
        return _cachedEventScanners;
    }
}
```

**Step 4:** Invalidate caches in `TryMountDelegate` — add these two lines at the **end** of `TryMountDelegate`, just before the closing brace (after the switch statement):

```csharp
    _cachedComponentPredicates = null;
    _cachedEventScanners = null;
```

**Step 5:** Invalidate caches in `UnmountDelegate` — add these two lines at the **end** of `UnmountDelegate`, after all the `Remove` calls but before the external-hit loop:

Wait — the end of `UnmountDelegate` currently does:
```csharp
private void UnmountDelegate(BreakpointId id)
{
    _componentPredicates.Remove(id);
    _eventScanners.Remove(id);
    _structuralTrackers.Remove(id);
    _spatialTrackers.Remove(id);
    _lifecycleTrackers.Remove(id);

    // Remove from external-hit registrations
    foreach (var tagList in _externalHitPredicates.Values)
        tagList.RemoveAll(entry => entry.id == id);
}
```

Add the cache invalidation lines right after the first two removes (where `_componentPredicates` and `_eventScanners` are modified), or simply at the end of the method before the closing brace:

```csharp
    _cachedComponentPredicates = null;
    _cachedEventScanners = null;
    // Remove from external-hit registrations
    foreach (var tagList in _externalHitPredicates.Values)
        tagList.RemoveAll(entry => entry.id == id);
}
```

(Move the cache-invalidation lines so they appear after the `Remove` calls and before the external-hit loop, or just add them at the end of the method — either position is correct.)

---

## Tests to write

Write tests in the following locations:

### New file: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/AllocationOptimizationTests.cs`

This file should contain all three test classes. Use `[Collection("ComponentRegistry")]` on all test classes.

#### Class 1: `DataBreakpointSystemAllocationTests`

**Test 1: `DataBreakpointSystem_StillFiresHits_AfterZeroAllocRefactor`**

Regression test — verify the refactored `Execute` still fires `OnHit` correctly.

Setup:
- `ComponentTypeRegistry.Clear()`
- Create `liveRepo`, `preTick`, `tc`, `snapshotProvider`, compiler, `mgr`
- Register `TestHealth` component
- Create 3 entities in liveRepo; add `TestHealth { Current = 50 }` to each
- Sync preTick: `snapshotProvider.SetEnabled(true); snapshotProvider.Execute(liveRepo, 0f);`
- Add BP: `mgr.AddBreakpoint(new PropertyMatchDto { ComponentTypeName = typeof(TestHealth).FullName, FieldPath = "Current", Operator = ">", Value = "0" })`
- Create `DataBreakpointSystem system = new(mgr)`

Execute:
- `system.Execute(liveRepo, 0.016f)`

Assert:
- `Assert.True(mgr.IsPaused)` — BP fired

**Test 2: `DataBreakpointSystem_ReusableBuffer_ClearedBetweenBreakpoints`**

Verify that `_pendingHitsBuffer.Clear()` is called between each breakpoint's evaluation, so one BP's entities don't bleed into another.

Setup:
- `ComponentTypeRegistry.Clear()`
- Create a manager with 2 breakpoints:
  - BP-A: `PropertyMatchDto` matching `TestHealth.Current > 100` (should NOT match any entity — no hits)
  - BP-B: `PropertyMatchDto` matching `TestHealth.Current > 0` (matches all 3 entities)
- 3 entities all with `TestHealth { Current = 50 }`
- Sync snapshot
- Create `DataBreakpointSystem system = new(mgr)`

Execute:
- `system.Execute(liveRepo, 0.016f)`

Assert:
- `Assert.True(mgr.IsPaused)` — BP-B fired (at least one entity matched)
- `Assert.Equal(1, tc.PauseRequestCount)` — paused exactly once

(This verifies buffer Clear: if the buffer were not cleared, the 3 entities from BP-B could incorrectly re-trigger for BP-A on subsequent evaluations or vice versa.)

#### Class 2: `ChunkVersionScanTests`

**Test 1: `DataBreakpointSystem_OnSecondExecute_DoesNotFireIfNoMutation`**

Verifies that after the first Execute updates `LastScanVersion`, a second Execute without any mutations does NOT fire the breakpoint.

Setup:
- `ComponentTypeRegistry.Clear()`
- 5 entities each with `TestHealth { Current = 10 }`; BP: `Current > 0` (always matches)
- Sync snapshot, create system
- Execute tick 0: BP fires, manager pauses → `tc.PauseRequestCount == 1`
- `mgr.RequestContinue()` → manager un-pauses
- Re-sync snapshot

Execute tick 1 (NO mutations to liveRepo between execute calls):
- `system.Execute(liveRepo, 0.016f)`

Assert:
- `Assert.False(mgr.IsPaused)` — no hit (LastScanVersion == current repo version, QueryDelta returns nothing)
- `Assert.Equal(1, tc.PauseRequestCount)` — still only 1 total

**Test 2: `DataBreakpointSystem_AfterMutation_DetectsNewEntity`**

After the version tracking is established, a mutation to one entity is detected on the next Execute.

Continuation from test 1 setup (or fresh setup):
- Same 5-entity setup as Test 1
- After tick 0 fires + `RequestContinue()` + re-sync + tick 1 (no hit)
- Now: add 1 NEW entity with `TestHealth { Current = 50 }` to liveRepo (this advances repo.GlobalVersion)
- Sync snapshot again
- Execute tick 2

Assert:
- `Assert.True(mgr.IsPaused)` — the new entity was detected (chunk changed since LastScanVersion)
- `Assert.Equal(2, tc.PauseRequestCount)` — second pause

#### Class 3: `MountedAccessorCacheTests`

**Test 1: `MountedComponentPredicates_ReturnsSameInstance_BetweenMutations`**

```csharp
ComponentTypeRegistry.Clear();
var (manager, _, _, _) = ManagerFactory.Create();

manager.AddBreakpoint(new PropertyMatchDto { /* ... any valid BP ... */ });

var list1 = manager.MountedComponentPredicates;
var list2 = manager.MountedComponentPredicates;

Assert.Same(list1, list2);  // same cached instance
```

**Test 2: `MountedComponentPredicates_Invalidated_AfterNewBreakpointAdded`**

```csharp
ComponentTypeRegistry.Clear();
var (manager, _, _, _) = ManagerFactory.Create();

manager.AddBreakpoint(new PropertyMatchDto { /* ... */ });
var list1 = manager.MountedComponentPredicates;

// Add another breakpoint → must invalidate cache
manager.AddBreakpoint(new PropertyMatchDto { /* ... */ });
var list2 = manager.MountedComponentPredicates;

Assert.NotSame(list1, list2);  // cache rebuilt
Assert.Equal(2, list2.Count);
```

**Test 3: `MountedEventScanners_ReturnsSameInstance_BetweenMutations`**

Same pattern as Test 1 but for event scanners — register a `TransientEventPredicateDto`. Note: `DataBreakpointManager` requires an `IEventScannerCompiler` to compile event scanners. Use the constructor overload that accepts the compiler. If the test infrastructure does not easily support this, skip this test and just test `MountedComponentPredicates` with two tests. Ensure at minimum that `MountedComponentPredicates` caching is covered by tests.

---

## Required imports / usings in the new test file

```csharp
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;
```

---

## Reference: test helpers available in the test project

From other test files you can see:
- `ManagerFactory.Create()` → returns `(DataBreakpointManager manager, EntityRepository liveRepo, DebugSnapshotProvider snapshotProvider, MockDebugTimeController tc)`
- `ManagerFactory.MakeBreakpoint(enabled: true)` → returns a `Breakpoint` with a pre-built condition
- `ComponentTypeRegistry.Clear()` → must be called at the start of each test (test isolation)
- `TestHealth`, `WeaponState` — test component structs available in the test project
- `PredicateCompiler` from `Fdp.Toolkit.ReplayBrowser.Search` — used to compile predicates
- `snapshotProvider.SetEnabled(true); snapshotProvider.Execute(liveRepo, 0f)` — to prime the pre-tick snapshot

Note: For the `ChunkVersionScanTests`, the re-sync snapshot step means calling `snapshotProvider.Execute(liveRepo, 0f)` again before each Execute call (this simulates what `DebugSnapshotProvider` does each BeforeSync tick in production).

---

## Build & test commands

```
dotnet build IOS-IG-SimHost.sln -v quiet
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~BreakpointSubsystemWiring"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

Expected: 0 errors, all tests pass (120+ unit, 20 integration wiring, 167 BTree, 192 HSM).

---

## Checklist

- [ ] `CompiledComponentPredicate` record gains `public uint LastScanVersion { get; set; } = 0u;`
- [ ] `DataBreakpointSystem._pendingHitsBuffer` field added
- [ ] `ExecuteCore` component loop uses `foreach` over `DeltaQueryEnumerable` (no lambda, no `new List<Entity>`)
- [ ] `compiled.LastScanVersion` read before QueryDelta, updated after
- [ ] `DataBreakpointManager._cachedComponentPredicates` and `_cachedEventScanners` nullable fields added
- [ ] `MountedComponentPredicates` and `MountedEventScanners` getters use cache
- [ ] Cache invalidated in `TryMountDelegate` and `UnmountDelegate`
- [ ] `AllocationOptimizationTests.cs` created with all tests
- [ ] Build: 0 errors
- [ ] All tests pass
