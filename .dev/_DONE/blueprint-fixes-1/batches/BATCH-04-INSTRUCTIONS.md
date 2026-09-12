# BATCH-04: BTree Host + NodeEditor Fixes

**Batch Number:** BATCH-04  
**Tasks:** BPF-018, BPF-026, BPF-027, BPF-045, BPF-028, BPF-029, BPF-030, BPF-047, BPF-048  
**Source:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/blueprint-fixes-1/TASK-TRACKER.md`  
**Estimated Effort:** 14-18 hours  
**Priority:** HIGH -- BPF-018 Critical (SubtreeAssetIds breaks runtime); BPF-028/029/030 break undo history for drag operations  
**Dependencies:** BATCH-03 (done)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch covers two areas: (1) BTree Host -- fixes a Critical subtree resolution bug and three High debug overlay issues; (2) NodeEditor -- fixes three High drag/undo defects and two missing test coverage gaps.

Work on BTree fixes first (BPF-018, BPF-026, BPF-027, BPF-045), then NodeEditor (BPF-028, BPF-029, BPF-030, BPF-047, BPF-048).

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-018, BPF-026, BPF-027, BPF-028, BPF-029, BPF-030, BPF-045, BPF-047, BPF-048
2. **BTree Host Design:** `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md`
3. **NodeEditor Design:** Locate via codebase graph (search for "NodeEditor" design doc under `.dev/_DONE/ai-hsm-btree-vis-edit/` or similar)
4. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
5. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture`. Use `mcp_codebase-memo_search_graph` and `mcp_codebase-memo_get_code_snippet` to find symbols.

### Source Code Location
- **BTree compiler/emitter:** `FDP/ExtDeps/FastHSM/src/` or `Hrot/Subsystems/AI/Hrot.BTree*/` (find via graph)
- **BTree debug session:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/` or similar
- **NodeEditor:** `Hrot/Editor/Hrot.NodeEditor*/` (find via graph)
- **Tests:** Find via graph search

### Report Submission
`.dev/blueprint-fixes-1/reports/BATCH-04-REPORT.md`  
Questions: `.dev/blueprint-fixes-1/questions/BATCH-04-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW

For **each task** in sequence:
1. **Define success condition** before implementing
2. **Implement the fix**
3. **Write tests** -- actual behavioral verification
4. **Run all tests** -- ALL must pass
5. **Fix failures at root cause** -- iterate until green
6. Only then move to next task

No stopping to ask permission. Write report only when all done and all tests pass.

---

## Context

BPF-018 is a Critical runtime crash: `SubtreeAssetIds` is never populated by `TreeCompiler`, causing `IndexOutOfRangeException` when a behavior tree tries to call a subtree. The BTree debug session never symbolicates its overlay (BPF-026), `EmitComposite` has a stray separator (BPF-027), and trace events carry `Guid.Empty` VisualId (BPF-045). NodeEditor drag ops bypass the undo stack entirely.

---

## ✅ BTree Tasks

### Task 1: BPF-018 -- SubtreeAssetIds never populated (CRITICAL)

**Task Definition:** [BPF-018](../TASK-DETAIL.md#bpf-018----btree-subtreeassetids-never-populated---projection-indexoutofrangeexception-emitter-writes-a-guid-where-a-tree-name-is-required-btree-host)

**Success Condition (define before implementing):**  
After compiling a behavior tree with a subtree node, `BehaviorTreeBlob.SubtreeAssetIds` must be populated with the asset IDs of the referenced subtrees. The emitter must write the tree name (not a Guid string) for the subtree node. Tests must verify populated `SubtreeAssetIds` with correct IDs and the correct name in the emitted code.

**What to do:**
1. Read `TreeCompiler` and the BTree Host Design to understand how subtree nodes should populate `SubtreeAssetIds`.
2. Fix `TreeCompiler` to populate `SubtreeAssetIds` during compilation.
3. Fix the emitter to write the subtree tree name (not a Guid).
4. Write a test: compile a tree with a subtree reference; assert `blob.SubtreeAssetIds` is non-empty and contains the correct asset ID; assert the emitted code contains the tree name (not a Guid).

**Tests Required:**
- `SubtreeAssetIds` populated with correct Guid after compilation
- Emitted code contains subtree tree name, not a Guid string

---

### Task 2: BPF-026 -- BTreeDebugSession.Update never symbolicates VisualIds (HIGH)

**Task Definition:** [BPF-026](../TASK-DETAIL.md#bpf-026----btreedebugsessionupdate-never-symbolicates-runningelementidstack-visualids---overlay-shows-nothing-btree-host)

**Success Condition (define before implementing):**  
`BTreeDebugSession.Update` must map the runtime `RunningElementIdStack` (raw indices) to the corresponding `VisualId` Guids using `MachineMetadata` or a similar lookup. Tests must verify the snapshot has populated `VisualId` values (not empty/Guid.Empty).

**What to do:**
1. Read `BTreeDebugSession.Update` and find where `RunningElementIdStack` is read but never symbolicates to `VisualId`.
2. Read the BTree host design for the symbolication contract.
3. Fix to resolve runtime indices to `VisualId` Guids via the metadata lookup.
4. Write a test with a known `RunningElementIdStack` value and known metadata; assert the snapshot `VisualIds` contain the expected Guid.

**Tests Required:**
- Snapshot `VisualIds` mapped correctly from runtime index via metadata

---

### Task 3: BPF-027 -- EmitComposite stray separator produces invalid C# (HIGH)

**Task Definition:** [BPF-027](../TASK-DETAIL.md#bpf-027----emitcomposite-emits-a-stray-separator-producing-invalid-c-for-non-empty-composites-btree-host)

**Success Condition (define before implementing):**  
`EmitComposite` must not emit a stray separator (trailing comma or leading comma) when the composite has children. The emitted C# for a composite with children must be syntactically valid (compilable). Tests must verify no stray separator in emitted code for non-empty composites.

**What to do:**
1. Find `EmitComposite` in the BTree emitter.
2. Fix the separator logic (comma join vs stray trailing/leading separator).
3. Write a test: emit a composite with 2+ children; assert emitted code has no stray comma; if possible, compile the emitted code and assert success.

**Tests Required:**
- Non-empty composite emits no stray separator
- Ideally: emitted code compiles without error

---

### Task 4: BPF-045 -- BTree trace events carry Guid.Empty NodeVisualId (MEDIUM)

**Task Definition:** [BPF-045](../TASK-DETAIL.md#bpf-045----btree-trace-events-carry-guidempty-nodevisualid---status-glyphs--async-badges-never-draw-btree-host)

**Success Condition (define before implementing):**  
BTree trace events must carry the correct `NodeVisualId` Guid corresponding to the node that was executed, not `Guid.Empty`. Tests must verify the trace event's `NodeVisualId` matches the expected node.

**What to do:**
1. Find where BTree trace events are created and where `NodeVisualId` is assigned as `Guid.Empty`.
2. Fix to populate the correct `NodeVisualId` from the metadata/blob mapping.
3. Write a test verifying a trace event for a known node contains the correct `NodeVisualId`.

**Tests Required:**
- Trace event `NodeVisualId` is non-empty and matches expected node Guid

---

## ✅ NodeEditor Tasks

### Task 5: BPF-028 -- Drag node ops bypass undo stack (HIGH)

**Task Definition:** [BPF-028](../TASK-DETAIL.md#bpf-028----drag-based-node-ops-call-viewcommandsapply-directly-bypassing-the-undo-stack-nodeeditor)

**Success Condition (define before implementing):**  
All drag-based node operations must use `Commands.Execute(...)` (which pushes to the undo stack), not `Commands.Apply(...)` (which bypasses it). After a drag, `Undo()` must reverse the drag.

**What to do:**
1. Find all call sites of `ViewCommands.Apply` in the NodeEditor drag handling code.
2. Change to `Commands.Execute` (or the equivalent undo-stack-aware method).
3. Write a test: perform a drag on a node; assert the undo stack has a new entry; call Undo; assert the node is back at its original position.

**Tests Required:**
- Drag adds entry to undo stack
- Undo after drag restores node position

---

### Task 6: BPF-029 -- Multi-select drag emits N ChangeParent commands (HIGH)

**Task Definition:** [BPF-029](../TASK-DETAIL.md#bpf-029----multi-selection-drag-emits-n-separate-changeparent-commands-instead-of-one-changeparentmultiple-nodeeditor)

**Success Condition (define before implementing):**  
Multi-select drag must emit one `ChangeParentMultiple` command covering all selected nodes, not N separate `ChangeParent` commands. Tests must verify one undo entry for a multi-node drag.

**What to do:**
1. Find the multi-select drag handler.
2. Replace the N-commands pattern with a single `ChangeParentMultiple` command.
3. Write a test: drag 3 selected nodes; assert exactly 1 undo entry was created (not 3).

**Tests Required:**
- Multi-node drag adds exactly 1 undo entry
- Undo restores all N nodes atomically

---

### Task 7: BPF-030 -- Missing ancestor-suppression during multi-select drag (HIGH)

**Task Definition:** [BPF-030](../TASK-DETAIL.md#bpf-030----missing-ancestor-in-selection-suppression---child-of-a-selected-container-moves-twice-as-far-nodeeditor)

**Success Condition (define before implementing):**  
When a container and its child are both selected and dragged, only the container's move should be applied (ancestor suppression). The child must NOT be moved independently, preventing a double-move. Tests must verify the child ends up at the correct position.

**What to do:**
1. Find the drag handler and add ancestor suppression logic (filter out nodes whose parent is also selected).
2. Write a test: drag a container + its child together; assert the child is at the expected position (moved once, not twice).

**Tests Required:**
- Child of selected container moves once (not twice) during multi-select drag

---

### Task 8: BPF-047 -- ChildOrderDeterminismTests use List stub (MEDIUM)

**Task Definition:** [BPF-047](../TASK-DETAIL.md#bpf-047----childorderdeterminismtests-test-a-list-backed-stub-not-any-production-model-nodeeditor)

**Success Condition (define before implementing):**  
`ChildOrderDeterminismTests` must test the production NodeEditor model (not a `List<>` stub). Tests must exercise the actual model's child-order determinism.

**What to do:**
1. Read the current `ChildOrderDeterminismTests`.
2. Replace the `List<>` stub with the production `NodeEditorModel` (or equivalent).
3. Verify the tests actually exercise production child-order behavior.

**Tests Required:**
- Tests use production model, not a stub
- Child order is deterministic across model re-constructions

---

### Task 9: BPF-048 -- No tests for drag undo entries / ancestor suppression (MEDIUM)

**Task Definition:** [BPF-048](../TASK-DETAIL.md#bpf-048----no-test-covers-drag-produced-undo-entries-or-ancestor-suppression-nodeeditor)

This is largely covered by the tests written for BPF-028, BPF-029, and BPF-030. Confirm all three behavioral scenarios (drag undo entry, multi-drag single-undo, ancestor suppression) are covered.

---

## 🧪 Testing Requirements

- Run all tests after each task including previously passing tests
- Tests must verify actual behavior (populated IDs, undo entry count, node positions)
- For BTree tasks: if emitted code is C#, prefer compilation tests over string-presence assertions

## ⚠️ Quality Standards

- **NOT ACCEPTABLE:** `Assert.True(blob.SubtreeAssetIds.Length > 0)` -- must assert the correct asset ID
- **REQUIRED:** `Assert.Equal(expectedSubtreeAssetId, blob.SubtreeAssetIds[0])`
- **NOT ACCEPTABLE:** `Assert.NotEqual(Guid.Empty, snap.VisualIds[0])` -- must match expected Guid
- **REQUIRED:** `Assert.Equal(expectedNodeVisualId, snap.VisualIds[0])`

## 📊 Report Requirements

Submit `.dev/blueprint-fixes-1/reports/BATCH-04-REPORT.md`.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BPF-018: SubtreeAssetIds populated; emitter writes tree name; tests pass
- [ ] BPF-026: BTreeDebugSession symbolicates VisualIds; test passes
- [ ] BPF-027: EmitComposite no stray separator; test passes
- [ ] BPF-045: Trace events have correct NodeVisualId; test passes
- [ ] BPF-028: Drag uses Execute (undo stack); undo test passes
- [ ] BPF-029: Multi-drag emits 1 ChangeParentMultiple; single-undo-entry test passes
- [ ] BPF-030: Ancestor suppression implemented; child position test passes
- [ ] BPF-047: ChildOrderDeterminismTests use production model
- [ ] BPF-048: All drag/ancestor scenarios covered by tests
- [ ] All pre-existing tests still pass
- [ ] Report submitted

---

## 📚 Reference Materials
- **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` (BPF-018, BPF-026, BPF-027, BPF-028, BPF-029, BPF-030, BPF-045, BPF-047, BPF-048)
- **BTree Host Design:** `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md`
- **BATCH-03 Review:** `.dev/blueprint-fixes-1/reviews/BATCH-03-REVIEW.md`
- **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`
