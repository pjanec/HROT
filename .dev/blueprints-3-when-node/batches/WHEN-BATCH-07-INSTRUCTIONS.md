# WHEN-BATCH-07 Instructions

**Task:** WHEN-M3-T3 — Condition Met runtime tests + degraded-mode safety

**Design reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §15.3, §15.7  
**Task detail:** `.dev/blueprints-3-when-node/TASK-DETAIL.md` lines 258–268

---

## Context

M3-T1 lowered the Condition Met mode (IR → C#). M3-T2 wired up the `InitializePredicates`
call in the registrar + coordinator. This batch (M3-T3) validates the runtime behaviour end-
to-end: predicate evaluated every tick, rising/falling edges, and degraded-mode safety when
the delegate is null (bad predicate after reload).

No production code changes are required. This batch adds tests only.

---

## Files to modify

### 1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/WhenNodeRuntimeTests.cs`

Add a **ConditionMet asset builder** + **four new `[Fact]` tests** after the existing
`EventFired_*` tests. Do NOT modify any existing code.

#### 1a. Shared mock helpers (add near top of `WhenNodeRuntimeTests` class, alongside the existing `EmptyEventCatalog` helper)

```csharp
// ---- ConditionMet helpers ----

/// <summary>
/// Mock IPredicateCompiler whose delegate can be swapped between tests.
/// </summary>
private sealed class DelegatingPredicateCompiler : IPredicateCompiler
{
    private readonly Func<EntityRepository, Entity, bool> _del;
    public DelegatingPredicateCompiler(Func<EntityRepository, Entity, bool> del) => _del = del;

    public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
        => _del;

    public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(
        SearchPredicateDto root)
        => Array.Empty<Type>();
}

private sealed class MockSearchPredicateRegistry : ISearchPredicateRegistry { }
```

You will also need this using directive at the top of the file if not present:
```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Compiler;
```

#### 1b. ConditionMet asset builder

Add this helper method inside `WhenNodeRuntimeTests`. It builds a minimal `Instance`
blueprint with a `WhenNode(ConditionMet)` + a `WasFired` (and optionally `WasEnded`)
bool variable set to `true` by a `SetVariableNode` wired to the `OnFired`/`OnEnded`
exec outputs.

```csharp
/// <summary>
/// Builds a minimal Instance blueprint with a WhenNode in ConditionMet mode.
/// The predicate content is just a placeholder (ComponentType=typeof(object)) that
/// passes Stage 2; the actual delegate is supplied at runtime by IPredicateCompiler.
///
/// The graph wires:
///   Entry → WhenNode → (OnFired → SetVar(WasFired=true) → Return)
///                    → (OnEnded → SetVar(WasEnded=true) → Return)  [if FallingEdge]
///                    → (Out → Return)
/// </summary>
private static (BlueprintAsset asset, Guid nodeId) BuildCondMetRuntimeAsset(
    WhenEdge edges = WhenEdge.RisingEdge)
{
    var assetId = Guid.NewGuid();
    var graphId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    // ---- Variables ----
    var firedVarId = Guid.NewGuid();
    var wasFiredVar = new VariableDecl
    {
        Id   = firedVarId,
        Name = "WasFired",
        Type = new BlueprintTypeRef { TypeId = "bool" },
        DefaultValueJson = "false",
    };
    var variables = new System.Collections.Generic.List<VariableDecl> { wasFiredVar };

    bool hasFalling = (edges & WhenEdge.FallingEdge) != 0;
    Guid? endedVarId = null;
    if (hasFalling)
    {
        var id = Guid.NewGuid();
        endedVarId = id;
        variables.Add(new VariableDecl
        {
            Id   = id,
            Name = "WasEnded",
            Type = new BlueprintTypeRef { TypeId = "bool" },
            DefaultValueJson = "false",
        });
    }

    // ---- Nodes ----
    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut",
                                  Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);

    var whenNode    = new WhenNode { Id = nodeId, Mode = WhenMode.ConditionMet, Edges = edges,
        ConditionMet = new ConditionMetPayload
        {
            Condition = new PropertyMatchDto
            {
                ComponentType = typeof(object),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto
                    { MinValue = 5.0, MaxValue = double.MaxValue },
            },
        },
    };
    var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
    var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
    whenNode.Pins.Add(whenExecIn);
    whenNode.Pins.Add(whenExecOut);
    if ((edges & WhenEdge.RisingEdge) != 0)
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
    if (hasFalling)
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() });

    // SetVariable(WasFired=true) after OnFired
    var setFiredNode = BuildSetBoolTrue(firedVarId, out var setFiredExecIn, out var setFiredExecOut, out var setFiredValIn);

    // Return nodes
    var retOut    = new ReturnNode { Id = Guid.NewGuid() };
    var retOutIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    retOut.Pins.Add(retOutIn);
    var retFired    = new ReturnNode { Id = Guid.NewGuid() };
    var retFiredIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    retFired.Pins.Add(retFiredIn);

    // Literal true
    var litFired    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
    var litFiredOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                                 TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
    litFired.Pins.Add(litFiredOut);

    var nodes = new System.Collections.Generic.List<Node>
        { entry, whenNode, setFiredNode, litFired, retOut, retFired };
    var links = new System.Collections.Generic.List<Link>
    {
        new Link { FromNodeId = entry.Id,        FromPinId = entryExecOut.Id,
                   ToNodeId   = whenNode.Id,     ToPinId   = whenExecIn.Id },
        new Link { FromNodeId = whenNode.Id,     FromPinId = whenExecOut.Id,
                   ToNodeId   = retOut.Id,       ToPinId   = retOutIn.Id },
        new Link { FromNodeId = whenNode.Id,     FromPinId = whenNode.Pins.First(p => p.Name == "OnFired").Id,
                   ToNodeId   = setFiredNode.Id, ToPinId   = setFiredExecIn.Id },
        new Link { FromNodeId = setFiredNode.Id, FromPinId = setFiredExecOut.Id,
                   ToNodeId   = retFired.Id,     ToPinId   = retFiredIn.Id },
        new Link { FromNodeId = litFired.Id,     FromPinId = litFiredOut.Id,
                   ToNodeId   = setFiredNode.Id, ToPinId   = setFiredValIn.Id },
    };

    if (hasFalling && endedVarId.HasValue)
    {
        var setEndedNode = BuildSetBoolTrue(endedVarId.Value, out var setEndedExecIn, out var setEndedExecOut, out var setEndedValIn);
        var retEnded     = new ReturnNode { Id = Guid.NewGuid() };
        var retEndedIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retEnded.Pins.Add(retEndedIn);
        var litEnded    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
        var litEndedOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                                     TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        litEnded.Pins.Add(litEndedOut);
        nodes.AddRange(new Node[] { setEndedNode, retEnded, litEnded });
        links.AddRange(new Link[]
        {
            new Link { FromNodeId = whenNode.Id,      FromPinId = whenNode.Pins.First(p => p.Name == "OnEnded").Id,
                       ToNodeId   = setEndedNode.Id,  ToPinId   = setEndedExecIn.Id },
            new Link { FromNodeId = setEndedNode.Id,  FromPinId = setEndedExecOut.Id,
                       ToNodeId   = retEnded.Id,      ToPinId   = retEndedIn.Id },
            new Link { FromNodeId = litEnded.Id,      FromPinId = litEndedOut.Id,
                       ToNodeId   = setEndedNode.Id,  ToPinId   = setEndedValIn.Id },
        });
    }

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Function,
        Nodes = nodes,
        Links = links,
    };

    var asset = new BlueprintAsset
    {
        AssetId   = assetId,
        Name      = "WhenCondMet",
        Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
        Variables = variables,
        Graphs    = { graph },
    };
    return (asset, nodeId);
}

/// <summary>Helper: creates a SetVariableNode that sets a bool variable to true.</summary>
private static SetVariableNode BuildSetBoolTrue(
    Guid varId,
    out Pin execIn,
    out Pin execOut,
    out Pin valueIn)
{
    var node = new SetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
    execIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
    execOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    valueIn = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false,
                         TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
    node.Pins.Add(execIn);
    node.Pins.Add(execOut);
    node.Pins.Add(valueIn);
    return node;
}
```

> **Implementation note:** `CompileAndLoad(asset, CompileOptions)` overload exists on the
> fixture. Use `OptionsWithEmptyEventCatalog()` for consistency with the existing tests
> (avoids BP2005 for unknown event types); however, since ConditionMet doesn't reference
> events, the default `CompileAndLoad(asset)` (no options param) works too. Use
> `CompileAndLoad(asset)` for simplicity.

#### 1c. Four ConditionMet test methods

Add these after the last `EventFired_*` test:

```csharp
// ======================== ConditionMet Tests ========================

[Fact]
public void ConditionMet_RisingEdge_Fires_WhenPredicateTransitionsToTrue()
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

    // Predicate: AnotherTestComponent.X > 5.0f
    fixture.PredicateCompiler  = new DelegatingPredicateCompiler(
        (repo, ent) => repo.GetComponentRO<AnotherTestComponent>(ent).X > 5.0f);
    fixture.PredicateRegistry = new MockSearchPredicateRegistry();

    var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge);
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.World.AddComponent(entity, new AnotherTestComponent { X = 3.0f }); // predicate false
    fixture.AttachBlueprint(asset, entity);

    // Tick 1: false → false → no fire
    fixture.TickFrame(0.016f);
    Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));

    // Set X > 5 so predicate becomes true
    fixture.World.GetComponentRW<AnotherTestComponent>(entity).X = 10.0f;

    // Tick 2: false → true → OnFired fires
    fixture.TickFrame(0.016f);
    Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
}

[Fact]
public void ConditionMet_NoRefire_WhenPredicateRemainsTrue()
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

    // Predicate: always true
    fixture.PredicateCompiler  = new DelegatingPredicateCompiler((_, _) => true);
    fixture.PredicateRegistry = new MockSearchPredicateRegistry();

    var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge);
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    // Tick 1: false → true → fires (rising edge)
    fixture.TickFrame(0.016f);
    Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));

    // Directly reset WasFired to false in the blackboard to detect re-fire.
    // Use GetBlueprintState + unsafe write via reflection to flip the byte.
    ResetBoolField(fixture, asset, entity, "WasFired");

    // Tick 2: true → true → no re-fire
    fixture.TickFrame(0.016f);
    Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
}

[Fact]
public void ConditionMet_FallingEdge_Fires_WhenPredicateBecomesFalse()
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

    // Predicate: X > 5.0f
    fixture.PredicateCompiler  = new DelegatingPredicateCompiler(
        (repo, ent) => repo.GetComponentRO<AnotherTestComponent>(ent).X > 5.0f);
    fixture.PredicateRegistry = new MockSearchPredicateRegistry();

    var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge | WhenEdge.FallingEdge);
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.World.AddComponent(entity, new AnotherTestComponent { X = 10.0f }); // predicate true
    fixture.AttachBlueprint(asset, entity);

    // Tick 1: false → true → OnFired (rising) — WasFired=true, WasEnded=false
    fixture.TickFrame(0.016f);
    Assert.True (ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
    Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasEnded"));

    // Set X < 5 so predicate becomes false
    fixture.World.GetComponentRW<AnotherTestComponent>(entity).X = 2.0f;

    // Tick 2: true → false → OnEnded (falling) — WasEnded=true
    fixture.TickFrame(0.016f);
    Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasEnded"));
}

[Fact]
public void ConditionMet_NullDelegate_NoCrash_NoFire()
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

    // Degraded mode: PredicateCompiler = null → InitializePredicates catches →
    // _whenCondPred_ = null → null check in generated code skips the block.
    fixture.PredicateCompiler  = null;
    fixture.PredicateRegistry = null;

    var (asset, _) = BuildCondMetRuntimeAsset(WhenEdge.RisingEdge);
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.World.AddComponent(entity, new AnotherTestComponent { X = 10.0f });
    fixture.AttachBlueprint(asset, entity);

    // Three ticks — no crash, WasFired never set (delegate is null)
    fixture.TickFrame(0.016f);
    fixture.TickFrame(0.016f);
    fixture.TickFrame(0.016f);

    Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
}
```

You also need a `ResetBoolField` helper (add alongside `ReadSlotField`):

```csharp
/// <summary>
/// Resets a bool field in the blueprint state to false. Used by NoRefire tests to
/// detect a second firing after the first.
/// </summary>
private static unsafe void ResetBoolField(
    BlueprintTestFixture fixture,
    BlueprintAsset asset,
    Entity entity,
    string fieldName)
{
    var state = fixture.GetBlueprintState(asset, entity);
    Assert.NotNull(state);
    var hash      = BlueprintIdHash.Compute(asset.AssetId);
    Assert.True(fixture.Registry.TryGetById(hash, out var def));
    var stateType = def!.StateClrType!;
    var offset    = (int)Marshal.OffsetOf(stateType, fieldName);
    state!.Value.AsSpan()[offset] = 0; // false
}
```

> **Note:** `BlueprintStateView.AsSpan()` should return a `Span<byte>`. If `GetBlueprintState`
> returns a nullable value type, unwrap it (`state.Value`). Look at existing uses of
> `GetBlueprintState` in the fixture for the correct usage pattern.

---

## File to modify

### 2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/WhenNodeHotReloadTests.cs`

Add `BadPredicateAfterReload_DegradedMode_NoCrash` test. This test was specified in
DESIGN §15.7 and verifies the degraded-mode safety guarantee when a predicate compile
throws after hot reload.

#### 2a. Add a throwing mock compiler near the top of the file (alongside `MockPredicateCompiler`)

```csharp
/// <summary>
/// Mock that throws InvalidOperationException from CompileComponentPredicate,
/// simulating a broken predicate after a hot reload.
/// </summary>
private sealed class ThrowingPredicateCompiler : IPredicateCompiler
{
    public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
        => throw new InvalidOperationException("Simulated bad predicate after reload.");

    public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(
        SearchPredicateDto root)
        => Array.Empty<Type>();
}
```

#### 2b. Add the missing using directives

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Hrot.Blueprints.Tests.Mocks;
```

Only add what is missing. The file already has `Fdp.Core`, `Fdp.Toolkit.Blueprints`,
`Fdp.Toolkit.ReplayBrowser.Search`, etc.

The additional usings needed are:
```csharp
using System.Runtime.InteropServices;
using Hrot.Blueprints.Tests.Mocks;   // AnotherTestComponent
```

#### 2c. Add the `BadPredicateAfterReload_DegradedMode_NoCrash` test

```csharp
/// <summary>
/// After a hot reload where the predicate compile throws,
/// InitializePredicates silently nulls the delegate.
/// The WhenNode no-ops on the next tick without crashing.
/// Per DESIGN §15.7 and M3-T3.
/// </summary>
[Fact]
public void BadPredicateAfterReload_DegradedMode_NoCrash()
{
    WeakReference<AssemblyLoadContext>[] alcWeakRefs;
    BadPredicateAfterReload_DegradedMode_NoCrash_Body(out alcWeakRefs);
    for (int i = 0; i < 50; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
        Thread.Sleep(50);
    }
    int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
    Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed after 50 retries.");
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static void BadPredicateAfterReload_DegradedMode_NoCrash_Body(
    out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

    // --- Load v1: valid compiler ---
    var mockCompiler = new MockPredicateCompiler(); // returns (_, _) => true
    fixture.PredicateCompiler = mockCompiler;
    fixture.PredicateRegistry = new MockSearchPredicateRegistry();

    var assetId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();
    var assetV1 = BuildCondMetAsset(assetId, nodeId, minValue: 10.0);
    var bpHash  = BlueprintIdHash.Compute(assetId);

    fixture.CompileAndLoad(assetV1, DefaultOptions());

    // Create entity with AnotherTestComponent (to satisfy the delegate's potential reads)
    // and attach the blueprint.
    var entity = fixture.CreateEntity();
    fixture.World.AddComponent(entity, new AnotherTestComponent { X = 10.0f });
    fixture.AttachBlueprint(assetV1, entity);

    // Tick 1: delegate is valid → ConditionMet check runs (no crash)
    fixture.TickFrame(0.016f);
    Assert.Equal(1, mockCompiler.CompileCallCount); // InitializePredicates called once

    // --- Load v2: throwing compiler (same StructureHash = Soft Reload) ---
    fixture.PredicateCompiler = new ThrowingPredicateCompiler();
    var assetV2 = BuildCondMetAsset(assetId, nodeId, minValue: 10.0); // same structure
    fixture.CompileAndLoad(assetV2, DefaultOptions());

    // Verify delegate is null in the freshly loaded assembly
    Assert.True(fixture.Registry.TryGetById(bpHash, out var defV2));
    var v2Type = fixture.GetCurrentAlcAssembly()!
        .GetTypes()
        .FirstOrDefault(t => t.Name.Contains("_Bp") && !t.Name.Contains("Registrar"));
    Assert.NotNull(v2Type);
    var predField = v2Type!.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .FirstOrDefault(f => f.Name.StartsWith("_whenCondPred_"));
    Assert.NotNull(predField);
    Assert.Null(predField!.GetValue(null)); // degraded: exception in compile → null

    // Tick 2: delegate is null → null check in generated code skips block → no crash
    fixture.TickFrame(0.016f); // Must not throw

    alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
}
```

> **Note on `fixture.GetCurrentAlcAssembly()`:** This helper may not exist on
> `BlueprintTestFixture`. If it doesn't, retrieve the assembly by finding a type in the
> ALC. The simplest alternative: use `Assembly.GetCallingAssembly()` or get the assembly
> from the loaded `BlueprintDefinition`'s `Tick` delegate's `Target.GetType().Assembly`.
>
> Better approach: look at how `QuickReloadTests.cs` accesses the current ALC:
> `fixture.GetCurrentAlc()` returns the `AssemblyLoadContext`. Then iterate
> `alc.Assemblies` to find the generated blueprint assembly:
>
> ```csharp
> var currentAlc = fixture.GetCurrentAlc();
> var v2Assembly = currentAlc!.Assemblies
>     .First(a => a.GetName().Name!.StartsWith("Bp_"));
> var v2Type = v2Assembly.GetTypes()
>     .FirstOrDefault(t => t.Name.Contains("_Bp") && !t.Name.Contains("Registrar"));
> ```
>
> Use this pattern — do NOT add `GetCurrentAlcAssembly()` to the fixture.

---

## Success criteria

1. **48 WhenNode tests pass** (44 existing + 4 new runtime tests + 1 new hot-reload test
   would be 49 total; verify exact count after implementation).
2. `ConditionMet_RisingEdge_Fires_WhenPredicateTransitionsToTrue` passes.
3. `ConditionMet_NoRefire_WhenPredicateRemainsTrue` passes.
4. `ConditionMet_FallingEdge_Fires_WhenPredicateBecomesFalse` passes.
5. `ConditionMet_NullDelegate_NoCrash_NoFire` passes.
6. `BadPredicateAfterReload_DegradedMode_NoCrash` passes.
7. No regressions in existing tests (expect ~98 pre-existing failures).

## Build + test commands

```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj

dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~WhenNode" -v normal
```

---

## Key constraints

- **No production code changes** — only test files.
- The `ConditionMet_NullDelegate_NoCrash_NoFire` test intentionally leaves
  `PredicateCompiler = null` on the fixture. The `InitializePredicates` generated method
  has a try/catch for each node; NullReferenceException from `predicateCompiler.CompileComponentPredicate(...)` is caught and the delegate field stays null. This is the DESIGNED degraded mode — do not add null-guards in production code.
- `ResetBoolField` uses `GetBlueprintState` (which returns `BlueprintStateView?`). Study
  its existing usages in the test fixture helpers before implementing — specifically how
  `BlueprintStateView.AsSpan()` is called.
- For `BadPredicateAfterReload_DegradedMode_NoCrash`, the `_whenCondPred_` null assertion
  via reflection is the key verification that degraded mode is active. Find the type by
  iterating the current ALC's assemblies (see note in §2c above).
