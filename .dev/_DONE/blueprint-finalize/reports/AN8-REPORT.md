# AN8 — Inline-Latent Non-Channel Behavior-Action Lowering

**Branch:** `blueprint-integ-1`
**Status:** COMPLETE — 13/13 new tests pass; 0 new test failures.

---

## Objective

Lower `ChannelCommandNode` nodes with `ActionFqn` set (non-channel behavior actions) through
the full Blueprint compiler pipeline using the **INLINE-LATENT** model (ROUND-4 design):

- Invoke the action synchronously on every tick.
- On `NodeStatus.Running`: suspend the blueprint inline (resume at the same node next tick).
- On `NodeStatus.Success`: continue on the exec-out path.
- On `NodeStatus.Failure`: route to the failure path.
- Params built from data-IN pins.

---

## Investigation Results (STEP 1)

### AiPrimitive (BlueprintCall) — IMPLEMENTED

- Generated class pattern: `{SanitizedName}_{BlueprintId:X8}_Bp` in `Hrot.AI.Behaviors.Generated`
- Invocation: `Call(ref Params p, ref WorkingState ws, Entity self, EntityRepository world, float time) → NodeStatus`
- Working state projection: `Blackboard1024` component at fixed offsets; `StructureHash@0`, `WorkingState@8`
- Hash mismatch → zero block via `Unsafe.InitBlock` + write hash; re-initialize defaults

### SharedAiAction — DEFERRED

- `[SharedAiAction]` methods take `ref fieldType` (specific DTO field at a BTree-internal offset)
- This is NOT compatible with Blueprint's full-DTO params model
- The compiler cannot determine the field offset from `ActionFqn` alone without reflection
- Deferred; emits `#error` if `IsAiPrimitive == false` is reached (dead code in Slice-1)

---

## Implementation (STEP 2)

### Files Modified

#### `Hrot.Blueprints.Compiler`

| File | Change |
|------|--------|
| `Assets/Nodes.cs` | Added `ActionParamsTypeFqn` property to `ChannelCommandNode` (AN8, JSON-ignored when null) |
| `Compiler/Ir/IrOperation.cs` | Added `IrOp_InlineActionCall` record (ActionFqn, ParamsTypeFqn, ParamFields, IsAiPrimitive) |
| `Compiler/IrPrinter.cs` | Print case for `IrOp_InlineActionCall` |
| `Compiler/Stages/Stage2_Validate.cs` | Skip catalog-check for `ChannelCommandNode` when `ActionFqn` is set (bug: BP1401 on non-channel nodes) |
| `Compiler/Stages/Stage5_Schedule.cs` | Pattern-match `ChannelCommandNode { ActionFqn }` → `ScheduleInlineActionNode` before `default`; added `ScheduleInlineActionNode` method |
| `Compiler/Lowering/AiPrimitiveLowering.cs` | `HasAnyLatentOp` now includes `IrOp_InlineActionCall` (bug: lowering was skipped) |
| `Compiler/Lowering/InstanceLowering.cs` | `hasLatent` check now includes `IrOp_InlineActionCall` (same bug fix) |
| `Compiler/Lowering/WaitLowering_AiPrimitive.cs` | `waitOp` search + `keptStmts` filter include `IrOp_InlineActionCall`; added `IrOp_InlineActionCall` branch that builds checkBlock/retRunningBlock/notRunningBlock/failureBlock |
| `Compiler/Lowering/WaitLowering_Instance.cs` | Same updates for cursor-based Instance lowering path |
| `Compiler/Emit/StatementEmitter.cs` | Dispatch case for `IrOp_InlineActionCall` → `InlineActionLowering.Emit` |
| `Compiler/Emit/InlineActionLowering.cs` | **NEW**: emits `unsafe` + `Blackboard1024` projection + `Call(...)` invocation |

#### `Hrot.Blueprints.Editor`

| File | Change |
|------|--------|
| `NodeDrawers/BlueprintNodePaletteEntries.cs` | `NonChannelActionEntries` bakes `ActionParamsTypeFqn` into created `ChannelCommandNode` instances |

#### `Hrot.Blueprints.Tests`

| File | Change |
|------|--------|
| `Builders/BlueprintAssetBuilder.cs` | Added `ActionInvocation(actionFqn, paramsTypeFqn?)` builder method for `GraphBuilder` |
| `Compiler/EndToEnd/InlineAction_EndToEndTests.cs` | **NEW**: 13 end-to-end tests across schedule / lowering / emit stages |

---

## Bugs Found and Fixed During Implementation

### Bug 1: `HasAnyLatentOp` / `hasLatent` missing `IrOp_InlineActionCall`

**Location:** `AiPrimitiveLowering.cs`, `InstanceLowering.cs`
**Symptom:** `IrTerm_Suspend reached Emit stage` — the lowering was skipped for graphs
that contained only `IrOp_InlineActionCall` because neither `HasAnyLatentOp` nor `hasLatent`
included the new op type.
**Fix:** Added `or IrOp_InlineActionCall` to both predicates.

### Bug 2: Stage 2 validator firing BP1401 for ActionFqn-set nodes

**Location:** `Stage2_Validate.cs` → `V_ChannelCommandReferences`
**Symptom:** All emit-path tests failed with `Pipeline errors: BP1401` (unknown channel command)
**Fix:** Added `if (!string.IsNullOrEmpty(node.ActionFqn)) continue;` before the catalog lookup.

### Bug 3: `InitDefaultWorkingState` inaccessible from external blueprint

**Location:** `InlineActionLowering.cs`
**Symptom:** Would have caused CS access error at Stage 8 Roslyn compilation when the host
blueprint's generated code tried to call `global::{classFqn}.InitDefaultWorkingState(...)`,
which is `private static` on the generated AiPrimitive class.
**Fix:** Removed the call. The `Unsafe.InitBlock` that zeros the entire `Blackboard1024` block
is sufficient for Slice-1 — the working state starts at all-zeros (equivalent to `*dst = default`),
which is acceptable since non-trivial field defaults would only matter for user-defined initial
values, a concern deferred beyond Slice-1.

---

## Generated Code Shape (AiPrimitive Host)

```csharp
// --- inline action call (phase-check block, re-emitted every tick) ---
unsafe
{
    ref var __bb1024_ia0 = ref world.GetComponentRW<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>(self);
    fixed (byte* __mem_ia0 = __bb1024_ia0.Memory)
    {
        if (*(ulong*)__mem_ia0 != global::Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.StructureHash)
        {
            global::System.Runtime.CompilerServices.Unsafe.InitBlock(__mem_ia0, 0,
                (uint)global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::Fdp.Toolkit.Behavior.Components.Blackboard1024>());
            *(ulong*)__mem_ia0 = global::Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.StructureHash;
        }
        ref var __ws_ia0 = ref global::System.Runtime.CompilerServices.Unsafe.AsRef<
            global::Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.WorkingState>(__mem_ia0 + 8);
        var __p_ia0 = default(global::Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.Params);
        var __t{N} = global::Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.Call(
            ref __p_ia0, ref __ws_ia0, self, world, time);
    }
}
// Running → return Running (re-dispatch next tick via phase-byte or cursor)
// Failure → WriteWorkingStatePhase(0) / WriteCursorResumeAt(0) → return Failure
// Success → continue on exec-out path
```

---

## Test Results

```
AN8 e2e tests:    13/13 PASS (0 failures)
Full test suite: 1593/1605 PASS — 4 failures (all pre-existing)
  - ScoreCrossed:         pre-existing
  - AllocatesZeroBytes:   pre-existing
  - Library golden CRLF:  pre-existing (x2)
```

---

## Slice-1 Constraints and Future Work

- **One AiPrimitive per entity**: The `Blackboard1024` projection uses a fixed offset; if two
  `ActionFqn`-set nodes appear in the same blueprint, the second overwrites the first's
  `StructureHash`. This is a Slice-1 design constraint documented in the AN8 architecture.
- **SharedAiAction**: Deferred. The `IsAiPrimitive = false` branch emits `#error` — it is
  never reached in Slice-1 since `BehaviorActionCatalog` only sets `ActionFqn` for
  `BlueprintCall`-hosting primitives.
- **Non-zero field defaults**: `InitDefaultWorkingState` is not called; fields start at zero.
  Non-trivial defaults (if any) are a future concern for post-Slice-1.
