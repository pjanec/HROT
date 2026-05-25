# WHEN-BATCH-04 Instructions: EventFired Emitter Fixes + Runtime Tests (M2-T4)

## Context
- **Task**: WHEN-M2-T4 — runtime tests for ValueChanged and EventFired lowering
- **Design**: `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md`
- **Task detail**: `.dev/blueprints-3-when-node/TASK-DETAIL.md` → section WHEN-M2-T4
- **Task tracker**: `.dev/blueprints-3-when-node/TASK-TRACKER.md`

## Pre-work: Correctness Fixes from BATCH-03

During BATCH-03, the StatementEmitter for `IrOp_WhenEventFiredCheck` was implemented with
bugs that only surface during Roslyn compilation (the lowering tests only check source text).
These must be fixed in this batch before the runtime tests can pass.

### Fix 1 — Add `TargetFieldName` to `IrOp_WhenEventFiredCheck`

**File**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs`

The current `IrOp_WhenEventFiredCheck` record is missing the field name to use for the
self-target filter. The StatementEmitter hardcodes `.Target`, which is wrong in general.

**Change**: Add `string TargetFieldName` as the third constructor parameter:

```csharp
// BEFORE:
public sealed record IrOp_WhenEventFiredCheck(
    string EventFqn,
    bool FilterSelf,
    string? PayloadFieldPath,
    string? PayloadOperatorCSharp,
    string? PayloadValueLiteral
) : IrOperation;

// AFTER:
public sealed record IrOp_WhenEventFiredCheck(
    string EventFqn,
    bool FilterSelf,
    string TargetFieldName,   // <-- NEW: name of Entity field to check for self-filter
    string? PayloadFieldPath,
    string? PayloadOperatorCSharp,
    string? PayloadValueLiteral
) : IrOperation;
```

### Fix 2 — Carry `TargetFieldName` from Stage 5

**File**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`

In `ScheduleWhenNode`, case `WhenMode.EventFired`, the `IrOp_WhenEventFiredCheck`
constructor call must pass `ef.TargetFieldName ?? "Target"`:

```csharp
// BEFORE:
Operation = new IrOp_WhenEventFiredCheck(
    EventFqn:              ef.EventTypeId,
    FilterSelf:            filterSelf,
    PayloadFieldPath:      payloadField,
    PayloadOperatorCSharp: payloadOp,
    PayloadValueLiteral:   payloadVal),

// AFTER:
Operation = new IrOp_WhenEventFiredCheck(
    EventFqn:              ef.EventTypeId,
    FilterSelf:            filterSelf,
    TargetFieldName:       ef.TargetFieldName ?? "Target",
    PayloadFieldPath:      payloadField,
    PayloadOperatorCSharp: payloadOp,
    PayloadValueLiteral:   payloadVal),
```

### Fix 3 — Fix StatementEmitter: fast path + full scan path

**File**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`

Three bugs in the `case IrOp_WhenEventFiredCheck op:` handler:

**Bug A — Fast path** uses `view.EventBus as IEventBus)?.HasEvent<T>()`.
`ISimulationView` has no `EventBus` property → Roslyn compile error.
Fix: use `view.ReadEvents<T>().Length > 0` instead.

**Bug B — Full scan path** uses `__events_X.Count`.
`ReadEvents<T>()` returns `ReadOnlySpan<T>` which has `.Length` not `.Count`.
Fix: use `.Length`.

**Bug C — Full scan path** hardcodes `__ev.Target` for the self-filter check.
Fix: use `__ev.{op.TargetFieldName}`.

Full replacement for the `case IrOp_WhenEventFiredCheck op:` block:

```csharp
case IrOp_WhenEventFiredCheck op:
{
    var evtShort = op.EventFqn.Split('.').Last();

    bool hasFilters = op.FilterSelf || op.PayloadFieldPath is not null;

    if (!hasFilters)
    {
        // Fast path: check whether any events of this type arrived this frame
        if (idx >= 0)
            e.WriteLine($"bool __t{idx} = {wv}.ReadEvents<global::{op.EventFqn}>().Length > 0;");
    }
    else
    {
        // Full scan path: iterate events and apply filters
        if (idx >= 0)
        {
            e.WriteLine($"bool __t{idx};");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var __events_{evtShort} = {wv}.ReadEvents<global::{op.EventFqn}>();");
            e.WriteLine($"bool __matched_{evtShort} = false;");
            e.WriteLine($"for (int __i = 0; __i < __events_{evtShort}.Length; __i++)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"var __ev = __events_{evtShort}[__i];");

            if (op.FilterSelf)
                e.WriteLine($"if (__ev.{op.TargetFieldName} != self) continue;");

            if (op.PayloadFieldPath is not null && op.PayloadOperatorCSharp is not null && op.PayloadValueLiteral is not null)
                e.WriteLine($"if (!(__ev.{op.PayloadFieldPath} {op.PayloadOperatorCSharp} {op.PayloadValueLiteral})) continue;");

            e.WriteLine($"__matched_{evtShort} = true;");
            e.WriteLine("break;");
            e.Outdent();
            e.WriteLine("}");
            e.WriteLine($"__t{idx} = __matched_{evtShort};");
            e.Outdent();
            e.WriteLine("}");
        }
    }
    break;
}
```

### Fix 4 — Update lowering test 7 assertion

**File**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/WhenNodeLoweringTests.cs`

Test `Lower_EventFired_NoFilters_EmitsHasEventFastPath` currently asserts
`Assert.Contains("HasEvent", src)`. The fast path no longer calls `HasEvent`; instead
it checks `.Length > 0`. Update the assertion:

```csharp
// BEFORE:
Assert.Contains("HasEvent", src);

// AFTER:
Assert.Contains(".Length > 0", src);
```

The `Assert.DoesNotContain("for (int", src)` assertion stays unchanged (still valid for fast path).

---

## Main Task: WHEN-M2-T4 Runtime Tests

### Fixture change: Add `CompileAndLoad` overload accepting `CompileOptions`

**File**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

The EventFired runtime tests use a locally-defined test event struct (not in
`BuiltInEngineEventCatalog`). To bypass the Stage 2 BP2005 catalog check, the tests
need to supply a custom `CompileOptions` with an empty event catalog.

Refactor: extract the body of `CompileAndLoadMany(assets, mode)` into a private
`CompileAndLoadCore(assets, options)` method, and add two public overloads:

```csharp
// ---- Compile and load ---------------------------------------------------

/// <summary>
/// Compiles one Blueprint asset and loads it into a new collectible ALC.
/// </summary>
public Assembly CompileAndLoad(BlueprintAsset asset, CompilerMode mode = CompilerMode.Debug)
    => CompileAndLoadCore(new[] { asset }, MakeDefaultOptions(mode));

/// <summary>
/// Compiles one Blueprint asset with custom CompileOptions.
/// </summary>
[MethodImpl(MethodImplOptions.NoInlining)]
public Assembly CompileAndLoad(BlueprintAsset asset, CompileOptions options)
    => CompileAndLoadCore(new[] { asset }, options);

/// <summary>
/// Compiles multiple Blueprint assets and loads them into a new collectible ALC.
/// </summary>
[MethodImpl(MethodImplOptions.NoInlining)]
public Assembly CompileAndLoadMany(
    IReadOnlyList<BlueprintAsset> assets,
    CompilerMode mode = CompilerMode.Debug)
    => CompileAndLoadCore(assets, MakeDefaultOptions(mode));

private static CompileOptions MakeDefaultOptions(CompilerMode mode) => new CompileOptions(
    Mode:              mode,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>());

/// <summary>Core implementation shared by all CompileAndLoad overloads.</summary>
[MethodImpl(MethodImplOptions.NoInlining)]
private Assembly CompileAndLoadCore(
    IReadOnlyList<BlueprintAsset> assets,
    CompileOptions options)
{
    // Move the body of the current CompileAndLoadMany here verbatim,
    // using `options` directly instead of constructing it internally.
    ...
}
```

The existing `SimulateReload` method constructs its own options internally, so it does
NOT need to call `CompileAndLoadCore`.

### New test file: `WhenNodeRuntimeTests.cs`

**File to create**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/WhenNodeRuntimeTests.cs`

#### Preamble and helper infrastructure

```csharp
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Mocks;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// A minimal event struct used only in WhenNode runtime tests.
/// Not in BuiltInEngineEventCatalog — tests use EmptyEventCatalog to bypass Stage 2.
/// </summary>
[EventId(90999)]
[StructLayout(LayoutKind.Sequential)]
public struct WhenTestHitEvent
{
    public Entity Target;
    public float Damage;
}

[Collection("DebugProbe")]
public sealed class WhenNodeRuntimeTests
{
    // ---- Empty event catalog (bypasses Stage 2 BP2005 for test event types) ----

    private sealed class EmptyEventCatalog : IEngineEventCatalog
    {
        public static readonly EmptyEventCatalog Instance = new();
        public System.Collections.Generic.IReadOnlyList<EngineEventCatalogEntry> GetEntries()
            => Array.Empty<EngineEventCatalogEntry>();
    }

    private static CompileOptions OptionsWithEmptyEventCatalog() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      EmptyEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ---- State reading helpers ----

    /// <summary>
    /// Reads a value-type field from a blueprint slot's state struct using Marshal.OffsetOf.
    /// Works with CompileAndLoad output (StateFields not populated by generated registrar).
    /// </summary>
    private static T ReadSlotField<T>(
        BlueprintTestFixture fixture,
        BlueprintAsset asset,
        Entity entity,
        string fieldName)
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
        return MemoryMarshal.Read<T>(state!.Value.AsSpan().Slice(offset, Unsafe.SizeOf<T>()));
    }

    // ---- AnotherTestComponent helper ----

    private static void SetX(BlueprintTestFixture fixture, Entity entity, float x)
        => fixture.World.GetComponentRW<AnotherTestComponent>(entity).X = x;
}
```

#### ValueChanged test helpers

Add a private helper method `BuildValueChangedAsset` that returns both the asset and the
synthesized field name:

```csharp
/// <summary>
/// Builds a minimal Instance blueprint with a WhenNode in ValueChanged mode.
/// ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent"
/// PropertyPath = "X" (float)
/// </summary>
private static (BlueprintAsset asset, string synthFieldName) BuildValueChangedAsset(
    WhenEdge edges = WhenEdge.RisingEdge,
    ValueChangedSource source = ValueChangedSource.SelfComponent,
    float epsilon = 0f)
{
    var assetId  = Guid.NewGuid();
    var graphId  = Guid.NewGuid();
    var nodeId   = Guid.NewGuid();
    var id8      = nodeId.ToString("N").Substring(0, 8);
    var synthName = $"_when_{id8}_prev";

    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

    var whenNode = new WhenNode
    {
        Id    = nodeId,
        Mode  = WhenMode.ValueChanged,
        Edges = edges,
        ValueChanged = new ValueChangedPayload
        {
            ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
            PropertyPath    = "X",
            Source          = source,
            Epsilon         = epsilon,
        },
    };
    var execIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
    var execOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
    var onFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
    whenNode.Pins.Add(execIn);
    whenNode.Pins.Add(execOut);
    whenNode.Pins.Add(onFired);

    var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, whenNode },
        Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                             ToNodeId = whenNode.Id, ToPinId = execIn.Id } },
    };
    var asset = new BlueprintAsset
    {
        AssetId = assetId, Name = "WhenVC",
        Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
    };
    return (asset, synthName);
}
```

#### EventFired test helpers

Add a private helper `BuildEventFiredAsset` that builds a blueprint with a WhenNode(EventFired)
wired to a SetVariableNode that sets `WasFired = true`:

```csharp
/// <summary>
/// Builds an Instance blueprint with:
///   - VariableDecl "WasFired" (bool, default false)
///   - EventEntryNode → WhenNode(EventFired) → SetVariableNode(WasFired = true)
///                                                ↑ LiteralNode(bool, "true")
/// </summary>
private static BlueprintAsset BuildEventFiredAsset(
    string eventTypeId                     = "Hrot.Blueprints.Tests.Runtime.WhenTestHitEvent",
    EventTargetFilter targetFilter         = EventTargetFilter.None,
    string? targetFieldName                = null,
    PayloadCondition? payloadCheck         = null)
{
    var assetId   = Guid.NewGuid();
    var graphId   = Guid.NewGuid();
    var varId     = Guid.NewGuid();

    // ---- Variable declaration ----
    var wasFiredVar = new VariableDecl
    {
        Id   = varId,
        Name = "WasFired",
        Type = new BlueprintTypeRef { TypeId = "bool" },
        DefaultValueJson = "false",
    };

    // ---- Nodes ----
    var entry = new EventEntryNode { Id = Guid.NewGuid() };
    var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    entry.Pins.Add(entryExecOut);

    var whenId   = Guid.NewGuid();
    var whenNode = new WhenNode
    {
        Id    = whenId,
        Mode  = WhenMode.EventFired,
        Edges = WhenEdge.RisingEdge,
        EventFired = new EventFiredPayload
        {
            EventTypeId     = eventTypeId,
            TargetFilter    = targetFilter,
            TargetFieldName = targetFieldName,
            PayloadCheck    = payloadCheck,
        },
    };
    var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
    var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
    var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
    whenNode.Pins.Add(whenExecIn);
    whenNode.Pins.Add(whenExecOut);
    whenNode.Pins.Add(whenOnFired);

    var litId     = Guid.NewGuid();
    var litOutPin = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false,
                               TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
    var litNode = new LiteralNode { Id = litId, TypeId = "bool", ValueJson = "true" };
    litNode.Pins.Add(litOutPin);

    var setId      = Guid.NewGuid();
    var setExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
    var setExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
    var setDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
    var setNode = new SetVariableNode { Id = setId, VariableId = varId.ToString() };
    setNode.Pins.Add(setExecIn);
    setNode.Pins.Add(setExecOut);
    setNode.Pins.Add(setDataIn);

    // ---- Graph ----
    var graph = new Graph
    {
        Id = graphId, Name = "Tick", Kind = GraphKind.Event,
        Nodes = { entry, whenNode, litNode, setNode },
        Links =
        {
            // Exec: entry → when
            new Link { FromNodeId = entry.Id, FromPinId = entryExecOut.Id,
                       ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
            // Exec: when.OnFired → setVar
            new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,
                       ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
            // Data: literal.Value → setVar.Value
            new Link { FromNodeId = litNode.Id, FromPinId = litOutPin.Id,
                       ToNodeId = setNode.Id, ToPinId = setDataIn.Id },
        },
    };

    return new BlueprintAsset
    {
        AssetId  = assetId,
        Name     = "WhenEF",
        Dispatch = AssetDispatchKind.Instance,
        Variables = { wasFiredVar },
        Graphs   = { graph },
    };
}
```

---

#### Tests — ValueChanged

**Test 1**: `ValueChanged_RisingEdge_Fires_WhenComponentValueChanges`

Verifies: after one TickFrame with X changed from 0 to 50, `_when_{id8}_prev` = 50f
(StorePrev ran, meaning OnFired was taken).

```
Setup:
  fixture = new BlueprintTestFixture()
  (asset, synthName) = BuildValueChangedAsset(WhenEdge.RisingEdge)
  fixture.CompileAndLoad(asset)
  entity = fixture.CreateEntity()
  fixture.World.RegisterComponent<AnotherTestComponent>(entity)  // if not already added
  fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f })
  fixture.AttachBlueprint(asset, entity)
  fixture.TickFrame(0.016f)

Assert:
  float prev = ReadSlotField<float>(fixture, asset, entity, synthName)
  Assert.Equal(50f, prev)
```

> Note: `RegisterComponent<T>` call before `AddComponent<T>` may or may not be needed
> depending on whether `AnotherTestComponent` (ComponentId=254) is already registered in
> `MockTestComponents.Register`. Verify by looking at existing test setup patterns.
> If already registered globally by the fixture constructor, only `AddComponent` is needed.

**Test 2**: `ValueChanged_NoFire_WhenValueUnchanged`

Verifies: second tick with same value does not update prev.

```
Setup:
  Build/attach same as Test 1
  fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f })
  fixture.AttachBlueprint(asset, entity)
  fixture.TickFrame(0.016f)  // Tick 1: X=50, prev fires → prev becomes 50

  // Do NOT change X; tick again
  fixture.TickFrame(0.016f)  // Tick 2: X still 50, no change → StorePrev does NOT run again

Assert:
  float prev = ReadSlotField<float>(fixture, asset, entity, synthName)
  Assert.Equal(50f, prev)   // still 50, not 0 and not updated to 50 a second time
                              // (semantically: StorePrev ran exactly once, on first tick)
```

**Test 3**: `ValueChanged_WorkingState_PrevPersists_AcrossMultipleTicks`

Verifies: multi-tick sequence: value changes (fires), stays same (no fire), changes again (fires).

```
Tick 1: X=50 → prev=50
Tick 2: X=50 → prev stays 50 (no change detected)
Tick 3: X=75 → prev=75 (changed again, OnFired fires, StorePrev updates)

Assert: prev == 75f after Tick 3
```

**Test 4**: `ValueChanged_BothEdge_FiredBlock_Fires_WhenChanged`

Verifies: `WhenEdge.RisingEdge | WhenEdge.FallingEdge` — in M2, `hasFired=true`, so
the OnFired block is allocated and StorePrev is registered. Change value → prev updates.

```
(asset, synthName) = BuildValueChangedAsset(WhenEdge.RisingEdge | WhenEdge.FallingEdge)
// Same tick setup as Test 1
Assert: prev == 50f
```

**Test 5**: `ValueChanged_FallingEdge_Only_NoCrash_NoPrevInM2`

Verifies: `WhenEdge.FallingEdge` only — in M2, `hasFired=false`:
- No StorePrev is registered
- `_when_{id8}_prev` synthesized field does NOT exist in the State struct
- Blueprint compiles, attaches, and ticks without exception

```
(asset, synthName) = BuildValueChangedAsset(WhenEdge.FallingEdge)
fixture.CompileAndLoad(asset)
entity = ...
fixture.AttachBlueprint(asset, entity)
// Should not throw:
fixture.TickFrame(0.016f)

// Verify: the synthesized field does NOT exist in the State struct
var hash = BlueprintIdHash.Compute(asset.AssetId)
Assert.True(fixture.Registry.TryGetById(hash, out var def))
var stateType = def!.StateClrType!
var field = stateType.GetField(synthName, BindingFlags.Public | BindingFlags.Instance)
Assert.Null(field)   // FallingEdge only → no synthesized field
```

> Note: `BindingFlags` import = `System.Reflection.BindingFlags`

**Test 6**: `ValueChanged_PeerVariable_CompilesAndTicks_NoCrash`

Verifies: `ValueChangedSource.PeerBlueprintVariable` compiles and ticks without crashing in M2.
(Peer variable slot lookup is deferred to M4; M2 emits code that runs without error.)

```csharp
(asset, _) = BuildValueChangedAsset(
    edges: WhenEdge.RisingEdge,
    source: ValueChangedSource.PeerBlueprintVariable);
// asset.ValueChanged.PeerBlueprintAssetId is not set — this is OK for M2,
// the emitter falls through gracefully

// Just verify: CompileAndLoad succeeds and TickFrame doesn't throw
fixture.CompileAndLoad(asset);
entity = ...;
fixture.AttachBlueprint(asset, entity);
Assert.Null(Record.Exception(() => fixture.TickFrame(0.016f)));
```

---

#### Tests — EventFired

All EventFired tests use `BuildEventFiredAsset(...)` and `fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog())`.
After ticking, read `WasFired` (bool) from the slot via `ReadSlotField<bool>(..., "WasFired")`.

**Event publishing pattern** (publish BEFORE TickFrame — SwapBuffers at the start of TickFrame
makes the events visible to blueprint code):

```csharp
fixture.World.Bus.Publish(new WhenTestHitEvent { Target = entity, Damage = 75f });
fixture.TickFrame(0.016f);
bool fired = ReadSlotField<bool>(fixture, asset, entity, "WasFired");
```

**Test 7**: `EventFired_NoFilters_FastPath_Fires_OnAnyEvent`

```csharp
var asset = BuildEventFiredAsset(
    targetFilter: EventTargetFilter.None);
fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
var entity = fixture.CreateEntity();
fixture.AttachBlueprint(asset, entity);

fixture.World.Bus.Publish(new WhenTestHitEvent { Damage = 10f });
fixture.TickFrame(0.016f);

Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
```

**Test 8**: `EventFired_WithSelfFilter_Fires_WhenTargetMatchesSelf`

```csharp
var asset = BuildEventFiredAsset(
    targetFilter: EventTargetFilter.Self,
    targetFieldName: "Target");
fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
var entity = fixture.CreateEntity();
fixture.AttachBlueprint(asset, entity);

fixture.World.Bus.Publish(new WhenTestHitEvent { Target = entity, Damage = 10f });
fixture.TickFrame(0.016f);

Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
```

**Test 9**: `EventFired_WithSelfFilter_DoesNotFire_WhenTargetDiffers`

```csharp
var asset = BuildEventFiredAsset(
    targetFilter: EventTargetFilter.Self,
    targetFieldName: "Target");
fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
var entity = fixture.CreateEntity();
var otherEntity = fixture.CreateEntity();
fixture.AttachBlueprint(asset, entity);

fixture.World.Bus.Publish(new WhenTestHitEvent { Target = otherEntity, Damage = 10f });
fixture.TickFrame(0.016f);

Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
```

**Test 10**: `EventFired_WithPayloadCondition_Fires_WhenConditionMet`

```csharp
var asset = BuildEventFiredAsset(
    targetFilter: EventTargetFilter.None,
    payloadCheck: new PayloadCondition
    {
        PropertyPath    = "Damage",
        Operator        = ComparisonOperator.GreaterThan,
        TargetValueText = "50f",
    });
fixture.CompileAndLoad(asset, OptionsWithEmptyEventCatalog());
var entity = fixture.CreateEntity();
fixture.AttachBlueprint(asset, entity);

fixture.World.Bus.Publish(new WhenTestHitEvent { Damage = 75f });
fixture.TickFrame(0.016f);

Assert.True(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
```

**Test 11**: `EventFired_WithPayloadCondition_DoesNotFire_WhenConditionNotMet`

```csharp
// Same setup as Test 10 but Damage = 25f (not > 50)
fixture.World.Bus.Publish(new WhenTestHitEvent { Damage = 25f });
fixture.TickFrame(0.016f);

Assert.False(ReadSlotField<bool>(fixture, asset, entity, "WasFired"));
```

---

## Key Constraints and Notes

### M2 Limitations
- **FallingEdge only** (`hasFired=false`): no `onFiredBlock` allocated, no `StorePrev`
  registered, no `_when_{id8}_prev` synthesized field — both branches go to `outBlock`.
  Test 5 verifies this via reflection.

- **BothEdge** (`RisingEdge | FallingEdge`): `hasFired=true`, `hasEnded=true`.
  `onFiredBlock` IS allocated, `StorePrev` IS registered. `onEndedBlock` is allocated
  but no branch reaches it in M2 — the branch terminator only wires
  `true→onFiredBlock` and `false→outBlock`. OnEnded is wired in M3.

- **PeerVariable**: emitter uses same code path as SelfComponent in M2; the peer
  slot lookup is deferred to M4. The compiled code will tick without crashing.

### AnotherTestComponent
- ComponentId = 254, fields `float X, float Y`
- Namespace: `Hrot.Blueprints.Tests.Mocks`
- Full TypeId: `"Hrot.Blueprints.Tests.Mocks.AnotherTestComponent"`
- Pre-registered via `MockTestComponents.Register` in the fixture constructor.
  Do NOT call `RegisterComponent<AnotherTestComponent>()` manually — just use
  `fixture.World.AddComponent(entity, new AnotherTestComponent { X = 50f })`.

### WhenTestHitEvent
- `[EventId(90999)]`, `[StructLayout(LayoutKind.Sequential)]`
- Fields: `Entity Target`, `float Damage`
- EventTypeId for blueprint asset: `"Hrot.Blueprints.Tests.Runtime.WhenTestHitEvent"`
- Live in the test assembly → accessible to Roslyn runtime compilation via
  `MetadataReferenceResolver.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies())`
- `EnforceExplicitEventRegistration` defaults to `false` → `Bus.Publish` works without pre-registration

### Reading State Fields
- `TryGetField<T>(name)` does NOT work for `CompileAndLoad` output (StateFields not
  populated by generated registrar).
- Use `Marshal.OffsetOf(def.StateClrType, fieldName)` + `state.AsSpan().Slice(offset, Unsafe.SizeOf<T>())`
  + `MemoryMarshal.Read<T>(slice)` instead. See `ReadSlotField<T>` helper above.

### Event Timing
- Publish event → call `TickFrame` → SwapBuffers at start of TickFrame makes event visible
  → blueprint Tick reads it. One Publish per frame is sufficient.

### Required usings for the new test file
```csharp
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Staging;    // CompileOptions, if in this namespace
using Hrot.Blueprints.Tests.Mocks;
using Xunit;
```
> Check the actual namespace of `CompileOptions` and `IEngineEventCatalog` in the codebase.

---

## Files to Modify / Create

| File | Action |
|------|--------|
| `Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs` | Add `TargetFieldName` param to `IrOp_WhenEventFiredCheck` |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` | Pass `ef.TargetFieldName ?? "Target"` to `IrOp_WhenEventFiredCheck` |
| `Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs` | Fix fast path + scan path as described |
| `Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/WhenNodeLoweringTests.cs` | Update test 7 assertion |
| `Hrot.Blueprints.Tests/BlueprintTestFixture.cs` | Add `CompileAndLoad(asset, CompileOptions)` overload |
| `Hrot.Blueprints.Tests/Runtime/WhenNodeRuntimeTests.cs` | **CREATE** with 11 tests |

---

## Success Criteria

1. `dotnet build` succeeds with 0 errors on `Hrot.Blueprints.Tests`
2. All pre-existing 8 lowering tests still pass
3. All 11 new runtime tests pass
4. Test 5 (`FallingEdge_Only_NoCrash`) verifies via reflection that no synthesized field exists
5. Tests 7–11 (EventFired) use `WhenTestHitEvent` and `EmptyEventCatalog`

## Batch Report

Provide a batch report with:
- Summary of all files changed and what changed
- Any deviations from these instructions (explain why)
- Whether all 11+8 tests pass
- Any test that needed to be skipped or modified from the spec (explain why)
