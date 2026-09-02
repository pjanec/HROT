# AN8b Report — `[SharedAiAction]` Direct-Invocation Lowering

**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-07  
**Status:** COMPLETE — build 0 CS errors, 0 new test failures

---

## 1. IsAiPrimitive Discriminator

**Problem found:** Stage 5 hardcoded `bool isAiPrimitive = true; // Slice-1: only AiPrimitive path implemented.`

**Fix applied in** `Stage5_Schedule.cs` (`ScheduleInlineActionNode`):

```csharp
// AiPrimitive generated classes follow the "{Name}_{Id:X8}_Bp.Call" pattern,
// so their ActionFqn always ends with "_Bp.Call".
// [SharedAiAction] methods are direct static method FQNs and do NOT end with "_Bp.Call".
bool isAiPrimitive = actionFqn.EndsWith("_Bp.Call", StringComparison.Ordinal);
```

**Basis:** The generated AiPrimitive class naming convention produces FQNs like
`Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.Call` — the `_Bp.Call` suffix is
structurally unambiguous. `[SharedAiAction]` FQNs (`Fdp.Toolkit.Behavior.Demo.DemoSharedActions.AlertNearbyUnits`)
never match this pattern.

**Test coverage:** `SharedAiAction_Stage5_SchedulesInlineActionCall_IsAiPrimitiveFalse_AN8b` and
`AiPrimitiveFqn_Stage5_SchedulesInlineActionCall_IsAiPrimitiveTrue_AN8b` (regression).

---

## 2. Direct-Call Emit Shape

The non-AiPrimitive branch in `InlineActionLowering.Emit()` (previously `#error`) now emits:

```csharp
// paramsFqn = "Fdp.Toolkit.Behavior.Demo.DemoSharedActionParams"
// op.ActionFqn = "Fdp.Toolkit.Behavior.Demo.DemoSharedActions.AlertNearbyUnits"
var __p_0 = default(global::Fdp.Toolkit.Behavior.Demo.DemoSharedActionParams);
// (or with pin fields:)
// var __p_0 = new global::Fdp.Toolkit.Behavior.Demo.DemoSharedActionParams
// {
//     AlertRadius = __t3,
//     PostureHint = __t4,
//     MaxUnits = __t5,
// };
var __t6 = global::Fdp.Toolkit.Behavior.Demo.DemoSharedActions.AlertNearbyUnits(ref __p_0, self, world);
```

Key differences from AiPrimitive path:
- NO `unsafe {}` block
- NO `Blackboard1024` projection
- NO `StructureHash` check
- NO `ref WorkingState __ws_` parameter
- NO `time` parameter
- Direct `global::{ActionFqn}(ref __p_{n}, self, {worldVar})` call

---

## 3. Params and self/world Sourcing

**Params DTO:** Built from the node's data-IN pins by the shared `EmitParamsLocal` helper (same
as the AiPrimitive path). `ParamFields` is the ordered list of `(FieldName, IrValue)` pairs
collected from `ChannelCommandNode.Pins` in Stage 5. `ParamsTypeFqn` is normalised from `+`
to `.` for C# syntax.

**`self`:** The literal `self` identifier — present as a parameter in both `TickCore` (AiPrimitive)
and the Instance blueprint's state machine method. Confirmed via `AiPrimitiveEmitter.cs` which
declares `global::Fdp.Core.Entity self` as a method parameter.

**`world`:** Sourced from `e.Ctx.WorldVar` which resolves to `"world"` for AiPrimitive dispatch
and `"((global::Fdp.Core.EntityRepository)view)"` for Instance dispatch. Both paths apply.

**classFqn** (used as fallback in `EmitParamsLocal`): for SharedAiAction, `classFqn` is the
declaring type (e.g. `Fdp.Toolkit.Behavior.Demo.DemoSharedActions`), obtained by splitting
`ActionFqn` at the last dot — same split already done at the top of `Emit()`.

---

## 4. Latent Machinery Reuse

The Stage 5 block-split (`ScheduleLatentNode`) and Stage 6 `WaitLowering_AiPrimitive` /
`WaitLowering_Instance` are **unchanged and fully reused** for the SharedAiAction path.

- `ScheduleLatentNode` treats the `IrOp_InlineActionCall` op as a latent op regardless of `IsAiPrimitive`, emitting it as a statement then splitting the block.
- `WaitLowering_AiPrimitive` lines 200–267 handle `IrOp_InlineActionCall`: they re-emit the **same** `IrOp_InlineActionCall` (with `IsAiPrimitive=false`) in the synthesized check block on every tick until non-Running.
- On each tick the check block re-runs `EmitParamsLocal` from the same pins → DTO is rebuilt from scratch (stateless, correct for `[SharedAiAction]`).
- Phase state machine (`ws.__phase`) tracks the suspend/resume slot for Running → re-dispatch; Success/Failure route to the resume block or failure block.

`WaitLowering_Instance` has an identical `IrOp_InlineActionCall` branch at line 186 — also reused unchanged.

---

## 5. E2E Tests Added

New test class `SharedAiAction_EndToEndTests` in
`Hrot.Blueprints.Tests/Compiler/EndToEnd/InlineAction_EndToEndTests.cs`:

| Test | Coverage |
|------|----------|
| `SharedAiAction_Stage5_SchedulesInlineActionCall_IsAiPrimitiveFalse_AN8b` | Discriminator: SharedAiAction FQN → `IsAiPrimitive=false` |
| `AiPrimitiveFqn_Stage5_SchedulesInlineActionCall_IsAiPrimitiveTrue_AN8b` | Regression: AiPrimitive FQN → `IsAiPrimitive=true` |
| `SharedAiAction_AiPrimitive_NoSuspendAfterLowering_AN8b` | Stage 6 removes `IrTerm_Suspend` (AiPrimitive host) |
| `SharedAiAction_Instance_NoSuspendAfterLowering_AN8b` | Stage 6 removes `IrTerm_Suspend` (Instance host) |
| `SharedAiAction_AiPrimitive_CompileSucceeds_NoDiagnosticsNoHashError_AN8b` | Primary contract: no `#error`, no pipeline errors |
| `SharedAiAction_AiPrimitive_EmittedSource_ContainsDirectMethodCall_AN8b` | `global::Fdp...DemoSharedActions.AlertNearbyUnits(` in source |
| `SharedAiAction_AiPrimitive_EmittedSource_ContainsParamsDto_AN8b` | `DemoSharedActionParams` in source |
| `SharedAiAction_AiPrimitive_EmittedSource_ContainsNodeStatusRouting_AN8b` | `NodeStatus.Running` + `NodeStatus.Failure` |
| `SharedAiAction_AiPrimitive_EmittedSource_NoWorkingStateProjectionAtCallSite_AN8b` | No `, ref __ws_` (no per-action WS) |
| `SharedAiAction_AiPrimitive_EmittedSource_ContainsPhaseField_AN8b` | `ws.__phase` (inline-latent machinery active) |
| `SharedAiAction_Instance_CompileSucceeds_AN8b` | Instance path: direct call + `ResumeAt` cursor |

---

## 6. Build / Test Results

```
dotnet build Hrot.Blueprints.Compiler   → 0 errors, 0 warnings
dotnet build Hrot.Blueprints.Tests      → 0 errors, 8 pre-existing warnings

dotnet test Hrot.Blueprints.Tests (full suite):
  Passed:  1614
  Failed:     4  (ALL pre-existing)
  Skipped:    8
  Total:   1626

Pre-existing failures (unchanged from before AN8b):
  - LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource        (golden CRLF flake)
  - ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold
  - AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes
  - LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot      (golden CRLF flake)

New AN8b tests (InlineAction_EndToEnd + SharedAiAction_EndToEnd):
  Passed:  24 / 24
```

---

## 7. Files Changed

| File | Change |
|------|--------|
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` | Fix `isAiPrimitive` discriminator: `actionFqn.EndsWith("_Bp.Call")` |
| `Hrot.Blueprints.Compiler/Compiler/Emit/InlineActionLowering.cs` | Replace `#error` with AN8b direct-call emit; update class doc |
| `Hrot.Blueprints.Tests/Compiler/EndToEnd/InlineAction_EndToEndTests.cs` | Add `SharedAiAction_EndToEndTests` class (11 new tests) |

---

## 8. STOP Items

None. All contracts satisfied. No goldens regenerated (no new golden test). The AN8 AiPrimitive path is fully regression-tested and unaffected.
