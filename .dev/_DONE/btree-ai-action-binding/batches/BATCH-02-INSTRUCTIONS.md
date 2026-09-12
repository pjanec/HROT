# BATCH-02: Per-asset blackboard struct + baked-offset registrar + validator unblock
**Tasks:** S1-2, S1-3, S1-4   **Phase:** Slice 1   **Est:** ~18h
**Dependencies:** BATCH-01 (S1-0 `bool [MarshalAs(I1)]` — landed). These three tasks **MUST land together** (S1-4 opens the `ThreeParamReusable` gate; without S1-2/S1-3's per-asset struct + registrar that turns clean skips into hard build breaks — AIB-DD §3.3 / SLICE1-DESIGN §3.3).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD") §3.2, §3.3 — the spec.
3. `.dev/_DONE/btree-ai-action-binding/SLICE1-DESIGN.md` §2 (ground truth), §3.2, §3.3.
4. `.dev/_DONE/btree-ai-action-binding/TASK-DETAIL.md` §S1-2, §S1-3, §S1-4 — **the exact named test specs + assertions you must implement. Do NOT invent acceptance criteria.**
5. `.dev/_DONE/btree-ai-action-binding/reviews/BATCH-01-REVIEW.md` — context (no corrective tasks).

Use codebase-memory MCP graph tools FIRST for exploration (`.claude/CLAUDE.md`). `read_file` only for exact edit targets.

**Complete tasks in sequence; do NOT start the next task until the current task's implementation is done, its tests are written, and ALL tests (including prior batches') pass.**

## Pipeline map (verified — use these insertion points; do not re-derive)
- **`Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs`** `GenerateOneAsset()` (lines 57–135): deserialize DTO (l.64) → validate (l.80–100) → `BTreeEmitCore.EmitTopologyCore(dto)` (l.106), registered `{Name}.g.cs` (l.119) → `BTreeBridgeEmitCore.EmitBridge(dto)` (l.125), registered `{Name}.Registrar.g.cs` (l.134). **No per-asset struct is emitted today.**
- **`BehaviorTreeAssetDto.Blackboard`** (`Hrot.AiEditor.Persistence/BTree/BehaviorTreeAssetDto.cs`): `BlackboardBlockDto { bool Managed; string TypeName; string? HeavyDtoType; List<BlackboardVariableDto> Variables }`.
- **`BlackboardBinPacker.Pack(masterVars, aggregatedVars?)`** → `PackResult.PackedVariables : IReadOnlyList<PackedVariable{ ByteOffset, Tier, ByteSize }>`; packer reorders (largest-alignment-first). (`Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs`)
- **`BlackboardDtoEmitter.Emit(BlackboardDtoModel)`** — emits the `[StructLayout(Sequential)]` struct; bool already carries `[MarshalAs(I1)]` (BATCH-01).
- **Reference thunk to mirror:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs:689-698` — `registry.Register("{Type}.{Method}@{offset}", static (ref TBB bb, ref BehaviorTreeState _, ref TCtx ctx, int _) => { ref var f = ref Unsafe.As<byte,FieldType>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters,(nint){offset})); return global::{FQN}(ref f, ctx.Self, ctx.World); });`. CompoundKey format `{Type.FullName}.{Method}@{offset}` (l.334).
- **Bridge today:** `BTreeBridgeEmitCore.EmitBTreeRegisterMethod()` (l.78–149) registers **stub fallbacks** keyed by bare FQN (`RegisterAction(..., =>NodeStatus.Success)` l.125; `RegisterCondition(..., =>true)` l.141). Real delegates arrive via the injected `ActionRegistry<BrainBlackboard,BTreeContext>` (populated from `[FbtRegistrar]`).
- **Validator:** `Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs` `CheckPayload(...)` — currently rejects `BTreeDelegateShapeDto.ThreeParamReusable` outright (the `TODO VE-DEBT-002` early-return, ~line 149).
- **Demo nodes:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/DemoCounterNodes.cs` — `DemoCounterParams{int Counter; int Threshold}`, `[BTreeCondition] Condition_CounterBelowThreshold`, `[BTreeAction] Action_IncrementCounter` (3-param reusable shape).
- **Tests live in:** `Hrot.AiEditor.Generators.Tests` (`Generator/BTreeJsonGeneratorTests.cs`, `Bridge/BlueprintRegistrarBridgeIntegrationTests.cs`, `Bridge/BTreeActionRegistryFactoryTests.cs`). Runtime tests: `Fdp.Toolkits.Tests/Behavior` (or a new behaviors runtime test).

---

## Task 1: Per-asset struct + topology-over-struct (S1-2)
**Spec:** AIB-DD §3.2; SLICE1-DESIGN §3.2. **Touches:** `BTreeJsonGenerator`, `Hrot.AiEditor.Persistence/Emit/*`, reuse `BlackboardDtoEmitter` + `BlackboardBinPacker`.

For `dto.Blackboard.Managed == true` assets only (guard everything behind this; `Managed==false` must emit **byte-identically** to today):
1. In `GenerateOneAsset` (after the topology emit), build a `BlackboardDtoModel` from `dto.Blackboard.Variables`, run `BlackboardBinPacker.Pack(...)`, and emit a per-asset `[StructLayout(Sequential)]` struct via `BlackboardDtoEmitter.Emit`. **Critical ordering invariant:** emit the struct's fields in the **same order** the packer placed them (packed order), so the compiled struct's `Marshal.OffsetOf(field)` equals the packer's `ByteOffset` for that field. (If you emit author-declaration order but key off packed offsets — or vice-versa — offsets silently diverge. Keep ONE order; assert it in the test.) Register as `{Name}.Blackboard.cs`. Total must be ≤100 B (inline region).
2. Emit the topology **over that struct** so each binding's blob `MethodNames` key is `{Type}.{Method}@{offset}` with `offset` = that variable's packed `ByteOffset` (NOT `@0` for non-first variables). Where the builder/topology currently produces the key, thread the per-variable offset through.

**Tests required (`Hrot.AiEditor.Generators.Tests`), exactly as named in TASK-DETAIL §S1-2:**
- `ManagedAsset_GeneratesStruct_OffsetsMatchBinPacker` — fixture asset with variables `{int A; Vector3 B; bool C}`: assert each field's `Marshal.OffsetOf` (compile the emitted struct, or compare against the packer) equals the `BlackboardBinPacker` offset, total ≤100 B, and `bool C` carries `[MarshalAs(I1)]`.
- `ManagedAsset_TopologyBuiltOverGeneratedStruct_BlobKeysCarryOffsets` — the generated topology's blob `MethodNames` contain `{Type}.{Method}@{offset}` keys whose offsets equal the generated struct's field offsets (assert a non-first variable's key is NOT `@0`).
- `ManagedAsset_MasterDtoOver100Bytes_HardErrors` — an asset whose aggregate DTOs exceed 100 B yields `FDP_001` or a `BTREE0002` skip diagnostic (not silent overflow).

**Build gate:** `dotnet build-server shutdown` then clean rebuild of `Hrot/Subsystems/Hrot.AI.Behaviors` = 0 errors; **byte-identity gate green** (`Hrot.AiEditor.Persistence.Tests` — `Managed==false` assets emit unchanged).

## Task 2: Baked-offset registrar + adapter (S1-3)
**Spec:** AIB-DD §3.2 ("BTree owns layout, blueprint provides `TickCore`"); SLICE1-DESIGN §3.2. **Touches:** `BTreeBridgeEmitCore` / per-asset registrar emission.

For managed assets, replace the stub-fallback registrations with real per-(method, variable) thunks: emit, keyed `{Type}.{Method}@{offset}` (same offset as the blob key from Task 1), a `ref BrainBlackboard` thunk that `Unsafe.As`/`Unsafe.AddByteOffset`-projects the DTO at its baked offset and calls the method — mirroring `BTreeActionGenerator.cs:689-698`. Register into the **injected** `ActionRegistry<BrainBlackboard,BTreeContext>` (do not `new` one). For a blueprint AiPrimitive action, emit a per-node adapter that calls the blueprint's `TickCore` at the BTree-controlled offset (ignore the blueprint's standalone `BTreeTick`). Non-managed assets keep current behavior.

**Tests required (runtime — `Fdp.Toolkits.Tests/Behavior` or a new behaviors runtime test), exactly as named in TASK-DETAIL §S1-3:**
- `MultiAction_DistinctDtos_ProjectAtDistinctOffsets` — managed asset binds two stateless actions over two variables of **different** DTO types at distinct offsets; build the interpreter as the bridge does (inject the populated registry); tick; assert each action mutates **only** its own DTO (offset O1 increments while DTO at O2 is untouched, and vice-versa) — no cross-talk. This is the gold-standard runtime assertion; do NOT settle for a key-string presence check.
- `MultiAction_BoundConditionGates` — a condition bound to variable V returns `Failure` once its threshold is met, halting the `Sequence`.

**Build gate:** clean rebuild 0 errors; `Hrot.AiEditor.Generators.Tests` green (the 2 known `MigrationEquivalenceTests` byte-stability cases are pre-existing failures — do not chase, count them out).

## Task 3: Validator unblock ThreeParamReusable (S1-4)
**Spec:** AIB-DD §3.3; SLICE1-DESIGN §3.3. **Touches:** `BTreeMethodCompatibilityValidator.cs` (`CheckPayload`, the `ThreeParamReusable` early-return ~line 149). **MUST ship with Tasks 1+2.**

Replace the outright rejection: accept `ThreeParamReusable` iff (a) the method has the 3-param reusable shape (`ref TDto`, `ref BehaviorTreeState`, `ref TCtx`, returns `Fbt.NodeStatus`) AND (b) `ExpressionTargetField` resolves to an authored variable whose declared type (FQN string equality) equals param-0's `TDto`. Otherwise emit a `BTREE0002` skip reason (never a build break).

**Tests required (`Hrot.AiEditor.Generators.Tests`), exactly as named in TASK-DETAIL §S1-4:**
- `ThreeParamReusable_TypeMatched_Validates` — valid binding ⇒ `Validate`/`CheckPayload` returns null (asset emitted).
- `ThreeParamReusable_TypeMismatch_SkipsWithBtree0002` — param-0 type ≠ variable type ⇒ non-null reason / BTREE0002, asset skipped, build still succeeds.
- `ThreeParamReusable_MissingExpressionTargetField_Skips` — no target ⇒ skip.

---

## Success Criteria
- [ ] S1-2: per-asset struct + topology-over-struct emitted for managed assets; all 3 named tests pass; byte-identity gate green; clean rebuild 0 errors.
- [ ] S1-3: real baked-offset thunks registered; both runtime cross-talk/gating tests pass.
- [ ] S1-4: validator accepts type-matched `ThreeParamReusable`, skips mismatches with `BTREE0002`; all 3 named tests pass; building `Hrot.AI.Behaviors` with a `ThreeParamReusable` demo asset succeeds.
- [ ] Full relevant suites green (0 net-new failures; the 2 `MigrationEquivalenceTests` cases excepted); report submitted.

Run all tests and fix root causes to completion **without asking permission**. Only stop on a breaking design↔codebase contradiction (describe it in the report).

## Report Requirements (`.dev/_DONE/btree-ai-action-binding/reports/BATCH-02-REPORT.md`)
Answer: the exact field-ordering decision (packed order vs author order) and how you guaranteed `Marshal.OffsetOf == packer offset`; how the blob key offset and registrar key offset are kept identical (single source of truth?); whether `[BTreeAction]`/`[BTreeCondition]` demo methods get registry entries via the existing path or needed new emission; how you built the interpreter in the runtime test; any FDP_001 vs BTREE0002 decision for the over-100B case; weak points; edge cases; suggested commit message. Do NOT ask comprehension questions.
