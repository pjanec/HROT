# BATCH-02 Report

## Summary

Implemented two features in `Fbt.Compiler`:

1. **FBT-003** — Expression-based blackboard parameter binding. Generic `Condition<TValue>` and `Action<TValue>` overloads on `BTreeBuilder<TBlackboard, TContext>` accept an `Expression<Func<TBlackboard, TValue>>` lambda, extract the field name, compute the byte offset once at build time via `Marshal.OffsetOf<TBlackboard>`, and register a curried `NodeLogicDelegate` closure that projects the full blackboard into a `ref TValue` using `Unsafe.As + Unsafe.AddByteOffset`. New file `ReusableDelegates.cs` defines the two delegate types.

2. **FBT-005** — Graph data structures for the future authoring tool. Five classes in `Fbt.Compiler.Graph`: `BehaviorTreeGraph` (root), `BehaviorTreeNode` (abstract), `CompositeNode`, `DecoratorNode`, `LogicNode`. Added `BTreeBuilder.ToGraph(string treeName)` which walks the internal `BuilderEntry` tree and produces the graph hierarchy with correct parent references and round-tripped `VisualId` values from the builder's debug metadata.

---

## Tasks Completed

- [x] FBT-003: Expression-based blackboard offset resolution
- [x] FBT-005: Graph data structures + BTreeBuilder.ToGraph()

---

## Test Results

**Total passing: 108 / 108** (including all 94 existing tests)

```
Passed!  - Failed: 0, Passed: 108, Skipped: 0, Total: 108, Duration: 43 ms - Fbt.Tests.dll (net8.0)
```

New test files:
- `tests/Fbt.Tests/Unit/ExpressionBindingTests.cs` — 7 tests for FBT-003
- `tests/Fbt.Tests/Unit/GraphTests.cs` — 7 tests for FBT-005

**FBT-003 tests:**
| Test | Verifies |
|------|----------|
| `Condition_LambdaFieldSelector_ComputesCorrectByteOffset` | Closure reads from correct byte offset (FieldA=-999, FieldB=5 → Success) |
| `Action_LambdaFieldSelector_MutatesCorrectField` | Closure writes to correct field (AmmoCount 5→4 after one tick) |
| `Condition_FieldB_WhenFieldBPositive_ReturnsSuccess` | FieldB > 0 → Success |
| `Condition_FieldB_WhenFieldBNegative_ReturnsFailure` | FieldB < 0 → Failure |
| `Action_DecrementsAmmo_Correctly` | Two ticks: AmmoCount 5→3 |
| `ExpressionBinding_InvalidExpression_ThrowsArgumentException` | `bb => 42f` throws `ArgumentException` |
| `ExpressionBinding_RegistryKey_IsStableAcrossBuilds` | Same delegate + field twice → 1 deduplicated MethodNames entry |

**FBT-005 tests:**
| Test | Verifies |
|------|----------|
| `ToGraph_SimpleSequence_ProducesCorrectRootNode` | Root is `CompositeNode` with `Type == Sequence` |
| `ToGraph_ChildCount_MatchesBuilderChildren` | Sequence with 2 actions → `Children.Count == 2` |
| `ToGraph_LeafNodes_AreLogicNodes` | Action/Condition → `LogicNode`, correct `NodeType` |
| `ToGraph_AllNodes_HaveUniqueNonEmptyVisualIds` | No `Guid.Empty`, all distinct |
| `ToGraph_ParentRefs_AreCorrect` | Root.Parent == null; child.Parent == root |
| `ToGraph_ExpressionBound_LogicNode_HasTargetFieldName` | `TargetFieldName == "Value"`, `TargetDtoType` non-empty |
| `ToGraph_TreeId_IsNonEmpty` | `graph.TreeId != Guid.Empty` |

---

## Developer Insights

**Q1: Issues encountered and how resolved?**

- **`Assert.Equal(1, collection.Length)` warning** — xUnit analyzer `xUnit2013` flagged this as incorrect idiom even though the test project has no `TreatWarningsAsErrors`. Changed to `Assert.Single(blob.MethodNames)` to eliminate the warning cleanly.

- **`switch` exhaustiveness** — `ConvertToGraphNode` uses a `switch` statement (not expression) on `NodeType`, so no C# exhaustiveness warning fires on the `default` case. The default branch handles `Action` and `Condition` correctly.

- **`Guid.Parse` in `ToGraph`** — `BuilderEntry.Meta.VisualId` is always a valid GUID string (set by `Guid.NewGuid().ToString()` in `BuildMeta`), so `Guid.Parse` is safe without a try-catch.

**Q2: Design decisions beyond the spec?**

- **`ExtractFieldInfo<TValue>` private static helper** — extracted the lambda-walk + `Marshal.OffsetOf` logic into a shared private static to avoid duplication between the `Condition<TValue>` and `Action<TValue>` overloads. The helper carries a `parameterName` parameter so `ArgumentException` names the correct parameter (`fieldSelector`) in both overloads.

- **`TargetDtoType = typeof(TBlackboard).FullName`** — for expression-bound leaves, `TargetDtoType` is set to the full blackboard type name (not the projected `TValue` type). This matches the authoring-tool use case: it identifies which blackboard the delegate belongs to. The `TargetFieldName` identifies which field within that blackboard is projected.

- **`BuilderEntry.TargetFieldName` and `TargetDtoType` as nullable `string?`** — kept them nullable so the `null` check in `ConvertToGraphNode` clearly distinguishes expression-bound from regular leaves (`entry.TargetDtoType ?? string.Empty`).

- **`CompositeNode.CustomComment` in `ToGraph`** — uses `entry.Meta.CustomComment` (which the builder currently leaves empty). The `Label` field is not duplicated into `CustomComment`; the authoring tool may populate `CustomComment` independently.

**Q3: Weak points or improvement opportunities?**

- **`Marshal.OffsetOf` and non-sequential structs** — `Marshal.OffsetOf` is documented as requiring `[StructLayout(LayoutKind.Sequential)]` or `[StructLayout(LayoutKind.Explicit)]` for predictable behavior. Test blackboards in `ExpressionBindingTests.cs` are explicitly decorated; production blackboards are not enforced at compile time. A Roslyn analyzer warning (future `Fbt.SourceGen` / `Fbt.Attributes`) could flag this.

- **`BTreeBuilder.ToGraph()` before `Compile()` only** — `ToGraph()` operates on `BuilderEntry` trees, not on compiled blobs. If called after `Compile()` on a builder that has already been used to build multiple trees (which is not currently possible since `Compile()` checks for single root), there is no issue. The current invariant is maintained.

- **`DecoratorNode.Duration` heuristic** — `Wait` nodes use `WaitTime`; `Cooldown` nodes use `CooldownTime`. The `Duration` field is set using `WaitTime > 0f ? WaitTime : CooldownTime`. This is correct for current node types but fragile if future decorator types have both durations set. Should be resolved by adding a `Duration` property to `BuilderNode` directly (tech debt P3).

**Q4: Edge cases discovered?**

- **Same delegate used on two different fields** — the registry key `DeclaringType.MethodName@offset` correctly distinguishes them (different offsets → different keys → two entries in `blob.MethodNames`). This was verified by inspection; no dedicated test was added since the spec's deduplication test covers only same delegate+same field.

- **`Guid.Parse` with `Guid.Empty.ToString()`** — if a caller somehow stores `Guid.Empty` as a `VisualId` string in `NodeDebugMetadata`, `Guid.Parse` still succeeds and the graph node gets `Guid.Empty`. The test `ToGraph_AllNodes_HaveUniqueNonEmptyVisualIds` verifies this cannot happen when the builder auto-assigns GUIDs (which it always does via `BuildMeta`).

---

**Suggested commit message:**
```
feat(fluent-btree): BATCH-02 -- FBT-003 expression binding + FBT-005 graph structs

FBT-003: Expression-Based Blackboard Parameter Binding
- Add ReusableDelegates.cs: ReusableConditionDelegate<TValue,TContext> and
  ReusableActionDelegate<TValue,TContext> (mirror NodeLogicDelegate pattern)
- BTreeBuilder: add Condition<TValue> and Action<TValue> overloads accepting
  Expression<Func<TBlackboard,TValue>> field selectors
- Byte offset computed once at build time via Marshal.OffsetOf<TBlackboard>
- Curried closure uses Unsafe.As + Unsafe.AddByteOffset (no unsafe blocks)
- Registry key stable across builds: DeclaringType.MethodName@byteOffset
- 7 new tests in ExpressionBindingTests.cs

FBT-005: Graph Data Structures for Authoring Tool
- Graph/ folder: BehaviorTreeGraph, BehaviorTreeNode, CompositeNode,
  DecoratorNode, LogicNode in namespace Fbt.Compiler.Graph
- BTreeBuilder.ToGraph(string) converts builder entries to graph hierarchy
- BuilderEntry extended with TargetFieldName/TargetDtoType for expression-bound
  leaves; LogicNode carries these for authoring tool field introspection
- 7 new tests in GraphTests.cs

Build: 0 errors, 0 warnings. Tests: 108/108 pass.
```
