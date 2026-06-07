# BATCH-36: Corrective Task 0 + Phase P2 — Universal Substrate (Component + Event Paths)

**Batch Number:** BATCH-36
**Tasks:** Corrective Task 0 (BATCH-35 test fixes), UBP-P2T1, UBP-P2T2
**Phase:** P2 Universal substrate (partial)
**Estimated Effort:** 14-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-35 must be complete (code is in place; tests need fixing)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Design Document:** `.dev/breakpoints-1/DESIGN.md` — §6 (Universal predicate substrate), especially §6.1 (DTO hierarchy), §6.2 (Breakpoint record), §6.3 (DataBreakpointSystem), §6.7 (Mandatory components)
3. **Task Definitions:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P2T1, UBP-P2T2
4. **Previous Review:** `.dev/breakpoints-1/reviews/BATCH-35-REVIEW.md` — All 5 issues must be resolved in Corrective Task 0

### Source Code Locations
- **New breakpoints project (P1 output):** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/`
- **New breakpoints test project (P1 output):** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/`
- **IPredicateCompiler:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs`
- **PredicateCompiler (concrete):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`
- **IEventScannerCompiler:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IEventScannerCompiler.cs`
- **EventScannerCompiler (concrete):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/EventScannerCompiler.cs`
- **EventScannerDelegate signature:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IEventScannerCompiler.cs` — `delegate void EventScannerDelegate(FdpEventBus bus, int frame, long ticks, List<SearchResultDto> results, EntityRepository repo, TargetEntityFilter? entityFilter)`
- **SearchPredicateDto hierarchy:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`
- **FdpEventBus:** search for `FdpEventBus` in `FDP/Toolkits/Fdp.Toolkits/`
- **Existing predicate compiler tests (reference patterns):** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/PredicateCompilerTests.cs`

### How to Build and Test
```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2\
dotnet build IOS-IG-SimHost.sln -c Debug

# Run relevant tests
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
```

### Report Submission
**When done, submit your report to:**
`.dev/breakpoints-1/reports/BATCH-36-REPORT.md`

**If you have questions, create:**
`.dev/breakpoints-1/questions/BATCH-36-QUESTIONS.md`

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **Corrective Task 0:** Fix all 5 test quality gaps → ALL tests pass
2. **UBP-P2T1:** Implement component-data path → Write tests → ALL tests pass
3. **UBP-P2T2:** Implement event path → Write tests → ALL tests pass

**DO NOT** move to the next task until current task tests pass. Fix all failures immediately.

---

## Corrective Task 0 — Fix BATCH-35 Test Quality Gaps

**Required reading first:** `.dev/breakpoints-1/reviews/BATCH-35-REVIEW.md`

All 5 issues in the BATCH-35 review must be fixed. No new feature work until this is done.

**Fix 1: Add UBP-P0T1 tests**

Add to `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` (or a new `TimeControllerTests.cs`):

```csharp
public sealed class EngineDebugTimeControllerTests
{
    [Fact]
    public void IEngineDebugTimeController_Implements_PauseResumeStepContract()
    {
        // Use the MockDebugTimeController (already defined in the test file) as a stand-in.
        // Assert correct state transitions.
        IEngineDebugTimeController tc = new MockDebugTimeController();

        tc.RequestPause();
        Assert.True(tc.IsPausedByDebugger);

        tc.RequestResume();
        Assert.False(tc.IsPausedByDebugger);

        tc.RequestPause();
        tc.RequestStepOneTick();
        Assert.False(tc.IsPausedByDebugger); // stepping clears pause
    }

    [Fact]
    public void IBlueprintTimeController_Still_Resolves_Through_Inheritance()
    {
        // IBlueprintTimeController must be a subtype of IEngineDebugTimeController.
        Assert.True(typeof(IEngineDebugTimeController).IsAssignableFrom(typeof(IBlueprintTimeController)));

        // An object assignable to IBlueprintTimeController is assignable to IEngineDebugTimeController.
        MockDebugTimeController tc = new MockDebugTimeController();
        IEngineDebugTimeController debugTc = tc; // must compile
        Assert.NotNull(debugTc);
    }
}
```

**Fix 2: Strengthen `GateOn_ExecuteRuns_WithoutException`**

Replace the existing test with one that verifies actual snapshot state:

```csharp
[Fact]
public void GateOn_SyncsSnapshotFromLiveRepo()
{
    ComponentTypeRegistry.Clear();
    var snapshot = new EntityRepository();
    var live     = new EntityRepository();

    // Register and add a test component.
    live.RegisterComponent<TestHealth>();
    snapshot.RegisterComponent<TestHealth>();

    var entity = live.CreateEntity();
    live.AddComponent(entity, new TestHealth { Current = 42 });

    var provider = new DebugSnapshotProvider(snapshot);
    provider.SetEnabled(true);

    provider.Execute(live, 0f);

    // Snapshot must contain the entity and component value.
    Assert.True(snapshot.HasComponent<TestHealth>(entity));
    Assert.Equal(42, snapshot.GetComponent<TestHealth>(entity).Current);
}
```

(Define `[ComponentId(200)] struct TestHealth { public int Current; }` as a test-only component in the test file.)

**Fix 3: Strengthen `OnHit_PerformsTripleBufferRewind_AndFiresEvents`**

The test must assert actual repository states. After calling `OnHit`:
- `_postTickSnapshot` must contain the value that was in `_liveRepo` at hit time.
- `_liveRepo` must be rewound to the `_preTickSnapshot` value.

Add internal test-seam properties to `DataBreakpointManager`:
```csharp
internal EntityRepository PreTickSnapshot => _preTickSnapshot;
internal EntityRepository PostTickSnapshot => _postTickSnapshot;
```

Then rewrite the test:
```csharp
[Fact]
public void OnHit_PerformsTripleBufferRewind_AndStateIsCorrect()
{
    ComponentTypeRegistry.Clear();
    var liveRepo        = new EntityRepository();
    var preTickSnapshot = new EntityRepository();
    liveRepo.RegisterComponent<TestHealth>();
    preTickSnapshot.RegisterComponent<TestHealth>();

    // Create entity in live + set pre-tick state
    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new TestHealth { Current = 100 });
    // Manually fill preTickSnapshot as if provider captured it at start of tick
    preTickSnapshot.SyncFrom(liveRepo);

    // Simulate mid-tick mutation: live repo changes to 50
    ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity);
    h.Current = 50;

    var tc               = new MockDebugTimeController();
    var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
    var manager          = new DataBreakpointManager(liveRepo, preTickSnapshot, snapshotProvider, tc);

    var id = manager.Add(new Breakpoint
    {
        Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "T"
    });
    var bp = manager.AllBreakpoints[0];

    manager.OnHit(bp, entity);

    // (a) postTickSnapshot captured live (50) at hit time
    Assert.Equal(50, manager.PostTickSnapshot.GetComponent<TestHealth>(entity).Current);
    // (b) liveRepo rewound to pre-tick (100)
    Assert.Equal(100, liveRepo.GetComponent<TestHealth>(entity).Current);
    // (c) clock paused
    Assert.True(tc.IsPausedByDebugger);
    Assert.True(manager.IsPaused);
}
```

**Fix 4: Strengthen `RequestStep`/`RequestContinue` to verify repo state**

After `RequestStep()`, `_liveRepo` must contain the post-tick values (restored from `_postTickSnapshot`). Add assertion:

```csharp
[Fact]
public void RequestStep_RestoresLiveRepoToPostTickState()
{
    // Setup same as Fix 3 test above...
    // After OnHit: liveRepo == preTickSnapshot (100)
    // After RequestStep: liveRepo must == postTickSnapshot (50)
    manager.RequestStep();
    Assert.Equal(50, liveRepo.GetComponent<TestHealth>(entity).Current);
}
```

Same pattern for `RequestContinue_RestoresLiveRepoToPostTickState`.

**Fix 5: Add `GateOff_Execute_ZeroAllocations` test**

```csharp
[Fact]
public void GateOff_Execute_ZeroAllocations()
{
    var snapshot = new EntityRepository();
    var provider = new DebugSnapshotProvider(snapshot);
    var live = new EntityRepository();

    // Gate stays off. Warm-up JIT.
    provider.Execute(live, 0f);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetTotalMemory(false);

    const int Iterations = 10_000;
    for (int i = 0; i < Iterations; i++)
        provider.Execute(live, 0f);

    long after = GC.GetTotalMemory(false);
    Assert.Equal(0L, after - before);
}
```

After all 5 fixes, run the full test suite and confirm all previously passing tests still pass.

---

## Task UBP-P2T1 — `DataBreakpointSystem` (Component-Data Path)

**Design reference:** [DESIGN.md §6.3](../DESIGN.md#63-databreakpointsystem), [§6.7](../DESIGN.md#67-mandatory-components-optimisation)
**Task detail:** [TASK-DETAIL.md UBP-P2T1](../TASK-DETAIL.md#ubp-p2t1--databreakpointsystem-component-data-path)

### Manager extensions required

Extend `IDataBreakpointManager` and `DataBreakpointManager` with the compilation pipeline needed by `DataBreakpointSystem`:

1. Add `IPredicateCompiler` and `IEventScannerCompiler` as constructor parameters to `DataBreakpointManager`.

2. When a breakpoint is `Add`ed with a non-null `Condition` of type `PropertyMatchDto`, `CompoundPredicateDto`, or `BehaviorParamPredicateDto`, compile it via `_predicateCompiler.CompileComponentPredicate(condition)` and store in a `Dictionary<BreakpointId, CompiledComponentPredicate>`. The `CompiledComponentPredicate` record should hold:
   - `Delegate: Func<EntityRepository, Entity, bool>`
   - `MandatoryComponents: IReadOnlyList<Type>` (from `ExtractMandatoryComponents`)

3. When a breakpoint is `Remove`d or `SetEnabled(false)`, unmount its compiled delegate.

4. Add to `IDataBreakpointManager`:
   ```csharp
   IReadOnlyList<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)> MountedComponentPredicates { get; }
   bool HasMountedDelegates { get; }
   ```

5. Add a public `OnHit` method to `IDataBreakpointManager` (it was already public on `DataBreakpointManager` but not on the interface — add it to the interface now):
   ```csharp
   void OnHit(Breakpoint bp, Entity entity);
   ```

### `DataBreakpointSystem` implementation

Create `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs`:

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class DataBreakpointSystem : IEcsModuleSystem
{
    private readonly IDataBreakpointManager _manager;

    public DataBreakpointSystem(IDataBreakpointManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!_manager.HasMountedDelegates) return;

        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"DataBreakpointSystem requires EntityRepository, got {view?.GetType().Name ?? "null"}.");

        foreach (var (bp, compiled) in _manager.MountedComponentPredicates)
        {
            // Build query from mandatory components
            var queryBuilder = repo.Query();
            foreach (var t in compiled.MandatoryComponents)
                queryBuilder.With(t);
            var query = queryBuilder.Build();

            uint sinceVersion = 0; // scan all entities on every tick for now
            repo.QueryDelta(query, sinceVersion, entity =>
            {
                if (bp.FilterEntity is { } filterEntity && filterEntity != entity) return;
                if (!compiled.Delegate(repo, entity)) return;
                _manager.OnHit(bp, entity);
            });
        }
    }
}
```

**Note on `sinceVersion`:** For now, pass `sinceVersion = 0` so all entities are scanned each tick. The optimization to track the last-scanned version per breakpoint is a future enhancement (add a TODO comment). The correctness tests do not depend on delta-optimization.

**Note on `Query().With(Type)`:** Check if `QueryBuilder.With(Type)` exists (it may be `With<T>()` only). If a non-generic overload is not available, use reflection or a helper to register components by type. Look at existing code in `RecordingSearchService` for the pattern used there.

### Tests for UBP-P2T1

Add to the test project. Define a simple `[ComponentId(201)] struct TestDamage { public float Value; }` component for use in tests.

```csharp
public sealed class DataBreakpointSystemTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        // Provide minimal compiler stubs or use real PredicateCompiler
        // (Use real PredicateCompiler if it's easily instantiated without heavy deps)
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var system        = new DataBreakpointSystem(manager);
        return (manager, system, repo);
    }

    // Test: DataBreakpointSystem_NoBreakpoints_DoesNoWork
    // Setup: empty manager; call Execute; assert no exception, IsPaused == false.
    [Fact]
    public void NoBreakpoints_DoesNoWork()
    {
        // ...
    }

    // Test: DataBreakpointSystem_PropertyMatchDto_FiresWhenConditionMet
    // Setup: register entity with TestDamage.Value = 5.0f
    // Register breakpoint: PropertyMatchDto(TestDamage.Value < 10)
    // Call Execute with live repo
    // Assert: manager.IsPaused == true, OnBreakpointHit fired
    [Fact]
    public void PropertyMatch_FiresWhenConditionMet()
    {
        // ...
    }

    // Test: DataBreakpointSystem_FilterEntity_ScopesPredicateToOneEntity
    // Register two entities (e1 with Value=5, e2 with Value=5)
    // Register breakpoint with FilterEntity = e1, condition: Value < 10
    // Execute; fire should only come from e1 — assert exactly 1 hit
    // Then unregister, register with FilterEntity = e2 pointing at e2 with Value=15 (no hit)
    // Actually simpler: register bp scoped to e1; mutate e2's Value to 5; assert no hit
    [Fact]
    public void FilterEntity_ScopesPredicateToOneEntity()
    {
        // ...
    }

    // Test: DataBreakpointSystem_OccurrenceThreshold_PausesOnNthHit
    // threshold=3; add entity with Value=5 matching condition
    // Execute 3 times (no re-arming needed for this simple test)
    // Assert: paused only after 3rd Execute
    [Fact]
    public void OccurrenceThreshold_PausesOnNthHit()
    {
        // threshold=3; fire 3 Execute calls; pause only on 3rd
        // ...
    }
}
```

**Important for tests:**
- Use a real `PredicateCompiler` instance if it can be constructed without complex dependencies. Look at `PredicateCompilerTests.cs` for how it's set up in tests — it uses `new PredicateCompiler(new ComponentEditServiceBuilder().Build())`.
- The `ComponentTypeRegistry.Clear()` call is critical at the start of each test to avoid component ID collisions.
- Register components (`repo.RegisterComponent<TestDamage>()`) before adding entities.
- Each test should be independent — set up a fresh manager/repo.

---

## Task UBP-P2T2 — `DataBreakpointSystem` (Event Path)

**Design reference:** [DESIGN.md §6.3](../DESIGN.md#63-databreakpointsystem)
**Task detail:** [TASK-DETAIL.md UBP-P2T2](../TASK-DETAIL.md#ubp-p2t2--databreakpointsystem-event-path)

### Manager extensions for event scanners

1. Define `CompiledEventScanner` as a helper record that wraps `EventScannerDelegate` and exposes a clean `bool Evaluate(FdpEventBus bus, EntityRepository repo)` method — matching the DESIGN §6.3 pseudocode `if (scanner.Evaluate(bus))`. Hold a private `List<SearchResultDto>` buffer inside the record (allocated once at construction) to avoid per-call allocation:

   ```csharp
   public sealed record CompiledEventScanner(EventScannerDelegate Delegate)
   {
       private readonly List<SearchResultDto> _buffer = new(4);

       public bool Evaluate(FdpEventBus bus, EntityRepository repo)
       {
           _buffer.Clear();
           Delegate(bus, 0, 0L, _buffer, repo, null);
           return _buffer.Count > 0;
       }
   }
   ```

2. When a breakpoint is added with `Condition` of type `TransientEventPredicateDto`, compile it via `_eventScannerCompiler.CompileScanner(dto)`, wrap in `CompiledEventScanner`, and store in a `Dictionary<BreakpointId, CompiledEventScanner>`.

3. Add to `IDataBreakpointManager`:
   ```csharp
   IReadOnlyList<(Breakpoint Breakpoint, CompiledEventScanner Scanner)> MountedEventScanners { get; }
   ```

### `DataBreakpointSystem` extension for events

Update the constructor to also accept `FdpEventBus`:
```csharp
public DataBreakpointSystem(IDataBreakpointManager manager, FdpEventBus bus)
```

Add a second loop to `DataBreakpointSystem.Execute` following the DESIGN §6.3 pseudocode:

```csharp
// Event path
foreach (var (bp, scanner) in _manager.MountedEventScanners)
{
    if (scanner.Evaluate(_bus, repo))
        _manager.OnHit(bp, Entity.Null);
}
```

The `HasMountedDelegates` gate must return `true` if either `MountedComponentPredicates` or `MountedEventScanners` is non-empty.

### Tests for UBP-P2T2

```csharp
public sealed class DataBreakpointSystemEventTests
{
    // Test: Bus_AnyOccurrence_Predicate_FiresOnAnyEventOfType
    // - Register AnyOccurrence breakpoint for a test event type HitTestEvent
    // - Publish HitTestEvent to bus
    // - Execute system
    // - Assert manager.IsPaused == true
    [Fact]
    public void Bus_AnyOccurrence_Predicate_FiresOnAnyEventOfType()
    {
        // ...
    }

    // Test: Bus_PayloadConstraint_FiresOnlyWhenPayloadMatches
    // - Register breakpoint: HitTestEvent.Damage > 50
    // - Publish HitTestEvent { Damage = 40 } → no hit
    // - Then publish HitTestEvent { Damage = 80 } → hit
    // - Assert IsPaused == true after the second Execute, not the first
    [Fact]
    public void Bus_PayloadConstraint_FiresOnlyWhenPayloadMatches()
    {
        // ...
    }
}

[ComponentId(202)]
[Flags]
struct HitTestEvent { public float Damage; }
```

**Note:** `EventScannerCompiler` requires an `IComponentEditService`. Look at how tests instantiate it — use `new EventScannerCompiler(new ComponentEditServiceBuilder().Build())`.

The `FdpEventBus` must be published to between tick boundaries. Look at how existing tests publish events using `bus.Publish<T>(event)` or the view's `PublishEvent`.

---

## Quality Standards

**Test Quality:**
- Every test must assert actual state values, not just "no exception" or "flag is set".
- Event tests must verify the FdpEventBus is actually scanned by observing hit/no-hit behavior.
- Each test has a positive case AND a negative case (e.g., condition not met → no pause).

**Code Quality:**
- No magic numbers. Name any constants.
- `DataBreakpointSystem.Execute` must return early (`HasMountedDelegates == false`) without touching the repo.
- The shared result buffer for event scanning must be pre-allocated in the constructor, not per-tick.
- Throw on wrong view type (as established in BATCH-35).

**Do not stop mid-batch to ask for permission** for anything obvious. Implement, test, fix, submit report.

---

## Success Criteria

- [ ] All 5 Corrective Task 0 fixes implemented; previous 16 tests still pass + new strengthened tests pass
- [ ] `CompiledComponentPredicate` record defined
- [ ] `DataBreakpointManager` compiles predicates on `Add` and exposes `MountedComponentPredicates`
- [ ] `DataBreakpointSystem` implements component-data loop with `QueryDelta`
- [ ] Event scanner loop added to `DataBreakpointSystem`
- [ ] `DataBreakpointManager` compiles event scanners on `Add` and exposes `MountedEventScanners`
- [ ] All required tests from TASK-DETAIL UBP-P2T1 and UBP-P2T2 pass
- [ ] Full solution builds with 0 errors
- [ ] Report submitted at `.dev/breakpoints-1/reports/BATCH-36-REPORT.md`

---

## Developer Insights (required in report)

**Q1:** What issues did you encounter implementing the compilation pipeline? How did you resolve them?

**Q2:** What edge cases did you discover around the event scanner in live mode vs. replay mode?

**Q3:** What design decisions did you make beyond the spec (e.g., query builder non-generic With, event bus access pattern)?

**Q4:** What performance concerns did you observe?

---

## Reference Materials
- **Design:** `.dev/breakpoints-1/DESIGN.md` — §6.3 (DataBreakpointSystem pseudocode), §6.7 (mandatory components)
- **Task Defs:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P2T1, UBP-P2T2
- **Previous review:** `.dev/breakpoints-1/reviews/BATCH-35-REVIEW.md`
- **PredicateCompiler tests (usage patterns):** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/PredicateCompilerTests.cs`
- **EventScannerCompiler tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/EventScannerCompilerTests.cs`
- **Code standards:** `.github/skills/CODE-STANDARDS.md`
