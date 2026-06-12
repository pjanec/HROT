# BATCH-HS-04 REPORT — Command sink: delete transition + container collapse

**Task:** TASK-HS-04. **Status:** Done. **Date:** 2026-06-13.

## Summary

Two changes to `HsmCommandSink.cs`, one new test file (9 tests). 417 passed, 0 failed (baseline 408; 0 new failures; 0 pre-existing failures).

## Part A — ApplyRemoveLinks refactor (latent bug fix)

**What changed:** Replaced the inlined logic in `ApplyRemoveLinks` (BB1 auto-var cleanup + `Source?.OutgoingTransitions.Remove`) with a single delegate call to the shared `RemoveTransitionInternal` helper (added in HS-02).

**Bug fixed:** The old path removed the transition only from the source's `OutgoingTransitions` list. It did NOT call `UnregisterTransition`, so `_visualIdToTransition` and `_flatIndexToTransition` still held the deleted transition. A subsequent `FindTransitionByVisualId` would resolve a dangling reference. The fix routes every removal through `RemoveTransitionInternal` → `UnregisterTransition`, which removes from both identity maps and the outgoing list — the same path used by `ApplyRemoveNodes`.

**Fields/internals used:**
- `_asset.FindTransitionByVisualId(linkId.Value)` — `LinkId.Value` is `Guid`
- Helper `RemoveTransitionInternal(transition)` — does BB1 `IsAutoManaged` var check + `_asset.UnregisterTransition(transition.VisualId)` (both maps + source outgoing list removal)

## Part B — ApplySetContainerCollapsed

**What changed:** Replaced the `{ /* TODO */ }` stub with a real implementation.

**Field names** (from `GraphCommand.SetContainerCollapsed` record):
- `ContainerId` (type `NodeId`) — which state to collapse/expand
- `IsCollapsed` (type `bool`) — the new value

**Implementation:**
```csharp
private void ApplySetContainerCollapsed(GraphCommand.SetContainerCollapsed cmd)
{
    var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
    if (state is not null)
        state.IsCollapsed = cmd.IsCollapsed;
}
```

`StateNode.IsCollapsed` is a settable editor-only property (persisted in the layout method).

## Test file

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkDeleteTransitionTests.cs`

Reuses helpers from `HsmCommandSinkDeleteStateTests.cs` (`BuildTestAsset`, `RegisterState`, `RegisterTransition`, `AddAutoManagedVar`, `AddSharedVar`).

### Tests (9 total)

| # | Test name | Assertions |
|---|-----------|------------|
| 1 | `RemoveLinks_removes_transition_from_AllTransitions` | A→B, RemoveLinks → `AllTransitions` empty |
| 2 | `RemoveLinks_transition_unresolvable_by_visual_id` | A→B, RemoveLinks → `FindTransitionByVisualId` returns null (regression guard for the latent map bug) |
| 3 | `RemoveLinks_transition_removed_from_source_OutgoingTransitions` | A→B, RemoveLinks → `A.OutgoingTransitions` empty |
| 4 | `RemoveLinks_source_and_target_states_survive` | After transition removal, A and B still in `AllStates` |
| 5 | `RemoveLinks_with_auto_managed_var_removes_variable` | Auto-managed var → removed from `BlackboardVariables` |
| 6 | `RemoveLinks_with_shared_var_preserves_variable` | Non-auto-managed var → preserved in `BlackboardVariables` |
| 7 | `RemoveLinks_unknown_id_no_throw` | Unknown visual ID → no throw, model unchanged |
| 8 | `SetContainerCollapsed_sets_IsCollapsed_to_true` | `IsCollapsed` false → command true → `IsCollapsed` true |
| 9 | `SetContainerCollapsed_sets_IsCollapsed_to_false` | `IsCollapsed` true → command false → `IsCollapsed` false |

## Before/after counts

| Metric | Before | After |
|--------|--------|-------|
| `HsmCommandSink.cs` lines | 433 | 425 |
| Tests passed | 408 | 417 |
| New tests | — | 9 |
| Tests failed | 0 | 0 |
| Build errors | 0 | 0 |

## Nothing not done

All items completed.
