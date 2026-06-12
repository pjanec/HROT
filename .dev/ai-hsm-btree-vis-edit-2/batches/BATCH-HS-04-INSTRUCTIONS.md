# BATCH-HS-04 — Command sink: delete transition + container collapse

**Task:** TASK-HS-04. **One objective only** (two small related sink methods). (a) Make deleting a transition fully remove it from the identity maps (not just the source's outgoing list); (b) implement `ApplySetContainerCollapsed`.

Design ref: TASK-DETAIL.md §TASK-HS-04. Builds on BATCH-HS-02 (the shared `RemoveTransitionInternal` helper) and HS-01 (`UnregisterTransition`).

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch only the files below. Do NOT change `ApplyAddNode`/`ApplyAddLink`/`ApplyRemoveNodes`/region/move handlers (except the small `ApplyRemoveLinks` refactor named here).
2. **No cheating to pass.** If blocked, STOP + write the blocker.
3. **Finish without asking** — build + test until `Failed: 0`, then report.
4. **Headless only.** 5. **Tests assert behavior** (maps/flags/counts), not strings. 6. **Litter-free.** 7. **Report = truth.**

## Files
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` — refactor `ApplyRemoveLinks`; implement `ApplySetContainerCollapsed`.
- Read: `GraphCommand.RemoveLinks` and `GraphCommand.SetContainerCollapsed` records (confirm field names).
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkDeleteTransitionTests.cs` (new).

## Part A — ApplyRemoveLinks (refactor onto the shared helper)

The current body does BB1 auto-var cleanup + `transition.Source?.OutgoingTransitions.Remove(transition)` but does NOT remove the transition from the identity maps (`_visualIdToTransition`/`_flatIndexToTransition`) — so a deleted transition still resolves via `FindTransitionByVisualId` (a latent dangling-reference bug). Replace the body so each removal goes through the existing `RemoveTransitionInternal` helper (added in HS-02), which does the BB1 var cleanup AND `UnregisterTransition` (full map + outgoing-list removal):
```csharp
private void ApplyRemoveLinks(GraphCommand.RemoveLinks cmd)
{
    foreach (var linkId in cmd.Links)
    {
        var transition = _asset.FindTransitionByVisualId(linkId.Value);
        if (transition is null) continue;
        RemoveTransitionInternal(transition);
    }
}
```
Do NOT duplicate the BB1 logic — it already lives in `RemoveTransitionInternal`. Confirm that helper exists (HS-02); if for any reason it doesn't, STOP and report (do not re-create it differently).

## Part B — ApplySetContainerCollapsed

Replace the `{ /* TODO */ }`:
```csharp
private void ApplySetContainerCollapsed(GraphCommand.SetContainerCollapsed cmd)
{
    var state = _asset.FindStateByStableId(/* cmd's container/node id */.Value);
    if (state is not null)
        state.IsCollapsed = /* cmd's collapsed bool */;
}
```
Read the `GraphCommand.SetContainerCollapsed` record for the exact field names (likely a `NodeId`/`ContainerId` and a `bool Collapsed`). `StateNode.IsCollapsed` is a settable editor-only property (persisted in the layout method).

## Tests (`Hrot.Hsm.Editor.Tests`, new file)
Reuse the `BuildTestAsset()`/`RegisterState`/`RegisterTransition` helpers (see `HsmCommandSinkDeleteStateTests.cs`). Assert:
1. **RemoveLinks removes the transition fully:** create `A→B`; `RemoveLinks([t.VisualId])` → gone from `AllTransitions`, `FindTransitionByVisualId` returns null, and removed from `A.OutgoingTransitions`. (The map-null assertion is the regression guard for the latent bug.)
2. **States survive:** after removing the transition, A and B still exist in `AllStates`.
3. **BB1 var cleanup:** transition with an `IsAutoManaged` `ExpressionTargetField` var → RemoveLinks removes the variable; a non-auto-managed var with the same field is preserved (two sub-tests or one each).
4. **RemoveLinks unknown id:** no throw, model unchanged.
5. **SetContainerCollapsed:** `SetContainerCollapsed(state, true)` → `state.IsCollapsed == true`; then `false` → `IsCollapsed == false`.

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 408 passed. List pre-existing failures; confirm 0 new.

## Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-04-REPORT.md`
The ApplyRemoveLinks refactor (and the map-removal bug it fixes); ApplySetContainerCollapsed + the field names used; test names + assertions; before/after counts; anything not done. Do not commit.
