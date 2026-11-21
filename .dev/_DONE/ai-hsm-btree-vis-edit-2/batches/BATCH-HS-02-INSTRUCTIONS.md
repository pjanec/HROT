# BATCH-HS-02 — Command sink: delete state (full cascade)

**Task:** TASK-HS-02. **One objective only.** Make deleting a state remove it cleanly — from identity maps, with its subtree, and with all incident transitions (and their auto-managed BB1 variables) — leaving no dangling references.

Design ref: TASK-DETAIL.md §TASK-HS-02. Builds on BATCH-HS-01 (which added `HsmAsset.UnregisterState` / `UnregisterTransition`).

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch only the files below. Do NOT change `ApplyAddNode`, `ApplyAddLink`, `ApplySetContainerCollapsed` (those are other tasks), the region/move/reparent handlers, or BTree/other-workstream code.
2. **No cheating to pass build/tests** — no suppressing diagnostics, commenting out code, weakening asserts, excluding files. If blocked, STOP and write the blocker.
3. **Finish without asking** — build + run tests, fix root causes, repeat until `Failed: 0`, then report.
4. **Headless only.** 5. **Tests assert behavior** (counts/refs/maps), not strings. 6. **Litter-free.** 7. **Report = truth.**

## Files
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` — rewrite `ApplyRemoveNodes` (currently only removes the state from its parent's `Children`). Add ONE shared private helper (see below).
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — only if a read accessor is missing (you should already have `AllStates`, `AllTransitions`, `UnregisterState`, `UnregisterTransition`, `BlackboardVariables`, `RemoveVariable`). Do NOT add new mutators beyond what HS-01 created.
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkDeleteStateTests.cs` (new).

## Behavior — ApplyRemoveNodes

For each `NodeId` in `cmd.Nodes`, resolve the state via `FindStateByStableId`. If found, delete it **and its entire subtree** (a composite delete removes its descendants). Concretely:

1. **Collect the subtree** of the target state (the state + all transitive `Children`). Use an explicit stack/queue with a visited set (defensive against cycles).
2. **Remove all incident transitions** for every state in that set — both:
   - outgoing (`state.OutgoingTransitions`), and
   - incoming (any transition in `_asset.AllTransitions` whose `Target` is in the set).
   Snapshot the transition list before mutating. Route each removal through the shared helper below so identity maps + BB1 vars are handled. (A transition fully inside the deleted subtree will be caught once; dedupe by `VisualId` / reference so you don't double-remove.)
3. **Unregister every state** in the set via `_asset.UnregisterState(state)` (this also detaches each from its parent's `Children`).

Order: remove transitions first, then unregister states.

### Shared helper (add it; HS-04 will reuse it)
```csharp
// Removes a transition: BB1 auto-managed variable cleanup (mirrors the existing
// ApplyRemoveLinks logic) + full identity-map removal via UnregisterTransition.
private void RemoveTransitionInternal(TransitionNode transition)
{
    if (!string.IsNullOrEmpty(transition.ExpressionTargetField))
    {
        var varEntry = _asset.BlackboardVariables
            .FirstOrDefault(v => v.Name == transition.ExpressionTargetField);
        if (varEntry is { IsAutoManaged: true })
            _asset.RemoveVariable(transition.ExpressionTargetField);
    }
    _asset.UnregisterTransition(transition.VisualId);
}
```
Use this helper in `ApplyRemoveNodes`. **Do not** change `ApplyRemoveLinks` in this batch (HS-04 will refactor it to call this helper).

> NOTE on StateNode: states do NOT carry an `ExpressionTargetField` (only transitions do — confirm by reading `StateNode`). So state deletion needs no per-state variable cleanup; only the incident-transition cleanup above.

## Tests (`Hrot.Hsm.Editor.Tests`, new file)
Reuse the `BuildTestAsset()` / sink pattern from `HsmCommandSinkCreateStateTests.cs` (build via `AddNode` + `ChangeParent`, or construct an asset directly with pre-wired states/transitions). Assert:
1. **Delete leaf** → gone from `AllStates`, `FindStateByStableId` returns null, removed from parent's `Children`.
2. **Delete composite** → the composite AND all its descendants are gone from `AllStates`/maps.
3. **Incident transitions removed** — set up `A → B`; delete `B`; the transition is gone from `AllTransitions`, `FindTransitionByVisualId` returns null, and it's no longer in `A.OutgoingTransitions` (no dangling Target). Also test deleting the *source* `A` removes the same transition.
4. **No dangling references** after a composite delete that contained a transition between two children.
5. (If feasible) a transition with an `IsAutoManaged` `ExpressionTargetField` variable → deleting an endpoint removes that variable; a non-auto-managed variable is preserved.

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 390 passed. List any pre-existing failures and confirm 0 new.

## Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-02-REPORT.md`
The new ApplyRemoveNodes algorithm; the shared helper; test names + assertions; before/after counts; anything not done. Do not commit.
