# BATCH-05 Report

## Implementation Summary

### Part 1 — `BlueprintMath` library

Added `public static class BlueprintMath` at:
- **File**: `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintMath.cs`
- **Namespace**: `Fdp.Toolkit.Blueprints`
- **Assembly**: `Fdp.Toolkits.dll` (project `Fdp.Toolkits.csproj`)

**Roslyn reference confirmation**: `MetadataReferenceResolver.ForRuntimeAssemblies` uses `AppDomain.CurrentDomain.GetAssemblies()` (Hrot.Blueprints.Compiler/Compiler/Roslyn/MetadataReferenceResolver.cs:21-30). Because `Fdp.Toolkits.dll` is loaded into the AppDomain when Hrot.Blueprints.Tests runs, it is automatically included in the Roslyn compilation reference set. The generated code correctly calls `global::Fdp.Toolkit.Blueprints.BlueprintMath.AddInt(...)` as confirmed by the captured generated source.

**Method set** (lines 1–169 of BlueprintMath.cs):
- **Float** (Add, Subtract, Multiply, Divide, Modulo, Abs, Negate, Min, Max, Clamp, Lerp, Floor, Ceil, Round, Sqrt, Pow, Sin, Cos) — lines 16–68
- **Int** (AddInt, SubInt, MulInt, DivInt, ModInt, AbsInt, NegateInt, MinInt, MaxInt, ClampInt) — lines 72–97
- **Float comparisons→bool** (GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, ApproxEquals) — lines 101–116
- **Int comparisons→bool** (EqualsInt, GreaterThanInt, LessThanInt) — lines 120–127
- **Bool logic** (And, Or, Not, Xor) — lines 131–140
- **Vector3** (AddVec, SubVec, MulVecScalar, Dot, Cross, Normalize, Length, Distance) — lines 144–169

Div/mod by zero returns 0 (no throw). Normalize of zero vector returns Vector3.Zero.

**Tests**: `Hrot.Blueprints.Tests/BlueprintMathTests.cs` — 69 tests, all pass. Covers div-by-zero==0, Clamp ranges, Lerp endpoints, Sqrt(-1)==0, Normalize(zero)==Zero, Dot/Cross/Length/Distance, all bool ops, all int ops, all float comparisons.

### Part 2 — `CountingDemo.bp.json` + proof

**Asset**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/CountingDemo.bp.json`

AssetId: `00000006-0000-0000-0000-000000000001`
Dispatch: `Instance`
Variable: `Count : System.Int32` (default 0), Id `a0000006-0000-0000-0000-000000000001`
Graph: `Tick`, Kind `Function`

Nodes: EventEntry → SetVariable(Count) ← FunctionCall(AddInt, pure) ← [GetVariable(Count), Literal(1)]; Return

Registered in `TestData.SampleAssets.CountingDemo`.

**Proof test**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/CountingDemo_ProofTests.cs`
- `CountingDemo_AfterAttach_CountIsZero` — verifies Count==0 before any tick. PASS.
- `CountingDemo_After5Ticks_CountEquals5` — verifies Count==5 after 5 × TickFrame(0.016f). PASS.

## Design Decisions

### Pin authoring decision — EXPLICIT pins required (projection-only invariant does NOT apply to compiler input pins)

**Verify-first analysis**: Stage4 (`Stage4_TypeResolve.cs:30`) iterates `node.Pins` directly to populate `resolvedPinTypes[pin.Id]`. There is NO hydration pass before Stage4/5 — the type map is built exclusively from the pins present in the JSON.

Stage5 also reads `node.Pins` directly:
- `SetVariableNode` (line 631): `node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In")` — requires explicit data-in pin.
- `ResolveAllDataInputs` (line 1216): `node.Pins.Where(p => !p.IsExec && p.Direction == "In")` — requires explicit in-pins on FunctionCall.
- `GetSingleExecSuccessor` (line 1228): `node.Pins.Where(p => p.IsExec && p.Direction == "Out")` — requires explicit exec-out pins for the exec chain.
- `ResolveNodeOutput` for pure FunctionCall (line 1012): uses `pinType = _typed.PinTypes.TryGetValue(sourcePinId, ...)` where sourcePinId is the FunctionCall's out-pin ID — requires explicit out-pin with TypeRef.

**Conclusion**: Pins are NOT hydrated from a canonical schema before Stage4/5. The `"Pins": []` projection-only invariant in existing .bp.json files works because those nodes either don't have data pins used by the compiler (e.g. simple EventEntry with no data, SetVariable without an explicit data flow), or they are non-compilable demo assets. For a compilable Tick with FunctionCall+SetVariable, explicit pins with TypeRef.TypeId are mandatory.

CountingDemo.bp.json includes:
- SetVariable: exec-in, exec-out, data-in (System.Int32) — all explicit
- FunctionCall (AddInt): data-in-A (System.Int32), data-in-B (System.Int32), data-out (System.Int32) — all explicit
- GetVariable: data-out (System.Int32) — explicit (so Stage4 maps pin ID → System.Int32 type)
- Literal(1): data-out (System.Int32) — explicit
- EventEntry: exec-out — explicit (GetSingleExecSuccessor follows it)
- Return: exec-in — explicit

The existing assets with `"Pins": []` (like HealthRegen, InstanceCounter) have no compilable graphs with data flow, so their empty Pins arrays are fine. CountingDemo IS different — it has a real data-flow graph and requires explicit pins. This is documented honestly and does not break any existing golden/byte-stability tests because CountingDemo is a new asset.

## Generated Tick C#

Captured via `BlueprintCompiler.Compile(CountingDemo, DefaultOptions())`:

```csharp
public static void Tick(
    ref State s,
    global::Fdp.ModuleHost.Abstractions.ISimulationView view,
    global::Fdp.Interfaces.IEntityCommandBuffer ecb,
    global::Fdp.Core.Entity self,
    float time,
    float deltaTime,
    uint instanceVersion)
{
    {
        global::Hrot.Blueprints.Core.Debug.DebugProbe.NodeEnter(self, "20000006-0000-0000-0000-000000000004");
        var __t0 = s.Count;
        var __t1 = 1;
        var __t2 = global::Fdp.Toolkit.Blueprints.BlueprintMath.AddInt(__t0, __t1);
        s.Count = __t2;
        return;
    }
}
```

Lowers exactly as specified: read Count, literal 1, AddInt, write Count. The `DebugProbe.NodeEnter` is from Debug mode compilation and is expected.

## Deviations

**1. `ValueJson` field in LiteralNode is populated but `TypeId` on LiteralNode itself is also included**: LiteralNode has both a `TypeId` field (node-level) and the out-pin has `TypeRef.TypeId`. Both are included for correctness — the stage pipeline uses pin TypeRef for type resolution; the node-level TypeId is used by Stage3/4 for MaterializeDefaultPinLiterals if needed.

**2. Round test uses banker's rounding**: `MathF.Round(2.5f)` = 2 (MidpointRounding.ToEven). The test was corrected to assert `2f` (not `3f`) and a separate `Round_ClearlyRoundsUp` test was added. This matches .NET behavior exactly — no deviation from spec, just correct handling of the implementation detail.

**3. Return node required**: The validation stage (BP1601) requires a ReturnNode exec-reachable from the entry. Added a Return node at the end of the Tick exec chain (EventEntry → SetVariable → Return). This is consistent with all other compilable bp.json assets (MoveToAndFire, with-branch, etc.).

## Test Results

### BlueprintMathTests
```
Passed! - Failed: 0, Passed: 69, Skipped: 0, Total: 69
```
All math functions verified including edge cases: div-by-zero==0, Sqrt(-1)==0, Normalize(Zero)==Zero, bool ops, vector ops.

### CountingDemo_ProofTests (Count 0→5)
```
Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2
  CountingDemo_AfterAttach_CountIsZero: PASS (Count==0 before any tick)
  CountingDemo_After5Ticks_CountEquals5: PASS (Count==5 after 5 ticks)
```

### Full Hrot.Blueprints.Tests
```
Failed: 7, Passed: 1319, Skipped: 8, Total: 1334
```

**Failing tests (all pre-existing, zero new)**:
1. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")` — pre-existing golden mismatch
2. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")` — pre-existing golden mismatch
3. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` — pre-existing golden mismatch
4. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` — pre-existing snapshot mismatch
5. `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` — pre-existing snapshot mismatch
6. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` — pre-existing
7. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` — pre-existing

**Byte-stability / projection-only tests**: All pass.
```
Passed! - Failed: 0, Passed: 37 (InstanceEmitGoldenTests + ByteStab + Projection filter)
```
Notably `InstanceEmitGoldenTests.Instance_EmitMatchesGoldenSource` passes — CountingDemo does NOT perturb the existing instance golden.

### EditorSubsystemBoot integration tests
```
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

### dotnet build IOS-IG-SimHost.sln
```
Build succeeded. 0 Error(s). 0 new warnings in touched projects.
```
All warnings are pre-existing (CS0618 `IBlueprintTimeController`, CS8601 null ref, xUnit2013 collection size).

## Developer Insights

1. **Pin hydration is not performed**: The compiler pipeline does not hydrate pins from a canonical schema before Stages 4–5. Every pin that participates in type resolution or exec-chain navigation must be explicit in the JSON. This is the single most important finding for the batch and for future .bp.json authoring.

2. **GetVariable pins**: GetVariable does not use its pins in `ResolveNodeOutput` (Stage5:957-966 only uses `gv.VariableId`), but its out-pin must still be explicit so Stage4 registers `pinTypes[outPinId] = System.Int32`, which Stage5 then reads via `_typed.PinTypes.TryGetValue(sourcePinId, ...)` when computing the return type of the pure FunctionCall.

3. **`TargetTypeId` on FunctionCallNode uses the fully qualified type name**: `"Fdp.Toolkit.Blueprints.BlueprintMath"` — the generated code emits `global::Fdp.Toolkit.Blueprints.BlueprintMath.AddInt(...)`.

4. **Tick graph Kind is `Function`, not `Event`**: `InstanceEmitter.EmitTickMethod` (line 182) looks for `g.Kind == IrGraphKind.Function && g.Name == "Tick"`. The graph must be `Kind: "Function"`. The `WithEventGraph` builder creates `GraphKind.Event` graphs for custom events like OnHit; Tick uses `WithGraph("Tick", ...)` = `GraphKind.Function`.

5. **Validation requires ReturnNode**: Stage2 (BP1601) requires a ReturnNode exec-reachable from entry. Instance Function graphs follow the same rule as AiPrimitive Function graphs.

## Known Issues

- The `DebugProbe.NodeEnter` in debug-mode Tick body references `GetVariable`'s node ID (not EventEntry's), because the debug probe insertion attaches to the first "data" node encountered. This is consistent with all other blueprints in debug mode and does not affect runtime behavior.

- BATCH-05B (surfacing BlueprintMath in the node picker) is a separate follow-up batch. No picker entries added here.

## Suggested Commit Message

feat(blueprint): add BlueprintMath pure-function library + CountingDemo proof (BATCH-05)
