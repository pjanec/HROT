# WHEN-BATCH-01 — Schema Foundation + EqsSensorHandle Registration

## Tasks Covered

- **WHEN-M0-T1** — Confirm EQS-side schema deliverables are scheduled (confirmation paragraph only, no code)
- **WHEN-M1-T1** — `EqsSensorHandle` consumed: add to type registry + write test
- **WHEN-M1-T2** — Schema classes for `WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode`

**Design reference (authoritative):** [When_Reactivity_Iteration_Design_v2_2.md](../When_Reactivity_Iteration_Design_v2_2.md)  
**Task detail (authoritative):** [TASK-DETAIL.md](../TASK-DETAIL.md)

---

## Context

The EQS-2 iteration is complete (BATCH-16 committed). All engine dependencies are confirmed:
- `EqsCognitiveBuffer.LastUpdateTimeSeconds` (float) field exists — added in EQS-033
- `EqsSensorHandle` wrapper struct exists — added in EQS-037 at `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs`, namespace `FDP.Eqs`
- `view.IsAlive(Entity)` exists and is the correct liveness API (confirmed)
- `EqsResult.EntityId` (long) + `PositionX`/`PositionY` (float) are the correct field names
- `IEntityCommandBuffer.CreateEntity()` + `AddComponent<T>(Entity, T)` exist (used in EqsLifecycleNodes.cs)

---

## M0-T1: Confirmation (batch report only)

Write one paragraph in the batch report confirming the five API points listed in TASK-DETAIL WHEN-M0-T1 hold against the current codebase. No code changes required; this is a verification task that is satisfied by the EQS-2 work already merged.

---

## M1-T1: EqsSensorHandle in the Type Registry

### 1. Add to `StaticTypeRegistry`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs`

Add one entry to `TypeTable` in the "FDP entity handle" section:

```csharp
// EQS sensor handle — wraps Entity (8 bytes), unmanaged value type
["FDP.Eqs.EqsSensorHandle"] = new IrTypeRef
{
    FullName    = "FDP.Eqs.EqsSensorHandle",
    IsUnmanaged = true,
    SizeBytes   = 8,  // same layout as Entity (single Entity field, Pack=4)
},
```

This allows `VariableDecl.Type.TypeId = "FDP.Eqs.EqsSensorHandle"` to resolve in Stage 4 (TypeResolve) and be laid out in the State struct.

### 2. Write test

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/SchemaReflectionTests.cs` (or a new `TypeRegistryTests.cs` if cleaner)

```csharp
[Fact]
public void EqsSensorHandle_IsPermittedVariableType()
{
    var typeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" };
    bool resolved = StaticTypeRegistry.Instance.TryResolve(typeRef, out var irType);

    Assert.True(resolved);
    Assert.Equal("FDP.Eqs.EqsSensorHandle", irType.FullName);
    Assert.True(irType.IsUnmanaged);
    Assert.Equal(8, irType.SizeBytes);
}
```

---

## M1-T2: Schema Classes

### Overview

Add three new concrete `Node` subclasses plus their supporting types (enums, payload classes) to the Blueprints compiler. Update the `Node` base class discriminator list. Update `SchemaReflectionTests`. Write round-trip tests.

### Step 1: Add supporting types + node classes to `Nodes.cs`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`

Append the following at the end of the file (after the existing `WaitForEventNode` class). The `SearchPredicateDto` type already exists at `Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto` — add the using directive.

```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
```

Add at the end of Nodes.cs:

```csharp
// ──────────────────────────────────────────────────────────────────────────
// WhenNode and supporting types (DESIGN §2.3)
// ──────────────────────────────────────────────────────────────────────────

public sealed class WhenNode : Node
{
    public WhenMode Mode { get; set; }
    public WhenEdge Edges { get; set; } = WhenEdge.RisingEdge;

    public ValueChangedPayload? ValueChanged { get; set; }
    public EventFiredPayload? EventFired { get; set; }
    public ConditionMetPayload? ConditionMet { get; set; }
    public EqsResultPayload? EqsResult { get; set; }
}

public enum WhenMode { ValueChanged, EventFired, ConditionMet, EqsResult }

[Flags]
public enum WhenEdge { None = 0, RisingEdge = 1, FallingEdge = 2 }

public sealed class ValueChangedPayload
{
    public string ComponentTypeId { get; set; } = "";
    public string PropertyPath { get; set; } = "";
    public double Epsilon { get; set; }
    public ValueChangedSource Source { get; set; }
    public Guid? PeerBlueprintAssetId { get; set; }
    public string? PeerVariableName { get; set; }
    public string? WorkingStateFieldId { get; set; }
}

public enum ValueChangedSource { SelfComponent, PeerBlueprintVariable, WorkingStateField }

public sealed class EventFiredPayload
{
    public string EventTypeId { get; set; } = "";
    public EventTargetFilter TargetFilter { get; set; } = EventTargetFilter.Self;
    public string? TargetFieldName { get; set; }
    public PayloadCondition? PayloadCheck { get; set; }
}

public enum EventTargetFilter { None, Self }

public sealed class PayloadCondition
{
    public string PropertyPath { get; set; } = "";
    public ComparisonOperator Operator { get; set; }
    public string TargetValueText { get; set; } = "";
}

/// <summary>Comparison operator for payload condition checks in WhenNode.EventFired mode.</summary>
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

public sealed class ConditionMetPayload
{
    public SearchPredicateDto? Condition { get; set; }
}

public sealed class EqsResultPayload
{
    public string SensorVariableName { get; set; } = "";
    public EqsTrigger Trigger { get; set; }
    public float ScoreThreshold { get; set; }
    public float MaxAgeSeconds { get; set; }
}

public enum EqsTrigger { FirstReady, TopChanged, ScoreCrossed, BecomesStale }

// ──────────────────────────────────────────────────────────────────────────
// ReadEqsResultNode (DESIGN §2.4)
// ──────────────────────────────────────────────────────────────────────────

public sealed class ReadEqsResultNode : Node
{
    public string SensorVariableName { get; set; } = "";
}

// ──────────────────────────────────────────────────────────────────────────
// SpawnEqsSensorNode (DESIGN §2.5)
// ──────────────────────────────────────────────────────────────────────────

public sealed class SpawnEqsSensorNode : Node
{
    /// <summary>
    /// The chosen EQS template's stable identifier (the AssetId from the template's
    /// [EqsTemplate(AssetId = "...")] declaration). At lowering time this resolves
    /// to the BlueprintId stored in the spawned EqsSensor component.
    /// </summary>
    public Guid TemplateAssetId { get; set; }
}
```

### Step 2: Update the `Node` base class discriminator list

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`

Add three new `[JsonDerivedType]` attributes to the `Node` class (after the existing `WaitForEventNode` entry):

```csharp
[JsonDerivedType(typeof(WhenNode),           "When")]           // NEW
[JsonDerivedType(typeof(ReadEqsResultNode),  "ReadEqsResult")]  // NEW
[JsonDerivedType(typeof(SpawnEqsSensorNode), "SpawnEqsSensor")] // NEW
```

The discriminator strings `"When"`, `"ReadEqsResult"`, `"SpawnEqsSensor"` match DESIGN §2.2 exactly and are part of the asset persistence boundary — do not change them.

### Step 3: Update `SchemaReflectionTests`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/SchemaReflectionTests.cs`

1. Update the count test from `Assert.Equal(19, count)` to `Assert.Equal(22, count)`.
2. Add three `[InlineData]` entries to the `DiscriminatorRoundTrip_EachNodeKind` theory:
   ```csharp
   [InlineData(typeof(WhenNode),           "When")]
   [InlineData(typeof(ReadEqsResultNode),  "ReadEqsResult")]
   [InlineData(typeof(SpawnEqsSensorNode), "SpawnEqsSensor")]
   ```

### Step 4: Write round-trip tests

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AssetJsonRoundTripTests.cs`

Add three new round-trip tests. Each test creates an asset containing one instance of the new node type (with all non-default fields populated so the round-trip exercises the payload classes), serializes, deserializes, re-serializes, and asserts the two JSON strings are equal.

**Test 1: `WhenNode_AllModes_RoundTrip`**

Create four separate `WhenNode` instances (one per mode), each with its payload populated:
- Mode `ValueChanged`: `ValueChanged = new ValueChangedPayload { ComponentTypeId = "Hrot.Components.Health", PropertyPath = "Current", Epsilon = 0.01, Source = ValueChangedSource.SelfComponent }`
- Mode `EventFired`: `EventFired = new EventFiredPayload { EventTypeId = "Hrot.Events.HitEvent", TargetFilter = EventTargetFilter.Self, TargetFieldName = "Target", PayloadCheck = new PayloadCondition { PropertyPath = "Damage", Operator = ComparisonOperator.GreaterThan, TargetValueText = "50" } }`
- Mode `ConditionMet`: `ConditionMet = new ConditionMetPayload { Condition = null }` (null is valid — the validator checks it later)
- Mode `EqsResult`: `EqsResult = new EqsResultPayload { SensorVariableName = "CoverQuery", Trigger = EqsTrigger.TopChanged, ScoreThreshold = 0.7f, MaxAgeSeconds = 2.0f }`, `Edges = WhenEdge.RisingEdge | WhenEdge.FallingEdge`

Put all four nodes in one graph. Verify discriminator `"When"` survives round-trip.

**Test 2: `ReadEqsResultNode_RoundTrip`**

```csharp
new ReadEqsResultNode
{
    Id = new Guid("d1000000-0001-0001-0001-000000000001"),
    SensorVariableName = "CoverQuery",
}
```
Verify discriminator `"ReadEqsResult"` survives round-trip.

**Test 3: `SpawnEqsSensorNode_RoundTrip`**

```csharp
new SpawnEqsSensorNode
{
    Id = new Guid("d1000000-0002-0001-0001-000000000001"),
    TemplateAssetId = new Guid("00000000-cccc-0001-0000-000000000001"),
}
```
Verify discriminator `"SpawnEqsSensor"` survives round-trip.

---

## Constraints

1. **Do NOT add validators, IR types, lowering code, or drawer code** — those are in later batches.
2. **Do NOT add compiler stages changes** — this batch is schema-only.
3. **Field names, casing, and JSON discriminator strings in `Nodes.cs` must exactly match DESIGN §2.2–§2.5.** These cross the asset persistence boundary.
4. **`WhenNode` mode-radio + per-mode payload class shape is non-negotiable** (DESIGN §1.5 rationale). Do not collapse per-mode payload classes into a single bag.
5. `ComparisonOperator` is a new enum defined alongside the Blueprint schema types; it is NOT the same as any existing `SearchOperator` enum.
6. The `SearchPredicateDto` using directive added to `Nodes.cs` references `Fdp.Toolkit.ReplayBrowser.Search` which is the existing package. Confirm the `Hrot.Blueprints.Compiler.csproj` already references the assembly that provides this type (look for an existing project reference to `Fdp.Toolkits`); if not, add one.

---

## Success Criteria

1. ✅ `StaticTypeRegistry` resolves `"FDP.Eqs.EqsSensorHandle"` to an unmanaged 8-byte type
2. ✅ Test `EqsSensorHandle_IsPermittedVariableType` passes
3. ✅ `SchemaReflectionTests.ConcreteNodeSubtypeCount_Is19` updated to 22 and passes
4. ✅ `SchemaReflectionTests.DiscriminatorRoundTrip_EachNodeKind` includes `"When"`, `"ReadEqsResult"`, `"SpawnEqsSensor"` and all pass
5. ✅ Three new round-trip tests pass
6. ✅ Solution builds (all projects)

---

## Files to Create or Modify

| File | Change |
|---|---|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs` | Add 3 new node classes + enums + payloads; add 3 `[JsonDerivedType]` to `Node` base; add `using Fdp.Toolkit.ReplayBrowser.Search;` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs` | Add `"FDP.Eqs.EqsSensorHandle"` entry |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/SchemaReflectionTests.cs` | Update count 19→22; add 3 InlineData entries |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AssetJsonRoundTripTests.cs` | Add 3 new round-trip tests |

---

## Run Tests

After implementing, run:

```
dotnet test "Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests" -c Debug --no-build
```

All pre-existing tests must still pass. New tests must pass.

---

## Batch Report

Write a file `.dev/blueprints-3-when-node/batches/WHEN-BATCH-01-REPORT.md` containing:

1. A confirmation paragraph for M0-T1 verifying all five API points hold against the current codebase
2. Summary of files changed
3. Test results (before/after counts)
4. Any deviations from the instructions with justification
