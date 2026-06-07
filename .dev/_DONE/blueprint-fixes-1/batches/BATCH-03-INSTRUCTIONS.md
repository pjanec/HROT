# BATCH-03: HSM Host Fixes + BATCH-02 Corrective Tests

**Batch Number:** BATCH-03  
**Tasks:** CORR-02-1 (BPF-001 AiPrimitive field-value test), CORR-02-2 (BPF-003 HitCount accumulation test), BPF-017, BPF-022, BPF-023, BPF-024, BPF-025  
**Source:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`, `.dev/blueprint-fixes-1/reviews/BATCH-02-REVIEW.md`  
**Tracker:** `.dev/blueprint-fixes-1/TASK-TRACKER.md`  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH -- BPF-017 (Critical) corrupts all action/guard names; BPF-024/025 break HSM debugging  
**Dependencies:** BATCH-02 (done)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch starts with two mandatory corrective tests from the BATCH-02 review, then fixes five HSM-host defects covering: action-name garbling (Critical), deferred-event emission (High), HsmDebugSession snapshot population (High), StepOver/StepOut predicate (High), and StableId assigned by positional sort (High).

**Complete the corrective tasks first, before any HSM work.**

### Required Reading (IN ORDER)
1. **BATCH-02 Review:** `.dev/blueprint-fixes-1/reviews/BATCH-02-REVIEW.md` -- read corrective task requirements
2. **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-017, BPF-022, BPF-023, BPF-024, BPF-025
3. **HSM Host Design:** `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md` -- Slice 2 runtime snapshot, action names, deferred events, StepOver/Out predicates, StableId
4. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
5. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture`. Use `mcp_codebase-memo_search_graph` and `mcp_codebase-memo_get_code_snippet` to find symbols before editing.

### Source Code Location
- **HSM editor host:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` (search via graph)
- **HSM emitter/flattener:** `Hrot/Subsystems/AI/Hrot.Hsm.*/` (search via graph)
- **Blueprint tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- **HSM tests:** `Hrot/Subsystems/AI/Hrot.Hsm.*/Tests/` (find via graph)

### Report Submission
`.dev/blueprint-fixes-1/reports/BATCH-03-REPORT.md`  
Questions: `.dev/blueprint-fixes-1/questions/BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

For **each task** in strict sequence:
1. **Define success condition** -- read the task entry; state exactly what correct looks like and what tests verify it
2. **Implement the fix**
3. **Write tests** -- actual behavioral verification, not string presence
4. **Run all tests** -- `dotnet test` on all affected test projects; ALL must pass
5. **Fix failures at root cause** -- never skip; iterate until green
6. Only when ALL tests pass: move to next task

Do not write the report until every task is done and all tests pass. No asking for permission to do obvious things.

---

## Context

Corrective tasks from BATCH-02 review must be done first. Then: BPF-017 is a Critical bug in the HSM host where `MachineMetadata.ActionNames` are keyed by positional index while the blob stores hash IDs, garbling all displayed action/guard names. The remaining four are High-severity HSM debug session issues.

---

## ✅ Corrective Tasks (MUST DO FIRST)

### Corrective Task 1: BPF-001 AiPrimitive field-value reading test

**From Review:** `.dev/blueprint-fixes-1/reviews/BATCH-02-REVIEW.md` Issue 1

**Success Condition:**  
A test must verify that `GetCurrentStateSnapshot()` returns `FieldValues` containing actual field values (e.g. `FieldValues["Speed"] == 3.14f`) when an AiPrimitive blueprint is paused and its entity has a `Blackboard1024` component with matching structure hash and known field bytes at known offsets.

**What to do:**
1. Create a stub `ISimulationView` that returns `HasComponent<Blackboard1024>(e) == true` and `GetComponentRO<Blackboard1024>(e)` returns a buffer with a known structure hash (first 8 bytes) followed by known field bytes.
2. Register a `BlueprintDefinition` of `AiPrimitive` kind with a matching `StructureHash` and `StateFields["Speed"] = {OffsetBytes=0, SizeBytes=4, ClrType=typeof(float)}`.
3. Pause on a node, call `GetCurrentStateSnapshot()`, assert `FieldValues["Speed"] == 3.14f` (or equivalent known value).
4. Also add a test verifying structure-hash mismatch returns empty `FieldValues`.

---

### Corrective Task 2: BPF-003 HitCount accumulation across same-frame entities

**From Review:** `.dev/blueprint-fixes-1/reviews/BATCH-02-REVIEW.md` Issue 2

**Success Condition:**  
After E1 and E2 both hit the same breakpoint in the same frame (E1 pauses, E2 is deduped but should increment HitCount), the `HitCount` on that breakpoint must be 2 (not 1).

**What to do:**
1. In the `BreakpointHashSafetyTests.OnNewTick_ResetsDedupSet_AllowingSecondTickHit` test (or a new test), after E2 hits the same-tick breakpoint (which doesn't re-pause), assert `session.GetBreakpoints()[0].HitCount == 2`.
2. This may require changing the implementation: the per-frame dedup should block the pause but still call `HitCount++` and emit a hit event. Check that `OnNodeEnter` does this.
3. If the current implementation does NOT increment HitCount on same-frame dedup hits, fix it.

---

## ✅ Main Tasks

### Task 3: BPF-017 -- HSM ActionNames keyed by positional index vs blob hash IDs (CRITICAL)

**Task Definition:** [BPF-017](../TASK-DETAIL.md#bpf-017----hsm-actionnames-keyed-by-positional-index-but-blob-stores-hashes---all-actionguard-names-garbled-hsm-host)

**Success Condition (define before implementing):**  
`MachineMetadata.ActionNames` must be keyed by the same hash IDs that `HsmDefinitionBlob` uses to identify actions/guards -- NOT by positional index. Action and guard display names in the overlay/inspector must show the correct name for each action, not a garbled/mismatched name.

**What to do:**
1. Use `get_code_snippet` to read `HsmFluentEmitter.BuildMachineMetadata`, `HsmFlattener` (to understand hash IDs), and the relevant section of the HSM Host Design.
2. Read the design section for how action/guard hash IDs are assigned by the emitter.
3. Fix `BuildMachineMetadata` to key `ActionNames` by the same hash IDs the blob emitter uses.
4. Write tests verifying that action/guard names are correctly mapped to the expected hash IDs.

**Tests Required:**
- Build a machine with known action/guard names; emit the blob; build the metadata; verify `ActionNames[hashId] == "ExpectedName"` for each action
- Test that the hash ID used in metadata matches the hash ID in the blob

---

### Task 4: BPF-022 -- HsmFluentEmitter never emits DeferEvent (HIGH)

**Task Definition:** [BPF-022](../TASK-DETAIL.md#bpf-022----hsmfluentemitter-never-emits-deferevent---deferred-event-lists-dropped-every-save-hsm-host)

**Success Condition (define before implementing):**  
`HsmFluentEmitter` must emit `DeferEvent()` calls for states with deferred events. Deferred event lists must survive a round-trip through the emitter (serialize + deserialize). Tests must verify the emitted code contains correct `DeferEvent()` calls.

**What to do:**
1. Read `HsmFluentEmitter` and the design to understand where `DeferEvent()` should be emitted.
2. Fix the emitter to emit `DeferEvent()` for each deferred event in each state.
3. Write a test: create a machine with a state that has at least two deferred events; emit; assert the emitted text contains `DeferEvent(...)` calls with correct event names; verify deferred events are preserved in the round-trip.

**Tests Required:**
- Emit a machine with deferred events; assert `DeferEvent()` calls are present in emitted code
- Round-trip test: emit + parse back; assert deferred events are preserved

---

### Task 5: BPF-023 -- HsmDebugSession.Update hardcodes empty active-leaf/event/timer/history (HIGH)

**Task Definition:** [BPF-023](../TASK-DETAIL.md#bpf-023----hsmdebugsessionupdate-hardcodes-empty-active-leafeventtimerhistory-arrays-hsm-host-localizes-bpf-010)

**Success Condition (define before implementing):**  
`HsmDebugSession.Update` must decode active states, event queue entries, timer slots, and history slots from the `BrainHsm*` component memory. The `HsmInstanceSnapshot` must contain non-empty arrays for a running HSM with active states. Tests must verify actual populated snapshot values.

**What to do:**
1. Read `HsmDebugSession.Update` and the HSM host design for how to decode active states / event queue / timers / history from `BrainHsm64` (or similar) ECS component.
2. Read the `BrainHsm*` component structure to understand the memory layout.
3. Implement the decoding in `Update`.
4. Write a test: create a `BrainHsm64` component with known active-state data; call `Update`; assert the snapshot has non-empty active state IDs that match known values.

**Tests Required:**
- Snapshot has non-empty `ActiveStateIds` for a running HSM
- Snapshot has correct `AssetId` (not Guid.Empty)
- Snapshot timer/event queue entries populated when non-empty

---

### Task 6: BPF-024 -- StepOver and StepOut share identical pause predicate (HIGH)

**Task Definition:** [BPF-024](../TASK-DETAIL.md#bpf-024----hsm-stepover-and-stepout-use-an-identical-pause-predicate---stepout-never-reaches-rtc-quiescence-hsm-host)

**Success Condition (define before implementing):**  
StepOut must pause at RTC quiescence (after all transitions complete, not just when depth decreases). StepOver must pause at the next sibling step. The two operations must use distinct predicates. Tests must verify StepOut actually waits for quiescence.

**What to do:**
1. Read the current `StepOut` and `StepOver` predicates in `HsmDebugSession`.
2. Read the design for the correct quiescence condition.
3. Fix StepOut to use the correct quiescence predicate (not the StepOver predicate).
4. Write tests distinguishing StepOver vs StepOut behavior.

**Tests Required:**
- StepOut re-pauses at RTC quiescence (distinct from StepOver pause point)
- StepOver still pauses at next node entry (unaffected by fix)

---

### Task 7: BPF-025 -- HSM layout StableId assigned by positional lexicographic sort (HIGH)

**Task Definition:** [BPF-025](../TASK-DETAIL.md#bpf-025----hsm-layout-stableid-assigned-by-positional-lexicographic-sort---identity-breaks-on-any-structural-edit-hsm-host)

**Success Condition (define before implementing):**  
`StableId` for an HSM state must be derived from a content hash (e.g. state name path hash) so it does not change when the state order changes. Adding a new state before an existing one must NOT change the existing state's `StableId`. Tests must verify this identity stability.

**What to do:**
1. Read the current `StableId` assignment in the HSM layout builder.
2. Read the design for how `StableId` should be derived.
3. Fix to use a content-based ID (e.g. hash of the state's fully-qualified name path).
4. Write a test: build a machine with states A, B, C. Record their StableIds. Insert a new state X before A. Re-build. Assert A/B/C still have the same StableIds.

**Tests Required:**
- Adding a state does not change existing states' StableIds
- Renaming/moving a state DOES change its StableId (content-based)

---

## 🧪 Testing Requirements

- Run all tests after each task; all must pass including BATCH-01/02 tests
- Tests must verify actual behavior (field values, name mappings, populated arrays, identity stability)
- Corrective tasks MUST be done before any HSM work

## ⚠️ Quality Standards

**TEST QUALITY EXPECTATIONS:**
- **NOT ACCEPTABLE:** `Assert.NotEmpty(snapshot.ActiveStateIds)` with no check on the actual IDs
- **REQUIRED:** `Assert.Equal(new[] { expectedStateGuid }, snapshot.ActiveStateIds)` (actual content)
- **NOT ACCEPTABLE:** `Assert.NotNull(metadata.ActionNames)` 
- **REQUIRED:** `Assert.Equal("MyAction", metadata.ActionNames[knownHashId])`

## 📊 Report Requirements

Submit `.dev/blueprint-fixes-1/reports/BATCH-03-REPORT.md`.

Required sections:
- Each task: completed? what you did, tests added
- Test count and pass/fail
- Issues encountered and how resolved
- Design decisions beyond the spec
- Weak points spotted
- Edge cases discovered
- Suggested commit message

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] CORR-02-1: AiPrimitive field-value reading tested with actual values; passes
- [ ] CORR-02-2: HitCount accumulation across same-frame entities tested and passes
- [ ] BPF-017: ActionNames keyed by hash IDs; name mapping test passes
- [ ] BPF-022: DeferEvent emitted; round-trip test passes
- [ ] BPF-023: HsmDebugSession.Update decodes actual state from component; snapshot populated; tests pass
- [ ] BPF-024: StepOver/StepOut use distinct predicates; behavioral tests pass
- [ ] BPF-025: StableId is content-based; identity stability test passes
- [ ] All pre-existing tests still pass
- [ ] Report submitted to `.dev/blueprint-fixes-1/reports/BATCH-03-REPORT.md`

---

## 📚 Reference Materials
- **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` (BPF-017, BPF-022, BPF-023, BPF-024, BPF-025)
- **HSM Host Design:** `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md`
- **Blueprints-2 DEBT-TRACKER:** `.dev/blueprints-2/DEBT-TRACKER.md`
- **BATCH-02 Review:** `.dev/blueprint-fixes-1/reviews/BATCH-02-REVIEW.md`
- **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`
