# BATCH-38: Corrective Task 0 + Phase P3T1 (IEntityStatefulGizmo signature change)

**Batch Number:** BATCH-38
**Tasks:** Corrective Task 0 (BATCH-37 fix), UBP-P3T1
**Phase:** P2 completion + P3 start
**Estimated Effort:** 8-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-37 must be complete

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **BATCH-37 Review:** `.dev/breakpoints-1/reviews/BATCH-37-REVIEW.md` — one fix required before P3T1
2. **Design §7.2:** `.dev/breakpoints-1/DESIGN.md` — lines around §7.2 ("IEntityStatefulGizmo signature change")
3. **Task Definitions:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P3T1
4. **Current interface:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`
5. **Gizmo systems:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`,  
   `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/BehaviorGizmoManagerSystem.cs`,  
   `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs`
6. **Breakpoints manager:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`

### Source Code Key Locations
- **Interface to change:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`
- **Gizmo systems (call sites):**
  - `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/BehaviorGizmoManagerSystem.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs`
- **Concrete gizmo implementations (all need UpdateAndDraw signature updated):**
  - `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/EntityPickerGizmo.cs`
  - `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/PointSequenceGizmo.cs`
  - `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/FdpLocationPickerGizmo.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityDragGizmo.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MeasureGizmo.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteWaypointGizmo.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/VertexEditGizmo.cs`
  - Any other gizmos in `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/` or `Hrot/Editor/`
  - Look for additional gizmos with `IEntityStatefulGizmo` in `Hrot/Subsystems/Hrot.SimHost/` (EntityRotatorGizmo, LocationPickerGizmo, ModalBoxSelectionGizmo, ObstaclePlacementGizmo)
  - **DO a grep search:** `grep -r "UpdateAndDraw" --include="*.cs"` to find ALL call sites and implementations before starting
- **Test mocks to update (also need signature update):**
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs` (MockGizmo)
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoUndoStackTests.cs` (MockUndoGizmo)
  - `Hrot/Runner/Hrot.ClusterRunner.Tests/DataDrivenGizmoPredicateTests.cs` (D003MockGizmo)
- **Breakpoints interface/impl (additions needed):**
  - `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`
  - `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`
- **New P3T1 test file (create in breakpoints test project):**
  - `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointGizmoViewTests.cs`

### How to Build and Test
```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2\
# Full solution build to check all signature changes compile
dotnet build IOS-IG-SimHost.sln -c Debug

# Run breakpoints tests
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj

# Run gizmo tests (check existing tests still pass)
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj
```

### Report Submission
**When done, submit your report to:**
`.dev/breakpoints-1/reports/BATCH-38-REPORT.md`

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Corrective Task 0** first — fix the NameSubstring lifecycle test → all 32 existing tests still pass + 1 new test
2. **P3T1** — change interface, update all implementations, update call sites, write test → all existing gizmo tests still pass + new P3T1 test passes
3. DO NOT move to next step until all previous tests pass

---

## Corrective Task 0 — NameSubstring Lifecycle Test Fix

**Required reading:** `.dev/breakpoints-1/reviews/BATCH-37-REVIEW.md` — Issue 1

**TASK-DETAIL requirement:** `LifecyclePredicateDto(NameSubstring, "EnemyTank")` must be tested.

The current `LifecyclePredicate_FiresOnBirth_AndOnDeath` test uses `EntityIdentifierType.EcsHandle`.
That test is valid and should remain (rename it to `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByHandle`).
Add a second test that exercises `NameSubstring` matching.

### Step 1: Add name component to test file

In `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointSystemStatefulTests.cs`,
add a new managed component (string field is a reference type, so it must be managed):

```csharp
/// <summary>Test managed name component for lifecycle NameSubstring tests (ID 212).</summary>
[ComponentId(212)]
internal sealed class EntityLabel
{
    public string? Name;
}
```

Register it in `Setup()`: `repo.RegisterManagedComponent<EntityLabel>();`

### Step 2: Rename existing test and add NameSubstring variant

Rename: `LifecyclePredicate_FiresOnBirth_AndOnDeath` → `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByHandle`

Add new test `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring`:

```csharp
[Fact]
public void LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring()
{
    var (manager, system, repo) = Setup();
    repo.RegisterManagedComponent<EntityLabel>();

    // Create entity and attach the label.
    var entity = repo.CreateEntity();
    repo.AddManagedComponent(entity, new EntityLabel { Name = "EnemyTank" });

    // Create a decoy entity whose name does NOT match; it must NOT trigger the breakpoint.
    var decoy = repo.CreateEntity();
    repo.AddManagedComponent(decoy, new EntityLabel { Name = "AlliedTank" });

    int hitCount = 0;
    Entity? lastHitEntity = null;
    manager.OnBreakpointHit += (_, e) => { hitCount++; lastHitEntity = e; };

    manager.Add(new Breakpoint
    {
        Id                  = BreakpointId.Invalid,
        Enabled             = true,
        OccurrenceThreshold = 1,
        DisplayName         = "LifecycleName",
        Condition           = new LifecyclePredicateDto
        {
            IdentifierType   = EntityIdentifierType.NameSubstring,
            TargetValue      = "EnemyTank",
            NameComponentType = typeof(EntityLabel),
            NamePropertyPath = "Name"
        }
    });

    // Tick 1: entity first seen → birth hit. Decoy must NOT fire.
    system.Execute(repo, 0f);
    Assert.Equal(1, hitCount);
    Assert.Equal(entity, lastHitEntity);

    manager.RequestContinue();

    // Tick 2: both entities still alive → no new birth hits.
    system.Execute(repo, 0f);
    Assert.Equal(1, hitCount);

    // Destroy the EnemyTank entity → death hit.
    repo.DestroyEntity(entity);
    system.Execute(repo, 0f);
    Assert.Equal(2, hitCount);

    // Decoy still alive — still no extra hits.
    Assert.Equal(1, hitCount - 1); // was 2 before, stays 2

    manager.RequestContinue();

    // Destroy the decoy → must NOT fire (name is "AlliedTank", not "EnemyTank").
    repo.DestroyEntity(decoy);
    system.Execute(repo, 0f);
    Assert.Equal(2, hitCount);
}
```

**Why managed component?** A struct with a `string?` field is not blittable (not `unmanaged`).
`RegisterManagedComponent<EntityLabel>()` and `AddManagedComponent` / `GetManagedComponentByTypeId`
are the correct APIs. Verify `ReadEntityName` in `DataBreakpointManager` takes the
`dto.NameComponentType.IsValueType` path for unmanaged, or the managed path for managed types.
`EntityLabel` being a `class` will take the managed path
(`repo.GetManagedComponentByTypeId(entity, typeId)`).

After Corrective Task 0: **33 total tests** should pass.

---

## Task UBP-P3T1 — IEntityStatefulGizmo Signature Change

**Design reference:** [DESIGN.md §7.2](../DESIGN.md#72-ientitystatefulgizmo-signature-change)
**Task detail:** [TASK-DETAIL.md UBP-P3T1](../TASK-DETAIL.md#ubp-p3t1--ientitystatefulgizmo-signature-change)

### Rationale

Currently gizmos cache the `ISimulationView` received at construction time. During a pause,
the system needs gizmos to render against the **pre-tick snapshot** (frozen world state) rather
than the live repo. By passing `view` into `UpdateAndDraw` each frame, the system can pass
the correct view without recreating gizmo instances.

### Step A: Extend IDataBreakpointManager

Add to `IDataBreakpointManager.cs`:
```csharp
/// <summary>
/// Returns the appropriate view for rendering: the pre-tick snapshot when paused,
/// or the live repo when running. Systems use this to feed the correct view to gizmos.
/// </summary>
ISimulationView ActiveView { get; }

/// <summary>
/// The engine tick at which the current pause was engaged. 0 when not paused.
/// Used by the temporal status banner.
/// </summary>
uint PausedTick { get; }
```

Add to `DataBreakpointManager.cs`:
```csharp
// Backing field (set in OnHit, cleared in RequestContinue/RequestStep).
private uint _pausedTick;

/// <inheritdoc/>
public ISimulationView ActiveView => _isPaused ? (ISimulationView)_preTickSnapshot : _liveRepo;

/// <inheritdoc/>
public uint PausedTick => _pausedTick;
```

In `OnHit`, after `_isPaused = true;`, add:
```csharp
_pausedTick = _preTickSnapshot.Tick;
```

In `RequestStep` and `RequestContinue`, after `_isPaused = false;`, add:
```csharp
_pausedTick = 0;
```

### Step B: Change IEntityStatefulGizmo.UpdateAndDraw signature

In `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`, change:

```csharp
// OLD:
void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder);

// NEW:
/// <summary>
/// Called once per frame for every active gizmo, regardless of focus state.
/// The gizmo emits visual primitives via <paramref name="drawBuilder"/>.
/// The <paramref name="view"/> is the manager's active view: the pre-tick snapshot
/// when a Data Breakpoint is paused, or the live repository when running.
/// </summary>
void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder drawBuilder);
```

Also update the XML doc comment (`<summary>`) to remove the old statement
"The view and entity were stored at construction time." since views are no longer cached.

**Add the using directive** for `Fdp.ModuleHost.Abstractions` if not already present
(for `ISimulationView`). Check the existing usings in the file.

### Step C: Update all concrete gizmo implementations

For EACH of the following files, change the `UpdateAndDraw` method signature from
`(float deltaTime, IDebugDrawBuilder draw)` to `(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)`.

If the gizmo currently caches `_view` (an `ISimulationView` or `EntityRepository` field
stored in the constructor), **drop that field** and replace usages inside `UpdateAndDraw`
with the `view` parameter. If the cached `_view` is also used in interaction handlers
(e.g. `OnDragUpdate`, `OnCommit`, `OnMouseEvent`), use a different strategy:
**keep the field but update it** in `UpdateAndDraw` with the passed view, or store it as
an `EntityRepository` field only used for writing (interaction handlers write to live repo;
views are only for reading in `UpdateAndDraw`).

**Use pragmatic approach**: the primary goal is the signature change and using the passed
view inside `UpdateAndDraw` for any entity-state reads. Do not over-engineer — if the
gizmo has no entity-state reads in `UpdateAndDraw` (e.g. it only uses cursor position),
just accept the parameter and ignore it.

Files to update (grep first to confirm full list):
```
FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/EntityPickerGizmo.cs
FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/PointSequenceGizmo.cs
FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/FdpLocationPickerGizmo.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityDragGizmo.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MeasureGizmo.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteWaypointGizmo.cs
Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/VertexEditGizmo.cs
Hrot/Subsystems/Hrot.SimHost/*.cs (EntityRotatorGizmo and any other IEntityStatefulGizmo)
Hrot/Editor/**/*.cs (LocationPickerGizmo, ModalBoxSelectionGizmo, ObstaclePlacementGizmo)
```

**Important:** Run `grep -r "void UpdateAndDraw" --include="*.cs"` to find ALL implementations.
Update every one found. A missed implementation causes a compile error.

### Step D: Update test mocks

For each test mock that implements `IEntityStatefulGizmo`:
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs` (MockGizmo)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoUndoStackTests.cs` (MockUndoGizmo)
- `Hrot/Runner/Hrot.ClusterRunner.Tests/DataDrivenGizmoPredicateTests.cs` (D003MockGizmo)

Change each mock's `UpdateAndDraw` signature. The mocks can continue to ignore the
`view` parameter — that's fine for test code.

**If any test mock also captures the view**, update the capture logic accordingly.
`D003MockGizmo` in `DataDrivenGizmoPredicateTests.cs` only counts calls, so the change is
purely mechanical (add `ISimulationView view` parameter).

### Step E: Update call sites in gizmo systems

In **each** of the three gizmo manager systems, update the `UpdateAndDraw` call to pass
the active view. Add an optional `IDataBreakpointManager?` parameter to each system's
constructor and store it. Then resolve the active view in `Execute`:

```csharp
ISimulationView activeView = _breakpointManager?.ActiveView ?? view;
```

#### DataDrivenGizmoSystem

Add optional constructor parameter:
```csharp
public DataDrivenGizmoSystem(
    GizmoRegistry registry,
    IDebugDrawBuilder drawBuilder,
    Func<ISimulationView, Entity, bool>? isSelectedPredicate = null,
    GizmoUndoStack? undoStack = null,
    FdpEventBus? interactionBus = null,
    IDataBreakpointManager? breakpointManager = null)     // NEW
```

Store as `private readonly IDataBreakpointManager? _breakpointManager;`

In `Execute`, at the top, compute:
```csharp
ISimulationView activeView = _breakpointManager?.ActiveView ?? view;
```

Replace ALL `gi.Instance.UpdateAndDraw(deltaTime, _drawBuilder)` calls with:
```csharp
gi.Instance.UpdateAndDraw(activeView, deltaTime, _drawBuilder);
```

Also update the "injected gizmos" path inside Execute that calls `UpdateAndDraw` on
`_injectedGizmos` entries.

#### BehaviorGizmoManagerSystem

Add optional constructor parameter:
```csharp
public BehaviorGizmoManagerSystem(
    BehaviorGizmoRegistry behaviorRegistry,
    IDebugDrawBuilder drawBuilder,
    Func<ISimulationView, Entity, bool>? isSelectedPredicate = null,
    IDataBreakpointManager? breakpointManager = null)     // NEW
```

In `Execute`, compute `activeView` and pass it to `UpdateAndDraw`.

#### GlobalGizmoManager

`GlobalGizmoManager` manages tool gizmos (placement, picker) that do not read entity state
from the repo in `UpdateAndDraw`. They use cursor positions and callbacks. The `view` parameter
will be passed but typically ignored by these gizmos. Add:

```csharp
public GlobalGizmoManager(
    IDebugDrawBuilder drawBuilder,
    FdpEventBus? interactionBus = null,
    IDataBreakpointManager? breakpointManager = null)     // NEW
```

In `Execute`:
```csharp
ISimulationView activeView = _breakpointManager?.ActiveView ?? view;
// ... in the gizmo loop:
kvp.Value.UpdateAndDraw(activeView, deltaTime, _drawBuilder);
```

### Step F: Verify all existing tests still pass

After all signature changes, run:
```powershell
dotnet build IOS-IG-SimHost.sln -c Debug
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
```

Fix all failures before proceeding to the new P3T1 test.

---

## Test for UBP-P3T1

Create new test file `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointGizmoViewTests.cs`.

Use `[Collection("ComponentRegistry")]`.

### Test: `Gizmo_RendersAgainstActiveView_ReflectsPauseState`

This test verifies that `DataDrivenGizmoSystem` (or the gizmo loop) passes the correct view
to `UpdateAndDraw` depending on pause state.

**Required:** A test-only gizmo implementation that captures the view passed to `UpdateAndDraw`.
Simulated via a mock component predicate breakpoint that causes a pause.

```csharp
[Collection("ComponentRegistry")]
public sealed class DataBreakpointGizmoViewTests
{
    // A minimal gizmo implementation that captures the view passed to UpdateAndDraw.
    private sealed class ViewCapturingGizmo : IEntityStatefulGizmo
    {
        public ISimulationView? LastView;
        public bool RequiresExclusiveFocus => false;
        public bool WantsRawInput => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool v) => IsFocused = v;
        public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder draw)
            => LastView = view;
        public void Dispose() { }
        // IGizmoInteractionHandler stubs
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool pressed, Vector3 world) { }
        public void OnKeyEvent(MapKeyboardKey key, bool pressed) { }
    }

    [Fact]
    public void Gizmo_RendersAgainstActiveView_ReflectsPauseState()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo  = new EntityRepository();
        var preTick   = new EntityRepository();
        var tc        = new MockDebugTimeController();
        var provider  = new DebugSnapshotProvider(preTick);
        var compiler  = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var manager   = new DataBreakpointManager(liveRepo, preTick, provider, tc, compiler);

        // Resolve the active view through the manager.
        // Before pause: ActiveView == liveRepo.
        Assert.Equal((ISimulationView)liveRepo, manager.ActiveView);

        // Wire a ViewCapturingGizmo into the gizmo system with the breakpoint manager.
        var drawBuilder = new NullDebugDrawBuilder(); // allocate a draw builder stub
        var registry    = new GizmoRegistry();
        var gizmoSystem = new DataDrivenGizmoSystem(registry, drawBuilder,
            breakpointManager: manager);

        // Inject the capturing gizmo for a test entity.
        var entity = liveRepo.CreateEntity();
        var capturingGizmo = new ViewCapturingGizmo();
        gizmoSystem.ActivateGizmo(entity, capturingGizmo);

        // Tick the gizmo system while NOT paused.
        gizmoSystem.Execute(liveRepo, 0f);
        // Gizmo should have received the live repo view.
        Assert.Equal((ISimulationView)liveRepo, capturingGizmo.LastView);

        // Now force a pause via manager.OnHit with a fake breakpoint.
        // Simplest approach: Add a breakpoint and call OnHit directly via test seam.
        // OR: Add a component predicate breakpoint and trigger it.
        // Simpler: use manager's internal method if it has a test seam.
        // Use the public API: add a breakpoint; trigger a component mutation that fires it.
        liveRepo.RegisterComponent<TestHealthP3>();
        var entityBP = liveRepo.CreateEntity();
        liveRepo.AddComponent(entityBP, new TestHealthP3 { Value = 0 });
        manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1,
            Condition = new PropertyMatchDto
            {
                ComponentType = typeof(TestHealthP3),
                PropertyPath  = "Value",
                Operator      = ComparisonOperator.GreaterThan,
                Target        = 0f
            }
        });

        // Mutate to trigger the breakpoint.
        liveRepo.Tick(); // advance global version so QueryDelta picks up changes
        liveRepo.GetRefRW<TestHealthP3>(entityBP).Value = 50;

        // Execute the breakpoint system.
        var bpSystem = new DataBreakpointSystem(manager);
        bpSystem.Execute(liveRepo, 0f);
        Assert.True(manager.IsPaused);

        // Now execute the gizmo system while PAUSED.
        gizmoSystem.Execute(liveRepo, 0f);
        // Gizmo should have received the pre-tick snapshot view.
        Assert.Equal((ISimulationView)manager.PreTickSnapshot, capturingGizmo.LastView);
        Assert.NotEqual((ISimulationView)liveRepo, capturingGizmo.LastView);

        // Resume and verify the gizmo returns to live view.
        manager.RequestContinue();
        gizmoSystem.Execute(liveRepo, 0f);
        Assert.Equal((ISimulationView)liveRepo, capturingGizmo.LastView);
    }
}
```

**Component for the test:**
```csharp
[ComponentId(213)]
internal struct TestHealthP3 { public float Value; }
```

**NullDebugDrawBuilder:** Create a minimal stub that implements `IDebugDrawBuilder`:
```csharp
private sealed class NullDebugDrawBuilder : IDebugDrawBuilder
{
    // Implement all methods as no-ops.
    // Check IDebugDrawBuilder interface members and add stubs.
}
```

**Note:** The test accesses `manager.PreTickSnapshot` which is an `internal` property.
The test file is in the same project as `DataBreakpointManager` OR in the test project
where `InternalsVisibleTo` is set. Check the test project setup.

The critical assertions are:
1. Before pause: `activeView == liveRepo`
2. During pause: `activeView == preTickSnapshot` (NOT liveRepo)
3. After resume: `activeView == liveRepo` again

---

## Quality Standards

**Mechanical changes (signature update):**
- Every `IEntityStatefulGizmo` concrete class and mock must compile.
- Gizmo implementations that don't use `view` in `UpdateAndDraw` just add the parameter and ignore it.
- Gizmos that previously cached `_view` for use in `UpdateAndDraw`: use `view` parameter instead (drop or nullify the cached field).

**New properties:**
- `ActiveView` must return `ISimulationView` (cast `EntityRepository` to interface).
- `PausedTick` must be 0 when not paused, tick value when paused.

**Test quality:**
- The P3T1 test verifies actual view identity (reference equality), not just "no exception".
- The test calls Execute 3 times (before pause, during pause, after resume) to verify all three state transitions.

---

## Success Criteria

- [ ] Corrective Task 0: NameSubstring lifecycle test with real `EntityLabel` class component
- [ ] Decoy entity in NameSubstring test does NOT trigger the breakpoint
- [ ] `IDataBreakpointManager` has `ActiveView` and `PausedTick`
- [ ] `DataBreakpointManager` correctly returns preTickSnapshot / liveRepo from `ActiveView`
- [ ] `IEntityStatefulGizmo.UpdateAndDraw` signature changed to include `ISimulationView view`
- [ ] ALL concrete gizmo implementations updated (solution builds with 0 errors)
- [ ] `DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem`, `GlobalGizmoManager` all pass `activeView`
- [ ] All existing gizmo tests in `Fdp.Toolkits.Tests` and `Hrot.ClusterRunner.Tests` still pass
- [ ] All 33+ breakpoints tests pass
- [ ] `Gizmo_RendersAgainstActiveView_ReflectsPauseState` test passes
- [ ] Full solution builds with 0 errors
- [ ] Report submitted at `.dev/breakpoints-1/reports/BATCH-38-REPORT.md`

---

## Developer Insights (required in report)

**Q1:** How many concrete IEntityStatefulGizmo implementations were found and updated?
List them all.

**Q2:** Which gizmos (if any) had a cached `_view` field that needed to be updated?
How was the `UpdateAndDraw` body changed?

**Q3:** Was `InternalsVisibleTo` needed for `manager.PreTickSnapshot` in the test?
How was it resolved?

**Q4:** Any unexpected compilation errors during the signature sweep?

---

## Reference Materials
- **Design §7.2:** `.dev/breakpoints-1/DESIGN.md`
- **Task spec:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P3T1
- **BATCH-37 review:** `.dev/breakpoints-1/reviews/BATCH-37-REVIEW.md`
- **IEntityStatefulGizmo definition:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`
- **IDebugDrawBuilder:** find via semantic search "IDebugDrawBuilder interface"
- **GizmoRegistry:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoRegistry.cs`
