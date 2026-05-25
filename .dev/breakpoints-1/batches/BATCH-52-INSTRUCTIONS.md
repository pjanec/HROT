# BATCH-52 Instructions

**Scope:** P11T8 (`StageMutation` size fix), P11T10 (reflection-free spatial position), P11T13 (lifecycle `NetworkId` resolution)

**Design reference:** [DESIGN.md](../DESIGN.md) §6.4, §6.5, §6.8; [TASK-DETAIL.md](../TASK-DETAIL.md) #ubp-p11t8, #ubp-p11t10, #ubp-p11t13

---

## Context

Three remaining correctness/performance fixes in `DataBreakpointManager.cs`:

1. **P11T8** — `StageMutation` stores `Marshal.SizeOf(componentType)` which can differ from the ECS chunk stride (`Unsafe.SizeOf<T>()`) for components with `fixed` buffers. The ECB needs the CLR managed size (not the interop size) to write the correct number of bytes.
2. **P11T10** — `ReadPosition2D` and `ReadFloatField` run reflection (`Marshal.PtrToStructure`, `FieldInfo.GetValue`) per entity per tick. Replace with a compiled `Func<EntityRepository, Entity, Vector2>` built once at spatial-BP mount time (mirrors `PredicateCompiler.BuildUnmanagedMatcher<T>`).
3. **P11T13** — `MatchesLifecycleCriteria` returns `false` silently for `EntityIdentifierType.NetworkId` (no comment, silent failure). Replace with `throw new NotSupportedException(...)` (Option B per TASK-DETAIL) so the failure is visible to developers.

All changes are in `DataBreakpointManager.cs` only (plus tests).

---

## Task 1 — P11T8: `StageMutation` size resolution via ECS registry

### What to change

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

**Goal:** Replace `Marshal.SizeOf(componentType)` with `Unsafe.SizeOf<T>()` via cached generic invocation.

`ComponentType<T>.Size` (at `FDP/Engine/Fdp.Core/ComponentType.cs`) returns `Unsafe.SizeOf<T>()` which is the CLR managed size (= ECS chunk stride). `Marshal.SizeOf` returns the interop layout size, which differs for components with `fixed` arrays (e.g. `BTreeTraceWorkingMemory1024` with `fixed byte Buffer[1016]`).

**Step 1:** Add a static cached-size dictionary near the other private fields:

```csharp
// Cache of component type → CLR managed size (Unsafe.SizeOf<T>() via ComponentType<T>.Size).
// Avoids repeated reflection on the hot path.
private static readonly Dictionary<Type, int> _componentSizeCache = new();
```

**Step 2:** Add a private static helper method:

```csharp
/// <summary>
/// Returns the CLR managed size of <paramref name="type"/> in bytes.
/// Uses <c>ComponentType&lt;T&gt;.Size</c> (= <c>Unsafe.SizeOf&lt;T&gt;()</c>) rather than
/// <c>Marshal.SizeOf</c>, which gives the interop layout size that may differ for
/// components containing <c>fixed</c> buffers or bool fields with <c>[MarshalAs(UnmanagedType.I1)]</c>.
/// </summary>
private static int GetEcsComponentSize(Type type)
{
    lock (_componentSizeCache)
    {
        if (_componentSizeCache.TryGetValue(type, out int cached))
            return cached;
        // ComponentType<T>.Size = Unsafe.SizeOf<T>() — matches the ECS chunk stride.
        var genericType = typeof(ComponentType<>).MakeGenericType(type);
        var prop        = genericType.GetProperty("Size",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        int size        = (int)prop.GetValue(null)!;
        _componentSizeCache[type] = size;
        return size;
    }
}
```

Note: `ComponentType<T>` has an `unmanaged` constraint, so this only works for value types. The caller already guards with `if (!componentType.IsValueType)` (via `isManaged`).

**Step 3:** In `StageMutation`, replace:

```csharp
int sizeBytes  = isManaged ? 0 : Marshal.SizeOf(componentType);
```

with:

```csharp
int sizeBytes  = isManaged ? 0 : GetEcsComponentSize(componentType);
```

You may also remove the `using System.Runtime.InteropServices;` import if `Marshal` is no longer used elsewhere in the file. **Check first** — `Marshal` is also used in `ReadPosition2D` (for `Marshal.PtrToStructure`). After P11T10 removes `ReadPosition2D`, re-check whether the import can be removed.

---

## Task 2 — P11T10: Reflection-free spatial position read

### What to change

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

The current `ReadPosition2D` / `ReadFloatField` methods use:
1. `Marshal.PtrToStructure(ptr, compType)` — allocates a boxed struct per entity per tick
2. `FieldInfo.GetValue(obj)` — reflection per field path segment, per entity per tick

Replace with a compiled `Func<EntityRepository, Entity, Vector2>` accessor per spatial tracker, built once at mount time. Pattern mirrors `PredicateCompiler.BuildUnmanagedMatcher<T>` in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`.

#### Step 1: Add a delegate type

Add near the top of `DataBreakpointManager.cs` (below the existing `CompiledEventScanner` class, before the `DataBreakpointManager` class):

```csharp
/// <summary>Delegate type for compiled spatial-position accessors.</summary>
internal delegate Vector2 SpatialPositionDelegate<T>(ref T component) where T : unmanaged;
```

#### Step 2: Change `_spatialTrackers` to include the compiled accessor

Change the field declaration from:

```csharp
private readonly Dictionary<BreakpointId, (Breakpoint bp, SpatialBoundingPredicateDto dto, HashSet<Entity> insideSet)>
    _spatialTrackers = new();
```

to:

```csharp
private readonly Dictionary<BreakpointId, (Breakpoint bp, SpatialBoundingPredicateDto dto, HashSet<Entity> insideSet, Func<EntityRepository, Entity, Vector2>? posAccessor)>
    _spatialTrackers = new();
```

#### Step 3: Add compiled accessor helper methods

Add the following two private static methods to `DataBreakpointManager`. Place them near the existing `ReadPosition2D` / `ReadFloatField` helpers:

```csharp
/// <summary>
/// Builds a compiled position accessor for unmanaged component type <paramref name="dto.PositionComponentType"/>.
/// Returns null if the type is null, not a value type, or its field paths cannot be resolved.
/// </summary>
private static Func<EntityRepository, Entity, Vector2>? CompileSpatialPositionAccessor(
    SpatialBoundingPredicateDto dto)
{
    Type? compType = dto.PositionComponentType;
    if (compType == null || !compType.IsValueType) return null;

    int typeId = ComponentTypeRegistry.GetId(compType);
    if (typeId < 0) return null;

    try
    {
        var method = typeof(DataBreakpointManager)
            .GetMethod(nameof(CompileSpatialPositionAccessorGeneric),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(compType);
        return (Func<EntityRepository, Entity, Vector2>)method.Invoke(null, new object[] { dto, typeId })!;
    }
    catch
    {
        return null; // Fall back to reflection-based ReadPosition2D if compilation fails.
    }
}

private static unsafe Func<EntityRepository, Entity, Vector2>
    CompileSpatialPositionAccessorGeneric<T>(SpatialBoundingPredicateDto dto, int typeId)
    where T : unmanaged
{
    // Build an expression tree:  (ref T comp) => new Vector2(comp.XPath, comp.YPath)
    var param = Expression.Parameter(typeof(T).MakeByRefType(), "comp");

    Expression xExpr = param;
    foreach (string seg in dto.PositionXPath.Split('.'))
        xExpr = Expression.PropertyOrField(xExpr, seg);
    if (xExpr.Type != typeof(float))
        xExpr = Expression.Convert(xExpr, typeof(float));

    Expression yExpr = param;
    foreach (string seg in dto.PositionYPath.Split('.'))
        yExpr = Expression.PropertyOrField(yExpr, seg);
    if (yExpr.Type != typeof(float))
        yExpr = Expression.Convert(yExpr, typeof(float));

    var ctor     = typeof(Vector2).GetConstructor(new[] { typeof(float), typeof(float) })!;
    var bodyExpr = Expression.New(ctor, xExpr, yExpr);
    var accessor = Expression.Lambda<SpatialPositionDelegate<T>>(bodyExpr, param).Compile();

    return (repo, entity) =>
    {
        if (!repo.HasComponentByTypeId(entity, typeId)) return Vector2.Zero;
        ref readonly T comp = ref repo.GetComponentRO<T>(entity);
        return accessor(ref Unsafe.AsRef(in comp));
    };
}
```

Note: `Expression` is from `System.Linq.Expressions`. Add `using System.Linq.Expressions;` at the top of the file if not already present. Also ensure `using System.Runtime.CompilerServices;` is present for `Unsafe.AsRef`.

#### Step 4: Use compiled accessor at mount time

In `TryMountDelegate`, change the `case SpatialBoundingPredicateDto spatialDto:` branch from:

```csharp
case SpatialBoundingPredicateDto spatialDto:
    _spatialTrackers[id] = (bp, spatialDto, new HashSet<Entity>());
    break;
```

to:

```csharp
case SpatialBoundingPredicateDto spatialDto:
    _spatialTrackers[id] = (bp, spatialDto, new HashSet<Entity>(),
        CompileSpatialPositionAccessor(spatialDto));
    break;
```

#### Step 5: Use compiled accessor in `EvaluateSpatialTrackers`

Find `EvaluateSpatialTrackers`. Replace:

```csharp
foreach (var (bpId, (bp, dto, insideSet)) in _spatialTrackers)
```

with:

```csharp
foreach (var (bpId, (bp, dto, insideSet, posAccessor)) in _spatialTrackers)
```

And replace:

```csharp
Vector2 pos = ReadPosition2D(repo, entity, dto);
```

with:

```csharp
Vector2 pos = posAccessor != null
    ? posAccessor(repo, entity)
    : ReadPosition2D(repo, entity, dto);  // fallback for managed components
```

Keep the `ReadPosition2D` / `ReadFloatField` methods as private fallbacks (they're still needed for managed-component cases, and for safety). Do NOT delete them.

After making this change, confirm that the existing spatial tests still pass.

---

## Task 3 — P11T13: Lifecycle `NetworkId` resolution

### What to change

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

`MatchesLifecycleCriteria` has a silent `_ => false` for `EntityIdentifierType.NetworkId`. Replace with a `NotSupportedException` per Option B in TASK-DETAIL §ubp-p11t13.

Find the `MatchesLifecycleCriteria` method and replace:

```csharp
// Network-id lookup not available without network module injection; skip.
_ => false
```

with:

```csharp
// Network-id lookup requires INetworkEntityMap, which is not injected into this manager.
// To support this, pass an INetworkEntityMap to the DataBreakpointManager constructor
// and resolve the entity in this branch. Until then, using NetworkId as identifier will throw.
EntityIdentifierType.NetworkId => throw new NotSupportedException(
    "LifecyclePredicateDto with EntityIdentifierType.NetworkId requires an INetworkEntityMap " +
    "injected into DataBreakpointManager. Wire the network map via the constructor, " +
    "or use EcsHandle or NameSubstring instead."),
_ => false
```

Also update the `MatchesLifecycleCriteria` XML doc to mention the NotSupportedException.

---

## Tests to write

### Location: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/P11CorrectnessTests.cs` (NEW FILE)

Use `[Collection("ComponentRegistry")]` on all test classes.

#### Class 1: `StageMutationSizeTests`

**Test 1: `StageMutation_SimpleStruct_SizeMatchesUnsafeSizeOf`**

```csharp
[Fact]
public void StageMutation_SimpleStruct_SizeMatchesUnsafeSizeOf()
{
    ComponentTypeRegistry.Clear();
    var (manager, _, _, _) = ManagerFactory.Create();
    // TestHealth is registered in ManagerFactory.Create() or at test start

    var entity = new Entity(1, 0);
    var value  = new TestHealth { Current = 42 };
    manager.StageMutation(entity, typeof(TestHealth), value);

    var mutation = manager.PendingMutationsQueue.Peek();
    int expected = System.Runtime.CompilerServices.Unsafe.SizeOf<TestHealth>();
    Assert.Equal(expected, mutation.SizeBytes);
}
```

**Test 2: `StageMutation_UsesUnsafeSizeOf_NotMarshalSizeOf`**

For simple structs without `fixed` buffers, `Unsafe.SizeOf<T>()` and `Marshal.SizeOf<T>()` are equal. This test documents the contract:

```csharp
[Fact]
public void StageMutation_StagedSize_EqualsManagedSize_NotInteropSize()
{
    ComponentTypeRegistry.Clear();
    var (manager, _, _, _) = ManagerFactory.Create();

    var entity = new Entity(1, 0);
    manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 10 });

    var mutation = manager.PendingMutationsQueue.Peek();
    // The stored size must equal Unsafe.SizeOf<T>() (CLR managed / chunk stride),
    // NOT Marshal.SizeOf<T>() (interop layout — may differ for fixed-buffer components).
    int clrSize     = System.Runtime.CompilerServices.Unsafe.SizeOf<TestHealth>();
    Assert.Equal(clrSize, mutation.SizeBytes);
    // For TestHealth (simple int field), both sizes happen to be equal.
    // For fixed-buffer components like BTreeTraceWorkingMemory1024, they would differ.
}
```

#### Class 2: `SpatialPositionAccessorTests`

These tests verify that the compiled position accessor works correctly.

**Test 1: `SpatialTracker_CompiledAccessor_ReturnsCorrectPosition`**

Setup a spatial BP with a `TestPosition2D` component (see note below) and verify the compiled accessor returns the right `Vector2`.

Since writing a test for expression-tree compilation requires a component with float X/Y fields, use a file-scoped component:

```csharp
[ComponentId(260)]
file struct TestPosition2D
{
    public float X;
    public float Y;
}
```

Test body:
```csharp
[Fact]
public void SpatialTracker_CompiledAccessor_ReturnsCorrectPosition()
{
    ComponentTypeRegistry.Clear();
    var liveRepo   = new EntityRepository();
    var preTick    = new EntityRepository();
    var tc         = new MockDebugTimeController();
    var provider   = new DebugSnapshotProvider(preTick);
    var manager    = new DataBreakpointManager(liveRepo, preTick, provider, tc);

    liveRepo.RegisterComponent<TestPosition2D>();

    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new TestPosition2D { X = 3.0f, Y = 7.5f });

    // Register a spatial BP using TestPosition2D
    var bpId = manager.AddBreakpoint(new SpatialBoundingPredicateDto
    {
        PositionComponentType = typeof(TestPosition2D),
        PositionXPath         = "X",
        PositionYPath         = "Y",
        MinX = 0f, MaxX = 10f,
        MinY = 0f, MaxY = 10f,
        AuthorityRequirement = AuthorityRequirement.AnyAuthority,
    });

    // Evaluate: entity at (3, 7.5) is inside the bounds [0–10, 0–10]
    manager.EvaluateStatefulBreakpoints(liveRepo);

    Assert.True(manager.IsPaused, "Entity at (3, 7.5) should be inside [0–10, 0–10] bounds");
}
```

**Test 2: `SpatialTracker_CompiledAccessor_DoesNotFireOutsideBounds`**

```csharp
[Fact]
public void SpatialTracker_CompiledAccessor_DoesNotFireOutsideBounds()
{
    ComponentTypeRegistry.Clear();
    var liveRepo = new EntityRepository();
    var preTick  = new EntityRepository();
    var tc       = new MockDebugTimeController();
    var provider = new DebugSnapshotProvider(preTick);
    var manager  = new DataBreakpointManager(liveRepo, preTick, provider, tc);

    liveRepo.RegisterComponent<TestPosition2D>();

    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new TestPosition2D { X = 50.0f, Y = 50.0f }); // outside

    manager.AddBreakpoint(new SpatialBoundingPredicateDto
    {
        PositionComponentType = typeof(TestPosition2D),
        PositionXPath         = "X",
        PositionYPath         = "Y",
        MinX = 0f, MaxX = 10f,
        MinY = 0f, MaxY = 10f,
        AuthorityRequirement = AuthorityRequirement.AnyAuthority,
    });

    manager.EvaluateStatefulBreakpoints(liveRepo);

    Assert.False(manager.IsPaused, "Entity at (50, 50) should be outside [0–10, 0–10] bounds");
}
```

**Note:** Check how `SpatialBoundingPredicateDto` is defined to confirm `MinX/MaxX/MinY/MaxY` field names. Read the DTO definition from the codebase before writing the test (look in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchDtos.cs` or similar).

#### Class 3: `LifecycleNetworkIdTests`

**Test 1: `Lifecycle_NetworkId_NoMapWired_ThrowsNotSupportedException`**

```csharp
[Fact]
public void Lifecycle_NetworkId_NoMapWired_ThrowsNotSupportedException()
{
    ComponentTypeRegistry.Clear();
    var liveRepo = new EntityRepository();
    var preTick  = new EntityRepository();
    var tc       = new MockDebugTimeController();
    var provider = new DebugSnapshotProvider(preTick);
    var manager  = new DataBreakpointManager(liveRepo, preTick, provider, tc);

    liveRepo.RegisterComponent<TestHealth>();

    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new TestHealth { Current = 10 });

    manager.AddBreakpoint(new LifecyclePredicateDto
    {
        IdentifierType = EntityIdentifierType.NetworkId,
        TargetValue    = "42",
        EventType      = LifecycleEventType.Spawned,
    });

    Assert.Throws<NotSupportedException>(() =>
        manager.EvaluateStatefulBreakpoints(liveRepo));
}
```

---

## Required imports for the new test file

```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;
```

You may also need to import the DTO namespace (check where `SpatialBoundingPredicateDto` and `LifecyclePredicateDto` are defined — likely `Fdp.Toolkit.ReplayBrowser.Search` or a sub-namespace).

---

## Reference: Existing spatial test in the codebase

Before implementing, read the existing spatial breakpoint test:
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointSystemStatefulTests.cs` — look for spatial predicate tests to understand what component types and DTO fields are used.

Also read:
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` lines 755–895 — the current `EvaluateSpatialTrackers`, `ReadPosition2D`, `ReadFloatField` code
- The `SpatialBoundingPredicateDto` definition to confirm field names

---

## Build & test commands

```
dotnet build IOS-IG-SimHost.sln -v quiet
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~BreakpointSubsystemWiring"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

---

## Checklist

- [ ] `DataBreakpointManager._componentSizeCache` static dict added
- [ ] `GetEcsComponentSize(Type)` helper method added
- [ ] `StageMutation`: `Marshal.SizeOf` replaced with `GetEcsComponentSize`
- [ ] `SpatialPositionDelegate<T>` delegate type added
- [ ] `_spatialTrackers` value type extended to include `posAccessor`
- [ ] `CompileSpatialPositionAccessor(dto)` method added (dispatches to generic version via reflection)
- [ ] `CompileSpatialPositionAccessorGeneric<T>(dto, typeId)` method added (expression tree)
- [ ] `TryMountDelegate` case for `SpatialBoundingPredicateDto` updated to compile and store accessor
- [ ] `EvaluateSpatialTrackers` uses compiled accessor when non-null, falls back to `ReadPosition2D`
- [ ] `MatchesLifecycleCriteria`: `_ => false` for NetworkId replaced with `throw new NotSupportedException(...)`
- [ ] `using System.Linq.Expressions;` added if not present
- [ ] `P11CorrectnessTests.cs` created with all tests
- [ ] All tests pass
- [ ] Build: 0 errors
