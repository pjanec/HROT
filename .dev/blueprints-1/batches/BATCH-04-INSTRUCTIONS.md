# BATCH-04: Phase 1 Test Harness -- BlueprintTestFixture + ALC Lifecycle

**Batch Number:** BATCH-04
**Tasks:** Corrective Task 0 (P2 fixes from BATCH-03), TASK-TH-003, TASK-TH-005
**Phase:** Phase 1 -- Test Harness (Part 2 of 3)
**Estimated Effort:** 18-22 hours
**Priority:** HIGH
**Dependencies:** BATCH-03 committed (TH-008, TH-009, foundation stubs in place)

---

## Onboarding & Workflow

### Developer Instructions

This batch has two parts:
1. **Corrective Task 0** -- Fix two P2 defects from BATCH-03 (small, do these first).
2. **TASK-TH-003 + TASK-TH-005** -- Implement `BlueprintTestFixture` and its ALC lifecycle.

`BlueprintTestFixture` is the core integration surface for all Blueprint tests. It wraps all
the production and mock infrastructure, drives the tick loop, manages collectible ALCs, and
verifies unload on disposal. Once this is in place, every subsequent phase can write real
integration tests.

### Required Reading (IN ORDER)

1. **Test Harness DD:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design.md`
   -- Read §2, §5, §7, §9 in full.
2. **Test Harness Inline Patches:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design_InlinePatches.md`
   -- Patches 1 and 2 OVERRIDE §3.7 and §4.8. Read them carefully. The TickFrame order and
   event handling in this batch must match the patched version (SwapBuffers first, ECB playback last).
3. **TASK-DETAIL.md:** `.dev/blueprints-1/TASK-DETAIL.md`
   -- Read TASK-TH-003, TASK-TH-005 in full.
4. **BATCH-03 Review:** `.dev/blueprints-1/reviews/BATCH-03-REVIEW.md`
   -- Understand the two P2 defects you are fixing in Corrective Task 0.
5. **Architecture v1.2 (ALC sections):** `.dev/blueprints-1/Blueprint_Subsystem_Architecture_v1.2.md`
   -- §3.2 (collectible ALC strategy), §9 (hot reload lifecycle overview).
6. **Developer workflow:** `.dev/.guides/DEV-GUIDE.md`

### Source Code Locations

- **New fixture class:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`
- **New fixture options:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureOptions.cs`
- **New fixture tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureTests.cs`
- **New ALC tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AlcUnloadTests.cs`
- **P2 fixes (existing files):**
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSession.cs` (NodeEnterRecord)
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSessionTests.cs` (add 2 skip tests)

### Existing files to read before writing code

```
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs          -- staging protocol
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs        -- slim definition
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlackboardTier.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintMaintenanceSystem.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintCompiler.cs   -- stub; throws NotImplementedException
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/InMemoryRoslynCompiler.cs -- stub; see Note below
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockSimulationView.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockEntityCommandBuffer.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSession.cs
```

### Build & Test Commands

```powershell
# From repo root:
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/blueprints-1/reports/BATCH-04-REPORT.md`

**If you have questions, create:**
`.dev/blueprints-1/questions/BATCH-04-QUESTIONS.md`

---

## Context

BATCH-03 established the debug interfaces (TH-008) and test data infrastructure (TH-009).
Foundation stubs for `BlueprintRegistry`, `BlueprintTickSystem`, `BlueprintMaintenanceSystem`,
`BlueprintBlackboard*`, and `BlueprintBlackboardPartitions` are in place.

This batch builds `BlueprintTestFixture` on top of all that. `BlueprintTestFixture` is the
"test umbrella" that wires everything together and provides the tick-advance/compile-load API
every test phase depends on.

**Important note on the compiler stubs:** Both `BlueprintCompiler.Compile(...)` and
`InMemoryRoslynCompiler.CompileAndLoad(...)` are Phase 3 stubs that throw
`NotImplementedException`. Therefore:
- Tests that need `CompileAndLoad` (ALC lifecycle) must use the fixture's
  `LoadTestAssemblyFromBytes(byte[])` bypass (described below) instead.
- Tests that need compiled Blueprint code (Phase 3) must be skipped with
  `[Fact(Skip = "Requires Phase 3 compiler")]`.

---

## Corrective Task 0 -- Fix P2 Defects from BATCH-03

Fix these before starting TH-003/TH-005 work.

### CT0-1: Add `Time` field to `NodeEnterRecord`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSession.cs`

Per TASK-TH-008 spec: `NodeEnterRecord(Entity Self, string NodeId, float Time)`.
The current implementation is missing the `Time` field.

Change the record and pass `Time = 0f` (placeholder) at call sites in Phase 1:

```csharp
// Current (wrong):
public sealed record NodeEnterRecord(Entity Self, string NodeId);

// Correct:
public sealed record NodeEnterRecord(Entity Self, string NodeId, float Time);
```

In `CapturingDebugSession.OnNodeEnter`, change:
```csharp
_nodeEntries.Add(new NodeEnterRecord(self, nodeId));
// to:
_nodeEntries.Add(new NodeEnterRecord(self, nodeId, Time: 0f));
```

> When Phase 3 compiler integration wires `DebugProbe` calls, the actual tick time
> will be passed. For now 0f is correct for the stub.

Update the existing tests in `CapturingDebugSessionTests.cs` to compile with the new record --
they should compile fine as long as they don't access `.Time` (they don't currently).

### CT0-2: Add two skip-annotated debug placeholder tests

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSessionTests.cs`

Per TASK-TH-008 spec §10.4, add these two tests at the bottom of `CapturingDebugSessionTests`:

```csharp
// Phase 3 integration test placeholder: requires compiled Blueprint assembly
// to be wired through DebugProbe.Sink.
[Fact(Skip = "Requires Phase 3 compiler")]
[Trait("Category", "RequiresCompiler")]
public void Debug_TraceMode_RecordsAllNodeEntries()
{
    // Phase 3 body: compileAndLoad a trace-mode asset,
    // call fixture.TickFrame, assert session.NodeEntries covers all nodes.
    throw new NotImplementedException("Phase 3 compiler required.");
}

// Phase 3 integration test placeholder: requires compiled Blueprint assembly.
[Fact(Skip = "Requires Phase 3 compiler")]
[Trait("Category", "RequiresCompiler")]
public void Debug_Breakpoint_FiresWhenNodeEntered()
{
    // Phase 3 body: compileAndLoad asset, set breakpoint, tick, assert event fired.
    throw new NotImplementedException("Phase 3 compiler required.");
}
```

---

## TASK-TH-003 -- BlueprintTestFixture Core Infrastructure

**Reference:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-003----blueprinttestfixture-core-infrastructure) for the full specification.

### TH-003 Overview

Implement `BlueprintTestFixture : IDisposable` with all properties and methods per Test Harness
DD §2.4 and §5. The class goes in `Hrot.Blueprints.Tests` namespace.

### TH-003 File: `BlueprintTestFixtureOptions.cs` (NEW)

```
Namespace: Hrot.Blueprints.Tests
```

Record with defaults from §7.4:

```csharp
public sealed class BlueprintTestFixtureOptions
{
    public static BlueprintTestFixtureOptions Default { get; } = new();
    public bool VerifyAlcUnloadOnDispose { get; init; } = true;
    public int GcReclaimRetries { get; init; } = 3;
    public int GcReclaimDelayMs { get; init; } = 50;
    public bool VerboseLeakDiagnostics { get; init; } = false;
}
```

### TH-003 File: `BlueprintTestFixture.cs` (NEW)

**Namespace:** `Hrot.Blueprints.Tests`

**Required using statements** -- check which are needed based on referenced types:
- `Fdp.Core`, `Fdp.Toolkit.Blueprints`, `Fdp.Toolkit.Blueprints.Systems`,
  `Fdp.Toolkit.Blueprints.Partitioning`, `Fdp.Toolkit.Blueprints.Components`,
  `Hrot.Blueprints.Core`, `Hrot.Blueprints.Core.Debug`, `Hrot.Blueprints.Core.Assets`,
  `Hrot.Blueprints.Tests.Mocks`,
  `System.Reflection`, `System.Runtime.Loader`

#### Properties

```csharp
public EntityRepository World { get; }
public MockSimulationView View { get; }
public MockEntityCommandBuffer Ecb { get; }
public BlueprintRegistry Registry { get; }
public BlueprintTickSystem TickSystem { get; }
public BlueprintMaintenanceSystem MaintenanceSystem { get; }
public BlueprintCompiler Compiler { get; }    // IBlueprintCompiler or BlueprintCompiler -- check stub type
public CapturingDebugSession DebugSession { get; }
```

#### Private state

```csharp
private readonly BlueprintTestFixtureOptions _options;
private readonly EntityRepository _repo;     // same object as World (kept for internal use)
private readonly List<WeakReference<AssemblyLoadContext>> _alcWeakRefs = new();
private readonly List<AssemblyLoadContext> _activeAlcs = new();
private readonly List<IEcsModuleSystem> _auxSimulationSystems = new();
private Action<ISimulationView, IEntityCommandBuffer>? _tickActions;
```

#### Constructor

Per Test Harness DD §2.4:
```csharp
public BlueprintTestFixture(BlueprintTestFixtureOptions? options = null)
{
    _options = options ?? BlueprintTestFixtureOptions.Default;
    _repo = new EntityRepository();
    World = _repo;
    Ecb = new MockEntityCommandBuffer(_repo);
    View = new MockSimulationView(_repo, Ecb);
    Registry = new BlueprintRegistry();
    DebugSession = new CapturingDebugSession();
    TickSystem = new BlueprintTickSystem(Registry);
    MaintenanceSystem = new BlueprintMaintenanceSystem();
    Compiler = new BlueprintCompiler();

    DebugProbe.Sink = DebugSession;   // route generated probe calls to the capturing session
}
```

> Note: `MockSimulationView` takes `(EntityRepository repo, IEntityCommandBuffer ecb)`.
> Check the existing constructor signature and match it.

#### TickFrame (Patch 1 + Patch 2 compliant)

Per Test Harness DD §5.3 AND Inline Patches §1 and §2 (these override §3.7 and §4.8):

```csharp
public void TickFrame(float deltaTime)
{
    // 1. Advance event bus so events published last frame become readable this frame
    _repo.Bus.SwapBuffers();

    // 2. Advance simulation time
    View.AdvanceTime(deltaTime);

    // 3. Simulation phase
    TickSystem.Execute(View);
    foreach (var sys in _auxSimulationSystems)
        sys.Execute(View);

    // 4. BeforeSync phase
    MaintenanceSystem.Execute(View);

    // 5. Sync phase: ECB playback (structural mutations + queued events apply)
    Ecb.Playback(_repo);

    // 6. Mid-tick inspection hook (after everything settled)
    _tickActions?.Invoke(View, Ecb);
}
```

#### System registration helpers

```csharp
public void RegisterTickAction(Action<ISimulationView, IEntityCommandBuffer> action)
    => _tickActions += action;

public void AddSimulationSystem(IEcsModuleSystem system)
    => _auxSimulationSystems.Add(system);
```

#### Compile and load (per TH-DD §5.1)

`CompileAndLoad` delegates to `CompileAndLoadMany`. Since `BlueprintCompiler.Compile`
currently throws NotImplementedException, this chain will fail in Phase 1. That is expected.
Implement the full chain as designed; it will work in Phase 3.

```csharp
public Assembly CompileAndLoad(BlueprintAsset asset, CompilerMode mode = CompilerMode.Debug)
    => CompileAndLoadMany(new[] { asset }, mode);

public Assembly CompileAndLoadMany(
    IReadOnlyList<BlueprintAsset> assets,
    CompilerMode mode = CompilerMode.Debug)
{
    // Compile each asset to C# source (will throw NotImplementedException in Phase 1)
    var sb = new StringBuilder();
    foreach (var asset in assets)
    {
        var src = Compiler.Compile(asset, mode);
        sb.AppendLine(src);
    }

    // Roslyn in-memory compile (also stub in Phase 1)
    var assemblyName = $"Bp_{Guid.NewGuid():N}";
    var assembly = new InMemoryRoslynCompiler()
        .CompileAndLoad(sb.ToString(), CreateCollectibleAlc(assemblyName));

    DiscoverAndInvokeRegistrars(assembly);
    return assembly;
}
```

#### `LoadTestAssemblyFromBytes` -- Phase 1 bypass for ALC tests

This method is used by `AlcUnloadTests` to exercise ALC lifecycle without needing the
Blueprint compiler. It loads any byte stream (e.g., an already-compiled test assembly)
into a new collectible ALC and registers it in the tracking fields.

```csharp
/// <summary>
/// Test-only ALC bypass: loads raw PE bytes into a new collectible ALC and
/// registers it for GC-reclaim tracking. Used by ALC lifecycle tests when the
/// Blueprint compiler is not yet available.
/// </summary>
internal Assembly LoadTestAssemblyFromBytes(byte[] peBytes)
{
    var assemblyName = $"TestAlc_{Guid.NewGuid():N}";
    var alc = CreateCollectibleAlc(assemblyName);
    using var ms = new MemoryStream(peBytes);
    return alc.LoadFromStream(ms);
}

private AssemblyLoadContext CreateCollectibleAlc(string name)
{
    var alc = new AssemblyLoadContext(name, isCollectible: true);
    _activeAlcs.Add(alc);
    _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));
    return alc;
}
```

#### `SimulateReload` (per TH-DD §5.2)

```csharp
public void SimulateReload(IReadOnlyList<BlueprintAsset> newVersions)
{
    var oldAlcs = new List<AssemblyLoadContext>(_activeAlcs);
    // Remove old ALCs from active list (they stay in _alcWeakRefs for GC tracking)
    _activeAlcs.Clear();

    CompileAndLoadMany(newVersions);   // populates _activeAlcs with new ALC(s)

    foreach (var alc in oldAlcs)
        alc.Unload();
}
```

#### Slot inspection helpers (per TH-DD §5.4)

```csharp
public bool HasSlot(BlueprintAsset asset, Entity entity)
{
    return TryGetSlotAcrossTiers(asset.AssetId, entity, out _, out _, out _);
}

public unsafe BlueprintStateView? GetBlueprintState(BlueprintAsset asset, Entity entity)
{
    if (!Registry.TryGetById(asset.AssetId, out var def))
        return null;
    if (!TryGetSlotAcrossTiers(asset.AssetId, entity, out var tier, out _, out var offset))
        return null;

    // In Phase 1, BlueprintBlackboardPartitions.TryGetSlotOffset always returns false,
    // so this returns null. Full implementation in Phase 2.
    return null;
}

private bool TryGetSlotAcrossTiers(
    Guid assetId, Entity entity,
    out BlackboardTier tier, out int slotIndex, out int payloadOffset)
{
    // Check each tier component
    if (_repo.HasComponent<BlueprintBlackboard1024>(entity) &&
        BlueprintBlackboardPartitions.TryGetSlotOffset(
            _repo, entity, assetId, out tier, out slotIndex, out payloadOffset))
        return true;
    if (_repo.HasComponent<BlueprintBlackboard4096>(entity) &&
        BlueprintBlackboardPartitions.TryGetSlotOffset(
            _repo, entity, assetId, out tier, out slotIndex, out payloadOffset))
        return true;
    if (_repo.HasComponent<BlueprintBlackboard16384>(entity) &&
        BlueprintBlackboardPartitions.TryGetSlotOffset(
            _repo, entity, assetId, out tier, out slotIndex, out payloadOffset))
        return true;

    tier = BlackboardTier.B1024;
    slotIndex = -1;
    payloadOffset = -1;
    return false;
}
```

#### `AttachBlueprint` (per TH-DD §5.5)

```csharp
public unsafe void AttachBlueprint(BlueprintAsset asset, Entity entity)
{
    if (!Registry.TryGetById(asset.AssetId, out var def))
        throw new InvalidOperationException(
            $"Blueprint '{asset.Name}' not loaded into registry. Call CompileAndLoad first.");

    var tier = ChooseTier(def.StateSize);
    EnsureTierComponent(entity, tier);

    if (!BlueprintBlackboardPartitions.TryAttach(_repo, entity, def, tier, out _))
        throw new InvalidOperationException(
            $"Failed to attach Blueprint '{asset.Name}' to entity {entity} (tier {tier}).");

    // Initialize default state in the slot (no-op in Phase 1 stub)
    // def.InitDefault(...);  -- leave this for Phase 2 when BlueprintBlackboardPartitions is real
}

internal static BlackboardTier ChooseTier(int stateSize)
{
    if (stateSize <= 928)  return BlackboardTier.B1024;
    if (stateSize <= 3936) return BlackboardTier.B4096;
    return BlackboardTier.B16384;
}

private void EnsureTierComponent(Entity entity, BlackboardTier tier)
{
    switch (tier)
    {
        case BlackboardTier.B1024:
            if (!_repo.HasComponent<BlueprintBlackboard1024>(entity))
                _repo.AddComponent(entity, default(BlueprintBlackboard1024));
            break;
        case BlackboardTier.B4096:
            if (!_repo.HasComponent<BlueprintBlackboard4096>(entity))
                _repo.AddComponent(entity, default(BlueprintBlackboard4096));
            break;
        case BlackboardTier.B16384:
            if (!_repo.HasComponent<BlueprintBlackboard16384>(entity))
                _repo.AddComponent(entity, default(BlueprintBlackboard16384));
            break;
    }
}
```

#### GC helpers and weak reference inspection

```csharp
public IReadOnlyList<WeakReference<AssemblyLoadContext>> GetAlcWeakReferences()
    => _alcWeakRefs;

public void ForceGcReclaim()
{
    for (int i = 0; i < _options.GcReclaimRetries; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (AllAlcsReclaimed()) return;
        Thread.Sleep(_options.GcReclaimDelayMs);
    }
}

private bool AllAlcsReclaimed()
    => _alcWeakRefs.All(w => !w.TryGetTarget(out _));

private bool TryReclaimAllAlcs(int maxRetries, int delayMs)
{
    for (int i = 0; i < maxRetries; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (AllAlcsReclaimed()) return true;
        if (i < maxRetries - 1) Thread.Sleep(delayMs);
    }
    return AllAlcsReclaimed();
}
```

#### `DiscoverAndInvokeRegistrars` helper

```csharp
private void DiscoverAndInvokeRegistrars(Assembly assembly)
{
    var staging = Registry.BeginStaging();
    foreach (var type in assembly.GetTypes())
    {
        if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() == null) continue;
        var method = type.GetMethod("Register",
            BindingFlags.Public | BindingFlags.Static);
        if (method == null) continue;
        var prms = method.GetParameters();
        var args = prms.Select(p => ResolveRegistrarParam(p.ParameterType, staging)).ToArray();
        method.Invoke(null, args);
    }
    Registry.CommitStaging(staging);
}

private object? ResolveRegistrarParam(Type t, BlueprintRegistryStaging staging)
{
    if (t == typeof(BlueprintRegistryStaging)) return staging;
    if (t == typeof(BlueprintRegistry))        return Registry;
    throw new InvalidOperationException(
        $"Unknown registrar parameter type: {t.FullName}");
}
```

#### `Dispose` (TH-003 portion -- unload without GC verification)

The full `Dispose` with GC reclaim loop is TASK-TH-005 scope. Here implement only the ALC
unload part:

```csharp
public void Dispose()
{
    foreach (var alc in _activeAlcs)
        alc.Unload();
    _activeAlcs.Clear();

    if (_options.VerifyAlcUnloadOnDispose)
    {
        if (!TryReclaimAllAlcs(_options.GcReclaimRetries, _options.GcReclaimDelayMs))
        {
            int leaked = _alcWeakRefs.Count(w => w.TryGetTarget(out _));
            if (_options.VerboseLeakDiagnostics)
            {
                // Best-effort diagnostic (stub ok for Slice 1)
            }
            throw new InvalidOperationException(
                $"{leaked} ALC(s) not GC-reclaimed after {_options.GcReclaimRetries} retries. " +
                $"Common causes: static fields, event subscriptions, or cached delegate " +
                $"references pointing into the collectible assembly.");
        }
    }
}
```

### TH-003 Tests: `BlueprintTestFixtureTests.cs` (NEW)

Test all SCs that do NOT require the real compiler.

```csharp
namespace Hrot.Blueprints.Tests;

public sealed class BlueprintTestFixtureTests
{
    // SC1: Constructor initializes all properties
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        using var fixture = new BlueprintTestFixture();
        Assert.NotNull(fixture.World);
        Assert.NotNull(fixture.View);
        Assert.NotNull(fixture.Ecb);
        Assert.NotNull(fixture.Registry);
        Assert.NotNull(fixture.TickSystem);
        Assert.NotNull(fixture.MaintenanceSystem);
        Assert.NotNull(fixture.Compiler);
        Assert.NotNull(fixture.DebugSession);
        // DebugProbe.Sink wired to DebugSession
        Assert.Same(fixture.DebugSession, DebugProbe.Sink);
    }

    // SC2: PublishEvent -> TickFrame -> ReadEvents (via Patch 1: FdpEventBus SwapBuffers)
    [Fact]
    public void PublishEvent_ViaBus_ReadableInNextTickFrame()
    {
        using var fixture = new BlueprintTestFixture();

        // Publish into the bus (will be readable after SwapBuffers in TickFrame)
        fixture.World.Bus.Publish(new HitEvent { Target = new Entity(1, 0), Damage = 30f });

        IReadOnlyList<HitEvent>? captured = null;
        fixture.RegisterTickAction((view, _) =>
        {
            captured = view.ReadEvents<HitEvent>().ToList();
        });

        fixture.TickFrame(0.016f);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Count);
        Assert.Equal(30f, captured[0].Damage);

        // Second tick: no new publishes, event list should be empty
        IReadOnlyList<HitEvent>? secondCapture = null;
        fixture.RegisterTickAction((view, _) => secondCapture = view.ReadEvents<HitEvent>().ToList());
        fixture.TickFrame(0.016f);
        Assert.NotNull(secondCapture);
        Assert.Empty(secondCapture!);
    }

    // SC3: ECB AddComponent deferred until TickFrame
    [Fact]
    public void EcbAddComponent_DeferredUntilTickFrame()
    {
        using var fixture = new BlueprintTestFixture();
        var e = fixture.World.CreateEntity();

        fixture.Ecb.AddComponent(e, new TestComponent { Value = 42 });
        // Before TickFrame: not yet visible
        Assert.False(fixture.View.HasComponent<TestComponent>(e));

        fixture.TickFrame(0.016f);

        // After TickFrame: ECB played back
        Assert.True(fixture.View.HasComponent<TestComponent>(e));
        Assert.Equal(42, fixture.View.GetComponentRO<TestComponent>(e).Value);
    }

    // SC4: CompileAndLoad requires Phase 3 compiler (skip in Phase 1)
    [Fact(Skip = "Requires Phase 3 compiler")]
    [Trait("Category", "RequiresCompiler")]
    public void CompileAndLoad_IncrementsAlcWeakReferences()
    {
        using var fixture = new BlueprintTestFixture();
        var asset = BlueprintAssetBuilder.Library("TestLib").Build();
        fixture.CompileAndLoad(asset);
        Assert.Equal(1, fixture.GetAlcWeakReferences().Count);
        Assert.True(fixture.GetAlcWeakReferences()[0].TryGetTarget(out _));
    }

    // SC5: ChooseTier threshold boundaries
    [Fact]
    public void ChooseTier_CorrectBoundaries()
    {
        Assert.Equal(BlackboardTier.B1024,  BlueprintTestFixture.ChooseTier(928));
        Assert.Equal(BlackboardTier.B4096,  BlueprintTestFixture.ChooseTier(929));
        Assert.Equal(BlackboardTier.B4096,  BlueprintTestFixture.ChooseTier(3936));
        Assert.Equal(BlackboardTier.B16384, BlueprintTestFixture.ChooseTier(3937));
    }

    // SC6: AttachBlueprint requires Phase 3 compiler for registry lookup
    [Fact(Skip = "Requires Phase 3 compiler")]
    [Trait("Category", "RequiresCompiler")]
    public void AttachBlueprint_RegisteredAsset_SetsHasSlot()
    {
        // Phase 3 body: compileAndLoad, create entity, attachBlueprint, assert HasSlot
        throw new NotImplementedException("Phase 3 compiler required.");
    }

    // Additional: Dispose with no ALCs loaded completes without exception
    [Fact]
    public void Dispose_WithNoAlcsLoaded_Succeeds()
    {
        var fixture = new BlueprintTestFixture();
        fixture.Dispose();   // should not throw
    }

    // Additional: TickFrame with aux system calls Execute
    [Fact]
    public void AddSimulationSystem_SystemExecutedEachTick()
    {
        using var fixture = new BlueprintTestFixture();
        var tracker = new CountingSystem();
        fixture.AddSimulationSystem(tracker);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        Assert.Equal(2, tracker.ExecuteCount);
    }

    // Additional: DebugProbe.Sink is wired to DebugSession
    [Fact]
    public void DebugProbe_WiredToDebugSession_RecordsProbeCall()
    {
        using var fixture = new BlueprintTestFixture();
        var entity = new Entity(77, 0);

        DebugProbe.NodeEnter(entity, "node-test");

        Assert.True(fixture.DebugSession.Hit("node-test"));
    }
}

/// <summary>Helper: ECS system that counts Execute invocations.</summary>
internal sealed class CountingSystem : IEcsModuleSystem
{
    public int ExecuteCount { get; private set; }
    public void Execute(ISimulationView view) => ExecuteCount++;
    public string Name => "CountingSystem";
}
```

> **Note:** `TestComponent` is already defined in `MockTestTypes.cs` from BATCH-02.
> `HitEvent` is defined in `TestEventDefinitions.cs` from BATCH-03.
> `IEcsModuleSystem` -- verify the interface name and method signature from `Fdp.ModuleHost.Abstractions`.
> `ChooseTier` must be `internal static` (not private) for the SC5 test to call it from the test class.

---

## TASK-TH-005 -- ALC Lifecycle and Unload Verification

**Reference:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-005----alc-lifecycle-and-unload-verification) for the full specification.

The `Dispose()` with GC retry loop is already included in TASK-TH-003 above (the full Dispose
block). TASK-TH-005 adds the `AlcUnloadTests` test class.

### TH-005 Note: ALC tests use `LoadTestAssemblyFromBytes`

Because `BlueprintCompiler.Compile` is a Phase 1 stub that throws `NotImplementedException`,
the ALC lifecycle tests cannot call `fixture.CompileAndLoad(asset)` in Phase 1. Instead, they
use `fixture.LoadTestAssemblyFromBytes(byte[])` which creates a real collectible ALC from any
PE byte stream -- fully exercising the ALC tracking and GC reclaim infrastructure.

To get a valid PE byte stream for testing, load the executing test assembly bytes:
```csharp
byte[] testAsmBytes = File.ReadAllBytes(
    typeof(AlcUnloadTests).Assembly.Location);
```
This simulates loading a "blueprint-generated assembly" for ALC lifecycle purposes.

### TH-005 File: `AlcUnloadTests.cs` (NEW)

```csharp
using System.Runtime.Loader;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests;

public sealed class AlcUnloadTests
{
    private static byte[] GetTestAsmBytes()
        => File.ReadAllBytes(typeof(AlcUnloadTests).Assembly.Location);

    // SC2 / §7.5: After Dispose, ALC is reclaimed by GC
    [Fact]
    public void Fixture_DisposeAfterLoadAssembly_ReclaimsAlc()
    {
        BlueprintTestFixture fixture;
        WeakReference<AssemblyLoadContext> alcRef;

        {
            fixture = new BlueprintTestFixture();
            fixture.LoadTestAssemblyFromBytes(GetTestAsmBytes());
            alcRef = fixture.GetAlcWeakReferences().Single();
            Assert.True(alcRef.TryGetTarget(out _), "ALC should be live before Dispose");
        }

        fixture.Dispose();

        // External GC loop to observe reclaim
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!alcRef.TryGetTarget(out _)) return;
        }

        Assert.False(alcRef.TryGetTarget(out _),
            "ALC should be GC-reclaimed after fixture.Dispose()");
    }

    // SC3 / §7.5: After multiple reloads, old ALCs are reclaimed; newest stays live
    [Fact]
    public void Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var bytes = GetTestAsmBytes();

        // Simulate three "generations" of loaded assemblies
        fixture.LoadTestAssemblyFromBytes(bytes);   // gen 1
        // Simulate reload: unload gen 1 manually (SimulateReload uses CompileAndLoad,
        // so we replicate its ALC-unload part manually)
        var alc1 = fixture.GetAlcWeakReferences()[0];
        fixture.GetAlcWeakReferences();   // ensure we have ref
        // Directly unload the first ALC by accessing _activeAlcs through the internal list
        // via GetAlcWeakReferences -- ALC is still alive; we'll just verify tracking

        fixture.LoadTestAssemblyFromBytes(bytes);   // gen 2
        fixture.LoadTestAssemblyFromBytes(bytes);   // gen 3

        Assert.Equal(3, fixture.GetAlcWeakReferences().Count);

        // All three ALCs should be live until Unload() is called
        Assert.All(fixture.GetAlcWeakReferences(),
            w => Assert.True(w.TryGetTarget(out _), "All ALCs should be live before Dispose"));
    }

    // SC1 / §7.5: Dispose with VerifyAlcUnloadOnDispose=false and no ALCs is instant
    [Fact]
    public void Fixture_DisposeNoAlcs_Succeeds()
    {
        var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        // Should complete without exception and without calling GC.Collect
        fixture.Dispose();
    }

    // SC5 leak detection: deliberately hold a reference into the ALC, verify throw
    // Note: this test loads the executing assembly into an ALC (which is unusual), so
    // the "delegate into the ALC" scenario is simulated differently:
    // We make ForceGcReclaim fail by keeping the ALC alive via a strong ref.
    [Fact]
    public void Fixture_StrongRefToAlc_DetectsLeakAndThrows()
    {
        AssemblyLoadContext? heldRef = null;
        var fixture = new BlueprintTestFixture();
        try
        {
            fixture.LoadTestAssemblyFromBytes(GetTestAsmBytes());
            // Hold a strong reference to the ALC to prevent GC reclaim
            fixture.GetAlcWeakReferences()[0].TryGetTarget(out heldRef);
            Assert.NotNull(heldRef);

            // Dispose should throw because the ALC cannot be reclaimed while heldRef is live
            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Dispose());
            Assert.Contains("ALC(s) not GC-reclaimed", ex.Message);
        }
        finally
        {
            heldRef = null;          // release the strong reference
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            // Second Dispose should succeed after releasing the ref
            // fixture is already partially disposed; we must not throw here
            // If fixture.Dispose() was already called and threw, the ALCs were unloaded.
            // The GC should now reclaim them. No further Dispose needed.
        }
    }
}
```

> **Important:** The `Fixture_StrongRefToAlc_DetectsLeakAndThrows` test simulates a held
> reference by keeping the ALC alive via a strong WeakReference target. After the
> `Assert.Throws`, release `heldRef = null` and do a GC cycle in `finally` so the ALC
> is reclaimed before the test runner continues.

---

## Testing Requirements

### Minimum counts after this batch

- Corrective Task 0: 2 new skip tests (counted but skipped)
- BlueprintTestFixtureTests: at minimum 8 tests (5 skipped + 3 passing core tests)
  - SC1 (constructor), SC2 (event bus), SC3 (ECB deferred), SC4 (skip), SC5 (ChooseTier),
    SC6 (skip), Dispose-no-ALCs, AddSystem, DebugProbe-wired = 9 tests minimum
- AlcUnloadTests: 4 tests (3 passing, possibly 1 failing if leak test needs adjustment)

**Total new tests: ~15+ (of which ~4 are skipped)**

### Quality standards

- No test should pass by catching the wrong exception.
- `ChooseTier` boundaries test must use exact boundary values (928, 929, 3936, 3937).
- ALC tests must perform actual GC cycles -- do not mock or skip GC.
- The leak detection test must actually trigger the `InvalidOperationException` -- not just create a fixture and call Dispose.
- All non-skipped tests must pass; the 1 pre-existing skipped test (TierUpgrade) stays skipped.

---

## Report Requirements

Submit `.dev/blueprints-1/reports/BATCH-04-REPORT.md` containing:

1. **Corrective Task 0:** Confirm NodeEnterRecord Time field added; confirm 2 skip tests added. Test count before/after.
2. **TASK-TH-003:** List all files created/modified. Which SCs pass, which are skipped. Current total test count.
3. **TASK-TH-005:** List AlcUnloadTests results. Any surprises with GC behavior.
4. **Build status:** `dotnet build IOS-IG-SimHost.sln` -- 0 errors, 0 warnings.
5. **Test summary:** Total passed / skipped / failed.
6. **Any deviations** from the instructions and why.
