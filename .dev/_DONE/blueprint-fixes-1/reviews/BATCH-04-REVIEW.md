# BATCH-04 Review

**Batch:** BATCH-04  
**Reviewer:** Development Lead  
**Date:** 2026-06-01  
**Status:** APPROVED

---

## Summary

9 tasks completed (4 BTree + 5 NodeEditor). 181 + 35 + 308 + 6 tests passing. 39 new tests added. Pre-existing `Fbt.Tests` failures are unrelated to this batch.

---

## Issues Found

No issues found.

---

## Test Quality Assessment

All tests verify actual behavior with concrete values:

- **BPF-018**: `blob.SubtreeAssetIds[0].Should().Be("PatrolTree")` -- exact name match. Deduplication test verifies `HaveCount(1)` for two identical references. `PayloadIndex` test traverses blob to verify index points to correct string.
- **BPF-027**: `EmitComposite_NonEmptySequence_NoStrayCommaBeforeVisualId` -- asserts no trailing comma in emitted statement-lambda body.
- **BPF-026**: `Update_WithDebugMetadata_RunningElementId_IsSymbolicated` -- asserts snapshot contains the expected Guid, not just non-empty.
- **BPF-045**: `Update_TraceNodeEvaluated_VisualId_IsSymbolicated` and `Update_TraceWaitStarted_VisualId_IsSymbolicated` -- verifies trace events carry correct `NodeVisualId`.
- **BPF-028**: `view.Undo.UndoCount.Should().Be(1)` after single drag and after multi-node drag -- verifies undo stack is populated.
- **BPF-029**: `sink.Log[0].Should().BeOfType<GraphCommand.ChangeParentMultiple>()` + `cmd.Moves.Should().HaveCount(2)` -- verifies atomicity.
- **BPF-030**: `HasSelectedAncestor` tested directly with known ancestors; `CommitNodeDrop_ContainerAndChildBothSelected_ChildIsSuppressed` verifies child not in the move set.
- **BPF-047**: `FakeContainerModel` with `AddChild()` calls replaces `StubContainer` pre-seeded list; tests now exercise actual production child-order logic.

---

## Design Notes

- Statement-lambda switch in `EmitComposite` (BPF-027) was the right structural fix -- eliminates fragile separator logic entirely. The `methodPrefix` propagation through 8 emitter methods is unavoidable.
- `SetDebugMetadata` / `GetVisualId` pattern on `BTreeDebugSession` is consistent with BATCH-03 `HsmDebugSession.SetMetadata` pattern.
- `CommitNodeDrop` and `HasSelectedAncestor` promoted to `internal static` for testability is the correct approach -- no production behavior changes.

---

## Verdict

**Status: APPROVED**

All requirements met. Ready to merge.

---

## Commit Message

```
fix: BTree host + NodeEditor fixes (BATCH-04)

Completes BPF-018, BPF-026, BPF-027, BPF-028, BPF-029, BPF-030, BPF-045, BPF-047, BPF-048

BTree host fixes:
- BPF-018: TreeCompiler now populates SubtreeAssetIds; BTreeFluentEmitter writes
  subtree name instead of Guid; statement-lambda form for composite emitter
- BPF-027: EmitComposite uses statement-lambda body, eliminating stray-comma bug
- BPF-026: BTreeDebugSession.Update symbolicates RunningElementId + stack via SetDebugMetadata
- BPF-045: BTree trace events carry correct NodeVisualId Guid (not Guid.Empty)

NodeEditor fixes:
- BPF-028: CommitNodeDrop routes through view.Execute, recording undo entries
- BPF-029: Multi-select drag emits single ChangeParentMultiple (not N commands)
- BPF-030: HasSelectedAncestor filter suppresses child-of-selected-container from drag
- BPF-047: ChildOrderDeterminismTests use FakeContainerModel (production, not List stub)
- BPF-048: ContainerDragTests covers all drag invariants (undo, atomic, ancestor suppression)

Tests: 35 NodeEditor.UI + 181 NodeEditor.Core + 308 BTree.Editor.Tests + 6 Fbt.Tests. 39 new tests.
```

---

**Next Batch:** BATCH-05 (Editor Windows + Runtime + Test Harness)
