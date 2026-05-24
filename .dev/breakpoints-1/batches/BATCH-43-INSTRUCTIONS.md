# BATCH-43 Instructions — UBP-P6T1 + UBP-P6T2

**Phase:** P6 — Blueprint variable integration  
**Tasks:** UBP-P6T1 (BlueprintVariablePredicateDto + JSON registration) + UBP-P6T2 (Slot-table-aware IL emission)  
**Design references:** [§6.5 DESIGN.md](../DESIGN.md#65-blueprint-variable-breakpoints), [TASK-DETAIL.md P6T1](../TASK-DETAIL.md#ubp-p6t1--blueprintvariablepredicatedto--json-registration), [TASK-DETAIL.md P6T2](../TASK-DETAIL.md#ubp-p6t2--slot-table-aware-il-emission)

---

## Context

All Blueprint blackboard types (`BlueprintBlackboard1024/4096/16384`) and supporting helpers
(`BlueprintBlackboardPartitions`, `BlueprintIdHash`, `BlueprintRegistry`, `BlueprintDefinition`,
`BlueprintFieldDescriptor`) live inside `Fdp.Toolkits` (`Fdp.Toolkit.Blueprints.*` namespaces),
the same assembly that contains `PredicateCompiler`. No new project references are required.

The `Hrot.Diagnostics.Breakpoints.Tests` project has transitive access to `Fdp.Toolkits` via
`Hrot.Blueprints.Editor` → `Fdp.Toolkits` and via `Hrot.Diagnostics.Breakpoints` → `Fdp.Toolkits`.

`BlueprintBlackboard16384` must NOT be registered in test repositories (it requires ~16 GB of
virtual-address space). `ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard16384))` returns -1
when the type is not registered, and `repo.HasComponentByTypeId(entity, -1)` safely returns false.

---

## Files to modify

### 1. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`

**P6T1.** Add `[JsonDerivedType(typeof(BlueprintVariablePredicateDto), "BlueprintVariable")]` to the
`[JsonPolymorphic]` attribute list on `SearchPredicateDto`, after the existing
`[JsonDerivedType(typeof(TraceBufferScanPredicateDto), "TraceBufferScan")]` entry.

Then add the class after the `TraceBufferScanPredicateDto` class (before the Result types region):

```csharp
// ──────────────────────────────────────────────────────────────────────────
// Blueprint variable breakpoints
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Predicate that navigates the multi-tier BlueprintBlackboard partition
/// allocator, finds the slot for <see cref="TargetBlueprintAssetId"/>,
/// reads <see cref="VariableName"/> at the baked field offset, and evaluates
/// <see cref="Predicate"/> against the value.
/// The delegate re-runs the slot scan on every evaluation, so tier upgrades
/// never invalidate a compiled delegate (see DESIGN §6.5).
/// </summary>
public sealed class BlueprintVariablePredicateDto : SearchPredicateDto
{
    /// <summary>
    /// Asset GUID of the target Blueprint.
    /// Converted to a 32-bit int at compile time via
    /// <c>BlueprintIdHash.Compute(TargetBlueprintAssetId)</c>.
    /// </summary>
    public Guid TargetBlueprintAssetId { get; set; }

    /// <summary>
    /// Variable name as declared in <c>BlueprintDefinition.StateFields</c>.
    /// Resolved to a byte offset at compile time.
    /// </summary>
    public string VariableName { get; set; } = string.Empty;

    public SearchOperator Operator { get; set; } = SearchOperator.Equals;

    /// <summary>
    /// Value sub-predicate: <see cref="NumericPredicateDto"/> or
    /// <see cref="StringPredicateDto"/> (same as <see cref="PropertyMatchDto.Predicate"/>).
    /// </summary>
    public SearchPredicateDto Predicate { get; set; } = null!;
}
```

---

### 2. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`

**P6T2.** Make the following changes:

#### 2a. Add usings (at the top with the existing usings)

```csharp
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
```

#### 2b. Add private field

Add after the existing `_behaviorRegistry` field:

```csharp
private readonly BlueprintRegistry? _blueprintRegistry;
```

#### 2c. Update constructor signature

Change the constructor from:
```csharp
public PredicateCompiler(IComponentEditService editService, BehaviorRegistry? behaviorRegistry = null)
```
to:
```csharp
public PredicateCompiler(IComponentEditService editService, BehaviorRegistry? behaviorRegistry = null, BlueprintRegistry? blueprintRegistry = null)
```

Add assignment in constructor body:
```csharp
_blueprintRegistry = blueprintRegistry;
```

#### 2d. Add switch case in `Compile`

In the `Compile` switch, add before the comment `// Specialized loop predicates: pass-through`:

```csharp
case BlueprintVariablePredicateDto blueprintVar:
    return CompileBlueprintVariablePredicate(blueprintVar);
```

#### 2e. Add `CompileBlueprintVariablePredicate` method

Add after `CompileTraceBufferScan`:

```csharp
private Func<EntityRepository, Entity, bool> CompileBlueprintVariablePredicate(BlueprintVariablePredicateDto dto)
{
    if (string.IsNullOrEmpty(dto.VariableName) || _blueprintRegistry == null)
        return static (_, _) => false;

    int blueprintId = BlueprintIdHash.Compute(dto.TargetBlueprintAssetId);

    if (!_blueprintRegistry.TryGetById(blueprintId, out var def) || def == null)
        return static (_, _) => false;

    if (!def.StateFields.TryGetValue(dto.VariableName, out var fieldDesc) || fieldDesc == null)
        return static (_, _) => false;

    var method = typeof(PredicateCompiler)
        .GetMethod(nameof(BuildBlueprintVariableMatcher), BindingFlags.NonPublic | BindingFlags.Static)!;
    return (Func<EntityRepository, Entity, bool>)method
        .MakeGenericMethod(fieldDesc.ClrType)
        .Invoke(null, new object[] { blueprintId, fieldDesc.OffsetBytes, dto })!;
}
```

#### 2f. Add `BuildBlueprintVariableMatcher<TField>` static helper

Add after `BuildTraceBufferScanMatcher`:

```csharp
private static unsafe Func<EntityRepository, Entity, bool> BuildBlueprintVariableMatcher<TField>(
    int blueprintId,
    int fieldOffset,
    BlueprintVariablePredicateDto dto)
    where TField : unmanaged
{
    // Build a compiled comparison expression for the field type.
    var param = Expression.Parameter(typeof(TField).MakeByRefType(), "field");
    Expression condition = BuildConditionExpression(param, dto.Operator, dto.Predicate);
    var matcher = Expression.Lambda<ComponentMatcherDelegate<TField>>(condition, param).Compile();

    // Bake tier component type IDs at compile time.
    // GetId returns -1 for unregistered types (BB16384 in test repos) → HasComponentByTypeId returns false.
    int typeId1024  = ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard1024));
    int typeId4096  = ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard4096));
    int typeId16384 = ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard16384));

    return (repo, entity) =>
    {
        unsafe
        {
            byte* memory = null;

            if (repo.HasComponentByTypeId(entity, typeId1024))
            {
                ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard1024>(entity);
                memory = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
            }
            else if (repo.HasComponentByTypeId(entity, typeId4096))
            {
                ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard4096>(entity);
                memory = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
            }
            else if (repo.HasComponentByTypeId(entity, typeId16384))
            {
                ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard16384>(entity);
                memory = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
            }

            if (memory == null) return false;

            if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
                return false;

            ref TField fieldRef = ref Unsafe.AsRef<TField>(memory + payloadOffset + fieldOffset);
            return matcher(ref fieldRef);
        }
    };
}
```

#### 2g. `CollectMandatoryComponents` — no addition needed

`BlueprintVariablePredicateDto` can reside in any of the three tiers. Adding a single mandatory
component would incorrectly filter entities that use a different tier. Leave the mandatory set
empty for this predicate type (the existing fall-through `else { }` already handles unknown types).

---

### 3. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

**P6T2.** In `TryMountDelegate`, add `case BlueprintVariablePredicateDto _:` to the component-
predicate switch block. The file already has the required `using` for SearchPredicateDto types.

Find this block:
```csharp
case PropertyMatchDto _:
case CompoundPredicateDto _:
case BehaviorParamPredicateDto _:
case TraceBufferScanPredicateDto _:
```

Change to:
```csharp
case PropertyMatchDto _:
case CompoundPredicateDto _:
case BehaviorParamPredicateDto _:
case TraceBufferScanPredicateDto _:
case BlueprintVariablePredicateDto _:
```

---

### 4. `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/SearchPredicateDtoSerializationTests.cs`

**P6T1.** Add this test to the `SearchPredicateDtoSerializationTests` class:

```csharp
/// <summary>
/// P6T1 success condition: BlueprintVariablePredicateDto survives a JSON round-trip
/// preserving all fields including the nested NumericPredicateDto.
/// </summary>
[Fact]
public void BlueprintVariablePredicate_SerializesRoundTrip()
{
    var assetId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    var dto = new BlueprintVariablePredicateDto
    {
        TargetBlueprintAssetId = assetId,
        VariableName           = "AmmoCount",
        Operator               = SearchOperator.Equals,
        Predicate              = new NumericPredicateDto { MinValue = 0.0, MaxValue = 0.0 },
    };

    string json = JsonSerializer.Serialize<SearchPredicateDto>(dto, _options);
    var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as BlueprintVariablePredicateDto;

    Assert.NotNull(back);
    Assert.Equal(assetId, back.TargetBlueprintAssetId);
    Assert.Equal("AmmoCount", back.VariableName);
    Assert.Equal(SearchOperator.Equals, back.Operator);
    var numPred = Assert.IsType<NumericPredicateDto>(back.Predicate);
    Assert.Equal(0.0, numPred.MinValue);
    Assert.Equal(0.0, numPred.MaxValue);
}
```

The `_options` field is already defined in the class as:
```csharp
private static readonly JsonSerializerOptions _options = new()
{
    WriteIndented = false,
    IncludeFields  = true
};
```

---

### 5. New file: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintVariableTests.cs`

**P6T2 tests.** Create this file:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// =============================================================================
// UBP-P6T2: Slot-table-aware IL emission for BlueprintVariablePredicateDto
// =============================================================================

/// <summary>
/// Unit tests for the <see cref="BlueprintVariablePredicateDto"/> compilation path
/// in <see cref="PredicateCompiler"/>.
///
/// Test blueprint: one Instance-dispatch blueprint "TestBP" with a single int field
/// "AmmoCount" at payload offset 0, stateSize=4. The asset GUID drives the
/// BlueprintId via BlueprintIdHash.Compute.
///
/// BB16384 is NOT registered (it would require ~16 GB of virtual-address space in
/// test repos). The compiler bakes typeId16384=-1, so HasComponentByTypeId(entity,-1)
/// returns false safely.
/// </summary>
[Collection("ComponentRegistry")]
public sealed unsafe class BlueprintVariableCompilerTests
{
    private static readonly Guid   s_assetGuid  = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly int    s_blueprintId = BlueprintIdHash.Compute(s_assetGuid);
    private const            string s_fieldName  = "AmmoCount";

    // -------------------------------------------------------------------------
    // Shared setup
    // -------------------------------------------------------------------------

    private static (EntityRepository repo, PredicateCompiler compiler) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo = new EntityRepository();
        repo.RegisterComponent<BlueprintBlackboard1024>();
        repo.RegisterComponent<BlueprintBlackboard4096>();
        // BB16384 intentionally omitted -- see class summary.

        var registry = new BlueprintRegistry();
        var def = new BlueprintDefinition
        {
            Name          = "TestBP",
            Kind          = BlueprintDispatchKind.Instance,
            StructureHash = 0,
            StateSize     = sizeof(int),
            AssetId       = s_assetGuid,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                [s_fieldName] = new BlueprintFieldDescriptor(
                    Name:           s_fieldName,
                    ClrType:        typeof(int),
                    OffsetBytes:    0,
                    SizeBytes:      sizeof(int),
                    CategoryOrEmpty: ""),
            },
        };
        registry.RegisterInstance(s_blueprintId, def);

        var compiler = new PredicateCompiler(
            new ComponentEditServiceBuilder().Build(),
            blueprintRegistry: registry);
        return (repo, compiler);
    }

    // -------------------------------------------------------------------------
    // Helper: attach the test blueprint to a BB1024 component on an entity.
    // Returns the payloadOffset written by TryAttach.
    // -------------------------------------------------------------------------
    private static int AttachToBB1024(EntityRepository repo, Entity entity)
    {
        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem = (byte*)Unsafe.AsPointer(ref bb);
        BlueprintBlackboardPartitions.Initialize(
            mem,
            BlueprintBlackboard1024.TotalSize,
            (byte)BlueprintBlackboard1024.MaxSlots);

        bool ok = BlueprintBlackboardPartitions.TryAttach(
            mem,
            s_blueprintId,
            requestedSize: sizeof(int),
            structureHash: 0,
            out int payloadOffset);
        Assert.True(ok, "TryAttach must succeed for a freshly initialised BB1024");
        return payloadOffset;
    }

    // =========================================================================
    // P6T2-SC1: No slot present → false
    // =========================================================================

    /// <summary>
    /// Entity has a BB1024 component that is fully initialised (valid header)
    /// but no blueprint slot has been attached. The predicate must return false
    /// rather than reading uninitialised memory.
    /// </summary>
    [Fact]
    public void Compile_BlueprintVariable_NoSlotPresent_ReturnsFalse()
    {
        var (repo, compiler) = Setup();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

        // Initialise the header so TryGetSlotOffset scans a valid structure,
        // but do not call TryAttach — SlotCount remains 0.
        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem = (byte*)Unsafe.AsPointer(ref bb);
        BlueprintBlackboardPartitions.Initialize(
            mem,
            BlueprintBlackboard1024.TotalSize,
            (byte)BlueprintBlackboard1024.MaxSlots);

        var dto = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = s_assetGuid,
            VariableName           = s_fieldName,
            Operator               = SearchOperator.Equals,
            Predicate              = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.False(predicate(repo, entity));
    }

    // =========================================================================
    // P6T2-SC2: Slot present with matching value → true
    // =========================================================================

    /// <summary>
    /// Entity has a BB1024 component with the blueprint attached and AmmoCount=0.
    /// Predicate "AmmoCount == 0" must return true.
    /// Also tests the negative: after writing AmmoCount=99, predicate returns false.
    /// </summary>
    [Fact]
    public void Compile_BlueprintVariable_SlotPresent_EvaluatesField()
    {
        var (repo, compiler) = Setup();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

        int payloadOffset = AttachToBB1024(repo, entity);

        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem = (byte*)Unsafe.AsPointer(ref bb);
        *(int*)(mem + payloadOffset) = 0; // AmmoCount = 0

        var dto = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = s_assetGuid,
            VariableName           = s_fieldName,
            Operator               = SearchOperator.Equals,
            Predicate              = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.True(predicate(repo, entity),  "AmmoCount=0 must satisfy == 0");

        // Mutate field and verify the delegate reads fresh data each call.
        ref var bb2 = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem2 = (byte*)Unsafe.AsPointer(ref bb2);
        *(int*)(mem2 + payloadOffset) = 99; // AmmoCount = 99
        Assert.False(predicate(repo, entity), "AmmoCount=99 must not satisfy == 0");
    }

    // =========================================================================
    // P6T2-SC3: Tier upgrade (BB1024 → BB4096) — delegate re-runs slot scan
    // =========================================================================

    /// <summary>
    /// Compile a delegate while the entity uses BB1024.
    /// Simulate a tier upgrade: add BB4096, copy slots via CopyToLargerTier,
    /// remove BB1024.
    /// The same compiled delegate must still find AmmoCount=5 via BB4096 because
    /// it probes all tiers on every evaluation.
    /// </summary>
    [Fact]
    public void Compile_BlueprintVariable_TierUpgrade_StillWorks()
    {
        var (repo, compiler) = Setup();

        // ── Phase 1: entity has BB1024, AmmoCount=5 ─────────────────────────
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

        int payloadOffset1024 = AttachToBB1024(repo, entity);

        ref var bb1024 = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem1024 = (byte*)Unsafe.AsPointer(ref bb1024);
        *(int*)(mem1024 + payloadOffset1024) = 5; // AmmoCount = 5

        var dto = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = s_assetGuid,
            VariableName           = s_fieldName,
            Operator               = SearchOperator.Equals,
            Predicate              = new NumericPredicateDto { MinValue = 5, MaxValue = 5 },
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.True(predicate(repo, entity), "Pre-upgrade: AmmoCount=5 on BB1024 must satisfy == 5");

        // ── Phase 2: upgrade to BB4096 via CopyToLargerTier ─────────────────
        repo.AddComponent(entity, new BlueprintBlackboard4096());

        // Re-fetch BB1024 pointer — structural change (AddComponent) may have
        // moved the entity to a new archetype chunk.
        ref var bb1024After = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem1024After  = (byte*)Unsafe.AsPointer(ref bb1024After);

        ref var bb4096 = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
        byte* mem4096  = (byte*)Unsafe.AsPointer(ref bb4096);

        BlueprintBlackboardPartitions.CopyToLargerTier(
            src:         mem1024After,
            srcSize:     BlueprintBlackboard1024.TotalSize,
            dst:         mem4096,
            dstSize:     BlueprintBlackboard4096.TotalSize,
            dstMaxSlots: (byte)BlueprintBlackboard4096.MaxSlots);

        repo.RemoveComponent<BlueprintBlackboard1024>(entity);

        // ── Phase 3: same compiled delegate works on BB4096 ─────────────────
        Assert.True(predicate(repo, entity), "Post-upgrade: AmmoCount=5 on BB4096 must still satisfy == 5");
    }
}
```

---

## Test verification checklist

- **P6T1:** `BlueprintVariablePredicate_SerializesRoundTrip` — confirm the JSON contains
  `"$type":"BlueprintVariable"` and the nested `"$type":"Numeric"` sub-predicate. Confirm all
  five assertion lines pass.

- **P6T2-SC1:** `Compile_BlueprintVariable_NoSlotPresent_ReturnsFalse` — the test initialises the
  BB1024 header but does NOT call `TryAttach`. The predicate must return false because
  `TryGetSlotOffset` finds no slot matching `s_blueprintId`.

- **P6T2-SC2:** `Compile_BlueprintVariable_SlotPresent_EvaluatesField` — the test writes
  `AmmoCount=0` then mutates to `AmmoCount=99` to verify the delegate reads fresh memory on
  every call (not cached). Both assertions must pass.

- **P6T2-SC3:** `Compile_BlueprintVariable_TierUpgrade_StillWorks` — the test uses
  `CopyToLargerTier` (the real production function, not a mock), removes BB1024, and verifies the
  same compiled delegate works on BB4096. The delegate must return true.

---

## Common mistakes to avoid

1. **Fake tests:** do NOT stub `BlueprintBlackboardPartitions.TryGetSlotOffset` or mock the
   `BlueprintRegistry`. Use the real implementations — they are allocation-free and testable.

2. **Stale pointer after `AddComponent`:** in `Compile_BlueprintVariable_TierUpgrade_StillWorks`,
   re-fetch the BB1024 reference after `repo.AddComponent(entity, new BlueprintBlackboard4096())`
   because the structural change may relocate the entity.

3. **BB16384 registration:** do NOT register `BlueprintBlackboard16384` in `Setup()`. The
   compiler correctly handles `typeId16384 = -1` — HasComponentByTypeId returns false.

4. **`CollectMandatoryComponents` for BlueprintVariable:** leave it empty (no-op for this type).
   BlueprintVariable can reside in any tier; adding a mandatory component would incorrectly
   filter entities using a different tier.

5. **`TreatWarningsAsErrors` is active** in `Fdp.Toolkits`. All new code must produce zero
   warnings (no unused variables, no CS8600, etc.).

6. **`DataBreakpointManager` always needs a new case** for every component-predicate DTO. Add
   `case BlueprintVariablePredicateDto _:` to `TryMountDelegate`'s switch block.

---

## Build and test command

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --configuration Debug --no-build 2>&1 | tail -20
```

After running, also run the Fdp.Toolkits.Tests to cover the P6T1 JSON serialization test:

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~BlueprintVariablePredicate" --configuration Debug 2>&1 | tail -20
```

---

## Report format

Provide a `BATCH-43-REPORT.md` in `.dev/breakpoints-1/reports/` with:
- Each file changed (path + summary of what changed)
- Each test added (name + result)
- Any deviations from instructions with justification
- Final dotnet test output (last 20 lines)
