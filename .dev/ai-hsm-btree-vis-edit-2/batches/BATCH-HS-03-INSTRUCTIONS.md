# BATCH-HS-03 — Command sink: draw transition

**Task:** TASK-HS-03. **One objective only.** Dragging from one state to another creates a `TransitionNode` (projected as a link), respecting `HsmLinkValidator`.

Design ref: TASK-DETAIL.md §TASK-HS-03; HSM host doc §7 (transitions = links via hidden pins). Builds on BATCH-HS-01 (`HsmAsset.RegisterTransition`).

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch only the files below. Do NOT change `ApplyAddNode`, `ApplyRemoveNodes`, `ApplyRemoveLinks`, `ApplySetContainerCollapsed`, region/move handlers, or BTree/other-workstream code.
2. **No cheating to pass** — no suppressing diagnostics, commenting out code, weakening asserts, excluding files. If blocked, STOP + write the blocker.
3. **Finish without asking** — build + test, fix root causes, until `Failed: 0`, then report.
4. **Headless only.** 5. **Tests assert behavior** (refs/counts/maps/validator-rejection), not strings. 6. **Litter-free.** 7. **Report = truth.**

## Files
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` — implement `ApplyAddLink` (currently `{ /* TODO */ }` at ~line 151).
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — add the two pin→state resolvers (below). No other new mutators.
- Read for convention: `HsmTransitionLink.cs`, `HsmPinModel.cs`, `HsmLinkValidator.cs`, `StateNode` (`HiddenOutputPinId`/`HiddenInputPinId`, `DeriveOutputPinId`/`DeriveInputPinId`), `GraphCommand.AddLink` record, `TransitionNode`, `TransitionKind` enum.
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkTransitionTests.cs` (new).

## Part 1 — HsmAsset pin→state resolvers

The transition link convention (`HsmTransitionLink`): `FromPin = source.HiddenOutputPinId`, `ToPin = target.HiddenInputPinId`. Add to HsmAsset (near the Find* lookups):
```csharp
/// <summary>Resolve the state whose hidden OUTPUT pin matches `pinId` (transition source side).</summary>
public StateNode? FindStateByOutputPin(Guid pinId)
{
    foreach (var s in AllStates)
        if (s.HiddenOutputPinId == pinId) return s;
    return null;
}

/// <summary>Resolve the state whose hidden INPUT pin matches `pinId` (transition target side).</summary>
public StateNode? FindStateByInputPin(Guid pinId)
{
    foreach (var s in AllStates)
        if (s.HiddenInputPinId == pinId) return s;
    return null;
}
```
(O(n) scan is fine for editor-scale machines. Read `StateNode.HiddenOutputPinId`/`HiddenInputPinId` to confirm exact names/types before using.)

## Part 2 — HsmCommandSink.ApplyAddLink

Replace the `{ /* TODO */ }` body:
1. **Validate first.** Construct `new HsmLinkValidator(_asset)` (read its ctor/Validate signature) and call `Validate(cmd.From, cmd.To)`. If the result is NOT valid (read the result type — e.g. `IsValid`/`Verdict`), `return` without creating anything. This enforces: no transition FROM a Final state, no transition INTO a History/DeepHistory pseudo-state.
2. **Resolve endpoints:** `source = _asset.FindStateByOutputPin(cmd.From.Value)`, `target = _asset.FindStateByInputPin(cmd.To.Value)`. If either is null, `return`.
3. **Create the transition:**
   ```csharp
   var t = new TransitionNode
   {
       VisualId = cmd.AssignedId.Value,   // stable across undo/redo
       Source   = source,
       Target   = target,
       Kind     = TransitionKind.External, // normal cross-state transition (confirm enum member)
       EventId  = 0,                        // unbound; author sets event/guard/action later
   };
   _asset.RegisterTransition(t);
   ```
   (`RegisterTransition` already adds it to `source.OutgoingTransitions`, the backing list, and the identity maps, and assigns a collision-free FlatIndex.)
4. The trailing `_asset.MarkDirty()` in `Apply(...)` already fires.

Confirm the exact `GraphCommand.AddLink` field names (`AssignedId`, `From`, `To`) and the `TransitionKind` default member by reading them. If `External` is not the right "normal" member, pick the plain cross-state kind (NOT Internal/History) and note it in the report.

## Tests (`Hrot.Hsm.Editor.Tests`, new file)
Reuse the `BuildTestAsset()`/`RegisterState` helper pattern. Build pin ids from `state.HiddenOutputPinId`/`HiddenInputPinId`. Assert:
1. **Happy path:** two simple states A,B; `AddLink(new LinkId(g), new PinId(A.HiddenOutputPinId), new PinId(B.HiddenInputPinId))` → a `TransitionNode` exists with `Source==A`, `Target==B`, `VisualId==g`; it's in `A.OutgoingTransitions`, in `asset.AllTransitions`, and `FindTransitionByVisualId(g)` resolves.
2. **Graph projection:** build an `HsmGraphModel(asset)` and assert the new transition appears in `graph.Links` with `Id.Value==g` (rebuilds on `Changed`).
3. **Validator reject — Final source:** A `IsFinal=true` → AddLink from A's output → NO transition created (`AllTransitions` unchanged).
4. **Validator reject — History target:** B `IsHistory=true` → AddLink into B's input → NO transition created.
5. **Unresolvable pin:** AddLink with a random pin id → no transition, no throw.

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 402 passed. List any pre-existing failures; confirm 0 new.

## Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-03-REPORT.md`
The resolvers added; the ApplyAddLink flow + the TransitionKind default chosen; the validator result API used; test names + assertions; before/after counts; anything not done. Do not commit.
