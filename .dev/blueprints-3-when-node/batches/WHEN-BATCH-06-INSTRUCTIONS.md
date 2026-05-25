# WHEN-BATCH-06 Instructions

**Task:** WHEN-M3-T2 — `AiHotReloadCoordinator.DrainPendingCallbacks` extension + Registrar wiring

**Design reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §7.4, §10.4  
**Task detail:** `.dev/blueprints-3-when-node/TASK-DETAIL.md` lines 232–256

---

## Context

WHEN-BATCH-05 implemented the `ConditionMetIrPayload` + Stage 6 lowering (M3-T1). The
generated blueprint class now has:
- Static fields `_whenCondDto_{id8}` (`SearchPredicateDto?`) and `_whenCondPred_{id8}`
  (`Func<EntityRepository, Entity, bool>?`) for each ConditionMet node.
- A static `InitializePredicates(IPredicateCompiler predicateCompiler,
  ISearchPredicateRegistry dtoRegistry)` method that deserializes the JSON DTO and calls
  `predicateCompiler.CompileComponentPredicate(dto)` to fill the delegate fields.

M3-T2's job is to wire the registrar and coordinator so that `InitializePredicates` is
**called automatically on every (re)load** of a ConditionMet blueprint.

---

## Files to modify

### 1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs`

**Method: `EmitRegistrarClass`**

Detect whether the asset has any `IrOp_WhenConditionMetCheck` operations. If it does AND
dispatch is Instance, add `IPredicateCompiler` + `ISearchPredicateRegistry` parameters to
the `Register` method signature, and emit the `InitializePredicates(predicateCompiler,
dtoRegistry)` call before `staging.Add`.

Current `EmitRegistrarClass` relevant excerpt (lines 102–141):
```csharp
private void EmitRegistrarClass(IrAsset asset)
{
    var className    = $"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";
    var registrarName = $"BlueprintRegistrar_{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp";

    bool needsBehReg = asset.Hostings.Any(h =>
        h == AiPrimitiveHosting.BTreeAction || h == AiPrimitiveHosting.BTreeCondition);

    WriteLine("[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]");
    // ... param building ...
    switch (asset.Dispatch)
    {
        case AssetDispatch.Instance:
            EmitInstanceRegistration(className, asset);
            break;
    }
}
```

**Required changes:**

After `bool needsBehReg = ...`, add:
```csharp
bool hasConditionMet = asset.Dispatch == AssetDispatch.Instance &&
    asset.Graphs
        .SelectMany(g => g.Blocks)
        .SelectMany(b => b.Statements)
        .Any(s => s.Operation is IrOp_WhenConditionMetCheck);
```

In the `paramParts` setup, after the `if (needsBehReg)` block, add:
```csharp
if (hasConditionMet)
{
    paramParts.Add("global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler predicateCompiler");
    paramParts.Add("global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry dtoRegistry");
}
```

In the switch `case AssetDispatch.Instance:`, emit the `InitializePredicates` call BEFORE
`EmitInstanceRegistration`:
```csharp
case AssetDispatch.Instance:
    if (hasConditionMet)
        WriteLine($"{className}.InitializePredicates(predicateCompiler, dtoRegistry);");
    EmitInstanceRegistration(className, asset);
    break;
```

No other methods need changing in this file.

---

### 2. `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs`

**Fields and constructor:** Add two optional fields and extend the constructor with optional
params so production code can wire in a predicate compiler.

Add private fields (alongside `_geoTransform` and `_entityMap`):
```csharp
private readonly global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler? _predicateCompiler;
private readonly global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry? _dtoRegistry;
```

Extend the constructor signature with two optional params at the end (after `entityMap`):
```csharp
global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler? predicateCompiler = null,
global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry? dtoRegistry = null
```

Assign them in the constructor body:
```csharp
_predicateCompiler = predicateCompiler;
_dtoRegistry       = dtoRegistry;
```

**Method: `ResolveRegistrarParam`**

Add two new cases BEFORE the final `return null;`:
```csharp
if (paramType == typeof(global::Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler))
    return _predicateCompiler;
if (paramType == typeof(global::Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry))
    return _dtoRegistry;
```

Using `global::` qualified names avoids any ambiguity and is consistent with the emitter.
Alternatively, add `using` directives at the top of the file:
```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Compiler;
```
…and use unqualified names. Either approach is acceptable; be consistent with the existing
file style.

---

### 3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

**Purpose:** Allow tests to inject a mock `IPredicateCompiler` and `ISearchPredicateRegistry`
so that the generated registrar's `Register(...)` call can resolve these params and
`InitializePredicates` will bind the delegate fields.

**Add two public properties** (after the existing `DebugSession` property or alongside the
private-state block, whichever is idiomatic):
```csharp
/// <summary>
/// When set, passed to generated registrars that declare an IPredicateCompiler parameter.
/// Null (default) means predicates compile in degraded mode (delegate fields stay null).
/// </summary>
public Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler? PredicateCompiler { get; set; }

/// <summary>
/// When set, passed to generated registrars that declare an ISearchPredicateRegistry parameter.
/// </summary>
public Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry? PredicateRegistry { get; set; }
```

**Update `ApplyQuickReloadFromAssembly`:** In the parameter-resolution loop, add two new
`else if` branches BEFORE the final `else throw`:
```csharp
else if (paramInfos[i].ParameterType == typeof(Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler))
    args[i] = PredicateCompiler;
else if (paramInfos[i].ParameterType == typeof(Hrot.Blueprints.Core.Compiler.ISearchPredicateRegistry))
    args[i] = PredicateRegistry;
```

These return `null` when not set (degraded mode — `InitializePredicates` will catch the
NullReferenceException internally and leave the delegate null).

Add the necessary `using` directives at the top of the file if not already present:
```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
```
(`Hrot.Blueprints.Core.Compiler` is already imported.)

---

## File to create

### 4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/WhenNodeHotReloadTests.cs`

A new test file with the `EditWhenNodePredicate_SoftReload_DelegateRecompiled` test from
DESIGN §15.7. Follow the `[MethodImpl(MethodImplOptions.NoInlining)]` two-method pattern
used throughout the `HotReload/` test suite (see `QuickReloadTests.cs` for the canonical
example).

```csharp
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Xunit;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Hot-reload tests for WhenNode per DESIGN §15.7.
/// Batch 06 scope: EditWhenNodePredicate_SoftReload_DelegateRecompiled.
/// </summary>
[Collection("DebugProbe")]
public sealed class WhenNodeHotReloadTests
{
    // ---- Mocks ----

    private sealed class MockPredicateCompiler : IPredicateCompiler
    {
        public int CompileCallCount { get; private set; }

        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
        {
            CompileCallCount++;
            return (_, _) => true;
        }

        public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(
            SearchPredicateDto root)
            => Array.Empty<Type>();
    }

    private sealed class MockSearchPredicateRegistry : ISearchPredicateRegistry { }

    // ---- Asset builder ----

    /// <summary>
    /// Builds a minimal Instance blueprint containing one ConditionMet WhenNode.
    /// The same assetId + nodeId across two calls yields the same StructureHash
    /// (same synthesized-field name), so the second load is a Soft Reload.
    /// </summary>
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static BlueprintAsset BuildCondMetAsset(Guid assetId, Guid nodeId, double minValue = 10.0)
    {
        var graphId = Guid.NewGuid();

        // Entry node
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut",
                                     Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        // WhenNode in ConditionMet mode
        var whenNode = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            ConditionMet = new ConditionMetPayload
            {
                Condition = new PropertyMatchDto
                {
                    ComponentType = typeof(object), // passes BP2009 (non-null)
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto
                    {
                        MinValue = minValue,
                        MaxValue = double.MaxValue,
                    },
                },
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        // Return node
        var retNode    = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var ret2Node   = new ReturnNode { Id = Guid.NewGuid() };
        var ret2ExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);
        ret2Node.Pins.Add(ret2ExecIn);

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode, ret2Node },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id,
                           ToNodeId   = whenNode.Id, ToPinId   = whenExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,
                           ToNodeId   = retNode.Id,  ToPinId   = retExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,
                           ToNodeId   = ret2Node.Id, ToPinId   = ret2ExecIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "WhenCondMet",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    // ---- Tests ----

    /// <summary>
    /// Verifies that when the same blueprint (same assetId + nodeId = same StructureHash)
    /// is loaded a second time with a different predicate, InitializePredicates is called
    /// again and the delegate is recompiled (Soft Reload path per DESIGN §10.4 / §15.7).
    /// </summary>
    [Fact]
    public void EditWhenNodePredicate_SoftReload_DelegateRecompiled()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        EditWhenNodePredicate_SoftReload_DelegateRecompiled_Body(out alcWeakRefs);
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
    private static void EditWhenNodePredicate_SoftReload_DelegateRecompiled_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var mockCompiler = new MockPredicateCompiler();
        fixture.PredicateCompiler = mockCompiler;
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();

        // Same assetId + nodeId guarantees the same StructureHash across both loads
        // (same synthesized field name _when_<id8>_prev).
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var bpHash  = BlueprintIdHash.Compute(assetId);

        // --- Load v1 (predicate MinValue = 10.0) ---
        var assetV1 = BuildCondMetAsset(assetId, nodeId, minValue: 10.0);
        fixture.CompileAndLoad(assetV1, DefaultOptions());

        Assert.True(fixture.Registry.TryGetById(bpHash, out var defV1),
            "Blueprint not registered after first load.");
        var structHashV1 = defV1!.StructureHash;
        Assert.Equal(1, mockCompiler.CompileCallCount);

        // --- Load v2 (predicate MinValue = 20.0 — Soft Reload: same StructureHash) ---
        var assetV2 = BuildCondMetAsset(assetId, nodeId, minValue: 20.0);
        fixture.CompileAndLoad(assetV2, DefaultOptions());

        Assert.True(fixture.Registry.TryGetById(bpHash, out var defV2),
            "Blueprint not registered after second load.");
        var structHashV2 = defV2!.StructureHash;

        // StructureHash unchanged → Soft Reload (predicate edit does not change field layout).
        Assert.Equal(structHashV1, structHashV2);

        // InitializePredicates was called a second time → delegate recompiled.
        Assert.Equal(2, mockCompiler.CompileCallCount);

        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
```

**Notes on the test:**
- `BlueprintTestFixture.CompileAndLoad(asset, options)` runs all 8 compiler stages including
  Roslyn, then calls `ApplyQuickReloadFromAssembly` which invokes the registrar. With the
  changes in items 1–3 above, the registrar's `Register` method includes
  `InitializePredicates(predicateCompiler, dtoRegistry)`, and the fixture resolves
  `IPredicateCompiler` from `fixture.PredicateCompiler`.
- `NumericPredicateDto` with `MinValue = 10.0` vs `20.0`: the predicate JSON embedded in
  the generated source differs between v1 and v2, causing two distinct calls to
  `CompileComponentPredicate`. The synthesized field name `_when_{id8}_prev` (where id8 is
  the first 8 chars of `nodeId`) is the SAME in both assemblies → same StructureHash →
  Soft Reload confirmed.
- `typeof(object)` as `ComponentType` passes Stage 2 validator `BP2009`
  (`HasUnresolvableComponentType` only fires when `ComponentType == null`).
- The `[Collection("DebugProbe")]` attribute is required to avoid parallel execution
  conflicts with other hot-reload tests (matches the pattern in `QuickReloadTests.cs`).

---

## Success criteria

1. **All 43 existing WhenNode tests still pass.**
2. **New test passes:** `EditWhenNodePredicate_SoftReload_DelegateRecompiled` in
   `Hrot.Blueprints.Tests/HotReload/WhenNodeHotReloadTests.cs`.
3. **No other pre-existing test regressions.** (Expect ~98 pre-existing failures unrelated
   to this batch.)

## Build command

```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-build --filter "FullyQualifiedName~WhenNode" -v normal
```

Run broader test:
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~WhenNodeHotReload|FullyQualifiedName~RegistrarInjection" -v normal
```

---

## Key architectural constraints

- **Do NOT touch** the `Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` (the simplified
  coordinator used inside `BlueprintTestFixture`). The fixture's own `ApplyQuickReloadFromAssembly`
  handles parameter resolution independently — it is NOT going through that coordinator's
  `ResolveRegistrarArgument`.
- The emitter change is isolated to the `Instance` dispatch branch of
  `EmitRegistrarClass`. `Library` and `AiPrimitive` registrars never have ConditionMet
  nodes and must not be changed.
- Passing `null` for `IPredicateCompiler` or `ISearchPredicateRegistry` is the degraded-mode
  path (handled by try/catch inside `InitializePredicates`). Do not add null-guards in the
  coordinator or fixture — let degraded mode work as designed.
