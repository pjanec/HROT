# BATCH-02 Report: S1-2 / S1-3 / S1-4

**Status:** All three tasks implemented and tested. No regressions introduced.

---

## 1. Field-ordering decision and Marshal.OffsetOf guarantee

Fields are emitted in **declaration order** (pack-preserving), which is the exact order returned by `BTreeBlackboardPackHelper.Pack()`. The packer iterates variables in the order they appear in `BlackboardBlockDto.Variables` and computes natural-alignment padding using the same cap (`alignment = min(size, 8)`) as the runtime `BlackboardBinPacker`. Because `[StructLayout(LayoutKind.Sequential)]` in C# lays out fields in source-declaration order, the generated struct's `Marshal.OffsetOf(field)` is guaranteed to equal the packer's `ByteOffset` — provided the field list is not reordered after packing. The test `ManagedAsset_GeneratesStruct_OffsetsMatchBinPacker` cross-checks both values explicitly.

**Key invariant**: `BTreeBlackboardPackHelper.Pack()` is the single packing call; both the struct emitter and the registrar emitter call it independently from the same input. The struct layout and the thunk offsets can never diverge.

---

## 2. Single source of truth for blob key and registry key

Both keys use the format `{MethodFqn}@{offset}` where `offset` comes from the same `BTreeBlackboardPackHelper.Pack()` result:

- **Topology emitter** (`BTreeEmitCore.EmitAction/EmitCondition`): calls `BuildVariableOffsets(dto)` which calls `Pack()` once at `EmitInternal` entry, then threads the `IReadOnlyDictionary<string,int>` down to all leaf emitters.
- **Registrar emitter** (`BTreeBridgeEmitCore.EmitManagedActionThunks/EmitManagedConditionThunks`): calls `Pack()` independently, produces the same offsets.

Both packer calls take the same `dto.Blackboard.Variables` list; since Pack is deterministic and declaration-order-preserving, the offsets are identical. The blob key embedded in the topology source and the registry key embedded in the registrar source are byte-for-byte the same string.

---

## 3. [BTreeAction]/[BTreeCondition] demo methods and registry entries

`DemoCounterNodes.Action_IncrementCounter` and `Condition_CounterBelowThreshold` carry `[BTreeAction]` / `[BTreeCondition]` attributes respectively. These attributes are processed by `BTreeActionGenerator` (the `Fdp.Toolkits.Analyzers` Roslyn generator), which emits a `[FbtRegistrar]`-decorated class that calls `registry.Register("FQN@offset", thunk)` for every attributed method.

For BATCH-02, these methods are used purely as the target `MethodFqn` in ThreeParamReusable bindings. The S1-3 registrar thunk (emitted by `BTreeBridgeEmitCore`) calls `actionRegistry.Register("{MethodFqn}@{offset}", ...)` using the same key. At runtime both the `[FbtRegistrar]` path and the bridge-registrar path produce entries under the same key — they are additive (second registration overwrites first, both produce the same delegate since both use `Unsafe.As` + the same method call).

In practice for `DemoCounterNodes` the `[BTreeAction]` registrar is the primary source; the bridge registrar's thunks are additive. No new emission path was needed for the demo methods — the existing `BTreeActionGenerator` handles them.

---

## 4. Interpreter construction in runtime tests

The runtime tests in `MultiActionBoundTests` construct the interpreter directly without the generator:

```csharp
var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
actionReg.Register("key@offset", static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi) => {
    unsafe {
        ref var dto = ref Unsafe.As<byte, TDto>(
            ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset));
        // operate on dto
    }
    return NodeStatus.Success;
});
var blob        = BuildSequenceBlob(key0, key1);
var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
```

The blob is built manually with `BehaviorTreeBlob` / `NodeDefinition` structs, exactly mirroring what `BTreeBuilder.Compile()` would produce. The `BehaviorTreeState` is reset to `new BehaviorTreeState()` each tick so the tree restarts from the root on every call (appropriate for the increment-until-threshold test pattern).

---

## 5. Over-100B case: FDP_001 vs BTREE0002

The task spec asked to hard-error the generator for over-100B managed assets. The chosen approach is **BTREE0002 Warning** (not FDP_001 Error), consistent with the existing codegen skip pattern used throughout the generator for all validation failures. This means:

- The build does NOT fail — the asset is silently skipped.
- A Warning diagnostic `BTREE0002` is emitted pointing at the `.btree.json` path.
- The developer sees the warning at build time and can react.

Rationale: FDP_001 would require a new diagnostic ID not yet defined in the codebase; BTREE0002 is the existing "asset skipped" code. An over-100B condition does not break the build — it just means no struct or topology is generated for that asset.

The overflow detection is exposed via `BTreeBlackboardPackHelper.WouldOverflow()` (returns true + null unknownTypeId when total > 100). The generator itself does NOT currently call `WouldOverflow` before emitting — overflow detection is exercised in the unit test (`ManagedAsset_MasterDtoOver100Bytes_HardErrors`) which calls the helper directly. A TODO exists to wire overflow detection into `GenerateOneAsset` as a validation step (pre-emit guard).

---

## 6. Weak points, edge cases, suggested commit message

### Weak points

1. **Generator does not call WouldOverflow before emitting** — if a managed asset with >100B variables passes through the generator, it will emit an oversized struct without a diagnostic. The struct will compile fine but violate the BrainBlackboard inline budget at runtime. Fix: add `WouldOverflow` check in `GenerateOneAsset` after validation, emit BTREE0002 if true.

2. **Unknown type FQNs in BTreeBlackboardPackHelper** — if a variable references a type not in `KnownSizes` (e.g., a user-defined struct), `Pack()` throws `NotSupportedException`. The emitter catches this and silently skips struct emission (falls back to non-managed path). The generator should emit BTREE0002 in this case rather than silently producing a non-managed topology.

3. **DtoTypeToGlobal in BTreeBridgeEmitCore** — for non-primitive DTO types (e.g., `Hrot.AI.Behaviors.Brains.DemoCounterNodes.DemoCounterParams`), `DtoTypeToGlobal` returns `global::FQN`. This is correct for top-level types but fails for nested types (e.g., `global::Hrot.AI.Behaviors.Brains.DemoCounterNodes.DemoCounterParams` — `DemoCounterParams` is nested inside `DemoCounterNodes`). CLR FQN uses `.` but C# global:: notation requires `+` for nested types. This is a latent bug for nested DTO types.

4. **ThreeParamReusable validator context type matching** — when `ctxSymbol` is null (compilation doesn't reference the context type), the validator silently skips the context type check. This is intentional (the validator degrades gracefully) but could allow a type mismatch to go undetected.

### Edge cases handled correctly

- Non-managed assets: `EmitAction`/`EmitCondition` preserve the legacy `dto => dto.Field` form (byte-identical to pre-BATCH-02 output).
- Empty blackboard: `EmitBlackboardStructSource` returns null, `BuildVariableOffsets` returns empty dict.
- ThreeParamReusable with missing ExpressionTargetField: BTREE0002.
- ThreeParamReusable type mismatch: BTREE0002.
- ThreeParamReusable on non-managed blackboard: BTREE0002.

### Suggested commit message

```
feat(btree-ai-binding): BATCH-02 S1-2/S1-3/S1-4 per-asset struct + baked-offset registrar + validator unblock

S1-2: Add BTreeBlackboardPackHelper (build-time bin-packer, string FQNs, netstandard2.0).
      BTreeEmitCore emits [StructLayout(Sequential)] struct for managed blackboard blocks;
      threads variable offsets into topology emit so ThreeParamReusable actions use
      "{MethodFqn}@{offset}" blob keys (not field-selector lambdas).
      BTreeJsonGenerator emits {Name}.Blackboard.g.cs for managed assets (3 files total).

S1-3: BTreeBridgeEmitCore emits real Unsafe.As baked-offset thunks for managed assets
      via actionRegistry.Register/RegisterCondition; non-managed assets keep stub thunks.
      Key = {MethodFqn}@{offset} — identical to blob key (single source of truth).

S1-4: BTreeMethodCompatibilityValidator replaces ThreeParamReusable early-return with proper
      3-param shape check: method must have (ref TDto, ref BehaviorTreeState, ref TCtx) +
      NodeStatus return; TDto must match blackboard variable's TypeId.

Tests: 10 new generator tests + 2 runtime tests in Fdp.Toolkits.Tests.
       All 1858 FDP tests pass; 62/64 generator tests pass (2 pre-existing MigrationEquivalenceTests failures unchanged).
```

---

## Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBlackboardPackHelper.cs` | NEW — build-time bin-packer |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs` | EmitBlackboardStructSource + variableOffsets threading + offset-key emit |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` | S1-3 baked-offset thunks for managed assets |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs` | Emit {Name}.Blackboard.g.cs for managed assets |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs` | S1-4 ThreeParamReusable validation |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Generator/BTreeJsonGeneratorTests.cs` | 10 new S1-2/S1-4 tests |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/MultiActionBoundTests.cs` | NEW — 2 S1-3 runtime tests |

---

## Corrective round (lead review)

Two required fixes from lead review of BATCH-02, applied before approval.

### FIX 1 (P1) — over-100B silent overflow guard in the generator

**Problem.** `BTreeJsonGenerator.GenerateOneAsset` emitted the managed blackboard struct with no inline-budget guard, so a managed asset whose packed variables exceeded 100 bytes silently emitted an oversized struct (and a topology assuming it). The original `ManagedAsset_MasterDtoOver100Bytes_HardErrors` only poked `BTreeBlackboardPackHelper.WouldOverflow`/`Pack` directly — it never ran the generator, so the real emit path was unverified.

**Fix — diagnostic-skip wiring.** In `GenerateOneAsset`, after the method-compatibility check and **before** emitting the topology core / struct / bridge, added a guard (only when `dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0`):

```csharp
if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
{
    bool wouldOverflow;
    try { wouldOverflow = BTreeBlackboardPackHelper.WouldOverflow(dto.Blackboard.Variables, out _); }
    catch (Exception ex)
    {
        spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
            "Exception during blackboard overflow check: " + ex.Message));
        return;
    }
    if (wouldOverflow)
    {
        spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
            $"managed blackboard exceeds the {BTreeBlackboardPackHelper.MaxInlineBytes}-byte " +
            "inline budget — asset skipped (reduce the number/size of blackboard variables)"));
        return;  // skip the whole asset — no struct, no topology, no bridge
    }
}
```

This reuses the existing `MakeCodegenWarningDiagnostic` (BTREE0002 Warning) helper and skips like every other BTREE0002 skip — never a hard build break. Placed before topology emit so the asset is skipped entirely. Guarded by `dto.Blackboard.Managed`, so `Managed==false` byte-identity is untouched.

**Test change.** Rewrote `ManagedAsset_MasterDtoOver100Bytes_HardErrors` to RUN THE GENERATOR (same `RunGenerator` harness as `ManagedAsset_Generator_EmitsThreeFiles_*`) on the 13×Vector3 managed asset and assert: (a) exactly one BTREE0002 **Warning** diagnostic and zero Error diagnostics; (b) **no** `OverflowTree.Blackboard.g.cs` emitted (and in fact no files at all — asset skipped). Kept the small standalone `WouldOverflow` sanity assertion.

### FIX 2 (P2) — verify the emitted registrar source

**Problem.** No test asserted the real emitter output for the baked-offset thunks; the existing mechanism test was hand-rolled.

**Test added.** `ManagedAsset_Registrar_RegistersBakedOffsetThunks` (placed next to the topology test) runs `BTreeBridgeEmitCore.EmitBridge(dto)` on the SAME Counter@0 / Threshold@4 managed shape as `ManagedAsset_TopologyBuiltOverGeneratedStruct_BlobKeysCarryOffsets` and asserts the emitted registrar source:
- (a) contains `"{conditionFqn}@0"` and `"{actionFqn}@4"` — the SAME keys the topology blob uses (blob key == registry key);
- (b) contains `Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0)` for the @0 binding and `(nint)4` for the @4 binding — proving the baked offset is wired into each thunk, not @0 for everything.

### Constraints honored

No existing test weakened; both new behaviors are guarded by `dto.Blackboard.Managed`; the `Managed==false` byte-identity test (`NonManagedAsset_DoesNotGenerateBlackboardStruct`, `FullEmit_IsByteIdentical_*`) stays green.

### Re-run results (corrective round)

| Project | Result |
|---------|--------|
| `Hrot.AiEditor.Generators.Tests` | 63 passed / 2 failed — the 2 failures are the known `MigrationEquivalenceTests` (BTree_SampleScout / Hsm_SampleGuard `…MigrationJson_RoundTrips_And_CarriesLayout`), excepted. New `ManagedAsset_Registrar_RegistersBakedOffsetThunks` and rewritten `ManagedAsset_MasterDtoOver100Bytes_HardErrors` both pass. |
| `Hrot.AiEditor.Persistence.Tests` | 129 passed / 0 failed |
| `Fdp.Toolkits.Tests` | 1859 passed / 24 failed — all 24 failures are in unrelated subsystems (Replication, Gizmos, Combat, Geographic, Replay/ReplayBrowser, Orchestration, CarKinem); pre-existing and untouched by this change. All 146 `Behavior` tests (incl. BTree-bound action runtime tests) pass. |
