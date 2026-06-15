# TASK-DETAIL — BTree AI Action/Condition Parameter Binding

> Per-task scope + **explicit success conditions** (build gates + named test specs). Tracker: [TASK-TRACKER.md](./TASK-TRACKER.md). Design of record: `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD"); drafts [SLICE1-DESIGN.md](./SLICE1-DESIGN.md) / [SLICE2-DESIGN.md](./SLICE2-DESIGN.md).
>
> **Global rules (every task):** `dotnet build-server shutdown` before codegen verification; all emit changes guarded behind `Managed==true`; the byte-identity gate (`Hrot.AiEditor.Persistence.Tests` — CombatShowcase/SampleScout) stays green; hard-verify diffs; never weaken a test to pass. Pre-existing unrelated failures that are NOT regressions: the 2 `MigrationEquivalenceTests` JSON byte-stability cases in `Hrot.AiEditor.Generators.Tests` (see DEBT-TRACKER).
> **Test-spec rule:** implement the named tests with the stated assertions exactly; do not substitute your own acceptance criteria.

---

## S1-0 — bool MarshalAs fix
**Design:** AIB-DD §3.2 (the `bool` 4-byte-`BOOL` trap). **Touches:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs`.
**Scope:** emit `[MarshalAs(UnmanagedType.I1)]` on every `bool` field of generated blackboard DTO structs (today it emits bare `bool`). Confirm the bin-packer's 1-byte `bool` assumption (`BlackboardBinPacker.cs:255`) matches the emitted struct's `Marshal.OffsetOf`.
**Success conditions:**
- Build: `Hrot.Editor.AiShared` builds 0 errors.
- Test (`Hrot.Editor.AiShared.Tests`, new in `BlackboardDtoEmitterTests`): `Emit_BoolField_CarriesMarshalAsI1` — emit a struct with fields `{int A; bool B; int C}`; assert the generated source contains `[MarshalAs(UnmanagedType.I1)]` immediately before `B`, and that `Marshal.OffsetOf` of `C` equals the `BlackboardBinPacker` offset for `C` (i.e. `B` occupies 1 byte, not 4). Compile the emitted struct in-test (Roslyn) and assert `Marshal.SizeOf` matches the packer's total.
**Done when:** the above passes; existing `BlackboardDtoEmitterTests` stay green.

## S1-1 — Category-1 DTO reflection in Variables panel
**Design:** AIB-DD §3.1; SLICE1-DESIGN §3.1, §9 (Q6). **Touches:** `Hrot.Editor.AiShared` (Variables panel / `ActionSchemaExporter` consumers); read-only rendering in `VariablesPanelControl`.
**Scope:** when a node binds a hardcoded action whose DTO is not an editor-managed variable, reflect that DTO's fields **read-only** in the panel. Use `ActionSchemaExporter` (first `ref` param type → `ActionSchemaEntry.DtoType`). Editor-only; no codegen.
**Success conditions:**
- Build: `Hrot.Editor.AiShared` + `Hrot.BTree.Editor` build 0 errors.
- Test (`Hrot.Editor.AiShared.Tests`): `ActionSchema_ReflectsFirstRefParamDto` — for a registered action `M(ref FooDto, ref BehaviorTreeState, ref BTreeContext)`, assert `ActionSchemaExporter` yields `DtoType == typeof(FooDto)` with its public fields enumerated.
- Test: `VariablesPanel_ReflectsHardcodedDto_ReadOnly` — given an asset whose node binds such an action, the panel's view-model lists `FooDto`'s fields with `IsReadOnly == true` and they are NOT in the editable managed-variable set.
**Done when:** both tests pass; `Hrot.Editor.AiShared.Tests` shows 0 net-new failures.

## S1-2 — Per-asset struct + topology-over-struct
**Design:** AIB-DD §3.2; SLICE1-DESIGN §3.2. **Touches:** `Hrot.AiEditor.Generators/BTreeJsonGenerator`, `Hrot.AiEditor.Persistence/Emit/*`, reuse `BlackboardDtoEmitter`.
**Scope:** for `Managed==true` assets, generate a per-asset `[StructLayout(Sequential)]` blackboard struct from the authored variables (bin-packed, ≤100 B, `bool`→`[MarshalAs(I1)]` per S1-0), and emit the BTree topology **over that struct** so each binding compiles to a blob key `{Type}.{Method}@{offset}`.
**Success conditions:**
- Build: `dotnet build-server shutdown` then `dotnet build Hrot/Subsystems/Hrot.AI.Behaviors -t:Rebuild` = 0 errors; **byte-identity gate green** (`Hrot.AiEditor.Persistence.Tests` 129/0 — existing `Managed==false` assets emit unchanged).
- Test (`Hrot.AiEditor.Generators.Tests`, new): `ManagedAsset_GeneratesStruct_OffsetsMatchBinPacker` — a fixture asset with variables `{int A; Vector3 B; bool C}` generates a struct where each field's `Marshal.OffsetOf` equals the `BlackboardBinPacker` offset and total ≤100 B; `bool C` carries `[MarshalAs(I1)]`.
- Test: `ManagedAsset_TopologyBuiltOverGeneratedStruct_BlobKeysCarryOffsets` — the generated topology's `BehaviorTreeBlob.MethodNames` contain `{Type}.{Method}@{offset}` keys with offsets equal to the generated struct's field offsets (not `@0` for non-first variables).
- Negative test: `ManagedAsset_MasterDtoOver100Bytes_HardErrors` — an asset whose aggregate master DTOs exceed 100 B produces `FDP_001` (or a `BTREE0002` skip diagnostic), not a silent overflow.
**Done when:** all pass; clean rebuild green.

## S1-3 — Baked-offset registrar + adapter
**Design:** AIB-DD §3.2 (composition model "BTree owns layout, blueprint provides `TickCore`"); SLICE1-DESIGN §3.2; reuses the D14 masquerade registrar (`BTree_HSM_JSON_Persistence_Detailed_Design.md`) — same bridge PREREQ-A (`8eb45e0c`) established.
**Touches:** `BTreeBridgeEmitCore` / per-asset registrar emission in `Hrot.AiEditor.Persistence`/`Hrot.AiEditor.Generators`.
**Scope:** emit, per bound (method, variable), a `ref BrainBlackboard` thunk keyed `{Type}.{Method}@{offset}` that `Unsafe.As`/`AddByteOffset`-projects the DTO at its baked offset and calls the method; register into the injected registry. For blueprint AiPrimitive actions, emit a per-node adapter that calls the blueprint's `TickCore` at the BTree-controlled offset (ignore the blueprint's standalone `BTreeTick`).
**Success conditions:**
- Build: clean rebuild 0 errors; `Hrot.AiEditor.Generators.Tests` green (minus the 2 known MigrationEquivalence).
- Runtime test (`Fdp.Toolkits.Tests/Behavior` or a new behaviors runtime test): `MultiAction_DistinctDtos_ProjectAtDistinctOffsets` — a managed asset binds two stateless actions over two variables of different DTO types at distinct offsets; build the interpreter as the bridge does (inject the populated registry); tick; assert each action mutates **only** its own DTO (e.g. `DemoCounterParams.Counter` at offset O1 increments while a second DTO at offset O2 is untouched, and vice-versa) — no cross-talk.
- Runtime test: `MultiAction_BoundConditionGates` — a condition bound to variable V returns `Failure` once its threshold is met, halting the `Sequence`.
**Done when:** both runtime tests pass; clean rebuild green.

## S1-4 — Validator unblock ThreeParamReusable
**Design:** AIB-DD §3.3; SLICE1-DESIGN §3.3. **Touches:** `Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs:149`. **MUST land with S1-2/S1-3.**
**Scope:** accept `ThreeParamReusable` iff the method has the 3-param reusable shape AND `ExpressionTargetField` resolves to an authored variable whose declared type (FQN string equality) equals param-0's DTO type; else emit `BTREE0002` skip (never a build break).
**Success conditions:**
- Build: clean rebuild; an asset with a valid `ThreeParamReusable` binding compiles (no BTREE0002); no build break.
- Test (`Hrot.AiEditor.Generators.Tests`): `ThreeParamReusable_TypeMatched_Validates` — valid binding ⇒ `Validate` returns null (asset emitted).
- Test: `ThreeParamReusable_TypeMismatch_SkipsWithBtree0002` — param-0 type ≠ variable type ⇒ non-null reason / BTREE0002, asset skipped, build still succeeds.
- Test: `ThreeParamReusable_MissingExpressionTargetField_Skips` — no target ⇒ skip.
**Done when:** all pass; building `Hrot.AI.Behaviors` with a `ThreeParamReusable` demo asset succeeds.

## S1-5 — Field-picker + promote-to-variable
**Design:** AIB-DD §3.1; SLICE1-DESIGN §3.4; `Blackboard_Authoring_Addendum_v3` §3 (node-owned/auto-managed). **Touches:** BTree node inspector field-picker drawer; mirror existing `PromoteBindTests`/`BlackboardFieldPickerDrawerTests` in `Hrot.BTree.Editor.Tests`.
**Scope:** node-inspector picker lists only variables whose type matches the action's param-0 DTO type (type-filtered) and sets `ExpressionTargetField`; "+ promote to new variable" creates an `IsAutoManaged` variable of that DTO type and binds it.
**Success conditions:**
- Build: `Hrot.BTree.Editor` builds 0 errors.
- Test (`Hrot.BTree.Editor.Tests/Inspector`): `FieldPicker_ListsOnlyTypeMatchingVariables` — picker options exclude variables of non-matching type.
- Test: `FieldPicker_SelectsVariable_SetsExpressionTargetField` — selecting variable V sets the node payload's `ExpressionTargetField == V`.
- Test: `PromoteToNewVariable_CreatesAutoManagedAndBinds` — invoking promote creates a variable with `IsAutoManaged == true` of the action's DTO type and binds the node to it.
**Done when:** all pass; `Hrot.BTree.Editor.Tests` 0 net-new failures.

## S1-G — Slice 1 DEMO GATE
**Design:** AIB-DD §5; SLICE1-DESIGN §5. **Demonstrates:** multiple distinct-DTO actions/conditions at distinct offsets + a decorator + aliasing, observable like the blueprint `CountingDemo`.
**Deliverables:**
- Demo asset `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/T10_MultiAction.btree.json` — managed blackboard with ≥2 variables of **different** DTO types at distinct offsets (e.g. `DemoCounterParams` + a second tiny stateless DTO), a gating `Condition`, and a `Repeater` decorator on the increment. (Demo nodes: extend `DemoCounterNodes` with a second stateless action/condition if needed.)
- Demo asset `Authoring/T11_Aliasing.btree.json` — two action nodes bound to the **same** variable.
**Success conditions (the gate):**
- Build: `dotnet build-server shutdown` + clean rebuild of `Hrot.AI.Behaviors` = 0 errors; byte-identity green.
- Proof test (mirror `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/CountingDemo_ProofTests.cs`), new e.g. `T10_MultiAction_ProofTests`:
  - `MultiAction_AfterNTicks_CounterReachesThresholdThenConditionFails` — attach behavior to an entity, tick until the gate; assert the counter DTO climbs to `Threshold` then the bound condition fails (Sequence stops).
  - `MultiAction_SecondDtoMutatesIndependently` — assert the second action's DTO changes on its own offset with no effect on the counter DTO.
  - `Aliasing_TwoNodesShareOneVariable` (T11) — a write by node A is observed by node B (same byte slice; zero-copy).
- Live (manual, documented in the gate notes): the per-asset DTO fields render in `BrainBlackboardRenderer` and the active node in `BTreeVisualizerRenderer`.
**Done when:** all proof tests pass; full relevant suites green; clean rebuild green. **Slice 1 complete.**

---

## S2-1 — Option beta working state + slot key
**Design:** AIB-DD §4.1–§4.2; SLICE2-DESIGN §2, §6.2, §9. **Touches:** `AiPrimitiveEmitter` / BTree per-node adapter; `BlueprintBlackboardPartitions` usage; reuse Slice-1-proven allocator.
**Scope:** move AiPrimitive WorkingState into the `BlueprintBlackboard*` tiers (Option β); per-node working-slot key = `FNV-1a(BehaviorAssetId, NodeVisualId)`; the per-node adapter projects `Params` (bin-packed offset over `BrainBlackboard`) **and** `WorkingState` (partition slot via `TryGetSlotOffset`), then calls `TickCore`.
**Success conditions:**
- Build: clean rebuild 0 errors.
- Runtime test (`Fdp.Toolkits.Tests/Behavior`): `StatefulPrimitive_WorkingStatePersistsAcrossTicks` — a stateful primitive's working state increments and persists across ticks (projected from its partition slot, not `Blackboard1024 Memory+8`).
- Runtime test: `SameStatefulPrimitive_TwoNodes_IndependentSlots` — the same stateful primitive used at two nodes gets two distinct slots (distinct FNV-1a keys); their working states evolve independently (no cross-talk).
**Done when:** both pass; clean rebuild green.

## S2-2 — Synchronous Input-phase provisioning
**Design:** AIB-DD §4.3 Fix 1; SLICE2-DESIGN §10 Flaw 1. **Touches:** `BehaviorIngressSystem` (FDP behavior systems).
**Scope:** at assignment, sum **reachable** stateful nodes, pre-provision worst-case, and perform any tier upgrade **synchronously in the `Input` phase** (`AddComponent`+`CopyToLargerTier`+`RemoveComponent`) — never deferred ECB.
**Success conditions:**
- Runtime test (`Fdp.Toolkits.Tests` / SimHost behavior tests): `Assign_UpgradesTierSynchronously_BeforeFirstTick` — an entity already carrying a smaller tier (e.g. `BlueprintBlackboard1024`) assigned a behavior needing more space has the correct larger tier present **and all slots allocated before the same frame's `Simulation` tick**; the first BTree tick does not hit a missing slot (no exception).
- Runtime test: `Assign_ProvisionsWorstCaseReachableStatefulNodes` — provisioned slot count equals the number of reachable stateful node instances (not the executed subset).
**Done when:** both pass.

## S2-3 — Hot-reload ghost-slot fix
**Design:** AIB-DD §4.3 Fix 2; SLICE2-DESIGN §10 Flaw 2; `Blueprint_Subsystem_Hot_Reload_Detailed_Design.md` (Slice-2 addendum). **Touches:** `AiHotReloadCoordinator` (hard-reload path) + `BehaviorIngressSystem`.
**Scope:** on a Hard Reload of a BTree asset, re-publish `AssignBehaviorEvent` for affected entities so old (possibly wrong-sized) slots are `TryDetach`ed and re-provisioned; do **not** rely on inline `ResetSlot` for BTree synthetic slots.
**Success conditions:**
- Runtime test (mirror the hot-reload harness, e.g. `Fdp.Toolkits.Tests` / Blueprints hot-reload fixture): `HardReload_GrowsWorkingState_NoNeighborCorruption` — start two stateful primitives at adjacent slots; Hard-Reload one so its `WorkingState` grows; assert (a) the reloaded primitive runs with a correctly-sized slot, (b) the **adjacent** primitive's slot bytes are intact (no overflow corruption).
- Runtime test: `HardReload_RepublishesAssignBehaviorEvent` — assert the coordinator publishes `AssignBehaviorEvent` for each entity running the reloaded behavior (detach + re-provision path taken, not inline `ResetSlot`).
**Done when:** both pass.

## S2-4 — Cross-region validator stateful Subtree
**Design:** AIB-DD §4.3 Fix 3; SLICE2-DESIGN §10 Flaw 3. **Touches:** the cross-region conflict validator (`WouldCreateCrossRegionConflict` / `GetParallelRegionMap`).
**Scope:** treat a stateful Subtree as a mutating writer of its synthetic keys; hard-error when the **same** stateful Subtree executes concurrently across orthogonal parallel regions.
**Success conditions:**
- Test (`Hrot.BTree.Editor.Tests` / `Hrot.Hsm.Editor.Tests` validation): `SameStatefulSubtree_InTwoParallelRegions_HardErrors` — asset with the same stateful Subtree in two orthogonal parallel regions ⇒ a hard validation error.
- Test: `StatelessSubtree_InParallelRegions_Allowed` — a stateless Subtree concurrent in parallel regions ⇒ no error.
**Done when:** both pass.

## S2-G — Slice 2 DEMO GATE
**Design:** AIB-DD §5; SLICE2-DESIGN §5. **Demonstrates:** multiple stateful primitives per entity + mixed with Slice-1 stateless.
**Deliverables:** demo asset(s) with the same stateful primitive at two nodes (independent state) and ≥1 stateless action; e.g. a stateful "increment-with-internal-cursor" or "wait-N-ticks-then-Success" used twice with different params.
**Success conditions (the gate):**
- Build: clean rebuild 0 errors; byte-identity green.
- Proof test `T20_MultiStateful_ProofTests`:
  - `TwoStatefulInstances_MaintainIndependentState` — tick; assert each node's working state (e.g. each cursor / counter) advances independently.
  - `MixedStatelessAndStateful_Coexist` — assert the Slice-1 stateless `Params` (over `BrainBlackboard`) and the stateful `WorkingState` (over the partition slot) both behave correctly in one behavior (disjoint memory, no interference).
- Live: the partitioned working state is inspectable (extend / reuse the `Blackboard1024Renderer` pattern for the tier component).
**Done when:** all proof tests pass; full relevant suites green; clean rebuild green. **Slice 2 complete.**
