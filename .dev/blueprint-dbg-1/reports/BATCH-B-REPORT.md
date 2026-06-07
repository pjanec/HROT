# BATCH-B Report

## Implementation Summary

**Batch:** BATCH-B — Runtime overlay (executing-node highlight)
**Status:** Complete (headless gates pass)

### What was built

1. **Created `BlueprintRuntimeOverlayRenderer`** — mirrors `BTreeRuntimeOverlayRenderer`. Renders at `AfterNodes` pass:
   - Gold outline on the most recently executing node (from `GetRecentNodeHistory(1)`)
   - Red outline on the paused-at node (when `IsPaused` and `PausedAt != null`)
   - Dim blue dots on recently executed nodes (history trail, up to 10 dots, suppressed at low zoom)
   - `IsActive == false` when session null (zero per-frame cost)
   - `LastExecutingNodeId` / `LastPausedNodeId` observables for headless test verification

2. **Wired into `BlueprintDocumentFactory.BuildRenderers`** — AfterNodes pass, between gutter renderer and WhenFiringPulseRenderer. Session is set via `SetSession(debugSession)`.

## Design Decisions

- **Simplified from BTree pattern:** BTree renderer draws stack ancestry outlines and status glyphs (OK/X/~). Blueprint `NodeExecuted` records don't carry status, and the call stack (`GetCurrentCallStack`) is peer-call frames (not node execution stack). So the blueprint overlay focuses on: executing-node highlight, pause highlight, and history-trail dots.

- **Used `GetRecentNodeHistory(1)` for executing node** instead of extending `BlueprintStateSnapshot` with an `ExecutingNodeId` field. The session already tracks history in `OnNodeEnter`. This avoids changing the core debug session API for this batch. A proper `ExecutingNodeId` on the snapshot can be added later if needed for richer UX (e.g., stack ancestry).

- **`FindNode` parses string→Guid** since `NodeExecuted.NodeIdString` is the string form and serialized `Node.Id` is Guid.

## Deviations

Minor scope reduction from BTree parity:
- No status glyphs (OK/X/~) — `NodeExecuted` lacks a `Status` field. Can be added when blueprint runtime reports node outcomes.
- No stack ancestry outlines — `GetCurrentCallStack()` returns peer-call frames, not a node execution stack. Different semantics.
- No async badges — blueprints don't have the async concept (yet).

These are documented as known limitations for a future batch.

## Test Results

### New tests (5 tests, all pass)
- `RuntimeOverlay_IsActive_False_WhenNullSession`
- `RuntimeOverlay_IsActive_True_WhenSessionSet`
- `RuntimeOverlay_Id_IsStable`
- `RuntimeOverlay_Pass_IsAfterNodes`
- `RuntimeOverlay_SetSession_Null_MakesInactive`

### Full suite
- **Hrot.Blueprints.Tests:** 1666 passed, 1 failed (pre-existing `AllocationFreeTests`), 8 skipped — **0 new failures**.

## Developer Insights

- **`GetRecentNodeHistory` returns `Guid.Empty` for `NodeId`:** The `NodeExecuted` record has `Guid NodeId` but the session passes `Guid.Empty` for it (the real node id is in `NodeIdString`). The renderer parses `NodeIdString` to find nodes. This is a known pattern — the session's history uses string node ids internally.

- **Paused-at node uses `PausedAt.NodeId`** which is also a string — same parsing approach works.

## Known Issues

- **User interactive smoke is PENDING.** Headless tests verify wiring; visual rendering (gold pulse, red outline, history dots) needs user verification on the live canvas.

- **No "currently executing" distinction during normal tick.** During normal execution (not paused), `GetRecentNodeHistory(1)` returns the most recent node, which is close to but not exactly "currently executing" — it's "just executed". True "currently executing" would require the session to track enter/exit pairs. Acceptable for Slice-1.

## Suggested Commit Message

```
feat: blueprint runtime overlay renderer (BATCH-B)

Adds BlueprintRuntimeOverlayRenderer — gold outline on executing node,
red outline on paused-at node, history-trail dots on recently executed
nodes. Wired into BuildRenderers (AfterNodes, after gutter renderer).

- BlueprintRuntimeOverlayRenderer: ICustomCanvasRenderer, reads
  GetRecentNodeHistory/PausedAt from IBlueprintDebugSession
- BuildRenderers: creates and wires runtime overlay with SetSession

Tests: 5 tests covering IsActive, Id, Pass, SetSession lifecycle
VISUAL/INTERACTIVE VERIFICATION PENDING
```
