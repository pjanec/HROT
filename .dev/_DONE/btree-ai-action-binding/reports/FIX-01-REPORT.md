# FIX-01 Report

## Implementation Summary

### Fix A (P0 crash): variable-row drag source attaches to a no-ID item

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`, method `DrawTable`.

**Exact reorder performed:**

Before the fix, the `else` (non-renaming) branch emitted:
1. `ImGui.Selectable(...)` — has an item ID
2. `if (IsItemHovered) { ... }` — reads last item (Selectable) correctly
3. `if (row.Comment is not null) { ImGui.SameLine(); ImGui.TextDisabled(...); }` — submits a no-ID item
4. `if (BeginDragDropSource(...))` — CRASH: last item is TextDisabled, which has no ID

After the fix:
1. `ImGui.Selectable(...)` — has an item ID
2. `if (IsItemHovered) { ... }` — reads last item (Selectable) correctly
3. `}` closes the `else` block
4. `if (!isRenaming && !schema.IsReadOnly && BeginDragDropSource(...))` — last item is Selectable (ID-bearing)
5. `if (!schema.IsReadOnly && BeginDragDropTarget())` — attaches to same Selectable
6. `if (!isRenaming && row.Comment is not null) { ImGui.SameLine(); ImGui.TextDisabled(...); }` — emitted last, after drag ops

The `IsItemHovered`/double-click-rename block was kept immediately after `Selectable` inside the `else` block (unchanged). The `if (!isRenaming)` guard on the new comment block prevents the InputText rename path from accidentally emitting a comment.

**Audit of other no-ID drag/popup sources in the same file:**

- `DrawTable` alias sub-rows (lines ~386-402): uses `BeginPopupContextItem("##alias_ctx")` after `TextColored`. Since an explicit non-null string ID is provided, Dear ImGui uses that string as the popup ID (not the last item's ID) — hit detection uses the last item's rect (which TextColored does populate via `ItemSize`/`ItemAdd`). This is NOT the same as `BeginDragDropSource` which unconditionally requires an ID-bearing last item. No fix needed.
- `DrawNodeOwnedTable`: uses only `TextUnformatted` + `IsItemHovered` for tooltip. No drag/popup. No issue.
- The UNBOUND-requirements section: already fixed prior to this batch (uses `Selectable`). Confirmed correct.

**No other `BeginDragDropSource` or `BeginPopupContextItem` with preceding no-ID items found.**

### Fix B (aggregator double-counts locally-bound nodes)

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/BTreeBlackboardAggregatorStrategy.cs`, method `Aggregate`.

**Change:** Added an `isLocallyBound` check inside the `if (fqn != null)` block. Before emitting a `DtoRequirement`, the aggregator now reads `node.Action?.ExpressionTargetField ?? node.Condition?.ExpressionTargetField`. If that value is non-null and non-empty, the node is locally bound — the requirement emission and schema-not-found warning are both skipped. Subtree recursion is entirely separate (outside the `fqn != null` block) and is unaffected.

**HSM aggregator audit:**

`HsmBlackboardAggregatorStrategy` uses a different binding model. Its `ExpressionTargetField` lives on `TransitionNode` and `GlobalTransitionNode`, not on state action payloads. The aggregator calls `EmitIfFound` with `state.OnEntryAction` / `state.OnExitAction` / `state.ActivityAction` / `state.TimerAction` — these are plain `string? fqn` (no accompanying DTO payload or ExpressionTargetField). The `ExpressionTargetField` on transitions controls whether the TRANSITION's action result is stored (a transition binding, not an action/condition parameter binding). `HsmAsset.cs` line 181 explicitly documents: "Returns 0; HSM does not use ExpressionTargetField in this phase." The HSM binding model is structurally different: there is no per-node `ExpressionTargetField` on state-slot actions in the aggregator's path. **No fix applied to the HSM aggregator.**

### New tests added (Hrot.BTree.Editor.Tests / BTreeBlackboardAggregatorTests)

Five new facts appended to `BTreeBlackboardAggregatorTests`:

| Test | Purpose |
|------|---------|
| `Aggregate_locally_bound_action_node_emits_no_requirement` | Single action with ETF set → 0 requirements |
| `Aggregate_locally_bound_condition_node_emits_no_requirement` | Single condition with ETF set → 0 requirements |
| `Aggregate_T10_mirror_all_locally_bound_yields_zero_requirements` | Mirror T10: condition(counter) + action(counter) + action(accum) all bound → 0 requirements |
| `Aggregate_unbound_action_node_still_emits_requirement` | Action with ETF=null → still produces 1 requirement (regression guard) |
| `Aggregate_locally_bound_parent_subtree_with_unbound_child_still_surfaces_child_requirements` | Locally-bound parent + subtree with unbound child → 1 requirement (child only) |

No existing tests were weakened. All existing tests already used nodes with no `ExpressionTargetField` set (the unbound case) so they remain valid.

## Design Decisions

- Used `!string.IsNullOrEmpty(etf)` (matching the pattern in `BTreeBlackboardVariableContributor.cs` lines 37-38) rather than `!= null` alone, to guard against empty-string ETF being treated as a binding.
- Kept the `isLocallyBound` variable explicit for readability rather than inlining into the `if` condition.
- Did NOT change HSM aggregator — the binding model is structurally different (transition-level vs. node-level).

## Deviations

None. Implementation follows the spec exactly.

## Test Results

| Suite | Filter | Failed | Passed | Skipped | Total |
|-------|--------|--------|--------|---------|-------|
| Hrot.Editor.AiShared.Tests | Stability!=Flaky&Environment&Broken | 0 | 1103 | 0 | 1103 |
| Hrot.BTree.Editor.Tests | Stability!=Flaky&Environment&Broken | 0 | 568 | 0 | 568 |
| Hrot.Hsm.Editor.Tests | Stability!=Flaky&Environment&Broken | 0 | 497 | 0 | 497 |

All suites green. `dotnet build-server shutdown` was run before rebuilding to flush any stale VBCSCompiler cache.

## Developer Insights

- The crash affects every demo variable row because both `counter` and `accum` have comments authored, so the crash was 100% reproducible on click.
- The `IsItemHovered`/double-click-rename block reads from the Selectable as the "last item" — it is critical that this block stays INSIDE the `else` block immediately after `Selectable`. The comment emission is guarded by `!isRenaming` so it is suppressed during active rename (when InputText is the last item instead).
- `BeginDragDropSource` and `BeginDragDropTarget` both work correctly with a preceding Selectable even when they don't submit visible content to the window — they attach invisibly and the comment SameLine/TextDisabled still renders inline next to the row name because SameLine resets the cursor to immediately after the Selectable's rect (not after the invisible drag source/target widgets).
- The HSM `ExpressionTargetField` is only used in the transition delete lifecycle (`HsmCommandSink.cs:249-254`), not in the aggregator. The comment at `HsmAsset.cs:181` confirms this explicitly.

## Known Issues

None. Fix A is not headless-unit-testable (requires a live ImGui context); correctness is verified by code review and compile pass.

## Suggested Commit Message

fix(blackboard): prevent crash on dragging variable rows with comments; skip locally-bound nodes in BTree aggregator
