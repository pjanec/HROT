# BP-2 REWORK: re-enable pin rehydration + fix the Stage 6/7 emit debt it exposes
**Why the first BP-2 "failed":** wiring `Stage0_Rehydrate` connected previously-disconnected projection-only
graphs, which made Stage 6/7 emit code that **does not compile** → ~20 Compile-path test regressions
(suite 8→27). Architect-confirmed + lead-verified against code: those emit errors are **pre-existing Stage 6/7
technical debt**, not a rehydration bug. So this rework = **fix the emit debt + re-enable Stage0**, then make the
~20 tests pass.

## Verified facts (cite; re-verify before changing)
- `StatementEmitter.cs:73-76`: `case IrOp_PureCall op: var call = $"global::{op.MethodFqn}({argList})";` — emits
  EVERY pure call as `global::{MethodFqn}(...)`. For the synthesized comparison ops this is invalid C#.
- `WaitLowering_AiPrimitive.cs` / `WaitLowering_Instance.cs` synthesize `IrOp_PureCall` with method names:
  `op_Eq_Byte`, `op_Eq_UInt32`, `op_Eq_NodeStatus`, `op_LessThan_Single` (grep both files for the full set).
  These must become C# infix operators, e.g. `op_Eq_*(a,b)` → `(a == b)`, `op_LessThan_*(a,b)` → `(a < b)`.
- `BuiltInChannelCommandCatalog.cs:7-16`: action names are short (`"MoveTo"`), component type is already an FQN
  (`"Fdp.Toolkit.Behavior.Components.LocomotionChannel"`). The emit of channel commands (Stage6
  `ChannelCommandLowering` + Stage7) references `global::{ChannelComponentTypeFqn}` / an action-id; the failing
  emit shows `LocomotionChannel` and `MoveTo` UNQUALIFIED → fix the qualification.
- `NodeStatus.Running` etc. emitted unqualified → must be fully qualified (`global::Hrot.Blueprints.Core.Assets.NodeStatus.Running`,
  or the mapped `Fbt.NodeStatus`). Find the emit site and qualify.
- `Stage0_Rehydrate` is currently DISABLED at `BlueprintCompiler.cs:28` (commented). The registry +
  `Stage0_Rehydrate.cs` from the first attempt are present.

## Tasks (sequence; build+test after each)

### Task 1 — Stage 7 `StatementEmitter`: translate comparison pure-calls to infix operators
In `StatementEmitter.cs` `IrOp_PureCall` handling: intercept the synthesized operator method names and emit
native C# instead of `global::op_Eq_Byte(...)`. Map ALL the `op_<Op>_<Type>` names produced by the lowering
stages (enumerate them from `WaitLowering_*` + anywhere else `IrOp_PureCall("op_...")` is created):
`op_Eq_* → (a == b)`, `op_NotEq_* → (a != b)`, `op_LessThan_* → (a < b)`, `op_LessThanOrEqual_* → (a <= b)`,
`op_GreaterThan_* → (a > b)`, `op_GreaterThanOrEqual_* → (a >= b)` (include whichever actually exist). Keep
genuine FQN method calls (real `MethodFqn` like `BlueprintMath.Add`) emitting as `global::{MethodFqn}(...)`.
Use a clear predicate (e.g. name starts with `op_` and matches the operator table) so real methods are unaffected.

### Task 2 — Channel-command + NodeStatus FQN qualification
Find the Stage6 `ChannelCommandLowering` (and the Stage7 site that emits the channel write) and ensure the
component type and action id are emitted **fully qualified** (component type FQN comes from the catalog entry;
the action constant must resolve — qualify it, do not emit a bare `MoveTo`). Qualify `NodeStatus.*` emissions.
The goal: the emitted C# for a connected channel-command/AiPrimitive blueprint compiles.

### Task 3 — Re-enable Stage0 rehydration + confirm pin correctness
Uncomment `Stage0_Rehydrate.Run(asset, options);` at `BlueprintCompiler.cs:28`. Confirm the rehydration
produces correct pins (data + exec; types from `BuiltInNodeRegistry` static + `IChannelCommandCatalog.ParamsTypeFqn`
for channel commands). If, after Tasks 1-2, any Compile-path test STILL fails due to wrong pin types/structure
(not the emit bugs), fix the rehydration (registry shapes / dynamic derivation). NO-SWALLOW: a node that can't be
rehydrated logs a diagnostic + exec-only fallback (no silent pinless nodes).

## Success Criteria — THE GATE (be exact; do NOT claim "0 regressions" loosely)
- [ ] These previously-passing Compile-path tests ALL pass again (they were the ~20 regressions):
      `RecipeIntegrityTests` (CoverAwarePatrol, SquadState, MoveAndFireCombo, HealthThresholdReaction,
      SquadAwareEngagement), `CoverAwarePatrolEndToEndTest` (4), `AlcLifecycleTests` (2), `FailureRollbackTests`,
      `QuickReloadTests` (2), `RegistrarInjectionTests`, `PdbLoadTests`, `AiPrimitiveReloadTests`,
      `MoveToAndFireDemoTests` (ALC_Reclaimed, MultipleReloads, Tick1_ReturnsRunning), `AllocationFreeTests`.
- [ ] Keystone: `CountingDemo_PinsStripped_ProofTests` (2) pass (Count→5 with pins stripped) AND
      `CountingDemo_ProofTests` (2) pass. The `Stage0_RehydrateTests` unit tests pass.
- [ ] The ONLY remaining failures are the **pre-existing DEBT-006 golden/snapshot** set
      (`AiPrimitiveEmitGoldenTests` MoveToAndFire/HasVisibleTarget, `LibraryEmitGoldenTests`,
      `LibraryMathDemoTests` snapshot, `MoveToAndFireDemoTests.*_GeneratedSource_Snapshot`,
      `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`). Confirm these are
      UNCHANGED (do not newly break them; do not silently regenerate goldens). Target: **≤7 failures, all
      pre-existing DEBT-006**. List the exact final failure set in the report and justify each as pre-existing.
- [ ] `dotnet build IOS-IG-SimHost.sln -c Debug` 0 errors / 0 new warnings (Count2.bp.json now under Blueprints/
      compiles AND — bonus — its generated blueprint is now connected). Report exact counts.
- [ ] Report → `.dev/blueprint-compile-fix/BP-2-REWORK-REPORT.md`.

## Report Requirements
The exact op_* operator set you mapped + the StatementEmitter predicate; the channel/NodeStatus qualification
fix sites; confirmation Stage0 re-enabled; whether any rehydration type fix was needed; the FULL final failure
list (must be only pre-existing DEBT-006) with a one-line justification each; exact build/test counts; weak
points; suggested commit message. NO comprehension questions. Do NOT claim 0 regressions without the explicit
before/after failure-set comparison.

## Constraints
Branch `blueprint-integ-1`. Do NOT regenerate/modify golden snapshot files to "fix" DEBT-006 (out of scope —
leave them failing as before). Do NOT touch the user's WIP (RecipeCreateModal.cs, AssetBrowserWindow.cs,
EditorSubsystem.cs). Compiler stays netstandard2.0-compatible. Do NOT commit (the lead commits). If the running
editor locks dlls, report it.
