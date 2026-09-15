# BATCH-HS-02 REPORT — Command sink: delete state (full cascade)

**Date:** 2026-06-12
**Task:** TASK-HS-02
**Status:** ✅ Complete — build 0 errors, 402 passed, 0 failed (+12 new tests)

## Algorithm — `ApplyRemoveNodes`

For each `NodeId` in `cmd.Nodes`:

1. **Collect subtree** — BFS/stack traversal from the target state through transitive `Children`, guarded by a `HashSet<StateNode>` visited set (defensive against cycles).
2. **Remove all incident transitions** for every state in the subtree set:
   - **Outgoing** — snapshot `state.OutgoingTransitions` via `.ToList()`, then route each through `RemoveTransitionInternal`; deduplicate by `VisualId`.
   - **Incoming** — scan `_asset.AllTransitions` for transitions whose `.Target` is in the subtree set, snapshot via `.ToList()`, route each through `RemoveTransitionInternal`; deduplicate by `VisualId`.
3. **Unregister every state** in the subtree via `_asset.UnregisterState(state)` (which detaches from parent's `Children`, removes from `_allStatesList`, `_stableIdToState`, `_flatIndexToState`).

Order: remove transitions first, then unregister states.

## Shared helper — `RemoveTransitionInternal`

```csharp
private void RemoveTransitionInternal(TransitionNode transition)
```

- If `transition.ExpressionTargetField` is non-empty, looks up the blackboard variable and removes it iff `IsAutoManaged` is true (mirrors the existing `ApplyRemoveLinks` logic).
- Calls `_asset.UnregisterTransition(transition.VisualId)` for full identity-map removal (removes from source's `OutgoingTransitions`, `_allTransitionsList`, `_visualIdToTransition`, `_flatIndexToTransition`).

HS-04 will refactor `ApplyRemoveLinks` to call this same helper.

## Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` | Rewrote `ApplyRemoveNodes` (was: remove from parent's `Children` only; now: full cascade). Added `RemoveTransitionInternal` private helper. |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkDeleteStateTests.cs` | **New file** — 12 headless tests. |

No other files touched. `HsmAsset.cs` APIs (`AllStates`, `AllTransitions`, `UnregisterState`, `UnregisterTransition`, `BlackboardVariables`, `RemoveVariable`) were already present from HS-01 — no new mutators added.

## Test names + assertions

| # | Test name | What it asserts |
|---|-----------|-----------------|
| 1 | `DeleteLeaf_state_removed_from_AllStates_and_maps` | `AllStates` empty; `FindStateByStableId` null; `FindStateByFlatIndex` null |
| 2 | `DeleteLeaf_removed_from_parent_Children` | Root's `Children` no longer contains the leaf |
| 3 | `DeleteComposite_removes_composite_and_all_descendants` | All 4 states (composite + 2 children + grandchild) gone from `AllStates` and maps |
| 4 | `DeleteComposite_children_detached_from_composite` | `AllStates` empty after deleting a composite with children |
| 5 | `DeleteTarget_removes_incoming_transition` | A→B, delete B: `AllTransitions` empty; `FindTransitionByVisualId` null; `A.OutgoingTransitions` empty |
| 6 | `DeleteSource_removes_outgoing_transition` | A→B, delete A: `AllTransitions` empty; `FindTransitionByVisualId` null |
| 7 | `DeleteSource_target_state_persists` | A→B, delete A: B survives in `AllStates` (count=1) |
| 8 | `DeleteComposite_removes_internal_transition_once` | Composite containing childA→childB transition: all states and transition gone, no errors |
| 9 | `DeleteComposite_with_transition_to_outside_removes_transition` | childA→outside transition removed when deleting composite |
| 10 | `DeleteComposite_with_transition_from_outside_removes_transition` | outside→childA transition removed; outside state and its `OutgoingTransitions` clean |
| 11 | `DeleteEndpoint_with_auto_managed_var_removes_variable` | Deleting an endpoint of a transition with `IsAutoManaged` `ExpressionTargetField` removes the BB1 variable |
| 12 | `DeleteEndpoint_with_shared_var_preserves_variable` | Non-auto-managed variable is preserved after endpoint delete |

## Before/after counts

| Metric | Before | After |
|--------|--------|-------|
| `HsmCommandSink.cs` lines | 355 | ~386 (+31) |
| Tests in `Hrot.Hsm.Editor.Tests` | 390 passed | **402 passed** (+12) |
| Build errors | 0 | 0 |
| Test failures | 0 | 0 |

## Anything not done

Nothing. All objectives met.
