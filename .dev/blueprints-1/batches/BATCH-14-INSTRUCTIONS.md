# BATCH-14 Instructions — TASK-CP-006: Compiler Test Suite

**Task:** TASK-CP-006 — Compiler Test Suite  
**Design refs:** Compiler DD §17 (all subsections), TASK-DETAIL.md §CP-006  
**Phase:** 3 — Compiler  
**Priority:** SC1 (zero failures), SC2 (validator coverage), SC5 (determinism), SC6 (MoveToAndFire end-to-end)

---

## 0. Context

You are implementing the full compiler test suite as described in TASK-DETAIL.md `TASK-CP-006` and Compiler DD §17.

Before writing any tests, you must fix **existing bugs** in `AiPrimitiveEmitter.cs` (wrong namespaces in thunk emission) and update `BlueprintTestFixture.CompileAndLoadMany` to use the current compiler API. These fixes are essential for the Stage 8 tests to pass.

All new test files go in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/` subdirectory.  
Namespace for all new tests: `namespace Hrot.Blueprints.Tests.Compiler`  
Reason: the SC1 filter is `dotnet test --filter "FullyQualifiedName~Compiler"` which requires "Compiler" in the fully qualified name.

---

## 1. Fix `AiPrimitiveEmitter.cs` (emitter namespace bugs)

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/AiPrimitiveEmitter.cs`

The BTree/HSM thunk methods emit C# code with incorrect fully-qualified type names. These would cause Roslyn compilation failures when Stage 8 tests try to compile AiPrimitive blueprints.

**Find and replace all occurrences:**

| Wrong | Correct |
|---|---|
| `global::Fdp.Toolkit.Behavior.BrainBlackboard` | `global::Fdp.Toolkit.Behavior.Components.BrainBlackboard` |
| `global::Fdp.Toolkit.Behavior.BehaviorTreeState` | `global::Fbt.BehaviorTreeState` |
| `global::Fdp.Toolkit.Blueprints.Blackboard1024` | `global::Fdp.Toolkit.Behavior.Components.Blackboard1024` |
| `global::FastHSM.HsmKernelBridge` | `global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge` |

These appear in: `EmitBTreeActionThunk`, `EmitBTreeConditionThunk`, `EmitHsmActivityThunk`, `EmitHsmGuardThunk`.

After fixing, the generated code for an AiPrimitive BTreeAction hosting should compile cleanly with Roslyn (tested in Stage 8 golden tests).

---

## 2. Fix `BlueprintTestFixture.cs`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

### 2a. Add missing using directives (at top with existing usings)
```csharp
using Fdp.Toolkit.Behavior.Components;        // BrainBlackboard, LocomotionChannel, etc.
using Fdp.Toolkit.Behavior.Systems;           // HsmKernelBridge (indirect ref)
using Hrot.Blueprints.Core.Compiler;          // BlueprintCompiler, CompileOptions, etc.
using Hrot.Blueprints.Core.Compiler.Catalogs; // BuiltInNodeRegistry etc.
using Hrot.Blueprints.Core.Compiler.Diagnostics; // DiagnosticSink
using Hrot.Blueprints.Core.Compiler.Roslyn;  // InMemoryRoslynCompiler, MetadataReferenceResolver
```

### 2b. Add channel component registrations to the constructor

In the constructor body, after `_repo.RegisterComponent<BlueprintBlackboard4096>()`, add:
```csharp
// Register behavior channel components needed for end-to-end compiled blueprint tests.
_repo.RegisterComponent<LocomotionChannel>();
_repo.RegisterComponent<WeaponChannel>();
_repo.RegisterComponent<InteractionChannel>();
_repo.RegisterComponent<BrainBlackboard>();
_repo.RegisterComponent<Blackboard1024>();   // FBT behavior blackboard (AiPrimitive working state)
```

### 2c. Add persistent working-state dictionary for InvokeBTreeAction

Add a new private field after the existing private state fields:
```csharp
// Persistent working-state per (assetId, entity) for TickCore reflection invocation.
private readonly Dictionary<(Guid assetId, Entity entity), object> _persistedWorkingState = new();
```

### 2d. Fix `CompileAndLoadMany`

Replace the existing `CompileAndLoadMany` method body (the one that calls `Compiler.Compile(asset, mode)` with old API) with the correct implementation:

```csharp
public Assembly CompileAndLoadMany(
    IReadOnlyList<BlueprintAsset> assets,
    CompilerMode mode = CompilerMode.Debug)
{
    var sink = new DiagnosticSink();
    var options = new CompileOptions(
        Mode:              mode,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    var sb = new StringBuilder();
    foreach (var asset in assets)
    {
        var result = Compiler.Compile(asset, options);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Blueprint '{asset.Name}' failed to compile: " +
                string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        sb.AppendLine(result.GeneratedSource);
    }

    var assemblyName = $"Bp_{Guid.NewGuid():N}";
    var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
        AppDomain.CurrentDomain.GetAssemblies());
    var roslynCompiler = new InMemoryRoslynCompiler(resolver);
    var (assembly, alc) = roslynCompiler.CompileAndLoad(
        sb.ToString(),
        $"{assemblyName}.g.cs",
        assemblyName,
        sink);

    _activeAlcs.Add(alc);
    _alcWeakRefs.Add(new WeakReference<AssemblyLoadContext>(alc));

    DiscoverAndInvokeRegistrars(assembly);
    return assembly;
}
```

### 2e. Implement `InvokeBTreeAction` (replace the stub)

The existing stub throws `NotImplementedException`. Replace it with a reflection-based implementation that calls `TickCore` directly (not `BTreeTick`, which requires BTree runtime types). The implementation:
1. Finds the generated type by name pattern (prefix `{SanitizedName}_`, suffix `_Bp`)
2. Gets the `TickCore` static method
3. Uses/creates persisted boxed `WorkingState` from `_persistedWorkingState[(assetId, entity)]`
4. Creates zero-initialized `Params`
5. Invokes via reflection (ref params work correctly: args[1] reflects the updated WorkingState after invocation)
6. Persists the updated WorkingState back to the dictionary
7. Returns the `NodeStatus`

```csharp
public NodeStatus InvokeBTreeAction(BlueprintAsset asset, Entity entity, int paramIndex = 0)
{
    var genType = FindGeneratedType(asset);
    var paramsType = genType.GetNestedType("Params")
        ?? throw new InvalidOperationException($"No Params nested type in {genType.Name}");
    var wsType = genType.GetNestedType("WorkingState")
        ?? throw new InvalidOperationException($"No WorkingState nested type in {genType.Name}");
    var tickCore = genType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"No TickCore method in {genType.Name}");

    var stateKey = (asset.AssetId, entity);
    if (!_persistedWorkingState.TryGetValue(stateKey, out var wsBoxed))
        wsBoxed = Activator.CreateInstance(wsType)!;

    var paramsBoxed = Activator.CreateInstance(paramsType)!;
    var args = new object?[] { paramsBoxed, wsBoxed, entity, World, View.Time };
    var status = (NodeStatus)tickCore.Invoke(null, args)!;

    // args[1] contains the updated WorkingState after invocation (ref param updated in-place).
    _persistedWorkingState[stateKey] = args[1]!;

    return status;
}

private Type FindGeneratedType(BlueprintAsset asset)
{
    var prefix = SanitizeNameForClass(asset.Name) + "_";
    foreach (var alc in _activeAlcs)
        foreach (var asm in alc.Assemblies)
        {
            var t = asm.GetTypes().FirstOrDefault(
                t => t.Name.StartsWith(prefix, StringComparison.Ordinal)
                  && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
            if (t != null) return t;
        }
    throw new InvalidOperationException($"No generated blueprint type found for '{asset.Name}'.");
}

private static string SanitizeNameForClass(string name)
{
    var sb = new System.Text.StringBuilder();
    foreach (var c in name)
        sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
    return sb.ToString();
}
```

Note: `View.Time` is already a public property on the fixture.

### 2f. Keep `InvokeHsmAction` and `InvokeHsmGuard` stubs unchanged (Phase 4 scope).

---

## 3. Create `Compiler/` test infrastructure files

### 3a. `Compiler/CoversDiagnosticCodeAttribute.cs`

```csharp
namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Marks a test method as providing positive coverage for a specific diagnostic code.
/// Applied to tests that deliberately trigger a diagnostic (the "fires when expected" case).
/// Used by <see cref="TestDiagnosticInventory"/> to build the covered-codes set for
/// <c>V_AllValidatorsCoverageTests</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversDiagnosticCodeAttribute : Attribute
{
    public string Code { get; }
    public CoversDiagnosticCodeAttribute(string code) => Code = code;
}
```

### 3b. `Compiler/TestDiagnosticInventory.cs`

```csharp
using System.Reflection;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Scans the test assembly for methods decorated with <see cref="CoversDiagnosticCodeAttribute"/>
/// and returns the set of covered diagnostic codes.
/// </summary>
internal static class TestDiagnosticInventory
{
    public static HashSet<string> GetCoveredCodes()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var asm = typeof(TestDiagnosticInventory).Assembly;

        foreach (var type in asm.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var attr in method.GetCustomAttributes<CoversDiagnosticCodeAttribute>())
                    covered.Add(attr.Code);
            }
        }
        return covered;
    }
}
```

---

## 4. Create `Compiler/Stage1_ParseTests.cs`

Namespace: `Hrot.Blueprints.Tests.Compiler`  
Design ref: Compiler DD §17.3

4 tests. Each test that FIRES a diagnostic must carry `[CoversDiagnosticCode("BPxxxx")]`.

Required usings: `Hrot.Blueprints.Core.Compiler.Diagnostics`, `Hrot.Blueprints.Core.Compiler.Stages`, `Hrot.Blueprints.Core`, `Hrot.Blueprints.Core.Assets`, `Hrot.Blueprints.Tests.Builders`

Tests:
1. `Parse_ValidJson_ProducesAssetWithCorrectName` — minimal library JSON → asset.Name == "MathLib" (uses `TestData.LoadAsset(TestData.SampleAssets.LibraryMath)`, serialize, parse, verify name)
2. `[CoversDiagnosticCode("BP0002")] Parse_MalformedJson_EmitsBP0002` — `Stage1_Parse.Run("{ bad json", sink)` → null result + BP0002 in sink
3. `[CoversDiagnosticCode("BP0001")] Parse_NullToken_EmitsBP0001` — `Stage1_Parse.Run("null", sink)` → null + BP0001
4. `Parse_AllDispatchKinds_RoundTrip` — Theory test loading LibraryMath, MoveToAndFire, HealthRegen, DoorActor; serialize each → parse → re-serialize → assert JSON equality

For BP0010 and BP0011: Add a 5th and 6th test:
5. `[CoversDiagnosticCode("BP0010")] Parse_EmptyAssetId_EmitsBP0010` — build minimal JSON with `"AssetId": "00000000-..."` zero GUID → BP0010
6. `[CoversDiagnosticCode("BP0011")] Parse_EmptyName_EmitsBP0011` — build JSON with `"Name": ""` → BP0011

Helper: build minimal valid library JSON manually using a multiline string constant in the test class.

---

## 5. Create `Compiler/Stage2_ValidationTests/` (5 files)

### 5a. `V_DispatchKindCompatibilityTests.cs`

Covers: BP1010, BP1011, BP1012, BP1013, BP1020, BP1021, BP1022, BP1023, BP1024, BP1025, BP1030, BP1031, BP1400, BP1401, BP1402, BP1601, BP1602

Pattern:
```csharp
private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset,
    IReadOnlyList<BlueprintSignature>? siblings = null)
{
    var sink = new DiagnosticSink();
    var opts = DefaultOptions(siblings);
    Stage2_Validate.Run(asset, new ValidationContext(sink, opts));
    return sink.All;
}
```

Tests (at minimum):
1. `[CoversDiagnosticCode("BP1010")] Library_WithPrimitiveBlock_EmitsBP1010`  
   — `BlueprintAssetBuilder.Library("L").WithPrimitive(AiPrimitiveIntent.Action, AiPrimitiveHosting.BTreeAction).Build()` → BP1010
2. `[CoversDiagnosticCode("BP1011")] Library_WithVariable_EmitsBP1011`  
   — library with `WithVariable("x", typeof(int))` → BP1011
3. `[CoversDiagnosticCode("BP1012")] Library_WithCustomEvent_EmitsBP1012`  
   — library with `WithCustomEvent(...)` → BP1012
4. `[CoversDiagnosticCode("BP1013")] Library_WithEventGraph_EmitsBP1013`  
   — library with an event-kind graph → BP1013
5. `[CoversDiagnosticCode("BP1020")] AiPrimitive_WithoutPrimitiveBlock_EmitsBP1020`  
   — build raw `BlueprintAsset` with Dispatch=AiPrimitive, Primitive=null → BP1020
6. `[CoversDiagnosticCode("BP1021")] AiPrimitive_WithNoHostings_EmitsBP1021`  
   — `BlueprintAssetBuilder.AiPrimitive("A").WithHostings(/* empty */).Build()` → BP1021  
   (If builder doesn't support empty hostings, set `asset.Primitive!.Hostings.Clear()` after build)
7. `[CoversDiagnosticCode("BP1022")] AiPrimitive_ActionWithConditionHosting_EmitsBP1022`  
   — Action intent + BTreeCondition hosting → BP1022
8. `[CoversDiagnosticCode("BP1023")] AiPrimitive_ConditionWithActionHosting_EmitsBP1023`  
   — Condition intent + BTreeAction hosting → BP1023
9. `[CoversDiagnosticCode("BP1024")] AiPrimitive_WithVariable_EmitsBP1024`  
   — AiPrimitive with `asset.Variables.Add(...)` → BP1024
10. `[CoversDiagnosticCode("BP1025")] AiPrimitive_WithEventGraph_EmitsBP1025`  
    — AiPrimitive with event-kind graph → BP1025
11. `[CoversDiagnosticCode("BP1030")] Instance_WithPrimitiveBlock_EmitsBP1030`  
    — Instance with Primitive != null → BP1030
12. `[CoversDiagnosticCode("BP1031")] Instance_WithParams_EmitsBP1031`  
    — Instance with non-empty Parameters list → BP1031
13. `[CoversDiagnosticCode("BP1400")] Instance_GraphWithUnknownEvent_EmitsBP1400`  
    — event entry node referencing a non-existent event type → BP1400
14. `[CoversDiagnosticCode("BP1401")] AiPrimitive_WithUnknownChannelCommand_EmitsBP1401`  
    — ChannelCommandNode with a command not in `BuiltInChannelCommandCatalog` → BP1401
15. `[CoversDiagnosticCode("BP1402")] AiPrimitive_WithUnknownWaitTarget_EmitsBP1402`  
    — WaitForChannelNode with a channel type not in `BuiltInWaitPrimitiveCatalog` → BP1402
16. `[CoversDiagnosticCode("BP1601")] Graph_WithNoReachableReturn_EmitsBP1601`  
    — Library graph where all ReturnNodes are unreachable (orphan-only) → BP1601
17. `[CoversDiagnosticCode("BP1602")] Graph_WithNoEntryNode_EmitsBP1602`  
    — graph with no node having an exec-out without incoming exec → BP1602
18. `Library_HappyPath_NoDiagnostics` — valid library → `Assert.Empty(diags.Where(d => d.IsError))`

For tests 13/14/15 that need specific node kinds, build them via `BlueprintAssetBuilder` callbacks that add raw nodes directly (the builder's `.WithGraph()` methods take a graph-builder lambda).

### 5b. `V_AiPrimitiveIntentTests.cs`

Covers: BP1100, BP1101

Tests:
1. `[CoversDiagnosticCode("BP1100")] Condition_WithReturnRunning_EmitsBP1100` — exact test from DD §17.4
2. `[CoversDiagnosticCode("BP1101")] Condition_WithLatentDelayNode_EmitsBP1101` — exact test from DD §17.4
3. `Condition_ReturningOnlySuccessFailure_NoDiagnostics` — exact test from DD §17.4

### 5c. `V_VariablesAndStateTests.cs`

Covers: BP1200, BP1201, BP1210, BP1211

Tests:
1. `[CoversDiagnosticCode("BP1200")] AiPrimitive_ParamsTooLarge_EmitsBP1200`  
   — add 26 int32 params (26×4=104 bytes > 100 limit) via `BlueprintAssetBuilder` → BP1200
2. `[CoversDiagnosticCode("BP1201")] AiPrimitive_WorkingStateTooLarge_EmitsBP1201`  
   — add enough int32 working state fields to exceed 1016 bytes → BP1201
3. `[CoversDiagnosticCode("BP1210")] Instance_StateExceedsAllTiers_EmitsBP1210`  
   — add many large variables (total > 16096 bytes) → BP1210
4. `[CoversDiagnosticCode("BP1211")] Instance_StateForcedTierTooSmall_EmitsBP1211`  
   — set `TierHint = BlackboardTierHint.Force1024` but state > 928 bytes → BP1211
5. `AiPrimitive_SmallParams_NoDiagnostics` — 1 int32 param → no BP1200/1201
6. `Instance_FitsInDefaultTier_NoDiagnostics` — 1 float variable → no tier error

### 5d. `V_PeerReferencesTests.cs`

Covers: BP1300, BP1301, BP1302

Tests:
1. `[CoversDiagnosticCode("BP1300")] PeerRef_NotInCallablePeersList_EmitsBP1300`  
   — `CallPeerBlueprintNode` targeting a GUID not in `asset.CallablePeers` → BP1300
2. `[CoversDiagnosticCode("BP1301")] PeerRef_NoSiblingSignature_EmitsBP1301`  
   — peer is in CallablePeers but no sibling signature provided → BP1301
3. `[CoversDiagnosticCode("BP1302")] PeerRef_FunctionNotExportedByPeer_EmitsBP1302`  
   — sibling has no function with the referenced name → BP1302
4. `PeerRef_ValidWithSiblingSignature_NoDiagnostics`  
   — correct setup with a sibling signature providing the function → no BP130x errors

For these tests, create a sibling `BlueprintSignature` manually (it's a record).

### 5e. `V_AllValidatorsCoverageTests.cs`

Covers: structural reflection test

```csharp
using System.Reflection;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class V_AllValidatorsCoverageTests
{
    // Codes declared in DiagnosticCodes but intentionally not yet emitted by any
    // compiler stage (reserved for Slice 2 or marked unused).
    private static readonly HashSet<string> KnownNotYetEmittedCodes = new(StringComparer.Ordinal)
    {
        "BP1600",  // OrphanedNode: declared as unused alias of BP2001 (see DiagnosticCodes comment)
        "BP2003",  // Reserved for Stage 3, Slice 2
        "BP3001",  // Reserved for Stage 4, Slice 2
        "BP4002",  // Reserved for Stage 5, Slice 2
        "BP4003",  // Reserved for Stage 5, Slice 2
        "BP6001",  // Reserved for Stage 7, Slice 2
    };

    [Fact]
    public void EveryDiagnosticCode_HasAtLeastOnePositiveTest()
    {
        var declaredCodes = typeof(DiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);

        // Remove placeholder codes that have no implementation yet.
        declaredCodes.ExceptWith(KnownNotYetEmittedCodes);

        var coveredCodes = TestDiagnosticInventory.GetCoveredCodes();
        var uncovered = declaredCodes.Except(coveredCodes).OrderBy(c => c).ToList();

        Assert.Empty(uncovered);
    }
}
```

---

## 6. Create `Compiler/Stage3_NormalizationTests.cs`

Covers: BP2001, BP2002; also happy path

Uses the pipeline: Stage1_Parse → Stage2_Validate → Stage3_Normalize.

Tests:
1. `[CoversDiagnosticCode("BP2001")] Normalize_OrphanNode_EmitsBP2001AndRemovesNode`  
   — build a library with a graph containing an unreachable (orphan) node → BP2001 warning is present, graph.Nodes does not contain the orphan after normalization
2. `[CoversDiagnosticCode("BP2002")] Normalize_ImplicitCast_EmitsBP2002AndInsertsNode`  
   — build a graph where a pin connection has coercible types (e.g., int→float); verify BP2002 warning + extra CastNode inserted
3. `Normalize_HappyPath_NoWarnings`  
   — valid clean library → no warnings at all

---

## 7. Create `Compiler/Stage4_TypeResolveTests.cs`

Covers: BP1500, BP1501, BP1502, BP1503  
Design ref: Compiler DD §4 + TASK-DETAIL.md

Note: Stage 4 runs on the output of Stage 3 (TypedAsset requires the validated+normalized asset). Use the `BlueprintCompiler` (calling `Compile` up to stage 4) or directly call `Stage4_TypeResolve.Run` with a pre-built `TypedAsset`.

Tests:
1. `[CoversDiagnosticCode("BP1500")] TypeResolve_UnknownTypePinRef_EmitsBP1500`  
   — add a pin with `TypeRef.TypeId = "Unknown.NonExistent.Type"` → BP1500 after stage 4
2. `[CoversDiagnosticCode("BP1501")] TypeResolve_CoercionNotAvailable_EmitsBP1501`  
   — connect pin with type that has no coercion path (e.g., int→bool) → BP1501
3. `[CoversDiagnosticCode("BP1502")] TypeResolve_Wildcard_EmitsBP1502`  
   — pin with wildcard type ref that Stage 3 can't resolve → BP1502
4. `[CoversDiagnosticCode("BP1503")] TypeResolve_ManagedTypeInState_EmitsBP1503`  
   — variable with TypeRef pointing to a managed type (e.g., string) → BP1503
5. `TypeResolve_BuiltInTypes_NoDiagnostics`  
   — all int/float/bool fields → no diagnostics

---

## 8. Create `Compiler/Stage5_ScheduleTests/GoldenIrTests.cs`

Design ref: Compiler DD §17.5

This test runs Stage 1–5 on 3 sample assets and compares serialized IR to snapshot files.

```csharp
[Theory]
[InlineData(TestData.SampleAssets.LibraryMath)]
[InlineData(TestData.SampleAssets.InstanceCounter)]
[InlineData(TestData.SampleAssets.MoveToAndFire)]
public void Schedule_ProducesExpectedIr(string sampleName)
{
    var asset = TestData.LoadAsset(sampleName);
    var opts = DefaultOptions();
    var sink = new DiagnosticSink();
    var ctx = new ValidationContext(sink, opts);

    // Run stages 1-5
    Stage2_Validate.Run(asset, ctx);
    asset = Stage3_Normalize.Run(asset, ctx);
    var typed = Stage4_TypeResolve.Run(asset, ctx);
    var ir = Stage5_Schedule.Run(typed, ctx);

    Assert.False(sink.HasErrors,
        string.Join(", ", sink.All.Where(d => d.IsError).Select(d => $"{d.Code}: {d.Message}")));

    var actual = IrPrinter.PrettyPrint(ir);
    TestData.ReadOrRegenerateSnapshot($"Schedule/{sampleName}.ir.txt", actual);
}
```

**Important:** After creating this file, the developer must regenerate snapshots (see §13 below).

Also add tests for:
- `[CoversDiagnosticCode("BP4001")] Schedule_UnconnectedDataPin_EmitsBP4001` — a node with a required data input pin that has no incoming link
- `[CoversDiagnosticCode("BP4004")] Schedule_UnknownImpureNode_EmitsBP4004` — add an impure node kind that Stage 5 doesn't recognize

---

## 9. Create `Compiler/Stage5_ScheduleTests/DataFlowCseTests.cs`

Tests CSE (common subexpression elimination) optimization:
1. `CseOptimization_IdenticalPureCalls_ProducesSingleTempInBlock` — two identical pure calls (e.g., `Math.Add(1,2)` appearing twice) → only one temp value in the IR block
2. `CseOptimization_PureCallsWithDifferentArgs_NotMerged` — different args → two temps

---

## 10. Create `Compiler/Stage5_ScheduleTests/LatentBlockSplitTests.cs`

Tests block splitting at latent operations:
1. `LatentBlock_WaitForChannel_SplitsIntoTwoBlocks` — WaitForChannel → graph has at least 2 blocks (pre-suspend and resume)
2. `LatentBlock_MultipleWaits_ProducesMultipleBlocks` — 3 sequential WaitForChannel → at least 4 blocks

---

## 11. Create `Compiler/Stage6_LoweringTests/LibraryLoweringTests.cs`

Covers: BP5001, BP9001

Tests:
1. `[CoversDiagnosticCode("BP5001")] Library_NoGraphs_EmitsBP5001` — empty library (no graphs) after stage 6 → BP5001 error
2. `[CoversDiagnosticCode("BP9001")] Library_WithLatentNode_EmitsBP9001Internal` — library with WaitForChannel node (bypassing Stage 2 validation by constructing IrAsset directly with a latent op) → BP9001
3. `Library_WithFunctions_NoErrors` — valid library → no errors

For test 2, construct an `IrAsset` directly with a library dispatch and a WaitForChannel statement, then call `Stage6_Lower.Run(ir, mode, sink)`.

---

## 12. Create `Compiler/Stage6_LoweringTests/AiPrimitiveLoweringTests.cs`

Tests phase-byte state machine insertion:
1. `AiPrimitive_WithWaitForChannel_HasPhaseByteInWorkingState` — after Stage 6, `lowered.WorkingState` contains `__phase` field
2. `AiPrimitive_WithLatentDelay_HasWaitUntilTimeField` — after Stage 6, working state has `__waitUntilTime`
3. `AiPrimitive_WithoutLatent_NoSynthesizedFields` — pure (non-latent) AiPrimitive → no `__phase` added

---

## 13. Create `Compiler/Stage6_LoweringTests/InstanceLoweringTests.cs`

Tests cursor switch insertion:
1. `Instance_WithEventGraph_HasCursorSwitchBlock` — Instance with an event graph → after Stage 6, at least one block contains cursor-switch terminator
2. `Instance_WithLatentDelay_WritesAndChecksCursor` — verifies `IrOp_WriteCursorPosition` and `IrOp_ReadCursorPosition` appear in lowered IR

---

## 14. Create `Compiler/Stage6_LoweringTests/ChannelCommandLoweringTests.cs`

Tests ChannelCommand lowering:
1. `ChannelCommand_Lowered_HasGetComponentRwOp` — ChannelCommandNode in AiPrimitive → lowered IR contains `IrOp_GetComponentRW`
2. `ChannelCommand_Lowered_HasActiveActionWrite` — verifies ActiveAction write op is in the lowered statements
3. `ChannelCommand_Lowered_HasActionInstanceIdIncrement` — verifies ActionInstanceId++ op is present

---

## 15. Create `Compiler/Stage6_LoweringTests/DebugProbeInsertionTests.cs`

Tests debug probe insertion (only in Debug mode):
1. `DebugProbe_DebugMode_InsertsProbeStatements` — Stage 6 Debug mode on a library with function → lowered IR has `IrOp_DebugProbe` statements
2. `DebugProbe_ReleaseMode_NoProbeStatements` — Stage 6 Release mode → no `IrOp_DebugProbe`

---

## 16. Create `Compiler/Stage7_EmitTests/LibraryEmitGoldenTests.cs`

Design ref: Compiler DD §17.6

Golden test against `LibraryMath` and `MathUtilsLib`:
```csharp
[Theory]
[InlineData(TestData.SampleAssets.LibraryMath)]
public void Emit_Library_MatchesGoldenSource(string sampleName)
{
    var asset = TestData.LoadAsset(sampleName);
    var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

    Assert.True(result.Succeeded,
        string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    TestData.ReadOrRegenerateSnapshot($"Emit/{sampleName}.cs.txt", result.GeneratedSource!);
}
```

Also validate structural requirements (present even outside snapshot mode):
- Generated source contains `public static class`
- Contains `_Bp` suffix
- Contains `BlueprintId` constant
- Contains registrar with `BlueprintRegistryStaging staging` parameter

After creation, regenerate snapshots (see §21).

---

## 17. Create `Compiler/Stage7_EmitTests/AiPrimitiveEmitGoldenTests.cs`

Same pattern as LibraryEmitGoldenTests but for `MoveToAndFire` and `HasVisibleTarget`:
```csharp
[Theory]
[InlineData(TestData.SampleAssets.MoveToAndFire)]
[InlineData(TestData.SampleAssets.HasVisibleTarget)]
public void Emit_AiPrimitive_MatchesGoldenSource(string sampleName)
```

Also validate AiPrimitive-specific constraints:
- Contains `public struct Params`
- Contains `public struct WorkingState`
- Contains `TickCore`
- Contains `BTreeTick` (if BTreeAction hosting)
- Registrar has `BlueprintRegistryStaging staging` parameter (Patch C1)
- AiPrimitive with BTreeAction hosting: registrar has `BehaviorRegistry behReg` parameter

---

## 18. Create `Compiler/Stage7_EmitTests/InstanceEmitGoldenTests.cs`

Same pattern for `HealthRegen` and `InstanceCounter`:
```csharp
[Theory]
[InlineData(TestData.SampleAssets.HealthRegen)]
[InlineData(TestData.SampleAssets.InstanceCounter)]
public void Emit_Instance_MatchesGoldenSource(string sampleName)
```

Also validate Instance-specific constraints:
- Contains `public struct State`
- Contains `BlueprintLatentCursor Cursor`
- Contains `uint instanceVersion` in tick method signature
- Contains `StateSize` property

---

## 19. Create `Compiler/Stage7_EmitTests/ThunkEmissionTests.cs`

Tests thunk shape without golden snapshots:
1. `BTreeActionThunk_HasCorrectSignature` — compiled AiPrimitive with BTreeAction hosting → source contains `BTreeTick(` + `ref global::Fdp.Toolkit.Behavior.Components.BrainBlackboard bb`
2. `HsmActionThunk_HasUnmanagedCallersOnly` — HSM hosting → source contains `[global::System.Runtime.InteropServices.UnmanagedCallersOnly]`
3. `InstanceTickThunk_HasInstanceVersionParam` — Instance → source contains `uint instanceVersion`
4. `RegistrarNamespace_IsFdpToolkitBlueprintsAttributes` — verifies `[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]` appears in registrar class
5. `NodeStatus_FullyQualified_IsHrotBlueprintsCore` — verifies NodeStatus references use `global::Hrot.Blueprints.Core.Assets.NodeStatus` in Library return type

---

## 20. Create `Compiler/Stage7_EmitTests/SanitizerTests.cs`

Tests name sanitization for class names:
1. `Sanitize_NameWithSpaces_ReplacesWithUnderscore` — asset named "Move To And Fire" → class name has underscores instead of spaces
2. `Sanitize_NameWithDots_ReplacesWithUnderscore` — asset named "Fdp.Math" → sanitized
3. `Sanitize_AlphanumericName_Unchanged` — "MoveToAndFire" → unchanged

Implement by calling `BlueprintCompiler.Compile(asset, opts)` and inspecting `result.GeneratedFileName`.

---

## 21. Create `Compiler/Stage8_RoslynTests/InMemoryCompileTests.cs`

Design ref: Compiler DD §17.7

Uses `InMemoryRoslynCompiler` directly (not the full pipeline).

Tests:
1. `[CoversDiagnosticCode("BP7001")] Compile_InvalidSource_ThrowsBlueprintCompileExceptionWithBP7001`  
   — `Assert.Throws<BlueprintCompileException>(() => new InMemoryRoslynCompiler(resolver).Compile("not valid C#", "x.cs", "X", sink))` + verify sink has BP7001
2. `Compile_ValidLibrarySource_ProducesLoadableAssembly`  
   — compile minimal valid C# class → PE non-empty → load into ALC → assembly loads OK
3. `CompileAndLoad_ValidSource_AssemblyTypeExists`  
   — compile a simple class → loaded assembly has the expected type
4. `Compile_AiPrimitiveBlueprint_RoslynCompilesSuccessfully`  
   — Use `BlueprintCompiler.Compile(asset, options with { EmitPdbWithEmbeddedSource = true })` on `TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire)` → `result.Succeeded == true`, `result.PortablePe` is non-empty

Helper `DefaultResolver()`:
```csharp
private static MetadataReferenceResolver DefaultResolver() =>
    MetadataReferenceResolver.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
```

---

## 22. Create `Compiler/Stage8_RoslynTests/PdbEmbeddedSourceTests.cs`

Tests PDB embedded source:
1. `Compile_WithEmbeddedSource_PdbBytesNonEmpty`  
   — compile with `EmitPdbWithEmbeddedSource = true` → `result.PortablePdb` is non-null and non-empty
2. `Compile_WithoutEmbeddedSource_PdbIsNull`  
   — compile with default options (no PDB) → `result.PortablePdb` is null
3. `Compile_RoslynOutput_PeAndPdbAreByteIdenticalOnTwoRuns`  
   — compile the same source twice → PE bytes identical (verifies `deterministic: true`)

---

## 23. Create `Compiler/Stage8_RoslynTests/MetadataReferenceResolverTests.cs`

Design ref: Compiler DD §8 and Patch 2

Critical tests (SC4):
1. `ForRuntimeAssemblies_WithDynamicAssemblies_FiltersThem`  
   — create a dynamic assembly via `AssemblyBuilder.DefineDynamicAssembly`, get assemblies list, call `ForRuntimeAssemblies`, verify dynamic assembly not in results
2. `ForRuntimeAssemblies_WithInMemoryAlcAssembly_FiltersIt`  
   — load a minimal PE into a `new AssemblyLoadContext(...).LoadFromStream(stream)` (in-memory, no file location), get assemblies, call `ForRuntimeAssemblies`, verify the in-memory assembly is NOT in the result set

```csharp
[Fact]
public void ForRuntimeAssemblies_WithInMemoryAlcAssembly_FiltersIt()
{
    // Compile a minimal PE in-memory
    var sink = new DiagnosticSink();
    var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(
        AppDomain.CurrentDomain.GetAssemblies());
    var compiler = new InMemoryRoslynCompiler(resolver);
    var (pe, _) = compiler.Compile(
        "namespace TestNs { public class TestClass {} }",
        "test.cs", "InMemoryTestAssembly", sink);

    // Load into an in-memory ALC (no file path)
    var alc = new AssemblyLoadContext("InMemTest", isCollectible: true);
    alc.LoadFromStream(new MemoryStream(pe));

    // Get the now-loaded assemblies (includes our in-memory one)
    var allAssemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Concat(alc.Assemblies)
        .ToArray();

    var filteredRefs = MetadataReferenceResolver.ForRuntimeAssemblies(allAssemblies).Resolve();

    // The in-memory assembly must NOT appear in filtered refs
    // (it has empty Location, so the Location!= "" predicate filters it out)
    Assert.DoesNotContain(filteredRefs, r =>
        r is Microsoft.CodeAnalysis.PortableExecutableReference per
        && string.IsNullOrEmpty(per.FilePath));

    alc.Unload();
}
```

3. `Resolve_IncludesCorLib` — result contains at least one reference pointing to `System.Runtime.dll` or `mscorlib.dll`

---

## 24. Create `Compiler/Determinism/CompilerDeterminismTests.cs`

Design ref: Compiler DD §17.8  
SC5: 16 parallel compiles → identical output

```csharp
[Theory]
[InlineData(TestData.SampleAssets.LibraryMath)]
[InlineData(TestData.SampleAssets.InstanceCounter)]
[InlineData(TestData.SampleAssets.MoveToAndFire)]
[InlineData(TestData.SampleAssets.HealthRegen)]
public void Compile_TwoRuns_ProducesByteIdenticalOutput(string sampleName) { ... }

[Theory]
[InlineData(TestData.SampleAssets.MoveToAndFire)]
public void Compile_ParallelCalls_AllProduceSameOutput(string sampleName)
{
    var asset = TestData.LoadAsset(sampleName);
    var opts = DefaultOptions();
    var results = Enumerable.Range(0, 16)
        .AsParallel()
        .Select(_ => new BlueprintCompiler().Compile(asset, opts))
        .ToList();

    var first = results[0].GeneratedSource;
    Assert.All(results, r => Assert.Equal(first, r.GeneratedSource));
}
```

---

## 25. Create `Compiler/Determinism/BlueprintIdHashTests.cs`

Tests:
1. `BlueprintId_SameGuid_ProducesSameHash` — same GUID → same BlueprintId
2. `BlueprintId_DifferentGuids_ProduceDifferentHashes` — different GUIDs → different BlueprintIds
3. `BlueprintId_IsReproducibleAcrossInstances` — compute in two separate calls → same result

---

## 26. Create `Compiler/Determinism/StructureHashTests.cs`

Tests hash sensitivity:
1. `StructureHash_FieldOrderChanges_HashChanges` — build asset with (X, Y) fields vs (Y, X) → different hashes
2. `StructureHash_FieldTypeChange_HashChanges` — (X: int) vs (X: float) → different hashes
3. `StructureHash_SameAsset_SameHashOnMultipleRuns` — same asset compiled 3× → same hash
4. `StructureHash_AddingField_ChangesHash` — one variable vs two variables → different hashes

---

## 27. Create `Compiler/EndToEnd/MoveToAndFire_EndToEndTests.cs`

Design ref: Compiler DD §17.9, SC6

SC6 requirement: phase-advance, 2-tick sequence (MoveToAndFire.bp.json uses LocomotionChannel MoveTo+Wait, not a 3-tick fire scenario — test what the actual asset does).

```csharp
public sealed class MoveToAndFire_EndToEndTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();
    private readonly BlueprintAsset _asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Compile_Succeeds()
    {
        var result = new BlueprintCompiler().Compile(_asset, DefaultOptions());
        Assert.True(result.Succeeded,
            string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.NotNull(result.GeneratedSource);
    }

    [Fact]
    public void CompileAndLoad_RegistersBlueprintInRegistry()
    {
        _fixture.CompileAndLoad(_asset);
        var id = BlueprintIdHash.Compute(_asset.AssetId);
        Assert.True(_fixture.Registry.TryGetById(id, out _),
            "Blueprint should be registered after CompileAndLoad.");
    }

    [Fact]
    public void BTreeTick_FirstCall_ReturnsRunning_WhenChannelIsIdle()
    {
        _fixture.CompileAndLoad(_asset);

        var entity = _fixture.World.CreateEntity();
        _fixture.World.AddComponent(entity, new LocomotionChannel());

        // First tick: issue MoveTo, WaitForChannel with idle channel → Running
        var status = _fixture.InvokeBTreeAction(_asset, entity);
        Assert.Equal(NodeStatus.Running, status);

        // Channel should have received the MoveTo command
        var channel = _fixture.World.GetComponent<LocomotionChannel>(entity);
        Assert.NotEqual(0, (int)channel.ActiveAction);
    }

    [Fact]
    public void BTreeTick_AfterChannelComplete_ReturnsSuccess()
    {
        _fixture.CompileAndLoad(_asset);

        var entity = _fixture.World.CreateEntity();
        _fixture.World.AddComponent(entity, new LocomotionChannel());

        // Tick 1: returns Running, working state is persisted by fixture
        var tick1 = _fixture.InvokeBTreeAction(_asset, entity);
        Assert.Equal(NodeStatus.Running, tick1);

        // Simulate move completion
        ref var chan = ref _fixture.World.GetComponentRW<LocomotionChannel>(entity);
        chan.Status = NodeStatus.Success;

        // Tick 2: WaitForChannel sees Success → continues → returns Success
        var tick2 = _fixture.InvokeBTreeAction(_asset, entity);
        Assert.Equal(NodeStatus.Success, tick2);
    }
}
```

**Key notes:**
- `LocomotionChannel` is now registered by `BlueprintTestFixture` constructor (see §2b)
- `_fixture.World.GetComponentRW<T>(entity)` returns a ref — wrap in a local to modify it
- `_fixture.InvokeBTreeAction` persists working state between calls via `_persistedWorkingState`

---

## 28. Create `Compiler/EndToEnd/HealthRegen_EndToEndTests.cs`

HealthRegen is an Instance blueprint (no graphs in the basic asset):
```csharp
[Fact]
public void Compile_Succeeds() { ... }

[Fact]
public void CompileAndLoad_RegistersBlueprintInRegistry() { ... }

[Fact]
public void GeneratedSource_ContainsStateAndTick()
{
    var result = new BlueprintCompiler().Compile(TestData.LoadAsset(TestData.SampleAssets.HealthRegen), DefaultOptions());
    Assert.True(result.Succeeded);
    Assert.Contains("public struct State", result.GeneratedSource!);
}
```

---

## 29. Create `Compiler/EndToEnd/HasVisibleTarget_EndToEndTests.cs`

HasVisibleTarget is an AiPrimitive (Condition):
```csharp
[Fact]
public void Compile_Succeeds() { ... }

[Fact]
public void CompileAndLoad_RegistersBlueprintInRegistry() { ... }

[Fact]
public void GeneratedSource_HasBTreeEvaluate()
{
    // BTreeCondition hosting → BTreeEvaluate method
    var result = new BlueprintCompiler().Compile(TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget), DefaultOptions());
    Assert.True(result.Succeeded);
    Assert.Contains("BTreeEvaluate", result.GeneratedSource!);
}
```

---

## 30. Create `Compiler/EndToEnd/DoorActor_DoorSensor_EndToEndTests.cs`

```csharp
[Fact]
public void DoorActor_Compiles_Succeeds() { ... }

[Fact]
public void DoorSensor_Compiles_Succeeds() { ... }

[Fact]
public void BothBlueprints_CompileAndLoad_InSingleAssembly()
{
    // Load both blueprints into a single assembly — verifies multi-asset compilation
    var doorActor = TestData.LoadAsset(TestData.SampleAssets.DoorActor);
    var doorSensor = TestData.LoadAsset(TestData.SampleAssets.DoorSensor);
    _fixture.CompileAndLoadMany(new[] { doorActor, doorSensor });
    
    Assert.True(_fixture.Registry.TryGetById(BlueprintIdHash.Compute(doorActor.AssetId), out _));
    Assert.True(_fixture.Registry.TryGetById(BlueprintIdHash.Compute(doorSensor.AssetId), out _));
}
```

---

## 31. Create `Compiler/EndToEnd/MathUtilsLib_EndToEndTests.cs`

```csharp
[Fact]
public void LibraryMath_Compiles_Succeeds() { ... }

[Fact]
public void LibraryMath_CompileAndLoad_RegistersBlueprintInRegistry() { ... }

[Fact]
public void LibraryMath_GeneratedSource_HasStaticLibraryClass()
{
    var result = ...;
    Assert.Contains("public static class LibraryMath_", result.GeneratedSource!);
}
```

---

## 32. Snapshot Generation

After implementing all the above test files, run the snapshot generation commands:

```powershell
# Generate Schedule IR snapshots
$env:BLUEPRINT_REGENERATE_SNAPSHOTS = "1"
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests --filter "FullyQualifiedName~Schedule_ProducesExpectedIr" -v minimal

# Generate Emit C# source snapshots
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests --filter "FullyQualifiedName~Emit.*MatchesGoldenSource" -v minimal

Remove-Item Env:BLUEPRINT_REGENERATE_SNAPSHOTS
```

Verify snapshot files were created in `Snapshots/Schedule/*.ir.txt` and `Snapshots/Emit/*.cs.txt`.

---

## 33. Final Verification

Run the full Compiler filter test suite:
```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests --filter "FullyQualifiedName~Compiler" -v minimal
```

Expected: **zero failures, zero skipped.**

Run the full suite to verify no regressions:
```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests -v minimal
```

Expected: all previous tests still pass (188 + new Compiler tests).

---

## Success Criteria (from TASK-DETAIL.md)

- **SC1**: `--filter "FullyQualifiedName~Compiler"` → 0 failures, 0 skipped ✓
- **SC2**: `V_AllValidatorsCoverageTests` passes (reflection over `DiagnosticCodes`) ✓
- **SC3**: Stage 7 golden tests pass — `result.GeneratedSource` byte-identical to checked-in snapshots ✓
- **SC4**: `ForRuntimeAssemblies_WithInMemoryAlcAssembly_FiltersIt` passes ✓
- **SC5**: 16 parallel compile determinism test passes ✓
- **SC6**: `MoveToAndFire_EndToEndTests` 2-tick sequence passes (Tick1=Running, Tick2=Success) ✓

---

## File Change Summary

**Modified:**
- `Hrot.Blueprints.Core/Compiler/Emit/AiPrimitiveEmitter.cs` — fix namespace bugs (§1)
- `Hrot.Blueprints.Tests/BlueprintTestFixture.cs` — fix CompileAndLoadMany, add InvokeBTreeAction (§2)

**Created (Compiler/ directory, 34 files):**
- `Compiler/CoversDiagnosticCodeAttribute.cs`
- `Compiler/TestDiagnosticInventory.cs`
- `Compiler/Stage1_ParseTests.cs`
- `Compiler/Stage2_ValidationTests/V_DispatchKindCompatibilityTests.cs`
- `Compiler/Stage2_ValidationTests/V_AiPrimitiveIntentTests.cs`
- `Compiler/Stage2_ValidationTests/V_VariablesAndStateTests.cs`
- `Compiler/Stage2_ValidationTests/V_PeerReferencesTests.cs`
- `Compiler/Stage2_ValidationTests/V_AllValidatorsCoverageTests.cs`
- `Compiler/Stage3_NormalizationTests.cs`
- `Compiler/Stage4_TypeResolveTests.cs`
- `Compiler/Stage5_ScheduleTests/GoldenIrTests.cs`
- `Compiler/Stage5_ScheduleTests/DataFlowCseTests.cs`
- `Compiler/Stage5_ScheduleTests/LatentBlockSplitTests.cs`
- `Compiler/Stage6_LoweringTests/LibraryLoweringTests.cs`
- `Compiler/Stage6_LoweringTests/AiPrimitiveLoweringTests.cs`
- `Compiler/Stage6_LoweringTests/InstanceLoweringTests.cs`
- `Compiler/Stage6_LoweringTests/ChannelCommandLoweringTests.cs`
- `Compiler/Stage6_LoweringTests/DebugProbeInsertionTests.cs`
- `Compiler/Stage7_EmitTests/LibraryEmitGoldenTests.cs`
- `Compiler/Stage7_EmitTests/AiPrimitiveEmitGoldenTests.cs`
- `Compiler/Stage7_EmitTests/InstanceEmitGoldenTests.cs`
- `Compiler/Stage7_EmitTests/ThunkEmissionTests.cs`
- `Compiler/Stage7_EmitTests/SanitizerTests.cs`
- `Compiler/Stage8_RoslynTests/InMemoryCompileTests.cs`
- `Compiler/Stage8_RoslynTests/PdbEmbeddedSourceTests.cs`
- `Compiler/Stage8_RoslynTests/MetadataReferenceResolverTests.cs`
- `Compiler/Determinism/CompilerDeterminismTests.cs`
- `Compiler/Determinism/BlueprintIdHashTests.cs`
- `Compiler/Determinism/StructureHashTests.cs`
- `Compiler/EndToEnd/MoveToAndFire_EndToEndTests.cs`
- `Compiler/EndToEnd/HealthRegen_EndToEndTests.cs`
- `Compiler/EndToEnd/HasVisibleTarget_EndToEndTests.cs`
- `Compiler/EndToEnd/DoorActor_DoorSensor_EndToEndTests.cs`
- `Compiler/EndToEnd/MathUtilsLib_EndToEndTests.cs`

**Generated snapshots:**
- `Snapshots/Schedule/LibraryMath.ir.txt`
- `Snapshots/Schedule/InstanceCounter.ir.txt`
- `Snapshots/Schedule/MoveToAndFire.ir.txt`
- `Snapshots/Emit/LibraryMath.cs.txt`
- `Snapshots/Emit/MoveToAndFire.cs.txt`
- `Snapshots/Emit/HasVisibleTarget.cs.txt`
- `Snapshots/Emit/HealthRegen.cs.txt`
- `Snapshots/Emit/InstanceCounter.cs.txt`
