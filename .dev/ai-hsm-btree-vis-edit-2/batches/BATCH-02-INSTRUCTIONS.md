# BATCH-02 — Node colors by kind (BTree) **[VISUAL GATE]**

**Task:** TASK-BT-02 (`.dev/ai-hsm-btree-vis-edit-2/TASK-DETAIL.md#task-bt-02--node-colors-by-kind`)
**Phase:** A · **One objective only.**

## 🔒 Working agreement (MANDATORY)
Same as BATCH-01 (see `.dev/ai-hsm-btree-vis-edit-2/TASK-TRACKER.md` "Working agreement"): one task only; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.
**This is a [VISUAL GATE] task:** you implement the mapping and write the **headless** test that asserts the projected `NodeCategory` enum value per kind. You do NOT need to verify pixels — the lead confirms colors in the running editor later.

## 📋 Onboarding
- Design: `docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md` §5 (EB-B); host `docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` §2.
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-02-REPORT.md`.

## 🎯 Objective
`BTreeNodeModel.Category` is hardcoded to `NodeCategory.FlowControl`, so every node renders the same color. Map it from the node's `KernelType` so composites, leaves, and subtrees are visually distinct.

## File (exact)
`Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs` — change `BTreeNodeModel.Category` from the constant to a switch over `_node.KernelType`:

| KernelType | NodeCategory |
|---|---|
| `Root`, `Sequence`, `Selector`, `ObserverSelector`, `Parallel` | `FlowControl` |
| `Action` | `Function` |
| `Wait` | `Function` |
| `Condition` | `Pure` |
| `Subtree` | `Macro` |
| anything else (defensive) | `FlowControl` |

`NodeCategory` is `NodeEditor.Primitives.NodeCategory` (members include `Function, Pure, FlowControl, Macro, Custom`). `NodeType` is `Fbt.NodeType`. Decorators are pills (not nodes) — do not handle them here.

**Touch nothing else.** Do not change `State`, `Title`, pins, or any other member.

## 🧪 Tests (write EXACTLY these; assert the enum value)
New file `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Model/BTreeNodeCategoryTests.cs`. Build a `BehaviorTreeAsset` containing one `BTreeEditorNode` of a given `KernelType`, wrap in `BTreeGraphModel`, read the single `INodeModel` from `.Nodes`, assert `.Category`. (Reuse the asset-construction helper pattern from `Host/BTreeDynamicCatalogTests.cs` — `EmptyBlob()` + `new BehaviorTreeAsset(...)` + `asset.AddNode(node)`.)

- `Category_Sequence_IsFlowControl` → `NodeCategory.FlowControl`
- `Category_Selector_IsFlowControl`, `Category_ObserverSelector_IsFlowControl`, `Category_Parallel_IsFlowControl`, `Category_Root_IsFlowControl`
- `Category_Action_IsFunction` → `NodeCategory.Function`
- `Category_Wait_IsFunction` → `NodeCategory.Function`
- `Category_Condition_IsPure` → `NodeCategory.Pure`
- `Category_Subtree_IsMacro` → `NodeCategory.Macro`

(One `[Theory]` with `[InlineData(NodeType.X, NodeCategory.Y)]` rows is acceptable and preferred — assert `model.Category == expected`.)

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. the new category tests).
- [ ] `BTreeNodeModel.Category` reflects `KernelType` per the table; no other member changed.
- [ ] Report written.

## Notes
- This is purely the projection mapping. Do NOT touch renderers, theme, or colors directly — `NodeCategory` is what the canvas uses to pick the color.
