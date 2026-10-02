# BATCH-17 Report — Generator symbol-check: incompatible bound method → BTREE0002, never build break

**Status:** COMPLETE  
**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-12

---

## Summary

Added a pre-emit method compatibility validator to `BTreeJsonGenerator`. Before any `.g.cs` is written, every reachable Action/Condition leaf with a non-empty `MethodFqn` is checked against the real `NodeLogicDelegate<TBB,TCtx>` signature. Incompatible or unresolved bindings produce a `BTREE0002` Warning and the asset is skipped — identical to the BT-12/BT-14 skip path. The `Hrot.AI.Behaviors` build never breaks.

---

## NodeLogicDelegate signature matched

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/NodeLogicDelegate.cs`  
**Lines:** 13–18

```csharp
public delegate NodeStatus NodeLogicDelegate<TBlackboard, TContext>(
    ref TBlackboard blackboard,
    ref BehaviorTreeState state,
    ref TContext context,
    int paramIndex)
    where TBlackboard : struct
    where TContext : struct, IAIContext;
```

---

## Compatibility rule implemented

A bound method (`MethodFqn`) is **VALID** iff ALL of the following hold:

1. `DelegateShape` is **not** `ThreeParamReusable` (that path is unsupported — VE-DEBT-002; treat as invalid for safety).
2. The method **resolves** in the `Compilation` (type `<TypePart>` found via `GetTypeByMetadataName`; method `<MethodPart>` found in `GetMembers`).
3. The method is `public static`.
4. Return type is `Fbt.NodeStatus` (compared via `SymbolEqualityComparer.Default`).
5. Exactly **4 parameters**:
   - Param 0: `ref TBB` where `TBB` = the asset's `BlackboardTypeName` (resolved via `GetTypeByMetadataName`).
   - Param 1: `ref Fbt.BehaviorTreeState`.
   - Param 2: `ref TCtx` where `TCtx` = the asset's `ContextTypeName`.
   - Param 3: `System.Int32` (no ref).

**Everything else is INVALID** → asset skipped + `BTREE0002` Warning.

---

## Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs` | **NEW** — validator class; resolves symbols, walks reachable leaves, checks each bound method |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs` | Modified `Initialize` to combine `CompilationProvider` with `rawFiles`; added `compatError` check in `GenerateOneAsset` before emit |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Generator/BTreeJsonGeneratorTests.cs` | Added 6 new BATCH-17 tests + `RunGeneratorWithStubs` helper + `ValidMethodStubs` constant + `BuildBoundActionJson`/`BuildBoundConditionJson` helpers |

---

## Per-project test counts

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| `Hrot.AiEditor.Generators.Tests` | 48 total (46 passed, **2 pre-existing failures**) | 54 total (52 passed, **2 pre-existing failures**) | +6 new passing tests |
| `Hrot.AiEditor.Persistence.Tests` | 123 passed, 0 failed | 123 passed, 0 failed | unchanged |
| `Hrot.BTree.Editor.Tests` | 505 passed, 0 failed | 505 passed, 0 failed | unchanged |

**Pre-existing failures (unchanged, unrelated to BT-17):**
- `MigrationEquivalenceTests.BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout`
- `MigrationEquivalenceTests.Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout`

---

## New BATCH-17 tests (all pass)

1. `Generator_IncompatibleBoundMethod_DtoParam_SkipsAndWarns_NoErrors` — Action with DTO-first-param → BTREE0002, zero Error
2. `Generator_IncompatibleBoundCondition_DtoParam_SkipsAndWarns_NoErrors` — Condition with DTO-first-param → BTREE0002, zero Error
3. `Generator_UnresolvedMethod_SkipsAndWarns` — unresolvable FQN → BTREE0002, zero Error
4. `Generator_CompatibleBoundMethod_EmitsNormally` — 4-param `NodeLogicDelegate` shape → 2 files emitted, no BTREE0002
5. `Generator_IncompatibleAsset_DoesNotSuppressValidSibling` — 1 bad + 1 good → good sibling emits, exactly 1 BTREE0002
6. `Generator_WrongArityOrReturn_IsInvalid` — wrong arity (3 params) + wrong return (void) → BTREE0002 in both cases

---

## CombatShowcase / Action_Wander verification

`CombatShowcase.btree.json` binds `Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander` which is `public static NodeStatus Action_Wander(ref BrainBlackboard, ref BehaviorTreeState, ref BTreeContext, int)` — matches the delegate shape exactly.  
Full solution build: **0 errors, 0 new warnings** (the 2 warnings are pre-existing NU1903 MessagePack vulnerability notices). No BTREE0002 fires for any committed asset.

---

## Incrementality note

Combining `rawFiles` with the full `CompilationProvider` (via `.Combine(context.CompilationProvider)`) means `GenerateOneAsset` re-runs whenever ANY compilation change occurs (not just when a `.btree.json` file changes). This is the accepted trade-off given the small number of BTree assets (VE-DEBT-003 tracks a fancier incremental symbol extraction for the future). The existing `BTREE0001`/`BTREE0002` diagnostics and the skip behavior are unaffected.

---

## Build result

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
    2 Warning(s)  ← pre-existing NU1903 (MessagePack), NOT BTREE0002
    0 Error(s)
```
