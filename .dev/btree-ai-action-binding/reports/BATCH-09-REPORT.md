# BATCH-09 Report — S2-G: Slice 2 Demo Gate

**Date:** 2026-06-16
**Workstream:** btree-ai-action-binding
**Slice:** S2-G (Capstone)

---

## Changes Made

### Task 1 — Author T20_MultiStateful.btree.json

**File (NEW):** `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/T20_MultiStateful.btree.json`

Managed BTree asset with AssetId `bb000020-0000-0000-0000-000000000000` and topology:

```
Root → Sequence
  AdvanceCursor_A (ThreeParamReusableStateful, ExpressionTargetField="cursorA",
                   WorkingStateTypeId="...+DemoCursorState", VisualId bb200000-...-0003)
  AdvanceCursor_B (ThreeParamReusableStateful, ExpressionTargetField="cursorB",
                   VisualId bb200000-...-0004)
  IncrementCounter (ThreeParamReusable, ExpressionTargetField="counter",
                   VisualId bb200000-...-0005)
```

Variables:
- `cursorA` (DemoCursorParams, DefaultValueJson `{"Limit":3}`) → packed offset 0
- `cursorB` (DemoCursorParams, DefaultValueJson `{"Limit":5}`) → packed offset 4
- `counter` (DemoCounterParams, DefaultValueJson `{"Counter":0,"Threshold":1000}`) → packed offset 8

---

### Task 2 — Make emitted stateful thunk compile (DEBT-AIB-026 closure)

Three root-cause bugs were fixed in the emitter pipeline:

#### Fix A — BTreeMethodCompatibilityValidator: no branch for ThreeParamReusableStateful

**File:** `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs`

`CheckPayload` had no branch for `DelegateShape == ThreeParamReusableStateful`, causing it to
fall through to the `FourParamFull` path which checked param0=ref TBB (not ref TDto). This
produced BTREE0002 errors like:

> Action leaf bb200000-...-0004 binds Action_AdvanceCursor: param 0 (blackboard (TBB)) has type
> DemoCursorParams but expected BrainBlackboard

**Fix:** Added dedicated branch and `CheckThreeParamReusableStateful` method validating:
`ExpressionTargetField` set, managed blackboard, variable found, method resolves/public/static/
returns NodeStatus, exactly 4 params, param0=ref TDto, param1=ref (any WorkingState struct),
param2=ref BehaviorTreeState, param3=ref TCtx.

#### Fix B — BTreeEmitCore: duplicate struct names across managed assets

**File:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`
(method `EmitBlackboardStructSource`)

Both T10 and T20 use `BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard"`,
so the sanitized struct name was `FdpToolkitBehaviorComponentsBrainBlackboard` for both —
causing CS0101 duplicate definition.

**Fix:** Prefix struct name with per-asset name:
```csharp
string structName = assetPrefix + "_" + typeSuffix;
// T10 → "T10_MultiAction_FdpToolkitBehaviorComponentsBrainBlackboard"
// T20 → "T20_MultiStateful_FdpToolkitBehaviorComponentsBrainBlackboard"
```

Verified safe: existing byte-identity gate tests (SampleScout/SampleGuard) do not cover managed
assets, and generator tests use `Contains()` checks not exact struct names.

#### Fix C — BTreeEmitCore: no ThreeParamReusableStateful branch in EmitAction

**File:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`
(method `EmitAction` and `EmitLeafWithPills`)

`EmitAction` had no branch for `ThreeParamReusableStateful`, so it fell through to the else
which emitted `seq.Action(DemoCounterNodes.Action_AdvanceCursor, ...)` — a method group with
the wrong 4-param signature, causing CS1503.

**Fix:** Added branch computing `slotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, node.VisualId)`
and emitting the string blob key `{MethodFqn}@{paramOffset}@{slotKey}`. Threaded `assetId` as
a new parameter through `EmitAction` and updated the call site in `EmitLeafWithPills` to
pass `dto.AssetId`.

#### Verification

After all three fixes, `dotnet build Hrot.AI.Behaviors -t:Rebuild` reports:

> Build succeeded. 0 Warning(s). 0 Error(s)

Generated files verified:

- `T20_MultiStateful.g.cs`: topology with blob keys `...Action_AdvanceCursor@0@1631759884`,
  `...Action_AdvanceCursor@4@1614982265`, `...Action_IncrementCounter@8`
- `T20_MultiStateful.Registrar.g.cs`: behaviorId=64542340, two stateful thunks with tier
  dispatch (16384→4096→1024), ParseParams (LimitA=3, LimitB=5, Threshold=1000),
  StatefulWorkingSlots (keys 1631759884 and 1614982265, payloadSize=SizeOf\<DemoCursorState\>=4)

---

### Task 3 — Proof tests

**File (NEW):** `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Demos/T20_MultiStateful_ProofTests.cs`

Full pipeline: JSON asset → BTreeJsonGenerator → Roslyn in-memory compile → bridge register
→ BehaviorIngressSystem (real provisioning) → interpreter.Tick → assert bytes.

Two named tests:

**`TwoStatefulInstances_MaintainIndependentState`**
- Runs 7 ticks via `interpreter.Tick(ref bb, ref state, ref ctx)` with `ctx.Self=entity, ctx.World=world`
- After 7 ticks: A.Cursor=7, B.Cursor=5 — proves FNV-1a slot keys are distinct and partition
  slots are independent (no cross-talk between two nodes calling the same method)
- Asserts `A.Cursor != B.Cursor` and slot offsets are distinct

**`MixedStatelessAndStateful_Coexist`**
- Verifies ParseParams wrote LimitA=3, LimitB=5, Threshold=1000 before first tick
- After 7 ticks: Counter.Counter=1 (stateless, from BrainBlackboard at offset 8),
  Threshold=1000 unchanged, cursorA.Limit=3 unchanged, cursorB.Limit=5 unchanged
- A.Cursor=7, B.Cursor=5 from partition slots
- Proves disjoint memory: cursor slot bytes (BlueprintBlackboard1024) are independent from
  counter DTO bytes (BrainBlackboard[8..15])

#### Root causes found during development:

1. **Fbt.Compiler assembly not loaded**: `GC.KeepAlive(typeof(T20_MultiStateful))` alone doesn't
   force `Fbt.Compiler` into the AppDomain since .NET lazily JIT-compiles methods.
   Fix: `GC.KeepAlive(typeof(Fbt.Compiler.FbtAutoDiscovery))` forces the assembly.

2. **Coordinator.Dispose() clears _liveRegistry**: `AiHotReloadCoordinator.Dispose()` calls
   `_behaviorRegistry.Clear()`, erasing the just-registered definition before
   `BehaviorIngressSystem` could find it. Fix: pass a throwaway `new BehaviorRegistry()` to
   the coordinator constructor, keeping `_liveRegistry` clear of the coordinator's lifecycle.

---

## Test Gate Results

| Suite | Run | Passed | Failed |
|---|---|---|---|
| `Hrot.AiEditor.Persistence.Tests` (byte-identity gate) | 129 | 129 | 0 |
| `Hrot.AiEditor.Generators.Tests` | 87 | 85 | 2 (known MigrationEquivalence non-regressions) |
| `Fdp.Toolkits.Tests --filter Behavior` | 153 | 153 | 0 |

T20 specifically:
- `T20_MultiStateful_ProofTests.TwoStatefulInstances_MaintainIndependentState` PASSED
- `T20_MultiStateful_ProofTests.MixedStatelessAndStateful_Coexist` PASSED

---

## Files Modified

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/T20_MultiStateful.btree.json` | NEW — T20 asset |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs` | Fix A: ThreeParamReusableStateful validator branch |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs` | Fix B: per-asset struct name; Fix C: stateful topology emit |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Demos/T20_MultiStateful_ProofTests.cs` | NEW — proof tests |

No commits made (per instructions).
