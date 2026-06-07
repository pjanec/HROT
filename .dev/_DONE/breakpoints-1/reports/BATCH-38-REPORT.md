# BATCH-38 Report

**Workstream:** breakpoints-1  
**Batch:** BATCH-38  
**Status:** COMPLETE — all tasks implemented, all tests pass

---

## Task Summary

| Task              | Title                                                                         | Status |
|-------------------|-------------------------------------------------------------------------------|--------|
| Corrective Task 0 | Rename ByHandle test + add `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring` | DONE |
| UBP-P3T1          | `IEntityStatefulGizmo.UpdateAndDraw` signature + `IActiveViewProvider` + gizmo view routing | DONE |

---

## Corrective Task 0 — NameSubstring Lifecycle Test

### What changed

In `DataBreakpointSystemStatefulTests.cs`:

1. Renamed `LifecyclePredicate_FiresOnBirth_AndOnDeath` →  
   `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByHandle`
2. Added managed component `EntityLabel` (ComponentId 212) with a `Name` string field,
   registered in every test via the `Setup()` helper.
3. Added new test `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring` that:
   - Creates an entity and attaches an `EntityLabel` with `Name = "EnemyTank"`.
   - Adds a lifecycle breakpoint with `IdentifierType = NameSubstring,
     TargetValue = "Enemy"`.
   - Verifies birth fires on first `system.Execute` call.
   - Destroys entity; verifies death fires on the next `system.Execute` call.
   - Verifies a second entity with a non-matching name (`"AllyTank"`) never fires.

---

## UBP-P3T1 — IEntityStatefulGizmo Signature + Gizmo View Routing

### Step A — Extended `IDataBreakpointManager`

Added to `IDataBreakpointManager.cs`:

```csharp
ISimulationView ActiveView { get; }
uint PausedTick { get; }
```

`ActiveView` returns the view that gizmo systems should render against:
- while paused → `_preTickSnapshot`
- otherwise → `_liveRepo`

### Step B — `IEntityStatefulGizmo.UpdateAndDraw` signature change

Changed from:

```csharp
void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder);
```

to:

```csharp
void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder drawBuilder);
```

`using Fdp.ModuleHost.Abstractions;` added to `IStatefulGizmo.cs`.

### Step C — Concrete gizmo implementations updated

All implementations updated to accept the new `ISimulationView view` first parameter.
Files changed:

- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/EntityPickerGizmo.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/FdpLocationPickerGizmo.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/PointSequenceGizmo.cs`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/BoundingBoxPickerGizmo.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityDragGizmo.cs`
  (also replaced `_view.` field accesses with the `view` parameter)
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs`
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MeasureGizmo.cs`
- `Hrot/Subsystems/Hrot.Editor/Gizmos/ModalBoxSelectionGizmo.cs`
- `Hrot/Subsystems/Hrot.Editor/Gizmos/LocationPickerGizmo.cs`
- `Hrot/Subsystems/Hrot.Editor/Gizmos/ObstaclePlacementGizmo.cs`

### Step D — Test mocks updated

Updated `UpdateAndDraw` call sites to pass `ISimulationView` as the first argument.
Where Moq was unavailable (`Hrot.Presentation.Tests`, `Hrot.IG.Tests`,
`Hrot.SimHost.Tests`), `new EntityRepository()` was used instead of
`Mock<ISimulationView>().Object`, since `EntityRepository` implements `ISimulationView`.

Files changed:
- `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/EntityPickerGizmoTests.cs`
- `Hrot/Engine/Hrot.Presentation.Tests/EntityDragGizmoTests.cs`
- `Hrot/Subsystems/Hrot.IG.Tests/MeasureToolTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs`

### Step E — Gizmo system call sites updated

#### Circular dependency resolution

`DataDrivenGizmoSystem` lives in `Fdp.Toolkits`; `DataBreakpointManager` lives in
`Hrot.Diagnostics.Breakpoints`. A direct reference from `Fdp.Toolkits` to
`Hrot.Diagnostics.Breakpoints` would create a circular dependency.

**Solution:** Added `IActiveViewProvider` interface in `Fdp.Toolkits`
(`Fdp.Toolkit.Diagnostics.Gizmos` namespace):

```csharp
using Fdp.ModuleHost.Abstractions;
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IActiveViewProvider
    {
        ISimulationView ActiveView { get; }
    }
}
```

`DataBreakpointManager` (in `Hrot.Diagnostics.Breakpoints`, which already references
`Fdp.Toolkits`) implements both `IDataBreakpointManager` and `IActiveViewProvider`.

#### DataDrivenGizmoSystem

Added private field and constructor parameter:

```csharp
private readonly IActiveViewProvider? _breakpointManager;

public DataDrivenGizmoSystem(
    GizmoRegistry registry,
    IDebugDrawBuilder drawBuilder,
    Func<ISimulationView, Entity, bool>? isSelectedPredicate = null,
    GizmoUndoStack? undoStack = null,
    FdpEventBus? interactionBus = null,
    IActiveViewProvider? breakpointManager = null)
```

In `Execute`:

```csharp
ISimulationView activeView = _breakpointManager?.ActiveView ?? view;
```

All `UpdateAndDraw` calls (unlimited path, time-sliced path, injected gizmos path)
pass `activeView` instead of `view`.

`BehaviorGizmoManagerSystem` and `GlobalGizmoManager` received the same treatment.

#### DataBreakpointManager additions

```csharp
public ISimulationView ActiveView => _isPaused ? (ISimulationView)_preTickSnapshot : _liveRepo;
public uint PausedTick => _pausedTick;
```

In `OnHit`: `_pausedTick = _preTickSnapshot.GlobalVersion;`  
In `RequestStep` and `RequestContinue`: `_pausedTick = 0;`

### Step F — New test: `DataBreakpointGizmoViewTests.cs`

New file created in `Hrot.Diagnostics.Breakpoints.Tests`:

**`Gizmo_RendersAgainstActiveView_ReflectsPauseState`** — integration test that:

1. Creates `liveRepo` and `preTick` as separate `EntityRepository` instances.
2. Creates `DataBreakpointManager(liveRepo, preTick, provider, tc)`.
3. Creates a `ViewCapturingGizmo` (captures the `ISimulationView` passed to
   `UpdateAndDraw`) and injects it into a `DataDrivenGizmoSystem` wired with the
   breakpoint manager as `IActiveViewProvider`.
4. Asserts that before pause, the gizmo receives `liveRepo`.
5. Triggers pause via a lifecycle breakpoint on the entity.
6. Asserts that while paused, the gizmo receives `preTick` (the pre-tick snapshot).
7. Resumes and asserts the gizmo returns to `liveRepo`.

#### Non-trivial issue during test authoring

`DataBreakpointManager.OnHit` performs the triple-buffer rewind:
`_liveRepo.SyncFrom(_preTickSnapshot)`. If `preTick` is an empty repository, the
rewind wipes the entity from `liveRepo`, making `view.IsAlive(entity)` return false
in the next `gizmoSystem.Execute` call — the gizmo never fires and `LastView` is not
updated.

**Fix:** Sync `preTick` from `liveRepo` after entity creation to simulate the
pre-tick snapshot having been taken before the test's simulated tick:

```csharp
var entity = liveRepo.CreateEntity();
// Simulate the pre-tick snapshot having been taken before this tick: both
// repos now hold the entity, so the SyncFrom rewind in OnHit leaves it alive.
preTick.SyncFrom(liveRepo);
```

After this, `OnHit`'s rewind restores `liveRepo` to a state that still contains the
entity, the gizmo fires, and the view routing assertions all pass.

---

## Test Results

```
Passed!  - Failed: 0, Passed: 34, Skipped: 0, Total: 34
Hrot.Diagnostics.Breakpoints.Tests.dll (net8.0)
```

All 34 tests pass (32 pre-existing + Corrective-0 NameSubstring test + P3T1 view
routing test). Full solution build: 0 errors.
