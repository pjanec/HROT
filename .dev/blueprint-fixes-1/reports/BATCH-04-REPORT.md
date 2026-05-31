# BATCH-04 Report

**Batch:** BATCH-04  
**Developer:** AI (GitHub Copilot)  
**Date:** 2025-06-01  
**Status:** Complete

---

## Task Completion

| Task ID  | Status | Notes |
|----------|--------|-------|
| BPF-018  | Done   | `TreeCompiler` populates `SubtreeAssetIds`; emitter uses `SubtreeName` not Guid |
| BPF-026  | Done   | `BTreeDebugSession.Update` resolves runtime indices to VisualId Guids via `_debugMetadata` |
| BPF-027  | Done   | `EmitComposite` uses statement-lambda form; stray comma before `visualId` arg eliminated |
| BPF-028  | Done   | `CommitNodeDrop` routes through `view.Execute` so the move lands on the undo stack |
| BPF-029  | Done   | All selected-node moves collected into a single `ChangeParentMultiple` command |
| BPF-030  | Done   | `HasSelectedAncestor` filter skips nodes whose ancestor container is also selected |
| BPF-045  | Done   | Trace polling uses `_assetId` and `GetVisualId(rec->NodeIndex)` instead of `Guid.Empty` |
| BPF-047  | Done   | `ChildOrderDeterminismTests` replaced `StubContainer` (pre-seeded list) with `FakeContainerModel` (AddChild-based) |
| BPF-048  | Done   | Covered by `ContainerDragTests`: all three drag invariants (undo, single-command, ancestor suppression) are tested |

---

## Testing Results

**NodeEditor.Core.Tests:** 181 passed, 0 failed  
**NodeEditor.UI.Tests:** 35 passed, 0 failed (10 new tests: BPF-028/029/030/048)  
**Fbt.Tests (BPF-018 filter):** 6 passed, 0 failed  
**Hrot.BTree.Editor.Tests:** 308 passed, 0 failed (8 new tests: BPF-026/027/045)

> Note: `Fbt.Tests` has pre-existing failures in unrelated test classes (`DtoTooLarge_ThrowsBehaviorTreeBuildException`, `GeneratedRegistrar_RegisterAll_PopulatesRegistry`, `SharedAiGeneratorTests`, `AutoDiscoveryTests`). These are pre-existing failures in unmodified test files; none are in `TreeCompilerSubtreeTests.cs` or any file touched by this batch.

**New tests added this batch:**

BPF-018 (`TreeCompilerSubtreeTests.cs`, 6 tests):
- `Compile_TreeWithSubtreeNode_SubtreeAssetIds_IsNonEmpty`
- `Compile_TreeWithSubtreeNode_SubtreeAssetIds_ContainsCorrectId`
- `Compile_TreeWithTwoDistinctSubtreeNodes_SubtreeAssetIds_HasTwoEntries`
- `Compile_TreeWithDuplicateSubtreeReference_SubtreeAssetIds_DeduplicatesId`
- `Compile_TreeWithNoSubtrees_SubtreeAssetIds_IsEmpty`
- `Compile_SubtreeName_WrittenToPayload_NotGuid`

BPF-026/027/045 (`BTreeFluentEmitterEmitTests.cs` and `BTreeDebugSessionSymbolicationTests.cs`, 15 tests):
- `EmitSubtree_WritesSubtreeName_NotGuidString` (BPF-018 emitter)
- `EmitSubtree_Escape_SubtreeName_Is_Quoted` (BPF-018 emitter)
- `EmitComposite_NonEmptySequence_UsesStatementLambda` (BPF-027)
- `EmitComposite_NonEmptySequence_NoStrayCommaBeforeVisualId` (BPF-027)
- `EmitComposite_EmptySequence_EmitsCorrectly` (BPF-027)
- `EmitComposite_WithPills_FirstPillUsesMethodPrefix` (BPF-027)
- `EmitComposite_ChildrenAreStatements_SemicolonTerminated` (BPF-027)
- `Update_WithDebugMetadata_RunningElementId_IsSymbolicated` (BPF-026)
- `Update_WithNoMetadata_RunningElementId_IsNull` (BPF-026)
- `Update_StackIds_AreSymbolicated` (BPF-026)
- `Update_AssetId_SetFromSetDebugMetadata` (BPF-026)
- `TrySymbolicateIndex_OutOfBounds_ReturnsNull` (BPF-026)
- `TrySymbolicateIndex_InvalidGuidFormat_ReturnsNull` (BPF-026)
- `Update_TraceNodeEvaluated_VisualId_IsSymbolicated` (BPF-045)
- `Update_TraceWaitStarted_VisualId_IsSymbolicated` (BPF-045)

BPF-028/029/030/048 (`ContainerDragTests.cs`, 10 tests):
- `CommitNodeDrop_SingleRootNode_PushesOneUndoEntry` (BPF-028)
- `CommitNodeDrop_TwoRootNodes_StillPushesOneUndoEntry` (BPF-028)
- `CommitNodeDrop_TwoNodes_EmitsSingleChangeParentMultiple` (BPF-029)
- `CommitNodeDrop_MovesContainAllSelectedNodeIds` (BPF-029)
- `HasSelectedAncestor_ContainerParentInSet_ReturnsTrue` (BPF-030)
- `HasSelectedAncestor_ContainerNotInSet_ReturnsFalse` (BPF-030)
- `HasSelectedAncestor_RootNode_ReturnsFalse` (BPF-030)
- `CommitNodeDrop_ContainerAndChildBothSelected_ChildIsSuppressed` (BPF-030)
- `CommitNodeDrop_ThreeNodes_SatisfiesAllThreeInvariants` (BPF-048)

BPF-047 (`ChildOrderDeterminismTests.cs`, existing test file modified):
- Replaced `StubContainer` (pre-seeded `IEnumerable<NodeId>`) with `FakeContainerModel` (AddChild-based insertion model); updated all tests to call `AddChild()` and assert actual count values.

---

## Changed Files

| File | Task | Change |
|------|------|--------|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` | BPF-018 | Added `subtreeAssetIds` list to `FlattenToBlobCore`; added `NodeType.Subtree` case in `FlattenRecursive` calling `GetOrAddSubtreeId`; added `SubtreeAssetIds = subtreeAssetIds.ToArray()` to blob; added `GetOrAddSubtreeId` helper |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` | BPF-018, BPF-027 | Fixed `EmitSubtree` to use `p.SubtreeName` instead of `p.SubtreeAssetId:D`; switched `EmitComposite` from expression lambda to statement lambda form; removed stray comma line before `visualId`; added `methodPrefix` parameter chain through `BuildNodeContent`, `EmitComposite`, `EmitChildNode`, `EmitLeafWithPills`, `BuildDecoratorOpen`, `EmitAction`, `EmitCondition`, `EmitWait`, `EmitSubtree` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` | BPF-026, BPF-045 | Added `_debugMetadata` and `_assetId` fields; added `SetDebugMetadata(metadata, assetId)` method; added `GetVisualId(nodeIndex)` private method; added `TrySymbolicateIndex(nodeIndex)` internal test hook; fixed `Update` snapshot section to use `GetVisualId`; fixed trace polling to use `_assetId` and `GetVisualId(rec->NodeIndex) ?? Guid.Empty` |
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs` | BPF-028, BPF-029, BPF-030 | Replaced `CommitNodeDrop` with new implementation using `view.Execute(forward, inverse, "Move nodes")`; collects all moves into `List<ChangeParentMove>` and emits single `GraphCommand.ChangeParentMultiple`; added `HasSelectedAncestor` filter; changed `CommitNodeDrop` and `HasSelectedAncestor` from `private static` to `internal static` for testability |
| `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Serialization/ChildOrderDeterminismTests.cs` | BPF-047 | Replaced `StubContainer` (pre-seeded list) with `FakeContainerModel` (AddChild-based); updated all tests to call `c.AddChild(id)` and assert actual `ChildNodeIds.Count` |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj` | BPF-018 | Added `FluentAssertions 6.12.0` package reference (required for new `TreeCompilerSubtreeTests`) |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/TreeCompilerSubtreeTests.cs` | BPF-018 | New test file: 6 tests for `SubtreeAssetIds` population and emitter name correctness |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeFluentEmitterEmitTests.cs` | BPF-018, BPF-027 | New test file: 7 tests for emitter subtree name and statement-lambda composite |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Debug/BTreeDebugSessionSymbolicationTests.cs` | BPF-026, BPF-045 | New test file: 8 tests for debug session symbolication |
| `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/Canvas/ContainerDragTests.cs` | BPF-028, BPF-029, BPF-030, BPF-048 | New test file: 10 tests for drag/undo invariants |

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three design issues surfaced during this batch:

1. **BPF-027 statement-lambda vs. expression-lambda form.** The original `EmitComposite` used expression-lambda form (`seq => seq.Action(...).Condition(...)`) where children were chained with method calls. The separator logic (`,` vs. `;` for last child) was broken because a stray comma line was emitted before the `visualId` argument, producing invalid C# (`);,`). The fix was to switch entirely to the statement-lambda form (`seq => { seq.Action(...); seq.Condition(...); }`). In statement form all children are statements terminated with `;`, eliminating separator logic. A `methodPrefix` parameter was added to propagate the correct receiver prefix (`"."` for chain form, `"seq."` for statement form) through the emitter call chain.

2. **`CommitNodeDrop` not directly testable.** The method was `private static`. To enable direct unit testing without an ImGui frame context, `CommitNodeDrop` and `HasSelectedAncestor` were changed to `internal static`, and `NodeEditor.UI.csproj` already had `InternalsVisibleTo("NodeEditor.UI.Tests")`. Tests then set up `GraphView` state (`Selection`, `Interaction.DragOverridePositions`, `Interaction.DropTargetContainerId`) directly and called `CanvasInput.CommitNodeDrop(view, input)`.

3. **`Fbt.Tests` missing `FluentAssertions` reference.** The project used `xunit` Assert assertions; the new `TreeCompilerSubtreeTests` uses FluentAssertions. Added `FluentAssertions 6.12.0` to `Fbt.Tests.csproj`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `BTreeDebugSession` had no mechanism to receive symbolication metadata from the host; the debug overlay was wired to display data but `AssetId`, `RunningElementId`, and all `StackElementIds` were hardcoded as `Guid.Empty`. The fix (a `SetDebugMetadata` method + `GetVisualId` helper) mirrors the pattern established for `HsmDebugSession` in BATCH-03.
- The `EmitComposite` method mixed two code-generation styles (expression-chain vs. statement-block) without clear separation, which made the separator logic brittle. A dedicated `BuildStatementLambdaBody` helper could clarify this further, but was not introduced as it would exceed the scope of the fix.
