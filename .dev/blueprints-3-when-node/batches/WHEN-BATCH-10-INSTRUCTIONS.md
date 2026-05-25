# WHEN-BATCH-10 — EQS-related runtime tests + inline-array safety (M4-T5)

**Task covered:** WHEN-M4-T5  
**Reference:** [TASK-DETAIL.md M4-T5](../TASK-DETAIL.md#when-m4-t5--eqs-related-runtime-tests--inline-array-safety), [DESIGN §15.3–§15.4](../When_Reactivity_Iteration_Design_v2_2.md)

---

## Context

WHEN-BATCH-09 completed: 87 tests pass (59 WhenNode + 5 ReadEqs lowering + 12 SpawnEqs lowering + 2 skipped). `ReadEqsResultNode` and `SpawnEqsSensorNode` lowering are fully implemented.

This batch implements runtime tests (integration-level, real Roslyn compilation + generated code execution) for EQS-related functionality.

---

## Files to Read First

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/WhenNodeRuntimeTests.cs` — for runtime test patterns (`BuildValueChangedAsset`, `ReadSlotField<T>`, `ResetBoolField`, `WriteSlotField<T>` approach)
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs` (lines 1-200) — for `CompileAndLoad`, `GetBlueprintState`, `TickFrame`
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockEntityCommandBuffer.cs` — for `OpsForInspection`, `EcbOp_AddComponentUnmanaged<T>`, `EcbOp_CreateEntityRecord`
4. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` — for `EqsCognitiveBuffer`, `EqsSensor`, `EqsResult`, `GetSpanRW()`
5. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs` — for `EqsSensorHandle.ChildId`
6. `FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs` — for `PartMetadata.ParentEntity`, `PartMetadata.InstanceId`

---

## Files to Create

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/ReadEqsResultNodeRuntimeTests.cs` — new file
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/SpawnEqsSensorRuntimeTests.cs` — new file
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/WhenNodeEqsInlineArrayTests.cs` — new file

## Files to Modify

4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/WhenNodeRuntimeTests.cs` — add 4 EQS-specific tests at the end of the class

---

## Infrastructure Notes

### Component Registration

The `BlueprintTestFixture` constructor does NOT register EQS component types. Any test that creates child entities with `EqsCognitiveBuffer`, `EqsSensor`, or `PartMetadata` components must register them first:

```csharp
fixture.World.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>();
fixture.World.RegisterComponent<Fdp.Toolkit.Spatial.Eqs.EqsSensor>();
fixture.World.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
```

For `SpawnEqsSensorRuntimeTests`, these must be registered BEFORE ticking (otherwise ECB playback fails when `AddComponent` is replayed).

### WriteSlotField Helper

The existing `ReadSlotField<T>` reads from blueprint state. A `WriteSlotField<T>` that writes to the live state blob is needed for EQS WhenNode tests (to seed the `SensorHandle` variable before ticking):

```csharp
private static unsafe void WriteSlotField<T>(
    BlueprintTestFixture fixture,
    BlueprintAsset asset,
    Entity entity,
    string fieldName,
    T value)
    where T : unmanaged
{
    var hash = BlueprintIdHash.Compute(asset.AssetId);
    Assert.True(fixture.Registry.TryGetById(hash, out var def),
        $"Blueprint definition not found for asset {asset.AssetId}");
    var stateType = def!.StateClrType;
    Assert.NotNull(stateType);
    var state = fixture.GetBlueprintState(asset, entity);
    Assert.NotNull(state);
    var offset = (int)Marshal.OffsetOf(stateType!, fieldName);
    var span = state!.Value.AsSpan();
    ref byte slotBase = ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));
    Unsafe.WriteUnaligned(ref Unsafe.Add(ref slotBase, offset), value);
}
```

This helper allows tests to directly seed a blueprint variable in the live state slot.

### Setting Up EqsCognitiveBuffer Test Data

`EqsCognitiveBuffer` has a `GetSpanRW()` helper that returns a `Span<EqsResult>` backed by the inline array:

```csharp
var buffer = new EqsCognitiveBuffer
{
    Count            = 1,
    LastUpdateTick   = 1u,    // makes IsReady = true
    LastUpdateTimeSeconds = 3f,
};
var span = buffer.GetSpanRW();
span[0] = new EqsResult
{
    EntityId  = 42L,
    PositionX = 1f,
    PositionY = 2f,
    Score     = 0.9f,
};
fixture.World.SetComponent(childEntity, buffer);
```

### EQS WhenNode Runtime Test Setup Pattern

For EQS WhenNode tests:
1. Build asset with `WhenNode(EqsResult)` + a `VariableDecl "SensorHandle"` of type `"FDP.Eqs.EqsSensorHandle"`
2. `CompileAndLoad` + create parent entity + `AttachBlueprint` + `TickFrame(0.016f)` (initialises state)
3. Create child entity (using `fixture.CreateEntity()`); add `EqsCognitiveBuffer` + `EqsSensor` components
4. Use `WriteSlotField<EqsSensorHandle>(fixture, asset, parentEntity, "SensorHandle", new EqsSensorHandle(childEntity))` to seed the handle
5. Tick again to trigger the EQS check

### EQS CompileOptions for Runtime Tests

Tests that compile blueprints with EQS content need to configure `EqsTemplates` in `CompileOptions`. The existing `StubEqsTemplateCatalog` (see `SpawnEqsSensorValidatorTests.cs`) can be reused. For WhenNode EQS tests (which don't use `SpawnEqsSensorNode`), you can use `null` for `EqsTemplates`.

For `SpawnEqsSensorRuntimeTests`, use a `StubEqsTemplateCatalog` that always returns `true` for `Contains(Guid)`.

---

## Part A — Additions to `WhenNodeRuntimeTests.cs`

### A1 — EQS WhenNode asset builder

Add a private helper `BuildEqsResultAsset(EqsTrigger trigger, ...)` near the other asset builders:

```csharp
/// <summary>
/// Builds a minimal Instance blueprint with WhenNode(EqsResult) mode,
/// a SensorHandle variable, and a WasFired bool variable.
/// OnFired -> SetVariable(WasFired = true).
/// </summary>
private static (BlueprintAsset asset, string synthFieldName, string sensorVarName) BuildEqsResultAsset(
    EqsTrigger trigger,
    WhenEdge edges         = WhenEdge.RisingEdge,
    float? scoreThreshold  = null,
    float? maxAgeSeconds   = null)
{
    var assetId      = Guid.NewGuid();
    var graphId      = Guid.NewGuid();
    var nodeId       = Guid.NewGuid();
    var id8          = nodeId.ToString("N").Substring(0, 8);
    var synthName    = $"_when_{id8}_prev";
    const string sensorVarName = "SensorHandle";

    // Variables
    var sensorHandleVar = new VariableDecl
    {
        Id   = Guid.NewGuid(),
        Name = sensorVarName,
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };
    var firedVarId = Guid.NewGuid();
    var wasFiredVar = new VariableDecl
    {
        Id   = firedVarId,
        Name = "WasFired",
        Type = new BlueprintTypeRef { TypeId = "bool" },
        DefaultValueJson = "false",
    };

    // Entry node
    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);

    // WhenNode
    var whenNode = new WhenNode
    {
        Id       = nodeId,
        Mode     = WhenMode.EqsResult,
        Edges    = edges,
        EqsResult = new EqsResultPayload
        {
            SensorVariableName = sensorVarName,
            Trigger            = trigger,
            ScoreThreshold     = scoreThreshold ?? 0f,
            MaxAgeSeconds      = maxAgeSeconds  ?? 0f,
        },
    };
    var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
    var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
    var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
    whenNode.Pins.Add(whenExecIn);
    whenNode.Pins.Add(whenExecOut);
    whenNode.Pins.Add(whenOnFired);

    // SetVariable(WasFired=true) + literal true + return nodes
    var litId     = Guid.NewGuid();
    var litOutPin = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                               TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
    var litNode   = new LiteralNode { Id = litId, TypeId = "bool", ValueJson = "true" };
    litNode.Pins.Add(litOutPin);

    var setId      = Guid.NewGuid();
    var setExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
    var setExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
    var setDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
    var setNode    = new SetVariableNode { Id = setId, VariableId = firedVarId.ToString() };
    setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

    var retId    = Guid.NewGuid();
    var retIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    var retNode  = new ReturnNode { Id = retId };
    retNode.Pins.Add(retIn);

    var ret2Id   = Guid.NewGuid();
    var ret2In   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    var ret2Node = new ReturnNode { Id = ret2Id };
    ret2Node.Pins.Add(ret2In);

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, whenNode, litNode, setNode, retNode, ret2Node },
        Links =
        {
            new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
            new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id, ToNodeId = retNode.Id,  ToPinId = retIn.Id },
            new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id, ToNodeId = setNode.Id,  ToPinId = setExecIn.Id },
            new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id,  ToNodeId = ret2Node.Id, ToPinId = ret2In.Id },
            new Link { FromNodeId = litNode.Id,  FromPinId = litOutPin.Id,   ToNodeId = setNode.Id,  ToPinId = setDataIn.Id },
        },
    };

    return (new BlueprintAsset
    {
        AssetId   = assetId,
        Name      = "WhenEqsTest",
        Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
        Variables = { sensorHandleVar, wasFiredVar },
        Graphs    = { graph },
    }, synthName, sensorVarName);
}
```

### A2 — Helper: SetupEqsChildEntity

Add a private helper for creating a child entity with sensor components:

```csharp
private static Entity SetupEqsChildEntity(
    BlueprintTestFixture fixture,
    EqsCognitiveBuffer buffer,
    EqsSensor sensor = default)
{
    // EQS component types must be registered before use
    if (!fixture.World.IsComponentRegistered<EqsCognitiveBuffer>())
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    if (!fixture.World.IsComponentRegistered<EqsSensor>())
        fixture.World.RegisterComponent<EqsSensor>();

    var childEntity = fixture.CreateEntity();
    fixture.World.AddComponent(childEntity, buffer);
    fixture.World.AddComponent(childEntity, sensor);
    return childEntity;
}
```

> **Note:** Check if `IsComponentRegistered<T>` exists on `EntityRepository`. If not, use a try/catch or a flag field to guard the double-registration. Alternatively, just always register them and suppress the exception if already registered. Look at how `RegisterComponent` behaves on duplicate calls — likely it's idempotent or throws. If it throws, use a static `HashSet` to track registrations.

### A3 — 4 New Tests in `WhenNodeRuntimeTests.cs`

Append at the end of the `WhenNodeRuntimeTests` class:

```csharp
[Fact]
public void EqsResult_FirstReady_FiresOnceOnChildEntity()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();

    var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.FirstReady);
    fixture.CompileAndLoad(asset);
    var parentEntity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, parentEntity);

    // Tick 1: buffer not ready yet (LastUpdateTick = 0)
    var notReadyBuffer = default(EqsCognitiveBuffer);
    var childEntity    = SetupEqsChildEntity(fixture, notReadyBuffer);
    WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));
    fixture.TickFrame(0.016f);
    bool wv1 = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    Assert.False(wv1, "Should not fire on tick 1 when buffer not ready");

    // Tick 2: buffer becomes ready (LastUpdateTick = 1)
    var readyBuffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 0 };
    fixture.World.SetComponent(childEntity, readyBuffer);
    fixture.TickFrame(0.016f);
    bool wv2 = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    Assert.True(wv2, "Should fire on first ready tick");

    // Tick 3: no re-fire (epoch guard prevents it)
    ResetBoolField(fixture, asset, parentEntity, "WasFired");
    fixture.TickFrame(0.016f);
    bool wv3 = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    Assert.False(wv3, "Should NOT re-fire on subsequent ticks");
}

[Fact]
public void EqsResult_TopChanged_PositionalQueries_HashesPosition()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();

    var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.TopChanged);
    fixture.CompileAndLoad(asset);
    var parentEntity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, parentEntity);

    // Set up child entity with positional result (EntityId = 0)
    var buffer1 = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 1 };
    var span1   = buffer1.GetSpanRW();
    span1[0]    = new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 2f, Score = 0.5f };
    var childEntity = SetupEqsChildEntity(fixture, buffer1);
    WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));

    // Tick 1: first evaluation (LastEvaluatedEpoch == 0, so OnFired fires on first detection)
    // Note: TopChanged fires when epoch != lastEvaluated AND top identity changes.
    // The first tick sets epoch = 1 but LastEvaluatedEpoch = 0, so it fires if top differs.
    // Verify: WasFired is set on first tick with a result.
    fixture.TickFrame(0.016f);
    // First tick: epoch transitions from 0 -> recorded; may or may not fire depending on LastEvaluatedEpoch==0 guard
    // Actually per DESIGN §6.5: "if (sensor.Epoch != prev.LastEvaluatedEpoch)" and "prev.LastEvaluatedEpoch != 0" guards re-fire
    // So on first tick (prev=0), it fires and records, but ONLY fires OnFired if prev.LastEvaluatedEpoch != 0 is FALSE (epoch=0 initially)
    // Check actual behavior: first tick should update prev state without firing OnFired
    ResetBoolField(fixture, asset, parentEntity, "WasFired");

    // Tick 2: epoch advances (update buffer epoch)
    var sensor2  = new EqsSensor { Epoch = 2u };
    fixture.World.AddComponent(childEntity, sensor2); // overwrite if needed, or SetComponent
    var buffer2  = new EqsCognitiveBuffer { LastUpdateTick = 2u, Count = 1 };
    var span2    = buffer2.GetSpanRW();
    span2[0]     = new EqsResult { EntityId = 0L, PositionX = 99f, PositionY = 99f, Score = 0.8f }; // different position
    fixture.World.SetComponent(childEntity, buffer2);
    fixture.TickFrame(0.016f);
    bool fired   = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    Assert.True(fired, "Should fire when positional query top changes");
}

[Fact]
public void EqsResult_BecomesStale_UsesSimTimeNotTicks()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();

    // MaxAge = 2 seconds; buffer written at time=5s; should fire when time >= 5+2 = 7s
    var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.BecomesStale, maxAgeSeconds: 2f);
    fixture.CompileAndLoad(asset);
    var parentEntity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, parentEntity);

    // Buffer last updated at time = 5s
    var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 0, LastUpdateTimeSeconds = 5f };
    var childEntity = SetupEqsChildEntity(fixture, buffer);
    WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));

    // Advance to time = 6s (not stale yet: 6 - 5 = 1 < 2)
    fixture.TickFrame(6f); // Big dt to jump to ~6s
    bool notYetStale = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    // At time ~6s, age = 1s < maxAge 2s; should not fire

    ResetBoolField(fixture, asset, parentEntity, "WasFired");

    // Advance to time = 8s (stale: 8 - 5 = 3 > 2)
    fixture.TickFrame(2f); // Advance by 2 more seconds (total ~8s)
    bool isStale = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    Assert.True(isStale, "Should fire when buffer age exceeds MaxAge based on sim time");
}

[Fact]
public void EqsResult_ChildEntityDestroyed_NoFire_NoCrash()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();

    var (asset, _, sensorVarName) = BuildEqsResultAsset(EqsTrigger.FirstReady);
    fixture.CompileAndLoad(asset);
    var parentEntity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, parentEntity);

    // Set up a valid child entity with a ready buffer
    var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 1 };
    var childEntity = SetupEqsChildEntity(fixture, buffer);
    WriteSlotField(fixture, asset, parentEntity, sensorVarName, new EqsSensorHandle(childEntity));

    // Destroy the child entity BEFORE ticking
    fixture.World.DestroyEntity(childEntity);
    Assert.False(fixture.World.IsAlive(childEntity));

    // Tick should not crash; OnFired should not be called
    var exception = Record.Exception(() => fixture.TickFrame(0.016f));
    Assert.Null(exception);

    bool fired = ReadSlotField<bool>(fixture, asset, parentEntity, "WasFired");
    Assert.False(fired, "No fire when child entity is destroyed");
}
```

> **Note on `EqsResult_TopChanged_PositionalQueries_HashesPosition`:** This test requires `EqsSensor` to be added to the child entity. The `SetupEqsChildEntity` helper should also add `EqsSensor` (passed as parameter). Look at the TopChanged lowering code (BATCH-08) to understand the exact epoch-gating logic — specifically when `prev.LastEvaluatedEpoch == 0`, the first tick updates prev without firing (to avoid spurious fire on first evaluation). The test must account for this by resetting `WasFired` after tick 1.

> **Note on Epoch management:** `EqsSensor.Epoch` must change between tick 1 and tick 2 for TopChanged to consider the buffer updated. If `EqsSensor` is not present on the child entity, `view.GetComponentRO<EqsSensor>(handle.ChildId)` may panic. Make sure to add `EqsSensor` with `Epoch = 1u` initially in `SetupEqsChildEntity`.

---

## Part B — `SpawnEqsSensorRuntimeTests.cs`

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/SpawnEqsSensorRuntimeTests.cs`.

### B1 — Asset Builders

```csharp
// Asset with SpawnEqsSensorNode in the Tick graph.
// The node stores the Handle in a "MySensor" variable.
// No wired pins (all literal defaults).
private static (BlueprintAsset asset, Guid templateId, Guid nodeId) BuildSpawnAsset(
    float? literalSearchRadius = null)
{
    var assetId    = Guid.NewGuid();
    var graphId    = Guid.NewGuid();
    var nodeId     = Guid.NewGuid();
    var templateId = Guid.NewGuid();

    // SpawnEqsSensorNode
    var spawnNode = new SpawnEqsSensorNode { Id = nodeId, TemplateAssetId = templateId };
    var execIn    = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
    var execOut   = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true,  TypeRef = new() };
    var handleOut = new Pin { Id = Guid.NewGuid(), Name = "Handle", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
    var srPin     = new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
    var ffPin     = new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.UInt32" } };
    var ttPin     = new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
    var ppPin     = new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
    var prPin     = new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
    spawnNode.Pins.AddRange(new[] { execIn, execOut, handleOut, srPin, ffPin, ttPin, ppPin, prPin });

    // SetVariable(MySensor = Handle)
    var sensorVarId  = Guid.NewGuid();
    var setId        = Guid.NewGuid();
    var setExecIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
    var setExecOut   = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
    var setDataIn    = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
    var setNode      = new SetVariableNode { Id = setId, VariableId = sensorVarId.ToString() };
    setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

    // Entry + return
    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);

    var retNode  = new ReturnNode { Id = Guid.NewGuid() };
    var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    retNode.Pins.Add(retExecIn);

    var links = new List<Link>
    {
        new Link { FromNodeId = entry.Id,      FromPinId = entryExecOut.Id, ToNodeId = spawnNode.Id, ToPinId = execIn.Id },
        new Link { FromNodeId = spawnNode.Id,  FromPinId = execOut.Id,      ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
        new Link { FromNodeId = setNode.Id,    FromPinId = setExecOut.Id,   ToNodeId = retNode.Id,   ToPinId = retExecIn.Id },
        new Link { FromNodeId = spawnNode.Id,  FromPinId = handleOut.Id,    ToNodeId = setNode.Id,   ToPinId = setDataIn.Id },
    };

    // Optionally wire a SearchRadius literal source
    // (For wired parameter test, caller can wire differently)

    var sensorVar = new VariableDecl
    {
        Id   = sensorVarId,
        Name = "MySensor",
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, spawnNode, setNode, retNode },
        Links = links,
    };

    return (new BlueprintAsset
    {
        AssetId   = assetId,
        Name      = "SpawnSensorTest",
        Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
        Variables = { sensorVar },
        Graphs    = { graph },
    }, templateId, nodeId);
}
```

### B2 — Helper: StubEqsTemplateCatalog

The `SpawnEqsSensorNode` requires a non-null `EqsTemplates` in `CompileOptions` (otherwise BP2031 blocks compilation). Use/copy the `StubEqsTemplateCatalog` already declared in `SpawnEqsSensorValidatorTests.cs`:

```csharp
private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
{
    public bool Contains(Guid assetId) => true;
}
```

Use this in `CompileOptions`:
```csharp
var options = new CompileOptions(
    Mode:            CompilerMode.Debug,
    NodeRegistry:    BuiltInNodeRegistry.Instance,
    TypeRegistry:    StaticTypeRegistry.Instance,
    EngineEvents:    BuiltInEngineEventCatalog.Instance,
    ChannelCommands: BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:  BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>(),
    EqsTemplates:    new AlwaysContainsCatalog());
```

> **Note:** Check the `CompileOptions` constructor signature for EQS-template catalog. If `EqsTemplates` is positional, pass it in the right order; if named, use the named parameter `EqsTemplates:`.

### B3 — Tests

```csharp
[Fact]
public void Spawn_CreatesChildEntity()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    // ECB should have recorded a CreateEntity op
    var createOps = fixture.Ecb.OpsForInspection.OfType<EcbOp_CreateEntityRecord>().ToList();
    Assert.True(createOps.Count >= 1, "Expected at least one CreateEntity ECB op");
}

[Fact]
public void Spawn_AttachesPartMetadata_WithParent()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);
    fixture.TickFrame(0.016f);
    fixture.Ecb.Playback(fixture.World); // replay ECB to apply ops

    // Find the most recently created entity (the child) and check PartMetadata
    // Since CreateEntity is eager in MockECB, the child is the last created entity.
    // Alternatively: find all entities with PartMetadata component.
    var partMetaOps = fixture.Ecb.OpsForInspection
        .OfType<EcbOp_AddComponentUnmanaged<PartMetadata>>()
        .ToList();
    Assert.True(partMetaOps.Count >= 1);
    // Note: Ecb.OpsForInspection is cleared after Playback; inspect BEFORE Playback.
}
```

> **Note:** `fixture.Ecb.Playback()` clears `OpsForInspection`. Assert BEFORE calling Playback, or use a separate approach. Looking at `TickFrame`, it calls `Ecb.Playback` internally. So after `TickFrame`, the ops are cleared. You need to either:
> 1. Inspect BEFORE `TickFrame` (not possible since ops are generated during tick)
> 2. Modify the fixture to use a separate recording ECB (complex)  
> 3. Apply ECB playback manually and check via component queries

**Simplest approach:** After `TickFrame`, since `MockEntityCommandBuffer.CreateEntity()` is EAGER, the child entity already exists in the world BEFORE playback. So check the component AFTER `TickFrame` (which includes playback) using component queries.

**Revised approach for all SpawnEqs tests:**
1. `TickFrame(0.016f)` (includes eager CreateEntity + ECB playback of AddComponent ops)
2. After tick, query world for entities with `PartMetadata` component to find the child
3. Assert on child's components

```csharp
[Fact]
public void Spawn_CreatesChildEntity()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    // After tick, the child entity should have PartMetadata
    var childEntities = fixture.World.Query()
        .With<PartMetadata>()
        .ToList();
    Assert.NotEmpty(childEntities);
}

[Fact]
public void Spawn_AttachesPartMetadata_WithParent()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var parentEntity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, parentEntity);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<PartMetadata>().ToList();
    Assert.True(childEntities.Count >= 1);
    var meta = fixture.World.GetComponentRO<PartMetadata>(childEntities[0]);
    Assert.Equal(parentEntity, meta.ParentEntity);
}

[Fact]
public void Spawn_AttachesEqsSensor_WithCorrectTemplate()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, templateId, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<EqsSensor>().ToList();
    Assert.True(childEntities.Count >= 1);
    var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
    uint expectedBpId = (uint)BlueprintIdHash.Compute(templateId);
    Assert.Equal(expectedBpId, sensor.BlueprintId);
}

[Fact]
public void Spawn_AttachesCognitiveBuffer_ZeroInit()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<EqsCognitiveBuffer>().ToList();
    Assert.True(childEntities.Count >= 1);
    var buffer = fixture.World.GetComponentRO<EqsCognitiveBuffer>(childEntities[0]);
    Assert.False(buffer.IsReady, "Buffer should start not-ready");
}

[Fact]
public void Spawn_PopulatesHandleOutput()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    // After tick, the MySensor variable should hold a valid handle
    var handle = ReadSlotField<EqsSensorHandle>(fixture, asset, entity, "MySensor");
    Assert.True(handle.IsValid, "MySensor handle should point to a valid entity");
    Assert.True(fixture.World.IsAlive(handle.ChildId), "Handle's ChildId should be alive");
}

[Fact]
public void Spawn_EmitsEqsSensor_WithEpochOne()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<EqsSensor>().ToList();
    Assert.True(childEntities.Count >= 1);
    var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
    Assert.Equal(1u, sensor.Epoch);
}

[Fact]
public void Spawn_AllFiveFields_HaveExpectedDefaults()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<EqsSensor>().ToList();
    var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
    Assert.Equal(0f,       sensor.SearchRadius);
    Assert.Equal(0u,       sensor.FactionFilter);
    Assert.Equal(0f,       sensor.ThreatThreshold);
    Assert.Equal((byte)0,  sensor.PublishPolicy);
    Assert.Equal((byte)0,  sensor.Priority);
}

[Fact]
public void Spawn_PartMetadataInstanceId_IsNonZero()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, nodeId) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<PartMetadata>().ToList();
    var meta = fixture.World.GetComponentRO<PartMetadata>(childEntities[0]);
    // InstanceId is derived from nodeId.GetHashCode() baked at compile time
    int expectedId = nodeId.GetHashCode();
    Assert.Equal(expectedId, meta.InstanceId);
}

[Fact]
public void Spawn_MultipleInvocations_CreateDistinctEntities()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    RegisterEqsComponents(fixture);
    var (asset, _, _) = BuildSpawnAsset();
    fixture.CompileAndLoad(asset, MakeEqsOptions());

    // Spawn from two parent entities
    var entity1 = fixture.CreateEntity(); fixture.AttachBlueprint(asset, entity1);
    var entity2 = fixture.CreateEntity(); fixture.AttachBlueprint(asset, entity2);

    fixture.TickFrame(0.016f);

    var childEntities = fixture.World.Query().With<EqsSensor>().ToList();
    Assert.True(childEntities.Count >= 2, "Two parents should produce two child sensor entities");
    // Children must be distinct
    Assert.NotEqual(childEntities[0], childEntities[1]);
}
```

> **Note on `fixture.World.Query().With<T>().ToList()`:** Check what `QueryBuilder.ToList()` returns — it may return `IReadOnlyList<Entity>`. Look at existing runtime tests or the `EntityRepository` API to find the right query pattern for "get all entities with component T". If `ToList()` isn't available, use a `ForEach` or LINQ equivalent.

### B4 — Helper methods

Add to the test class:
```csharp
private static void RegisterEqsComponents(BlueprintTestFixture fixture)
{
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();
    fixture.World.RegisterComponent<PartMetadata>();
}

private static CompileOptions MakeEqsOptions() => new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>(),
    EqsTemplates:      new AlwaysContainsCatalog());
```

---

## Part C — `ReadEqsResultNodeRuntimeTests.cs`

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/ReadEqsResultNodeRuntimeTests.cs`.

This file tests that a compiled blueprint with `ReadEqsResultNode` correctly reads from `EqsCognitiveBuffer`.

### C1 — Asset Builder

```csharp
/// Builds a blueprint with:
/// - SensorHandle : EqsSensorHandle variable
/// - WasReady    : bool variable (written from IsReady output)
/// - TickCount   : int variable (written from ResultCount output)
/// 
/// Tick graph:
///   Entry -> ReadEqsResultNode -> SetVar(WasReady) -> SetVar(TickCount) -> Return
private static (BlueprintAsset asset, string sensorVarName) BuildReadEqsAsset()
{
    var assetId       = Guid.NewGuid();
    var graphId       = Guid.NewGuid();
    var sensorVarId   = Guid.NewGuid();
    var readyVarId    = Guid.NewGuid();
    var countVarId    = Guid.NewGuid();

    // Variables
    var sensorHandleVar = new VariableDecl { Id = sensorVarId, Name = "SensorHandle", Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
    var wasReadyVar     = new VariableDecl { Id = readyVarId,  Name = "WasReady",     Type = new BlueprintTypeRef { TypeId = "bool" } };
    var resultCountVar  = new VariableDecl { Id = countVarId,  Name = "ResultCount",  Type = new BlueprintTypeRef { TypeId = "int" } };

    // ReadEqsResultNode
    var readNodeId  = Guid.NewGuid();
    var readNode    = new ReadEqsResultNode { Id = readNodeId, SensorVariableName = "SensorHandle" };
    var indexPin    = new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
    var isReadyPin  = new Pin { Id = Guid.NewGuid(), Name = "IsReady",     Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
    var countPin    = new Pin { Id = Guid.NewGuid(), Name = "ResultCount", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
    var entityPin   = new Pin { Id = Guid.NewGuid(), Name = "Entity",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
    var posPin      = new Pin { Id = Guid.NewGuid(), Name = "Position",    Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Numerics.Vector2" } };
    var scorePin    = new Pin { Id = Guid.NewGuid(), Name = "Score",       Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
    readNode.Pins.AddRange(new[] { indexPin, isReadyPin, countPin, entityPin, posPin, scorePin });

    // SetVariable(WasReady) consuming IsReady output
    var setReadyId   = Guid.NewGuid();
    var setReadyExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
    var setReadyExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
    var setReadyDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
    var setReadyNode    = new SetVariableNode { Id = setReadyId, VariableId = readyVarId.ToString() };
    setReadyNode.Pins.AddRange(new[] { setReadyExecIn, setReadyExecOut, setReadyDataIn });

    // SetVariable(ResultCount) consuming ResultCount output
    var setCountId      = Guid.NewGuid();
    var setCountExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
    var setCountExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
    var setCountDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
    var setCountNode    = new SetVariableNode { Id = setCountId, VariableId = countVarId.ToString() };
    setCountNode.Pins.AddRange(new[] { setCountExecIn, setCountExecOut, setCountDataIn });

    // Entry + return
    var entry        = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);
    var retNode  = new ReturnNode { Id = Guid.NewGuid() };
    var retIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
    retNode.Pins.Add(retIn);

    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Function,
        Nodes = { entry, readNode, setReadyNode, setCountNode, retNode },
        Links =
        {
            new Link { FromNodeId = entry.Id,       FromPinId = entryExecOut.Id,    ToNodeId = setReadyNode.Id, ToPinId = setReadyExecIn.Id },
            new Link { FromNodeId = setReadyNode.Id, FromPinId = setReadyExecOut.Id, ToNodeId = setCountNode.Id, ToPinId = setCountExecIn.Id },
            new Link { FromNodeId = setCountNode.Id, FromPinId = setCountExecOut.Id, ToNodeId = retNode.Id,     ToPinId = retIn.Id },
            // Data: ReadEqsResult.IsReady -> setReady.Value
            new Link { FromNodeId = readNodeId, FromPinId = isReadyPin.Id,  ToNodeId = setReadyId, ToPinId = setReadyDataIn.Id },
            // Data: ReadEqsResult.ResultCount -> setCount.Value
            new Link { FromNodeId = readNodeId, FromPinId = countPin.Id,    ToNodeId = setCountId, ToPinId = setCountDataIn.Id },
        },
    };

    return (new BlueprintAsset
    {
        AssetId   = assetId,
        Name      = "ReadEqsTest",
        Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
        Variables = { sensorHandleVar, wasReadyVar, resultCountVar },
        Graphs    = { graph },
    }, "SensorHandle");
}
```

### C2 — Tests

```csharp
[Fact]
public void ReadEqsResult_ReturnsIsReady_True_WhenBufferReady()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    fixture.World.RegisterComponent<EqsSensor>();
    var (asset, sensorVarName) = BuildReadEqsAsset();
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);
    fixture.TickFrame(0.016f); // init tick

    // Set up child entity with ready buffer
    var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 2 };
    var child  = fixture.CreateEntity();
    fixture.World.AddComponent(child, buffer);
    WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

    fixture.TickFrame(0.016f);

    bool wasReady    = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
    int  resultCount = ReadSlotField<int>(fixture, asset, entity, "ResultCount");
    Assert.True(wasReady, "IsReady should be true when buffer is ready");
    Assert.Equal(2, resultCount);
}

[Fact]
public void ReadEqsResult_ReturnsIsReady_False_WhenBufferNotReady()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    var (asset, sensorVarName) = BuildReadEqsAsset();
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    // Set up child entity with NOT ready buffer (LastUpdateTick = 0)
    var buffer = new EqsCognitiveBuffer { LastUpdateTick = 0u };
    var child  = fixture.CreateEntity();
    fixture.World.AddComponent(child, buffer);
    WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

    fixture.TickFrame(0.016f);

    bool wasReady = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
    Assert.False(wasReady);
}

[Fact]
public void ReadEqsResult_ReturnsIsReady_False_WhenChildDead()
{
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    var (asset, sensorVarName) = BuildReadEqsAsset();
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    // Create + immediately destroy child
    var child = fixture.CreateEntity();
    fixture.World.DestroyEntity(child);
    WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

    // Should not crash; IsReady should be false
    var exception = Record.Exception(() => fixture.TickFrame(0.016f));
    Assert.Null(exception);

    bool wasReady = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
    Assert.False(wasReady);
}

[Fact]
public void ReadEqsResult_ClampsIndex_ToValidRange()
{
    // This is primarily a compiler test already covered in ReadEqsResultLoweringTests.
    // Here we verify the runtime behavior: requesting index 999 returns the last valid result.
    // (Covered by Lower_ClampsIndex; this runtime test is a sanity check only.)
    // Just compile + load and verify no crash with a high ResultIndex.
    using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    fixture.World.RegisterComponent<EqsCognitiveBuffer>();
    var (asset, sensorVarName) = BuildReadEqsAsset();
    fixture.CompileAndLoad(asset);
    var entity = fixture.CreateEntity();
    fixture.AttachBlueprint(asset, entity);

    var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 1 };
    var span   = buffer.GetSpanRW();
    span[0]    = new EqsResult { EntityId = 77L, Score = 0.5f };
    var child  = fixture.CreateEntity();
    fixture.World.AddComponent(child, buffer);
    WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

    // The test blueprint uses ResultIndex = 0 (unconnected, default). Just verify no crash.
    var exception = Record.Exception(() => fixture.TickFrame(0.016f));
    Assert.Null(exception);
    bool wasReady = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
    Assert.True(wasReady);
}
```

---

## Part D — `WhenNodeEqsInlineArrayTests.cs`

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/WhenNodeEqsInlineArrayTests.cs`.

These tests verify that the generated code accesses `EqsCognitiveBuffer` results via `GetSpanRO()` (not direct `Results[0]` access that would cause defensive copies).

They are **compiler output tests** (pattern assertions on generated source) — similar to lowering tests but using `CompileOptions` with the full Roslyn compilation path.

```csharp
namespace Hrot.Blueprints.Tests.Runtime;

[Collection("DebugProbe")]
public sealed class WhenNodeEqsInlineArrayTests
{
    private static string? CompileToSource(BlueprintAsset asset, CompileOptions? options = null)
    {
        var compiler = new BlueprintCompiler();
        var opts = options ?? new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var result = compiler.Compile(asset, opts);
        return result.Succeeded ? result.GeneratedSource : null;
    }

    [Fact]
    public void EqsTopChanged_Generated_UsesGetSpanRO()
    {
        var asset  = WhenNodeLoweringTests.BuildEqsResultAsset(EqsTrigger.TopChanged);
        // Reuse the helper from WhenNodeLoweringTests or duplicate asset builder here.
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        Assert.Contains("GetSpanRO()", source!);
    }

    [Fact]
    public void EqsTopChanged_Generated_DoesNotUseDirectResultsIndex()
    {
        var asset  = WhenNodeLoweringTests.BuildEqsResultAsset(EqsTrigger.TopChanged);
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        // Direct indexer ".Results[" would cause defensive copy issues.
        // Generated code must not contain this pattern (it must use GetSpanRO() instead).
        Assert.DoesNotContain(".Results[", source!);
    }

    [Fact]
    public void ReadEqsResult_Generated_UsesGetSpanRO()
    {
        var asset  = ReadEqsResultNodeRuntimeTests.BuildReadEqsAssetForInlineArrayTest();
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        Assert.Contains("GetSpanRO()", source!);
    }

    [Fact]
    public void ReadEqsResult_Generated_ClampsIndex()
    {
        var asset  = ReadEqsResultNodeRuntimeTests.BuildReadEqsAssetForInlineArrayTest();
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        Assert.Contains("Math.Clamp", source!);
    }
}
```

> **Note:** This test class references helpers from `WhenNodeLoweringTests` and `ReadEqsResultNodeRuntimeTests`. Either:
> 1. Make the asset-builder helpers `internal static` so they're accessible across test files, OR
> 2. Duplicate the simple asset builders in `WhenNodeEqsInlineArrayTests.cs`
> 
> **Preferred approach:** Expose `static internal` methods on the test classes OR duplicate the 10-line inline asset builder in `WhenNodeEqsInlineArrayTests`. Duplication is fine here given the small size.
> 
> Add `internal static BlueprintAsset BuildReadEqsAssetForInlineArrayTest()` to `ReadEqsResultNodeRuntimeTests` (or reuse `BuildReadEqsAsset()`).

---

## Build & Test

```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj 2>&1 | Select-String "error" | Where-Object { $_ -notmatch "0 Error" } | Select-Object -Last 20
```

```
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~WhenNode|FullyQualifiedName~ReadEqs|FullyQualifiedName~SpawnEqs" 2>&1 | Select-String "passed|failed|Total|FAILED" | Select-Object -Last 10
```

All 87 pre-existing tests must still pass. New runtime + inline-array tests must pass.

---

## Commit

```
git -C d:\WORK\IOS-IG-SimHost-FDP add -A
git -C d:\WORK\IOS-IG-SimHost-FDP commit -m "WHEN-BATCH-10: EQS-related runtime tests + inline-array safety (M4-T5)"
```
