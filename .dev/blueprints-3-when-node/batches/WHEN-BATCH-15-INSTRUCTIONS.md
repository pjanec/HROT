# WHEN-BATCH-15 — End-to-end demo, performance CI gates, hot-reload battery (M9)

**Tasks:** WHEN-M9-T1, WHEN-M9-T2, WHEN-M9-T3  
**Design reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §15.7, §15.8, §15.9  
**Task detail:** `.dev/blueprints-3-when-node/TASK-DETAIL.md` §WHEN-M9-T1 through §WHEN-M9-T3

---

## Context

This batch completes the entire blueprints-3-when-node iteration. Three tasks:
- **M9-T1**: CoverAwarePatrol end-to-end integration test (3 tests)
- **M9-T2**: Performance CI gates — zero-allocation hot-path and timing smoke-tests (7 tests)
- **M9-T3**: Hot-reload integration battery — 8 new tests in `WhenNodeHotReloadTests.cs`

**Key infrastructure facts:**
- `BlueprintTestFixture` provides `CreateEntity()`, `AttachBlueprint()`, `TickFrame()`, `CompileAndLoad()`, `SimulateReload()`, `GetBlueprintState()`
- `StructureHash` is computed from `Parameters + WorkingState + Variables` — NOT from node names or counts
- `ValueChanged` synthesizes `_when_<id>_prev` field (float, double, Vector2, etc.) → contributes to WorkingState → changes StructureHash
- `EventFired` and `ConditionMet` have NO synthesized state fields (ConditionMet predicate is static)
- `EqsResult(TopChanged)` synthesizes a struct (~16 bytes), `EqsResult(FirstReady)` synthesizes a smaller struct (4 bytes) → struct size difference → StructureHash differs
- `ReadEqsResultNode` and `SpawnEqsSensorNode` have zero state contribution (no synthesized fields) — per `Lower_ZeroStateContribution`
- `ComponentTypeId` for ValueChanged: use `"Hrot.Blueprints.Tests.Mocks.AnotherTestComponent"` (already tested in `WhenNodeRuntimeTests.cs`)
- `PropertyPath` for float field: `"X"` or `"Y"` (both are float on `AnotherTestComponent`)
- `StructureHash` IS included in Variables — so if you add/remove a VariableDecl, StructureHash changes
- For `AddReadEqsResultNode_SoftReload` and `AddSpawnEqsSensorNode_SoftReload`: both v1 and v2 must have the SAME declared variables (add the variable in v1 but don't wire it to any special node, then wire it in v2)
- `SubEntityCleanupSystem` is in namespace `Fdp.Toolkit.Replication.Systems`
- `EqsCognitiveBuffer`, `EqsSensor`, `PartMetadata` are in namespace `FDP.Eqs` / `Fdp.Toolkit.Spatial.Eqs` / `Fdp.Toolkit.Replication.Components`

**Helper to get StructureHash from fixture:**
```csharp
private static ulong GetStructureHash(BlueprintTestFixture fixture, Guid assetId)
{
    var hash = BlueprintIdHash.Compute(assetId);
    Assert.True(fixture.Registry.TryGetById(hash, out var def),
        $"Blueprint definition not found for asset {assetId}");
    return def!.StructureHash;
}
```

**`CompileOptions` for EQS tests:** (same as SpawnEqsSensorRuntimeTests.cs)
```csharp
private static CompileOptions MakeEqsOptions(IReadOnlyList<BlueprintSignature>? siblings = null) => new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>(),
    EqsTemplates:      new AlwaysContainsCatalog());

private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
{
    public bool Contains(Guid assetId) => true;
}
```

---

## Deliverables checklist

---

### File 1 — NEW: `Integration/CoverAwarePatrolEndToEndTest.cs` (M9-T1)

**Namespace:** `Hrot.Blueprints.Tests.Integration`  
**3 tests:** `CoverAwarePatrol_FullScenario`, `CoverAwarePatrol_ParentDeath_AutoCleanup`, `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor`

**Required usings:**
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Xunit;
```

**Recipe loading helper:**
```csharp
private static BlueprintAsset LoadCoverAwarePatrol()
{
    var assetsDir = TestData.ResolveTestAssetsDir();
    var path = Path.Combine(assetsDir, "Recipes", "CoverAwarePatrol.bp.json");
    var json = File.ReadAllText(path);
    return BlueprintJsonServices.Deserialize(json)
        ?? throw new InvalidDataException("Could not deserialize CoverAwarePatrol.bp.json");
}
```

**EQS registration helper:**
```csharp
private static void RegisterEqsComponents(BlueprintTestFixture fixture)
{
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();
    fixture.World.RegisterComponent<PartMetadata>();
}
```

**EQS compile options:**
Use `AlwaysContainsCatalog` same as SpawnEqsSensorRuntimeTests.

**Helper to find child entity:**
```csharp
private static Entity FindChildWithBuffer(BlueprintTestFixture fixture)
{
    var result = Entity.Null;
    fixture.World.Query().With<EqsCognitiveBuffer>().Build()
        .ForEach(e => { if (result.Equals(Entity.Null)) result = e; });
    return result;
}
```

**Helper to write EQS result to cognitive buffer:**
The `EqsCognitiveBuffer` inline array has a setter. Write a single result at index 0:
```csharp
private static unsafe void PublishEqsResult(
    BlueprintTestFixture fixture,
    Entity childEntity,
    Entity targetEntity,
    float score,
    double timestamp)
{
    ref var buf = ref fixture.World.GetComponentRW<EqsCognitiveBuffer>(childEntity);
    var result = new EqsResult
    {
        EntityId  = targetEntity.Id,
        PositionX = 10f,
        PositionY = 20f,
        Score     = score,
        Epoch     = (uint)(timestamp * 10),   // non-zero epoch = fresh result
    };
    buf.Results[0] = result;
    buf.Count      = 1;
    buf.LastUpdateTimeSeconds = (float)timestamp;
    buf.Epoch = buf.Epoch == 0 ? 1u : buf.Epoch + 1u;  // ensure epoch is non-zero
}
```

NOTE: Check the exact fields of `EqsCognitiveBuffer` and `EqsResult` in `FDP.Eqs` before writing this. Look at `WhenNodeEqsInlineArrayTests.cs` and `SpawnEqsSensorRuntimeTests.cs` for the correct field access pattern. If `EqsCognitiveBuffer` has no `Results[]` indexer but uses a struct-level access, adapt accordingly.

**Test 1: `CoverAwarePatrol_FullScenario`**
```
1. LoadCoverAwarePatrol(), compile with EqsOptions (AlwaysContainsCatalog)
2. RegisterEqsComponents, create parent entity, AttachBlueprint
3. TickFrame(0.016f) — first tick: SpawnEqsSensorNode should create child entity with EqsCognitiveBuffer
4. Assert: at least one entity has EqsCognitiveBuffer (child entity created)
5. Find child entity; verify it has PartMetadata with ParentEntity == parent
6. PublishEqsResult on child entity with a real target entity, timestamp > MaxAgeSeconds guard
7. TickFrame(0.016f) — second tick: WhenNode(EqsResult) should evaluate; cognitive buffer is fresh
8. Assert: blueprint state still healthy (no crash) — the WhenNode fired successfully
```

For the "WhenNode fired" assertion: check `fixture.DebugSession.NodeEnterEvents` for any node entries after tick 2. If the WhenNode OnFired branch executed, there should be additional node enter events (from nodes downstream of OnFired). Accept that the exact nodes depend on the recipe structure — just assert that tick 2 runs without throwing.

**Test 2: `CoverAwarePatrol_ParentDeath_AutoCleanup`**
```
1. Same setup as FullScenario through step 4 (child entity exists)
2. Find child entity before death
3. Destroy parent entity: fixture.World.DestroyEntity(parentEntity)
4. Run SubEntityCleanupSystem: var cleanup = new SubEntityCleanupSystem(); cleanup.Execute(fixture.World, 0.016f);
5. Flush: (no Ecb needed — SubEntityCleanupSystem destroys synchronously)
6. Assert: child entity is no longer alive: Assert.False(fixture.World.IsAlive(childEntity))
```

**Test 3: `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor`**
```
1. LoadCoverAwarePatrol(), compile+load (v1)
2. Register EQS components, create entity, AttachBlueprint
3. TickFrame(0.016f) → child sensor created; find childEntity
4. Load same recipe again with a slight modification (create a programmatic copy of the asset
   with different MaxAgeSeconds on its EqsResult WhenNode) → compile+load (v2 via SimulateReload)
   Actually: since we can't easily modify the loaded JSON asset, instead:
   Load from JSON (v1), compile+load normally. Then for v2, load again from JSON and adjust the WhenNode's EqsResult.MaxAgeSeconds in memory before compiling. Use SimulateReload([assetV2]).
5. Assert StructureHash v1 == StructureHash v2 (MaxAgeSeconds doesn't affect struct layout → Soft Reload)
6. TickFrame(0.016f) → verify no crash and child entity still alive (handle preserved across soft reload)
```

Note: `SimulateReload` recompiles and hot-swaps the definition. After reload, the entity continues using the new definition on the next tick.

---

### File 2 — NEW: `Benchmarks/WhenNodePerfTests.cs` (M9-T2)

**Namespace:** `Hrot.Blueprints.Tests.Benchmarks`  
**7 tests** per DESIGN §15.8. Follow the `ProbeOverheadTests.cs` pattern: zero-allocation as CI gate.

**Required usings:**
```csharp
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Mocks;
using Xunit;
```

**Class skeleton:**
```csharp
/// <summary>
/// CI performance gates for WhenNode hot-path per DESIGN §15.8.
/// Uses zero-allocation as the proxy for timing requirements (< allocation = < latency).
/// Precise timing is measured separately via BenchmarkDotNet benchmarks.
/// </summary>
[Collection("DebugProbe")]
public sealed class WhenNodePerfTests { ... }
```

**Helper `DefaultOptions()`:** Same as in WhenNodeHotReloadTests.cs (ConditionMet options).

**Test 1: `WhenNode_ZeroAllocOnHotPath` (DESIGN: WhenNode_ZeroAllocOnHotPath)**
Build a blueprint with all 4 WhenNode modes on separate entities. Run 10 warm-up frames. Measure GC.GetAllocatedBytesForCurrentThread() over 100 frames. Assert == 0.
- Mode 1: ConditionMet (ConditionMet WhenNode on entity 1)  
- Mode 2: EventFired (EventFired WhenNode on entity 2) — use EmptyEventCatalog same as WhenNodeRuntimeTests
- Mode 3: ValueChanged (on entity 3) — ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent", PropertyPath = "X"
- Mode 4: EqsResult (on entity 4) — needs EQS components registered, EqsResult WhenNode

**Simplification:** You may build a single blueprint with one WhenNode (ValueChanged mode) to keep the test simple. The zero-allocation test is the key invariant. Use separate tests for each mode if time permits, but one combined test is acceptable.

**Test 2: `WhenNode_ValueChanged_Under100ns_perTick` (DESIGN §15.8)**
Use Stopwatch. Build + compile + attach a ValueChanged blueprint to one entity. Warm up 1000 ticks. Measure average tick time over 1000 ticks. Assert average < 10µs (100x target; target is 100ns but CI machines are slow). Log the actual measured time via `Console.WriteLine`.

```csharp
[Fact]
public void WhenNode_ValueChanged_Under100ns_perTick()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    // build ValueChanged blueprint...
    // compile + load
    // attach entity
    // warmup: 1000 ticks
    // measure: var sw = Stopwatch.StartNew(); for 1000 ticks; sw.Stop();
    var nsPerTick = sw.Elapsed.TotalNanoseconds / 1000.0;
    Console.WriteLine($"ValueChanged avg: {nsPerTick:F1} ns/tick");
    Assert.True(nsPerTick < 10_000, $"ValueChanged hot path too slow: {nsPerTick:F1} ns/tick (target < 10µs in CI)");
}
```

Use `System.Diagnostics.Stopwatch` (already imported via `using System;`).

**Test 3: `WhenNode_EventFired_Under500ns_perTick`**
Same pattern with EventFired WhenNode. Threshold: 50µs in CI (100x target).

**Test 4: `WhenNode_ConditionMet_Under200ns_perTick`**
Same pattern with ConditionMet WhenNode (needs MockPredicateCompiler that returns false always). Threshold: 20µs in CI.

**Test 5: `WhenNode_EqsResult_Under150ns_perTick`**
Same with EqsResult WhenNode (TopChanged mode). Needs EQS components. Cognitive buffer left empty (epoch=0 → fast path, no buffer read needed). Threshold: 15µs in CI.

**Test 6: `ReadEqsResultNode_Under80ns_perInvocation`**
Blueprint with SpawnEqsSensor + WhenNode(EqsResult) + ReadEqsResult. Warm up + measure 1000 ticks. Threshold: 8µs in CI.

**Test 7: `SpawnEqsSensorNode_Under5us_perInvocation`**
This is one-time cost (not a hot-path test). Just verify it runs without crashing and the child entity is created in < 1 second total. Use:
```csharp
Assert.True(sw.ElapsedMilliseconds < 1000, "SpawnEqsSensor took > 1s (clearly a hang)");
```

**Important:** For tests that need `AlwaysContainsCatalog` (EqsResult, ReadEqsResult, SpawnEqsSensor), define it as an inner class.

**For ValueChanged in perf tests:** Add `AnotherTestComponent` to each entity via `fixture.World.AddComponent(entity, new AnotherTestComponent { X = 0f })` and update X on some ticks to exercise the comparison path.

---

### File 3 — MODIFY: `HotReload/WhenNodeHotReloadTests.cs` (M9-T3)

Add 8 new tests to the existing `WhenNodeHotReloadTests` class. The existing class already has the following helpers:
- `MockPredicateCompiler`, `ThrowingPredicateCompiler`, `MockSearchPredicateRegistry`
- `BuildCondMetAsset(Guid assetId, Guid nodeId, double minValue)`
- `DefaultOptions()` with BuiltInChannelCommandCatalog

**Add the following helpers to the class (private static methods):**

**Helper: `BuildSimpleAsset(Guid assetId)`** — blueprint with no WhenNode:
```csharp
private static BlueprintAsset BuildSimpleAsset(Guid assetId) 
{
    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);
    var retNode   = new ReturnNode { Id = Guid.NewGuid() };
    var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    retNode.Pins.Add(retExecIn);

    var graph = new Graph
    {
        Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, retNode },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = entryExecOut.Id,
                             ToNodeId = retNode.Id, ToPinId = retExecIn.Id } },
    };
    return new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = "SimpleTest",
        Dispatch = AssetDispatchKind.Instance,
        Graphs   = { graph },
    };
}
```

**Helper: `BuildValueChangedAsset(Guid assetId, Guid nodeId, string propertyPath = "X")`**
```csharp
private static BlueprintAsset BuildValueChangedAsset(Guid assetId, Guid nodeId, string propertyPath = "X")
{
    var whenNode = new WhenNode
    {
        Id   = nodeId,
        Mode = WhenMode.ValueChanged,
        Edges = WhenEdge.RisingEdge,
        ValueChanged = new ValueChangedPayload
        {
            ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
            PropertyPath    = propertyPath,
            Source          = ValueChangedSource.SelfComponent,
            Epsilon         = 0f,
        },
    };
    whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);
    var ret  = new ReturnNode { Id = Guid.NewGuid() };
    var ret2 = new ReturnNode { Id = Guid.NewGuid() };
    ret.Pins.Add( new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });
    ret2.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });

    var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");
    var outPin     = whenNode.Pins.First(p => p.IsExec && p.Direction == "Out" && p.Name == "Out");
    var firedPin   = whenNode.Pins.First(p => p.Name == "OnFired");
    var retExecIn  = ret.Pins[0];
    var ret2ExecIn = ret2.Pins[0];

    var graph = new Graph
    {
        Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, whenNode, ret, ret2 },
        Links =
        {
            new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = execInPin.Id },
            new Link { FromNodeId = whenNode.Id, FromPinId = outPin.Id,       ToNodeId = ret.Id,      ToPinId = retExecIn.Id },
            new Link { FromNodeId = whenNode.Id, FromPinId = firedPin.Id,     ToNodeId = ret2.Id,     ToPinId = ret2ExecIn.Id },
        },
    };
    return new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = "ValueChangedTest",
        Dispatch = AssetDispatchKind.Instance,
        Graphs   = { graph },
    };
}
```

**Helper: `BuildEqsResultAsset(Guid assetId, Guid nodeId, string sensorVarName, EqsTrigger trigger, IReadOnlyList<VariableDecl>? variables = null)`**
Similar to `BuildValueChangedAsset` but for EqsResult mode. Include the `sensorVarName` in the payload. Include `sensorVar` in the blueprint's `Variables` list.

```csharp
private static BlueprintAsset BuildEqsResultAsset(
    Guid assetId, Guid nodeId, string sensorVarName, EqsTrigger trigger,
    VariableDecl? sensorVarDecl = null)
{
    var whenNode = new WhenNode
    {
        Id    = nodeId,
        Mode  = WhenMode.EqsResult,
        Edges = WhenEdge.RisingEdge,
        EqsResult = new EqsResultPayload
        {
            SensorVariableName = sensorVarName,
            Trigger            = trigger,
            ScoreThreshold     = 0f,
            MaxAgeSeconds      = 10f,
        },
    };
    whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
    whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
    whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);
    var ret  = new ReturnNode { Id = Guid.NewGuid() };
    var ret2 = new ReturnNode { Id = Guid.NewGuid() };
    ret.Pins.Add( new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });
    ret2.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });

    var execIn = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");
    var outPin = whenNode.Pins.First(p => p.Name == "Out");
    var fired  = whenNode.Pins.First(p => p.Name == "OnFired");

    var graph = new Graph
    {
        Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, whenNode, ret, ret2 },
        Links =
        {
            new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = execIn.Id },
            new Link { FromNodeId = whenNode.Id, FromPinId = outPin.Id,       ToNodeId = ret.Id,      ToPinId = ret.Pins[0].Id },
            new Link { FromNodeId = whenNode.Id, FromPinId = fired.Id,        ToNodeId = ret2.Id,     ToPinId = ret2.Pins[0].Id },
        },
    };
    var asset = new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = "EqsResultTest",
        Dispatch = AssetDispatchKind.Instance,
        Graphs   = { graph },
    };
    if (sensorVarDecl is not null)
        asset.Variables.Add(sensorVarDecl);
    return asset;
}
```

**Helper: `BuildWithReadEqsResultNode(Guid assetId, VariableDecl sensorVar)`**
Blueprint with SpawnEqsSensorNode + ReadEqsResultNode (no WhenNode). The sensor variable must be declared. This is for the Soft Reload test where only ReadEqsResultNode is added between v1 and v2.

Actually for simplicity: 
- v1 = `BuildSimpleAsset(assetId)` + the sensorVar declared (via the Variables list)
- v2 = same + ReadEqsResultNode wired to the sensor variable

Wait — if v1 has the sensor variable declared, it affects the StructureHash. So v2 adds ReadEqsResultNode but keeps the same variable → StructureHash unchanged → Soft Reload. ✓

But actually, let me simplify even more:
- v1 = `BuildSimpleAsset(assetId)` with an extra `VariableDecl` of type `EqsSensorHandle` called "MySensor"
- v2 = same nodes + ReadEqsResultNode that reads from "MySensor"
- The `Variables` list is identical in both → StructureHash equal → Soft Reload ✓

**Helper: `MakeEqsOptions()`** for hot-reload tests with EQS:
```csharp
private static CompileOptions MakeEqsOptions() => new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>(),
    EqsTemplates:      new AlwaysContainsCatalog());

private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
{
    public bool Contains(Guid assetId) => true;
}
```

**Helper: `GetStructureHash(BlueprintTestFixture fixture, Guid assetId)`** as shown in Context section.

---

**The 8 tests:**

#### Test 1: `AddWhenNode_TriggersHardReload`
```csharp
[Fact]
public void AddWhenNode_TriggersHardReload()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    var assetId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    // v1: no WhenNode
    var assetV1 = BuildSimpleAsset(assetId);
    fixture.CompileAndLoad(assetV1, DefaultOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: add ValueChanged WhenNode (adds synthesized float field to WorkingState)
    var assetV2 = BuildValueChangedAsset(assetId, nodeId);
    fixture.CompileAndLoad(assetV2, DefaultOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    // StructureHash must differ → Hard Reload path
    Assert.NotEqual(hashV1, hashV2);
}
```

#### Test 2: `RemoveWhenNode_TriggersHardReload`
```csharp
[Fact]
public void RemoveWhenNode_TriggersHardReload()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    var assetId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    // v1: has ValueChanged WhenNode
    var assetV1 = BuildValueChangedAsset(assetId, nodeId);
    fixture.CompileAndLoad(assetV1, DefaultOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: remove WhenNode (simple entry→return)
    var assetV2 = BuildSimpleAsset(assetId);
    fixture.CompileAndLoad(assetV2, DefaultOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    Assert.NotEqual(hashV1, hashV2);
}
```

#### Test 3: `EditWhenNodeMode_HardReload`
```csharp
[Fact]
public void EditWhenNodeMode_HardReload()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    var assetId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    // v1: ConditionMet mode — no synthesized state fields → smaller/zero state contribution
    var assetV1 = BuildCondMetAsset(assetId, nodeId);
    fixture.CompileAndLoad(assetV1, DefaultOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: SAME nodeId, but ValueChanged mode — adds synthesized float field to WorkingState
    var assetV2 = BuildValueChangedAsset(assetId, nodeId);
    fixture.CompileAndLoad(assetV2, DefaultOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    Assert.NotEqual(hashV1, hashV2);
}
```

#### Test 4: `ValueChangedFieldType_Soft_PreservesPrev`
```csharp
[Fact]
public void ValueChangedFieldType_Soft_PreservesPrev()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    var assetId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();

    // v1: ValueChanged watching AnotherTestComponent.X (float)
    var assetV1 = BuildValueChangedAsset(assetId, nodeId, propertyPath: "X");
    fixture.CompileAndLoad(assetV1, DefaultOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: SAME nodeId, ValueChanged watching AnotherTestComponent.Y (also float)
    // Same synthesized field type (float) → same WorkingState layout → same StructureHash
    var assetV2 = BuildValueChangedAsset(assetId, nodeId, propertyPath: "Y");
    fixture.CompileAndLoad(assetV2, DefaultOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    // Soft Reload: same structure (field type unchanged)
    Assert.Equal(hashV1, hashV2);
}
```

#### Test 5: `EqsTriggerChange_HardReload`
```csharp
[Fact]
public void EqsTriggerChange_HardReload()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    var assetId = Guid.NewGuid();
    var nodeId  = Guid.NewGuid();
    var sensorVar = new VariableDecl
    {
        Id   = Guid.NewGuid(),
        Name = "Sensor",
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };

    // v1: EqsResult(TopChanged) — TopChanged synthesized struct (~16 bytes)
    var assetV1 = BuildEqsResultAsset(assetId, nodeId, "Sensor", EqsTrigger.TopChanged, sensorVar);
    fixture.CompileAndLoad(assetV1, MakeEqsOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: SAME nodeId, EqsResult(FirstReady) — FirstReady synthesized struct (4 bytes)
    // Different struct size → different WorkingState layout → different StructureHash
    var assetV2 = BuildEqsResultAsset(assetId, nodeId, "Sensor", EqsTrigger.FirstReady, sensorVar);
    fixture.CompileAndLoad(assetV2, MakeEqsOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    Assert.NotEqual(hashV1, hashV2);
}
```

Note: The `sensorVar` VariableDecl is the SAME instance with the SAME `Id` for both v1 and v2, ensuring the Variables contribution to StructureHash is identical. Only the WorkingState differs.

#### Test 6: `AddReadEqsResultNode_SoftReload`

```csharp
[Fact]
public void AddReadEqsResultNode_SoftReload()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    var assetId = Guid.NewGuid();
    var sensorVarId = Guid.NewGuid();
    var sensorVar = new VariableDecl
    {
        Id   = sensorVarId,
        Name = "MySensor",
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };

    // v1: simple blueprint with sensor variable declared but no ReadEqsResultNode
    var assetV1 = BuildSimpleAsset(assetId);
    assetV1.Variables.Add(sensorVar);
    fixture.CompileAndLoad(assetV1, MakeEqsOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: same blueprint + ReadEqsResultNode (no new variables, no new state fields)
    // ReadEqsResultNode has zero state contribution per DESIGN Lower_ZeroStateContribution.
    var assetV2 = BuildSimpleAssetWithReadEqsResult(assetId, sensorVar);
    fixture.CompileAndLoad(assetV2, MakeEqsOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    // ReadEqsResultNode adds pure code, not state → Soft Reload
    Assert.Equal(hashV1, hashV2);
}
```

**Helper: `BuildSimpleAssetWithReadEqsResult(Guid assetId, VariableDecl sensorVar)`**
Build a blueprint with:
- Entry → ReadEqsResultNode → Return
- ReadEqsResultNode: Handle input wired from GetVariable(sensorVar), ResultIndex=literal 0
- All output pins of ReadEqsResultNode left unconnected (just the exec path)
- Same sensorVar declared in Variables

For the ReadEqsResultNode structure, look at how `SpawnEqsSensorRuntimeTests.cs` builds asset nodes with SetVariable, and adapt it for ReadEqsResult. The ReadEqsResult node in `WhenNodePaletteEntries.cs` shows the pin names: Handle (In), ResultIndex (In), IsReady (Out), ResultCount (Out), Entity (Out), Position (Out), Score (Out).

**For simplicity**, you can just add a ReadEqsResultNode with exec pins and no data wiring:
- The node executes but without a valid handle it returns defaults
- What matters for this test is only the StructureHash comparison

```csharp
private static BlueprintAsset BuildSimpleAssetWithReadEqsResult(Guid assetId, VariableDecl sensorVar)
{
    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);

    var readNode    = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = sensorVar.Name };
    var readExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",     Direction = "In",  IsExec = true,  TypeRef = new() };
    var readExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",    Direction = "Out", IsExec = true,  TypeRef = new() };
    var handleIn    = new Pin { Id = Guid.NewGuid(), Name = "Handle", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
    var indexIn     = new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
    readNode.Pins.AddRange(new[] { readExecIn, readExecOut, handleIn, indexIn });

    var ret   = new ReturnNode { Id = Guid.NewGuid() };
    var retIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    ret.Pins.Add(retIn);

    var graph = new Graph
    {
        Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, readNode, ret },
        Links =
        {
            new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = readNode.Id, ToPinId = readExecIn.Id },
            new Link { FromNodeId = readNode.Id, FromPinId = readExecOut.Id, ToNodeId = ret.Id,      ToPinId = retIn.Id },
        },
    };
    var asset = new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = "ReadEqsTest",
        Dispatch = AssetDispatchKind.Instance,
        Graphs   = { graph },
    };
    asset.Variables.Add(sensorVar);
    return asset;
}
```

#### Test 7: `AddSpawnEqsSensorNode_SoftReload`
Same pattern as Test 6 but adding SpawnEqsSensorNode between v1 and v2.

```csharp
[Fact]
public void AddSpawnEqsSensorNode_SoftReload()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();
    fixture.World.RegisterComponent<PartMetadata>();
    var assetId = Guid.NewGuid();
    var templateId = Guid.NewGuid();
    var sensorVarId = Guid.NewGuid();
    var sensorVar = new VariableDecl
    {
        Id   = sensorVarId,
        Name = "MySensor",
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };

    // v1: simple blueprint with sensor variable declared
    var assetV1 = BuildSimpleAsset(assetId);
    assetV1.Variables.Add(sensorVar);
    fixture.CompileAndLoad(assetV1, MakeEqsOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: same variable + SpawnEqsSensorNode that writes to MySensor
    // SpawnEqsSensorNode has zero state contribution per DESIGN Lower_ZeroStateContribution.
    var assetV2 = BuildSimpleAssetWithSpawnEqsSensor(assetId, templateId, sensorVar);
    fixture.CompileAndLoad(assetV2, MakeEqsOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    Assert.Equal(hashV1, hashV2);
}
```

**Helper: `BuildSimpleAssetWithSpawnEqsSensor(Guid assetId, Guid templateId, VariableDecl sensorVar)`**
Build: Entry → SpawnEqsSensorNode → SetVariable(sensorVar) → Return.
Follow `SpawnEqsSensorRuntimeTests.BuildSpawnAsset()` as a template (which already builds this exact pattern).

#### Test 8: `EditSpawnTemplate_SoftReload_PreservesHandle`
```csharp
[Fact]
public void EditSpawnTemplate_SoftReload_PreservesHandle()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();
    fixture.World.RegisterComponent<PartMetadata>();
    var assetId = Guid.NewGuid();
    var sensorVarId = Guid.NewGuid();
    var sensorVar = new VariableDecl
    {
        Id   = sensorVarId,
        Name = "MySensor",
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };

    // v1: SpawnEqsSensor with template A
    var templateA = Guid.NewGuid();
    var assetV1 = BuildSimpleAssetWithSpawnEqsSensor(assetId, templateA, sensorVar);
    fixture.CompileAndLoad(assetV1, MakeEqsOptions());
    var hashV1 = GetStructureHash(fixture, assetId);

    // v2: SpawnEqsSensor with template B (same variable, same node structure)
    // Only the emitted BlueprintId constant changes → no state change → Soft Reload
    var templateB = Guid.NewGuid();
    var assetV2 = BuildSimpleAssetWithSpawnEqsSensor(assetId, templateB, sensorVar);
    fixture.CompileAndLoad(assetV2, MakeEqsOptions());
    var hashV2 = GetStructureHash(fixture, assetId);

    // Same structure hash → Soft Reload path
    Assert.Equal(hashV1, hashV2);
}
```

---

## Build and test verification

After implementing, run:

```powershell
Set-Location "d:\WORK\IOS-IG-SimHost-FDP"

# Run ALL new tests
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj `
  --filter "FullyQualifiedName~CoverAwarePatrol|FullyQualifiedName~WhenNodePerf|FullyQualifiedName~AddWhenNode|FullyQualifiedName~RemoveWhenNode|FullyQualifiedName~EditWhenNode|FullyQualifiedName~ValueChangedField|FullyQualifiedName~EqsTriggerChange|FullyQualifiedName~AddReadEqs|FullyQualifiedName~AddSpawnEqs|FullyQualifiedName~EditSpawnTemplate" `
  2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3

# Verify no regressions in full WhenNode suite
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj `
  --filter "FullyQualifiedName~WhenNode|FullyQualifiedName~Recipe|FullyQualifiedName~HotReload" `
  2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3
```

Expected: 18+ new tests passing (3 E2E + 7 perf + 8 hot-reload). 0 failures.

---

## Notes

- `EqsTrigger` enum: values include at minimum `TopChanged`, `FirstReady`, `ScoreCrossed`, `BecomesStale`. Verify the exact names in `FDP.Eqs.EqsTrigger` or `Hrot.Blueprints.Core.Assets.EqsTrigger`.
- `EqsCognitiveBuffer.Results` — check the actual API before writing the PublishEqsResult helper. It may be an inline array or a span-based accessor. See `WhenNodeEqsInlineArrayTests.cs` for reference.
- If `SubEntityCleanupSystem.Execute()` signature differs from `IEcsModuleSystem.Execute(ISimulationView, float)`, check the actual signature in `Fdp.Toolkit.Replication.Systems.SubEntityCleanupSystem`.
- For the performance tests, using `System.Diagnostics.Stopwatch` (the `TotalNanoseconds` property is .NET 7+). On .NET 8 this is available. Use `(double)sw.ElapsedTicks / Stopwatch.Frequency * 1e9` if `TotalNanoseconds` is unavailable.
- For CoverAwarePatrol E2E: if the recipe compilation fails with unexpected diagnostics, add debug output (`sink.All.Select(d => d.Message)`) to understand why.
- The ConditionMet WhenNode test in M9 hot-reload: ConditionMet synthesized static field `_whenCondPred_<id>` is NOT in WorkingState — it's a static field on the generated class. Therefore ConditionMet WhenNode presence/absence affects code but NOT the StructureHash. This means `EditWhenNodeMode_HardReload` using ConditionMet vs ValueChanged correctly shows hash difference.
