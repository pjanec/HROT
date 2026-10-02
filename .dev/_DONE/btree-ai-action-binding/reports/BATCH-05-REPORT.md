# BATCH-05 Report (reconstructed by lead — coder sub-agent terminated on an org-auth error before writing its own report; lead verified all results directly)

## Tasks
- **S1-G demo gate** (Task 1): demo nodes + T10/T11 assets + proof tests — DONE, all green.
- **Live inspector wiring** (Task 2): DEBT-AIB-012 (multi-DTO projection) DONE & wired; DEBT-AIB-009 (hardcoded-DTO reflection) plumbed but NOT wired in production DI (see debt).

## What landed
- `DemoCounterNodes.cs`: added `DemoAccumParams {int Sum; int Step}` + `[BTreeAction] Action_AddStepToSum`.
- `T10_MultiAction.btree.json`: managed BB with two distinct struct-DTO variables — `counter : DemoCounterNodes+DemoCounterParams` (offset 0) and `accum : DemoCounterNodes+DemoAccumParams` (offset 8). Tree = Sequence[ Condition_CounterBelowThreshold(counter), Action_IncrementCounter(counter) + Repeater(1) pill, Action_AddStepToSum(accum) ].
- `T11_Aliasing.btree.json`: two `Action_IncrementCounter` nodes both bound to `counter` (aliasing).
- `BTreeBuilder.cs` (FBT kernel): added `Action(string methodKey,…)` / `Condition(string methodKey,…)` string-key builder overloads — **required**: BATCH-02's managed topology emits `.Action("{blobKey}",…)` but that overload never existed (BATCH-02 only string-tested the emit, never compiled the generated topology). S1-G's end-to-end compile surfaced and closed the gap. Additive, backward-compatible.
- `BehaviorRegistry.cs`: added `ManagedBlackboardVariable(Name,Type,ByteOffset)` record + `BehaviorDefinition.ManagedBlackboardVariables` (additive, nullable).
- `BTreeBridgeEmitCore.cs`: emits the `ManagedBlackboardVariables[]` initializer (name/type/offset from the same packer) into the registrar's `BehaviorDefinition`.
- `BTreeEmitCore.cs`: struct name now falls back to `{AssetName}Blackboard` when `Blackboard.TypeName` is empty (T10/T11 have empty TypeName → avoids CS0101 collisions).
- `BrainBlackboardRenderer.cs`: `RenderTypedDtoAtOffset` loops `ManagedBlackboardVariables` and projects each DTO at its `ByteOffset` (`Marshal.PtrToStructure((IntPtr)(bb.BehaviorParameters + byteOffset), type)`); falls back to offset-0 behavior when the list is null (no regression).
- `BlackboardAuthoringWindow.cs`: render path now passes the (optional) `IActionSchemaExporter` into `BuildViewModel`.

## Defaults (DEBT-AIB-013)
Managed-asset variable defaults (`DefaultValueJson`) are NOT auto-written into `BehaviorParameters` at assignment (no generated `ParseParams` for managed assets). The proof tests therefore seed `Threshold`/`Step` manually before ticking. Recorded as DEBT-AIB-013.

## Verification (by lead)
- `Hrot.AI.Behaviors` clean rebuild: 0 errors (T10/T11 codegen, no BTREE0002 → bindings validated).
- `Hrot.AiEditor.Generators.Tests`: 74/2 (the 2 = known `MigrationEquivalenceTests`); the 3 proof tests pass.
- `Hrot.AiEditor.Persistence.Tests` byte-identity: 129/0.
- `Hrot.Presentation.Tests` (BrainBlackboardRenderer): 6/0. `Hrot.Editor.AiShared.Tests`: 1101/0. `Hrot.BTree.Editor.Tests`: 561/0.

Proof tests (real generate→compile→register→tick, byte-level assertions):
- `MultiAction_AfterNTicks_CounterReachesThresholdThenConditionFails` — Counter climbs to Threshold then the bound condition fails (Sequence aborts).
- `MultiAction_SecondDtoMutatesIndependently` — accum.Sum advances by Step at offset 8; counter DTO bytes untouched (and vice-versa) — no cross-talk.
- `Aliasing_TwoNodesShareOneVariable` — two nodes → Counter +2/tick; raw byte read-back confirms shared slice.

## Manual visual check for the user (S1-G "Live" gate)
1. Open the editor, load **T10_MultiAction** in the BTree perspective.
2. Blackboard Variables panel: confirm `counter` (DemoCounterParams) and `accum` (DemoAccumParams) appear as managed variables.
3. Attach/run the behavior on an entity; in the blackboard inspector (`BrainBlackboardRenderer`) confirm BOTH DTOs render — `counter` at offset 0 and `accum` at offset 8 (not just offset 0). This exercises DEBT-AIB-012.
4. In the BTree visualizer, confirm the active node highlights as the Sequence ticks (Condition → Increment(Repeater) → AddStepToSum).
5. Known gap: read-only reflection of *hardcoded* (non-managed) Category-1 DTOs (DEBT-AIB-009) will NOT show yet — no `IActionSchemaExporter` is injected in production DI. Not required for the T10 demo (its DTOs are managed variables).
