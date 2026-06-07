# BATCH-07 INSTRUCTIONS — Phase 5 Part 1 (Action Nodes: Montage & Stance)

**Scope:** ANC-P5-01, ANC-P5-02, ANC-P5-03  
**Estimated Effort:** 25–30 hours  
**Deliverables:** 9 action nodes (PlayMontage, StopMontage, PlayMontageChain, Enqueue, ClearQueue, SetStance + schema), infrastructure for codegen, Layer-2 integration tests

---

## Context & References

See `.dev/anim-ctrl/` for all design docs:
- **DD-5** (§1–6): Blueprint authoring primitives architecture, node roster, codegen patterns
- **DD-1** (§4–9): Channel mechanics, queue mechanics, executor patterns
- **DESIGN.md** (Phase 5 section): Vertical slice walkthrough

**Prior approvals:** Phase 0–4 complete. All event types, catalog, validators, TKB infrastructure ready.  
**Current state:** The animation runtime (Phase 1–3) and event catalog (Phase 4) are fully green. Blueprint compiler infrastructure exists; existing nodes (`LocomotionChannel`-based primitives) follow the same AiPrimitive dispatch pattern.

---

## Success Criteria (Non-Negotiable)

1. **All 9 nodes created** with correct `struct` definitions, properties, `[InlineArray]`-safe mutation patterns.
2. **Schema generation** for editor property binding (AiPrimitive schema includes each node's fields + picker attributes).
3. **No silent failures.** Codegen `AssertNodeCount`, `AssertFieldNames` tests verify exact node roster.
4. **Comprehensive Layer-2 tests:** Each action node tested in isolation against fake backend (e.g., PlayMontageNode writes correct staged data; StopMontageNode arms blend-out).
5. **Integration:** Nodes register with `BlueprintRegistry.RegisterAiPrimitive(…)` (no manual node kind edits; dispatch automatic).
6. **Build clean, 0 warnings.** Solution builds with `-maxcpucount:4`; no CS errors; Phase 0–4 tests all still green (no regressions).

---

## Scope Breakdown

### ANC-P5-01: `PlayMontageNode` + `StopMontageNode`

**Deliverables:**
- `PlayMontageNode : AiPrimitiveNode` (in `Hrot.MuscleCharacter.Animation.Nodes`)
  - Fields: `TargetCharacter` (ActorHandle), `MontageId` (int, `[MontagePicker]`), `SlotIndex` (byte)
  - Behavior: Emit `PlayMontageParams` → `AnimationChannel` (dispatches to channel, stages executor)
  - Test: `PlayMontageNode_EmitsPlayMontageParams`, `PlayMontageNode_UnknownMontage_EmitsFailure`

- `StopMontageNode : AiPrimitiveNode`
  - Fields: `TargetCharacter` (ActorHandle), `SlotIndex` (byte)
  - Behavior: Emit `StopMontageParams` → `AnimationChannel` (stages blend-out)
  - Test: `StopMontageNode_EmitsStopMontageParams`, `StopMontageNode_UnknownTarget_NoOps`

**Schema notes:**
- Both nodes read/write `AnimationChannel` directly (not through dispatcher system yet — that's Phase 3 runtime, already done).
- Tests construct a channel, emit the node's params, verify channel state changes.
- `[MontagePicker]` on `MontageId` discovered by editor property drawer system (same discovery as DD-3 event fields).

**Design references:** DD-5 §3, DD-1 §4 (channel shape), DD-4 §5 (TKB query API for picker data).

---

### ANC-P5-02: Queue-mutation nodes (`PlayMontageChainNode` / `EnqueueMontageNode` / `ClearMontageQueueNode`)

**Deliverables:**
- `PlayMontageChainNode : AiPrimitiveNode`
  - Fields: `TargetCharacter`, **`ChainedMontages`** (array-like, max 8 montage IDs, `[MontagePicker]` each)
  - Behavior: Emit `PlayMontageQueueParams` (encodes chain), stage queue mutation
  - Validation: Chain length ≤ 8 (codegen assert; ANIM010 covers this)
  - Test: `PlayMontageChainNode_EncodesChain_Max8`, `PlayMontageChainNode_ChainLength9_ValidationError`

- `EnqueueMontageNode : AiPrimitiveNode`
  - Fields: `TargetCharacter`, `MontageId` (int, `[MontagePicker]`), `OnlyIfEmpty` (bool flag — queue only if no pending entries)
  - Behavior: Emit `PlayMontageQueueParams` (single entry), stage queue append
  - Test: `EnqueueMontageNode_AppendsToQueue`, `EnqueueMontageNode_OnlyIfEmpty_SkipsIfQueueActive`

- `ClearMontageQueueNode : AiPrimitiveNode`
  - Fields: `TargetCharacter` (ActorHandle only — no montage picker here)
  - Behavior: Emit `PlayMontageQueueParams` (clear opcode), stage queue truncation
  - Test: `ClearMontageQueueNode_DropsQueueEntries`, `ClearMontageQueueNode_NoPendingQueue_NoOps`

**Codegen notes:**
- `[InlineArray]`-safe mutation pattern per DD-5 §9: all writes go through `Span<byte>` cast to `PlayMontageQueueParams[]` (not direct array access). Tests verify Span-cast round-tripping.
- Codegen self-check (ANIM010): `AssertQueueMutationCodegenSafety` verifies that all queue nodes use Span-cast idiom, no direct array dereference.
- `PlayMontageChainNode` has a **custom drawer** (ANC-P5-08, Phase 5 Part 2) for inline array visualization; for now, stub drawer with comment "custom drawer deferred to P5-08".

**Schema notes:**
- `ChainedMontages` is a special field: fixed-size array of montage IDs with variable population (Count tracks used entries).
- Editor schema must encode this as an iterable with max length 8.

**Design references:** DD-5 §3–4, DD-1 §7 (queue mechanics), DD-1 §6.4 (side-buffer mutation patterns).

---

### ANC-P5-03: `SetStanceNode`

**Deliverables:**
- `SetStanceNode : AiPrimitiveNode`
  - Fields: `TargetCharacter` (ActorHandle), `StanceId` (byte enum, `[SortedEnum]` or picker — sourced from TKB query API `GetSupportedStances`)
  - Behavior: Emit `StanceIntent` via side-buffer mutation (no action in the channel sense; pure descriptor side-effect)
  - Test: `SetStanceNode_UpdatesStanceIntent`, `SetStanceNode_InvalidStance_Fails`, `SetStanceNode_SameStance_Idempotent`

**Schema notes:**
- `StanceId` is a byte enum populated from `IAnimationTkbQueries.GetSupportedStances` (returns list of (id, name) pairs).
- Editor property drawer uses this list to render a dropdown (mirror how existing primitives pick resources).

**Design references:** DD-5 §4, DD-1 §9 (stance transition driver).

---

## Phase 5 Part 1 Test Plan

**Layer-2 integration tests:** Construct a minimal Blueprint with each node type; emit it against the fake backend; verify state changes (staged params, queue entries, stance intent updates).

**Test file:** `Hrot.MuscleCharacter.Animation.Tests/Phase5ActionNodesTests.cs` (new; mirrors Layer-2 structure from Phase 3).

**Example test breakdown:**
- 3 tests for `PlayMontageNode` (happy path, invalid montage, no target)
- 3 tests for `StopMontageNode`
- 3 tests for `PlayMontageChainNode` (valid chain, chain too long, overwrite queue)
- 3 tests for `EnqueueMontageNode` (append, OnlyIfEmpty flag, queue full)
- 2 tests for `ClearMontageQueueNode` (clear populated, clear empty)
- 3 tests for `SetStanceNode` (valid transition, invalid stance, same-stance noop)

**Total: ~17–20 new tests** (all behavioral; no smoke tests).

---

## Developer Insights Section

**After implementing, answer these questions in your batch report:**

1. **Node struct definitions:**
   - What challenges did you encounter defining the `[InlineArray]` field in `PlayMontageChainNode.ChainedMontages`? Did the fixed-size array vs. runtime Count create any Span-cast surprises?

2. **Schema generation:**
   - How does the Blueprint editor schema discovery work for these new nodes? Did you need to extend `BlueprintNodeSchema` or register metadata, or does reflection over node struct fields suffice?

3. **Picker attribute integration:**
   - `[MontagePicker]` on `MontageId` fields: did you just mark the field with the attribute, or did the property-drawer registration require additional wiring?

4. **Codegen safety (ANIM010):**
   - You wrote an assert that verifies queue-mutation nodes use `Span<byte>` cast patterns. What bytecode patterns or AST checks does the assert look for? Did any node fail this check during implementation, and if so, how was it corrected?

5. **Integration points you discovered:**
   - Were there any unexpected dependencies between the action nodes and the dispatcher/executor systems? Any "this phase assumes X already works" assumptions that broke?

6. **Test ergonomics:**
   - For Layer-2 tests, did you have to write a lot of boilerplate to set up a minimal channel/queue/backend state, or was there good infrastructure from Phase 3 to reuse?

---

## Non-Negotiable Workflow

### Before Starting
1. **Read** DD-5 §1–6 + DD-1 §4–9 + DD-4 §5 (TKB queries).
2. **Verify** Phase 0–4 test suite green: `dotnet test Hrot.MuscleCharacter.Animation.Tests --no-build` passes 130+ tests.
3. **Scan** `Hrot/Subsystems/Blueprints/` for existing AiPrimitive node implementations (e.g., `LocomotionChannel`-based primitives) to match code style + structure.

### As You Go
- **Keep ANIM010 in mind:** All queue-mutation code uses Span-cast, never direct array access. Write the assert *as you code*, not at the end.
- **Schema field order:** Fields in the node struct will appear in editor UI in declaration order; name them intuitively.
- **No silent failures:** If a montage ID is unknown or a target actor doesn't exist, emit a Failure status, don't silently no-op (except `OnlyIfEmpty` logic, which is documented silent).

### Deliverables
- **Source files:**
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AnimationActionNodes.cs` (or split per node if preferred)
  - Pair with corresponding `*.Tests.cs` files
- **Build clean:** `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore` produces 0 errors.
- **Tests pass:** `dotnet test Hrot.MuscleCharacter.Animation.Tests --no-build -v minimal` shows 130+ passing (117 pre-existing + ~17–20 new).
- **Batch report:** `.dev/anim-ctrl/reports/BATCH-07-REPORT.md` with all insight questions answered + test summary.

---

## Success Checklist (For Dev Lead Review)

- [ ] All 9 nodes defined (2 simple action + 3 queue + 1 stance + 1 reserved for getters + 2 reserved placeholders)
  - Wait, re-counting: ANC-P5-01 = 2 nodes (Play+Stop), ANC-P5-02 = 3 nodes (Chain+Enqueue+Clear), ANC-P5-03 = 1 node (Stance). That's 6 in this batch. ANC-P5-04 (3 look-at) + ANC-P5-05 (2 getter) are Phase 5 Part 2.
- [ ] Schema generation produces schema for each node
- [ ] Layer-2 tests comprehensive (17–20 tests, all behavioral)
- [ ] Codegen ANIM010 asserts Span-cast pattern in queue nodes
- [ ] Solution builds clean, no warnings
- [ ] Phase 0–4 test suite still green (no regressions)
- [ ] Report includes all developer insights + test breakdown

---

## Git Commit (After Approval)

```
ANC-P5-01 through ANC-P5-03: Phase 5 Part 1 — Action Nodes (Montage/Queue/Stance)

- PlayMontageNode, StopMontageNode: single montage dispatch to AnimationChannel
- PlayMontageChainNode, EnqueueMontageNode, ClearMontageQueueNode: queue mutation via Span-cast
- SetStanceNode: stance intent descriptor update
- Schema generation for editor property binding
- [MontagePicker] attribute integration for montage ID fields
- Codegen ANIM010: Span-cast pattern verification for queue nodes
- Layer-2 tests: ~17–20 new behavioral tests (node isolation + fake backend integration)

Test Results: 130+ passing (Phase 0–4 intact + new Phase 5 Part 1 tests)
Build: clean (0 errors, 0 warnings)

Verified:
- All 6 action nodes defined with correct field layout
- [InlineArray] chain field tested via Span-cast round-trip
- Queue mutation code safe from buffer overruns (ANIM010)
- Editor schema includes node fields + picker attributes
- Integration: nodes dispatch through existing AiPrimitive registry
- No regressions Phase 0–4

Ready for Phase 5 Part 2 (Look-at nodes, getters, validators, registration).
```
