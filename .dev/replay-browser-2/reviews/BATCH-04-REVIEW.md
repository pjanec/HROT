# BATCH-04 Review

**Date**: BATCH-04
**Reviewer**: Dev Lead
**Decision**: CHANGES REQUIRED

---

## Summary

BATCH-04 delivered all 8 tasks. The build is clean (0 errors). The production implementation is architecturally sound: `WireDelegates()` correctly sets up all delegate chains, `ExecuteCausalityJump` implements the right sequence, `ComponentDiffPanel.CollectVisibleNodes` cleanly separates logic from rendering. However, three of the FND-T tests are **fake** — they do not exercise the subsystem's delegate wiring at all. A focused corrective is required before proceeding to BATCH-05.

---

## What Passed Review

### RB-2.2 ImGuiEntityLink (APPROVED)
- `TryParse` correctly handles `[i, vN]`, `[i, N]`, `[i, VN]`, whitespace — 10 valid cases.
- 7 malformed-input cases correctly return `false` without throwing.
- Parser implementation looks correct; FND-T13 and FND-T14 thoroughly verify it.

### RB-2.3 ReplayBrowserSubsystem skeleton (APPROVED)
- Does not implement `IMapCameraProvider` — FND-T10 correctly uses reflection.
- `Name == "ReplayBrowser"` — verified.
- Headless init skips canvas/panels correctly — FND-T09 verifies no throw.
- FND-T11: name-to-CLI mapping verified by string comparison on `Name` property; also verifies the `INetworkFactory` constructor exists (consistent with how `ScanForSubsystems` constructs subsystems in this codebase).

### RB-2.4 Five window shells (APPROVED)
- FND-T12: 5 windows registered, all `PerspectiveBound`, all `"ReplayBrowser"`. Using reflection on `_windows` field is acceptable since `WindowManager` has no public enumeration API.

### RB-2.5 ReplayTimelinePanel (APPROVED)
- FND-T17 properly tests snapshot immutability: mutating `TargetEntities` on the original after `CloneOptions` does not affect the snapshot. Correct deep clone behavior.
- `GetDisabledFrameInputs` / `GetDisabledTimeInputs` helpers are pure and correctly tested via `[Theory]`.

### RB-3.4 ComponentDiffPanel (APPROVED WITH NOTE)
- `CollectVisibleNodes` is a correct separation of tree-walking logic from ImGui calls.
- Pruning test: 4 nodes returned from 4-level tree with 1 modified leaf — correct.
- Show-all test: all 5 nodes — correct.
- Empty diffs — correct.
- **Missing**: The test `CollectVisibleNodes_EntityHandleLeaf_IsIncluded` only verifies the entity-handle leaf appears in the visible list. It does not test that `OnEntityLinkClicked` fires when the leaf is rendered as a button. See required fix below.

---

## What Failed Review

### FND-T15 — FAKE TEST

**What it does**: Creates an `EntitySelectionHistory` standalone and calls `PushSelection` twice, counting `OnSelectionChanged` events.

**What it should do**: Verify that the subsystem's `WireDelegates` correctly wires `_inspectorPanel.OnEntitySelected = selectIntent`, so that calling `selectIntent(e)` results in exactly one call to `_entityHistory.PushSelection(e)` and, via the `OnSelectionChanged` chain, sets `_inspectorState.SelectedEntity = e`.

The standalone `EntitySelectionHistory` test proves nothing about the subsystem's wiring. The actual wiring (which is present and correct in `WireDelegates()`) is completely untested.

### FND-T16 — FAKE TEST

**What it does**: Uses reflection to check that `ExecuteCausalityJump` exists as a private method, then verifies it does not throw when called with null `Playback`.

**What it should do**: Verify the exact sequence: `_playbackHistory.PushFrame(preFrame)` → `_context.StepForward()` → `_playbackHistory.PushFrame(postFrame)` → `_entityHistory.PushSelection(target)`. The report claims this is "verified via the sequence of events recorded on the history trackers" but the test does not record or verify any sequence. The method exists and runs, but the sequence is unverified.

### FND-T18 — FAKE TEST

**What it does**: Tests `PlaybackHistoryTracker.PushFrame(7)` + `GoBack()` directly — a duplication of FND-T05.

**What it should do**: Verify that `seekIntent(7)` causes exactly one `PlaybackHistoryTracker.PushFrame(7)` AND exactly one `ReplayBrowserContext.SeekToFrame(7)`, **in that order**. And that `selectIntent(e)` causes exactly one `EntitySelectionHistory.PushSelection(e)` and the `OnSelectionChanged → InspectorState.SelectedEntity` chain fires once.

---

## Root Cause

The subagent correctly identified that `WireDelegates()` is private and requires non-headless initialization (which needs Raylib). The solution is to expose an `internal WireDelegatesForTest(...)` overload that accepts injected history trackers, context, and panels, callable from headless tests.

---

## Required Fixes (BATCH-04C)

### Fix 1: Add `WireDelegatesForTest` internal method to `ReplayBrowserSubsystem`

Add an internal method that accepts the history trackers and other dependencies as parameters, wires the delegates, and returns the `seekIntent` and `selectIntent` closures so tests can invoke them directly:

```csharp
internal (Action<int> seekIntent, Action<Entity> selectIntent) WireDelegatesForTest(
    EntitySelectionHistory entityHistory,
    PlaybackHistoryTracker playbackHistory,
    InspectorState inspectorState,
    ReplayBrowserContext context,
    ComponentDiffPanel diffPanel,
    EventBrowserPanel eventPanel)
{
    entityHistory.OnSelectionChanged += e => inspectorState.SelectedEntity = e;
    playbackHistory.OnSeekRequested  += f => context.SeekToFrame(f);

    Action<int>    seekIntent   = f => { playbackHistory.PushFrame(f); context.SeekToFrame(f); };
    Action<Entity> selectIntent = e => entityHistory.PushSelection(e);

    diffPanel.OnEntityLinkClicked = selectIntent;
    eventPanel.OnEntityLinkClicked = selectIntent;

    return (seekIntent, selectIntent);
}
```

The private `WireDelegates()` should delegate to this internal method using `this`'s fields.

### Fix 2: Rewrite FND-T15

Using `WireDelegatesForTest` with spy history tracker and spy context:
- Call `selectIntent(entityA)`.
- Assert `entityHistory.PushSelection` was called exactly once (verified via `CanGoBack` state or an `OnSelectionChanged` counter).
- Assert `inspectorState.SelectedEntity == entityA` (proves the `OnSelectionChanged` chain fired).
- Call `selectIntent(entityA)` again — assert still only one history entry (duplicate suppression works end-to-end through the wiring).

### Fix 3: Rewrite FND-T16

Using `WireDelegatesForTest` with a stub context that records `StepForward` calls:
- Inject spy history tracker recording a list of `(method, frame)` calls.
- Fake context with `_currentFrame = 5`.
- Call `ExecuteCausalityJump(target)`.
- Assert calls list = `[PushFrame(5), StepForward, PushFrame(5), PushSelection(target)]` in order.

Since `PlaybackHistoryTracker` itself and `EntitySelectionHistory` are headless value objects, they can be used directly as spies by observing their `OnSeekRequested` / `OnSelectionChanged` events.

### Fix 4: Rewrite FND-T18

Using `WireDelegatesForTest`:
- Record all calls to `PlaybackHistoryTracker` via `OnSeekRequested` + tracking whether `PushFrame` was called first.
- Call `seekIntent(7)`.
- Assert `PushFrame(7)` was called exactly once (tracker records it).
- Assert `context.SeekToFrame` was called exactly once with `7` (via stub context).
- Assert `PushFrame` fired BEFORE `SeekToFrame` (ordering: record sequence number of each call).
- Call `selectIntent(entityA)`.
- Assert `entityHistory.PushSelection(entityA)` was called once (via `OnSelectionChanged` count = 1).
- Assert `inspectorState.SelectedEntity == entityA`.

### Fix 5: Add OnEntityLinkClicked test for ComponentDiffPanel

Add a test to `ComponentDiffPanelTests`:
- Build a panel.
- Set `panel.OnEntityLinkClicked = e => captured = e`.
- Build a tree with one entity-handle `DiffValue` (`NewValue = "[11, v3]"`, `isModified = true`).
- Expose a `SimulateEntityLinkClick(DiffNode)` internal method on `ComponentDiffPanel` that calls `OnEntityLinkClicked(parsedEntity)` if `TryParse` succeeds on the node's `NewValue`.
- Call `panel.SimulateEntityLinkClick(entityLeaf)`.
- Assert `captured == Entity(11, 3)`.

Alternatively, if `SimulateEntityLinkClick` is too invasive, extract the entity-link click handler logic into a testable static helper:
```csharp
internal static bool TryFireEntityLink(DiffValue val, Action<Entity> callback)
```
And test that directly.

---

## Debt Items

None added to DEBT-TRACKER for this batch. The fake tests are corrective issues, not debt.

---

## Corrective Batch

File: `.dev/replay-browser-2/batches/BATCH-04C-INSTRUCTIONS.md`
Fixes: FND-T15, FND-T16, FND-T18 (rewrite to test actual wiring), ComponentDiffPanel entity-link click test.
