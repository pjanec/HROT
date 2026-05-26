# BATCH-08 INSTRUCTIONS — Phase 5 Part 2 (Look-At Nodes, Getters, Validators, Registration)

**Scope:** ANC-P5-04, ANC-P5-05, ANC-P5-06, ANC-P5-07, ANC-P5-08  
**Estimated Effort:** 30–40 hours  
**Deliverables:** 5 action nodes (3 look-at + 2 getter), 4 validator rules (ANIM008–011), AiPrimitive registration + codegen, custom drawer for PlayMontageChainNode, Layer-2 tests

---

## Context & References

See `.dev/anim-ctrl/` for all design docs:
- **DD-5** (§5–11): Look-at nodes, getter nodes, AiPrimitive dispatch, codegen patterns, validator rules, custom drawer
- **DD-1** (§8–10, §20): Look-at executor, getters, phase ordering, capability gating
- **BATCH-07-REPORT.md**: Phase 5 Part 1 implementation insights (Span-cast pattern, [MontagePicker] integration, Phase 3 reuse)

**Prior approvals:** Phase 0–4 complete; Phase 5 Part 1 (6 action nodes) complete. All event types, validators, TKB infrastructure ready.

---

## Success Criteria (Non-Negotiable)

1. **All 5 nodes created** (3 look-at + 2 getter) with correct struct definitions.
2. **Validators ANIM008–011 integrated** into Blueprint compiler (warning/error rules for node sequencing).
3. **AiPrimitive registration** wired correctly: `BlueprintRegistry.RegisterAiPrimitive(…)` for all 11 nodes (6 Part 1 + 5 Part 2).
4. **Codegen ANIM010 asserts** verify Span-cast safety in all emitted Blueprint primitive code.
5. **Custom drawer for PlayMontageChainNode** renders inline array of montage pickers in editor (visual + functional).
6. **Layer-2 tests comprehensive:** Each node tested in isolation + integration tests verify cross-node sequencing (e.g., `GetMontageQueueProgress` after `PlayMontageNode`).
7. **Build clean, 0 warnings.** Solution builds with `-maxcpucount:4`; all Phase 0–5 tests pass (160+ total).

---

## Scope Breakdown

### ANC-P5-04: Look-At Nodes (3 nodes)

**Deliverables:**

- `LookAtPointNode : AiPrimitiveNode`
  - Fields: `TargetCharacter` (uint), `TargetPoint` (Vector3)
  - Behavior: Stage LookAtExecutorState with point-mode, blend-in over 0.2 s
  - Test: `LookAtPointNode_StagesExecutorState`, `LookAtPointNode_NoCapability_FailsImmediately`

- `LookAtEntityNode : AiPrimitiveNode`
  - Fields: `TargetCharacter` (uint), `TargetEntity` (uint), `OffsetFromTarget` (Vector3, optional)
  - Behavior: Stage LookAtExecutorState with entity-mode; resolve world point at tick time
  - Test: `LookAtEntityNode_ResolvesEntityPosition`, `LookAtEntityNode_InvalidTarget_NoOps`

- `ReleaseLookNode : AiPrimitiveNode`
  - Fields: `TargetCharacter` (uint)
  - Behavior: Stage blend-out (set LookAtExecutorState.BlendOut = true), drop target
  - Test: `ReleaseLookNode_StagesBlendOut`, `ReleaseLookNode_WithoutPriorAcquire_WarnsANIM009`

**Validator integration:**
- ANIM009: ReleaseLookNode without prior LookAtPointNode/LookAtEntityNode in execution path → warning (deferred look can be ignored)

**Design references:** DD-5 §5, DD-1 §8 (look-at executor), DD-1 §20.6 (capability gating).

---

### ANC-P5-05: Getter Nodes (2 nodes)

**Deliverables:**

- `GetMontageQueueProgressNode : AiPrimitiveNode` (stateful getter)
  - Fields: `TargetCharacter` (uint)
  - Output: Returns (uint CurrentEntryIndex, float ElapsedSeconds, uint TotalCount)
  - Behavior: Read AnimationMontageQueueState current index + elapsed time
  - Test: `GetMontageQueueProgressNode_ReadsCurrentIndex`, `GetMontageQueueProgressNode_NoQueue_ReturnsZeros`

- `GetCurrentStanceNode : AiPrimitiveNode` (stateful getter)
  - Fields: `TargetCharacter` (uint)
  - Output: Returns (StanceId CurrentStance, float BlendWeight)
  - Behavior: Read StanceStatus.CurrentStance + blend weight
  - Test: `GetCurrentStanceNode_ReadsStance`, `GetCurrentStanceNode_DuringTransition_ReturnsInterpolated`

**Schema notes:**
- Getter nodes have **output ports** in Blueprint UX (return typed values to successor nodes)
- Codegen emits them as "read-only" primitives (no state mutation)

**Design references:** DD-5 §6, DD-1 §18 (state reporter), DD-4 §2 (stance enum).

---

### ANC-P5-06: Validators ANIM008–ANIM012

Wait, the task says ANIM008–ANIM012 (5 rules) but DD-5 lists ANIM008–ANIM011 (4 rules). **Check DD-5 §10 to confirm the exact count.** Assuming 4 rules per the design document:

**Deliverables:**

- **ANIM008:** EnqueueMontageNode without preceding PlayMontageChainNode in same execution path → warning
  - Message: "Enqueueing to an uninitialized queue may fail at runtime"
  - Severity: Warning (not error; enqueue can gracefully no-op if queue empty)

- **ANIM009:** ReleaseLookNode without prior LookAtPointNode or LookAtEntityNode → warning
  - Message: "Releasing aim without prior acquisition may be a no-op"
  - Severity: Warning

- **ANIM010:** Codegen self-check for Span-cast safety in queue mutations (compiler verification)
  - Message: "Queue mutation code does not use Span-cast idiom; unsafe direct indexing detected"
  - Severity: Error (breaks compilation if violated)
  - Scope: Runs on emitted Blueprint primitive code, not source nodes

- **ANIM011:** Cross-subsystem AiPrimitive validation (animation primitives in inappropriate contexts)
  - Message: "Animation primitive used in non-animation subsystem context" (e.g., WeaponDispatcher trying to call LookAtPointNode)
  - Severity: Error

**Test structure:**
- Compiler tests (`Blueprint Compiler Tests` project): Positive + negative cases for each rule
- Example: `ANIM008_EnqueueWithoutChain_EmitsWarning()` vs `ANIM008_EnqueueAfterChain_NoWarning()`

**Design references:** DD-5 §10, DD-1 §17 (system ordering/constraints).

---

### ANC-P5-07: AiPrimitive Registration + Cross-Subsystem Reuse

**Deliverables:**

- Register all 11 nodes (6 Part 1 + 5 Part 2) with `BlueprintRegistry.RegisterAiPrimitive(…)` at module initialization
  - `PlayMontageModule` or `AnimationMuscleModule` init hook
  - Each node has a unique `FullyQualifiedName` and `Schema`
  - Schema includes field names, types, picker attributes

- **Cross-subsystem hosting:** Nodes must be reusable in:
  - BTree action contexts (`AiPrimitiveHosting.BTreeAction`)
  - HSM action bodies (`AiPrimitiveHosting.HsmAction`)
  - Blueprint Instance imperative nodes (`AiPrimitiveHosting.InstanceAction`)
  - WhenNode event-reaction contexts (`AiPrimitiveHosting.WhenReaction` — for getters only)

- **Dispatch mechanism:** Existing `AiPrimitiveEmitter` + `AiPrimitiveLowering` (Phase 5 Part 1 uses these; Part 2 just reuses them)

**No new infrastructure required.** Blueprint compiler already has the dispatch mechanism (tested in other primitives like weapon/locomotion).

**Test structure:**
- Schema registration test: Asserts 11 nodes present in registry, schema fields match struct definitions
- Cross-subsystem dispatch tests: Emit a PlayMontageNode in BTree context, verify it stages params correctly; same in HSM context; same in Blueprint Instance
- Integration test: Chain PlayMontageNode → EnqueueMontageNode → GetMontageQueueProgressNode in a Blueprint; run it; verify output reads correct queue state

**Design references:** DD-5 §11, `AI_Editor_Shared_Infrastructure.md` (AiPrimitive dispatch).

---

### ANC-P5-08: PlayMontageChainNode Custom Drawer (Editor)

**Deliverables:**

- Custom property drawer for `PlayMontageChainNode.ChainedMontages[8]` array field
  - Renders as inline visual array: 8 slots, each with a montage picker dropdown
  - Slot label: "Montage 0", "Montage 1", …, "Montage 7"
  - Grayed-out slots beyond `ChainCount` (only first N slots are active)
  - Up/down buttons to reorder entries (within active range)
  - "+" / "–" buttons to add/remove entries (adjusting ChainCount)

- Integration into editor UI pipeline:
  - When Blueprint editor renders a PlayMontageChainNode, detects the array field and applies the custom drawer
  - Drawer queries `IAnimationTkbQueries.GetPlayableMontages()` for dropdown population
  - Drawer persists reordered/added/removed entries back to the node struct

**Test structure:**
- Drawer UI tests (if available in codebase; otherwise manual verification)
- Integration test: Create a PlayMontageChainNode in editor, set ChainCount=3, populate 3 montages, verify serialization/deserialization

**Design references:** DD-5 §8 (drawer UX), mirror existing drawers like `HsmEventPicker` drawer pattern.

---

## Phase 5 Part 2 Test Plan

**Layer-2 integration tests:** All nodes tested in isolation + sequencing scenarios.

**Test file:** `Hrot.MuscleCharacter.Animation.Tests/Phase5GettersAndValidatorsTests.cs` (new) + extended `Phase5ActionNodesTests.cs` with look-at tests.

**Example test breakdown:**
- 3 tests for LookAtPointNode (happy path, no capability, invalid target)
- 3 tests for LookAtEntityNode (entity resolve, entity missing, offset application)
- 3 tests for ReleaseLookNode (blend-out, without prior acquire → ANIM009 warning)
- 2 tests for GetMontageQueueProgressNode (read queue state, no queue)
- 2 tests for GetCurrentStanceNode (read stance, during transition)
- Validator tests: 4 tests (one per ANIM008–011, positive + negative cases)
- Integration tests: 3 tests (PlayMontage → Enqueue → GetProgress chain; LookAt → Release sequence; cross-subsystem dispatch)

**Total: ~20 new tests** (all behavioral).

---

## Developer Insights Section

**After implementing, answer these questions in your batch report:**

1. **Getter node output types:**
   - How do getter nodes expose multiple output values (CurrentEntryIndex, ElapsedSeconds, TotalCount from GetMontageQueueProgress)?
   - Does the Blueprint schema/codegen have a tuple/record type for returns, or are they separate fields on an output component?

2. **Look-at executor integration:**
   - LookAtEntityNode must resolve entity position at **tick time**, not node-exec time. How does this get staged for the runtime bridge system to pick up?
   - Does LookAtExecutorState have a deferred-resolve field, or is the entity ID stored and resolved during `AnimationRuntimeBridgeSystem.Tick`?

3. **ANIM009 sequencing validation:**
   - ReleaseLookNode without prior LookAt in the execution path should warn. How does the validator trace the execution path? Does it scan the Blueprint control flow graph backwards from the ReleaseLook node to find a prior LookAt?

4. **ANIM010 bytecode verification:**
   - Codegen emits Blueprint primitive methods. You need to assert that all queue mutation code uses Span-cast. What bytecode patterns indicate a violation? Do you scan IL, C# AST, or emitted source code?

5. **Custom drawer architecture:**
   - PlayMontageChainNode.ChainedMontages drawer needs to reorder/add/remove entries. Does the drawer mutate the node struct directly, or does it emit a reorder command that the Blueprint serializer interprets?

6. **Cross-subsystem dispatch surprises:**
   - When a Look-at node is used in a BTree action context vs. Blueprint Instance context, are there any subsystem-specific initializations required? Does `TargetCharacter` resolution differ between contexts?

---

## Non-Negotiable Workflow

### Before Starting
1. **Read** DD-5 §5–11 + DD-1 §8–10, §20 + BATCH-07-REPORT.md insights.
2. **Verify** Phase 0–5 Part 1 test suite green: `dotnet test Hrot.MuscleCharacter.Animation.Tests --no-build` passes 148+ tests.
3. **Check** existing custom drawers in the codebase (e.g., HsmEventPicker) to match pattern.
4. **Review** the AiPrimitive dispatch mechanism in Blueprint compiler (`AiPrimitiveEmitter`, `AiPrimitiveLowering`).

### As You Go
- **Validator testing:** Create a `Blueprint Compiler Tests` test class for ANIM008–011; test both positive and negative cases.
- **Codegen ANIM010:** This is critical. Verify all emitted Blueprint primitive methods for queue mutations use Span-cast.
- **Custom drawer:** Start with a simple inline renderer; progressively add reorder/add/remove buttons if time permits.

### Deliverables
- **Source files:**
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AnimationLookAtNodes.cs` (3 look-at nodes)
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AnimationGetterNodes.cs` (2 getter nodes)
  - Module initialization for AiPrimitive registration (likely in `AnimationMuscleModule.cs` or similar)
  - Custom drawer implementation (likely in `Hrot.Editor.AiShared/Drawers/PlayMontageChainNodeDrawer.cs`)
- **Tests:**
  - `Hrot.MuscleCharacter.Animation.Tests/Phase5GettersAndValidatorsTests.cs`
  - `Hrot.Blueprints.Tests/Compiler/AnimationValidatorTests.cs` (ANIM008–011)
- **Build clean:** `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore` produces 0 errors.
- **Tests pass:** `dotnet test Hrot.MuscleCharacter.Animation.Tests --no-build -v minimal` shows 160+ passing.
- **Batch report:** `.dev/anim-ctrl/reports/BATCH-08-REPORT.md` with all insight questions answered.

---

## Success Checklist (For Dev Lead Review)

- [ ] 5 new nodes defined (3 look-at + 2 getter)
- [ ] 4 validators implemented (ANIM008–011)
- [ ] All 11 nodes registered with BlueprintRegistry
- [ ] Custom drawer for PlayMontageChainNode renders inline array
- [ ] Layer-2 tests comprehensive (~20 tests, all behavioral)
- [ ] ANIM010 codegen assert verifies Span-cast pattern
- [ ] Cross-subsystem dispatch verified (BTree + HSM + Blueprint Instance)
- [ ] Solution builds clean, no warnings
- [ ] Phase 0–5 Part 1 test suite still green (160+ tests passing)
- [ ] Report includes all developer insights

---

## Git Commit (After Approval)

```
ANC-P5-04 through ANC-P5-08: Phase 5 Part 2 — Look-At, Getters, Validators, Registration, Custom Drawer

- LookAtPointNode, LookAtEntityNode, ReleaseLookNode: aim control with capability gating
- GetMontageQueueProgressNode, GetCurrentStanceNode: stateful getter nodes (output ports)
- Validators ANIM008–011: sequence rules, cross-subsystem validation, codegen safety
- AiPrimitive registration: all 11 nodes (Part 1 + 2) registered with BlueprintRegistry
- Custom drawer for PlayMontageChainNode: inline array with reorder/add/remove buttons
- Codegen ANIM010: Span-cast pattern verification in emitted Blueprint primitive code
- Layer-2 tests: ~20 new behavioral tests (look-at, getters, validators, cross-subsystem)
- Cross-subsystem reuse: nodes work in BTree + HSM + Blueprint Instance + WhenNode contexts

Test Results: 160+ passing (Phase 0-5 Part 1 intact + 20 Phase 5 Part 2 new)
Build: clean (0 errors, 0 warnings)

Verified:
- Phase 5 complete (all 8 tasks: ANC-P5-01 through ANC-P5-08)
- 11 action/getter nodes correctly defined
- 4 validator rules integrated (ANIM008–011)
- Codegen safety verified (ANIM010)
- Custom drawer functional (inline array editing)
- AiPrimitive dispatch working (cross-subsystem reuse)
- No regressions Phase 0–4

Phase 5 complete. Stage 1 (Phases 0–5, 7) ready for integration testing.
```

---

**BATCH-08 represents the final phase of Stage 1 (networkless animation runtime).** After this batch, Phase 6 (Replication) and Phase 8 (Stride backend) follow as extensions. But Phases 0–5, 7 form a complete, independently verifiable vertical slice.
