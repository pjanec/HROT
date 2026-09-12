# BATCH-02 REPORT — Node colors by kind (BTree)

**Task:** TASK-BT-02 · **Phase:** A · **Date:** 2026-06-12

## Summary

Changed `BTreeNodeModel.Category` from the hardcoded constant `NodeCategory.FlowControl` to a switch expression over the node's `KernelType`, implementing the EB-B mapping table. Wrote a headless `[Theory]` test covering all 9 required mappings.

## Changes

### Production code

**`Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs:87`**

```diff
-    public NodeCategory        Category      => NodeCategory.FlowControl;
+    public NodeCategory        Category      => _node.KernelType switch
+        {
+            Fbt.NodeType.Action           => NodeCategory.Function,
+            Fbt.NodeType.Wait             => NodeCategory.Function,
+            Fbt.NodeType.Condition        => NodeCategory.Pure,
+            Fbt.NodeType.Subtree          => NodeCategory.Macro,
+            // Composites (Root, Sequence, Selector, ObserverSelector, Parallel)
+            // and unknown types map to FlowControl.
+            _                             => NodeCategory.FlowControl,
+        };
```

- Only `Category` was changed — `State`, `Title`, `Kind`, `Pins`, and all other members are untouched.
- Decorators (Inverter, Repeater, etc.) are not handled because they render as pills/attachments, not nodes. They fall through to the default `FlowControl`.

### Test code

**`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Model/BTreeNodeCategoryTests.cs`** (new file)

Single `[Theory]` with 9 `[InlineData]` rows:

| KernelType | Expected Category |
|---|---|
| `Root` | `FlowControl` |
| `Sequence` | `FlowControl` |
| `Selector` | `FlowControl` |
| `ObserverSelector` | `FlowControl` |
| `Parallel` | `FlowControl` |
| `Action` | `Function` |
| `Wait` | `Function` |
| `Condition` | `Pure` |
| `Subtree` | `Macro` |

Each row: builds a `BehaviorTreeAsset` containing a single `BTreeEditorNode` of the given `KernelType`, wraps it in `BTreeGraphModel`, reads the sole `INodeModel` from `.Nodes`, and asserts `.Category == expected`.

## Build & Test Results

```
dotnet build IOS-IG-SimHost.sln → Build succeeded. 0 Error(s)

dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests →
  Passed! - Failed: 0, Passed: 458, Skipped: 0, Total: 458, Duration: 218 ms
```

All 9 new category tests passed (contributing to the 458 total). No regressions.

## Success criteria

- [x] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`
- [x] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — Failed: 0
- [x] `BTreeNodeModel.Category` reflects `KernelType` per the EB-B table
- [x] No other member of `BTreeNodeModel` changed
- [x] Report written
