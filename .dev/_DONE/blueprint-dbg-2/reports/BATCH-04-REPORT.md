# BATCH-04 Report

## Implementation Summary

### Task 1 — Inspector shows per-node state (NGS-2.4a)
**File:** `Hrot.Blueprints.Editor/Inspector/BlueprintRuntimeInspectorPane.cs`

Added a public static helper `ResolveInspectorSnapshot(IBlueprintDebugSession session, Entity entity, Guid assetId)`:
- When `session.IsPaused`, calls `GetCurrentStateSnapshot()` first. That method already returns the virtual-pointer's restored per-node state (or null if the paused entity differs from the selected one).
- Falls back to `CaptureLiveState(entity, assetId)` in all other cases (not paused, or paused entity differs from selected).

`Draw()` calls `ResolveInspectorSnapshot` instead of `CaptureLiveState` directly. An additional private `FormatPausedHint` helper builds the `(paused — node X/N)` label shown in the header when `IsPaused && RecordedNodeCount > 0` (ImGui-only; not unit-tested as it's pure display).

### Task 2 — Node highlight follows virtual pointer (NGS-2.4b)
**File:** `Hrot.Blueprints.Editor/Debug/BlueprintDebugToNodeEditAdapter.cs`

`CurrentlyExecutingNode` priority changed to:
1. **Virtual pointer** (`session.CurrentNodePointer >= 0`): parse `session.CurrentNodeId` → return `NodeId`. This makes the canvas highlight move on every `StepBack`/`StepInto`.
2. **PausedAt node** (existing CF-6 path, used when no recordings exist).
3. **Recent execution history** (existing live-running overlay).

Event wiring: `StepBack`, `StepOver`, `StepInto`, `StepOut` all already call `OnSessionStateChanged?.Invoke()` in `BlueprintDebugSession` (verified in BATCH-03). The adapter already forwards `OnSessionStateChanged` → `StateChanged`, so no additional plumbing was needed.

### Task 3 — Step Back button + position indicator (NGS-2.4c)
**File:** `Hrot.Blueprints.Editor/Debug/DebugStepControls.cs`

- Added `"Step Back"` button before `"Step Over"`. The button is `BeginDisabled`/`EndDisabled` gated when `CurrentNodePointer <= 0` (at the first node or no recordings). Clicking calls `session.StepBack()` and `onStepAction?.Invoke("StepBack")` — same pattern as all other step buttons.
- Added a node-position indicator label `"node X / N"` shown on the same row as `"PAUSED"` when `RecordedNodeCount > 0`.
- Extracted `FormatNodePosition(IBlueprintDebugSession)` as a public static testable helper returning `""` when not paused/no recordings, else `"node {pointer+1} / {count}"`.

## Design Decisions

**`ResolveInspectorSnapshot` null-safety:** `GetCurrentStateSnapshot()` already returns null when `_pausedOnEntity` differs from `entity` (it reads `_pausedOnEntity`, not the passed-in `entity`). Falling back to `CaptureLiveState` covers the cross-entity case cleanly without extra logic.

**`StepBack` button disabled at pointer=0:** Disabling at the boundary is more accurate than hiding — the user can see the button exists but is not applicable yet. The existing step buttons are always shown (not disabled) for a different UX reason (they have the CF-6 fallback). `StepBack` has no fallback, so disabling is correct.

**`FormatPausedHint` private vs public:** The pane-header hint is purely decorative and duplicates `FormatNodePosition` logic. Kept private (not testable via unit tests), while `FormatNodePosition` in `DebugStepControls` is public and tested. The batch instructions say "ImGui rendering that can't be headlessly tested is fine to leave for the human smoke."

## Deviations

None from the spec. The session already raises `OnSessionStateChanged` on every pointer move (BATCH-03 implemented this), so no changes to `BlueprintDebugSession` were needed.

## Test Results

### New tests: `NodeGranularEditorUITests.cs` — 17 tests, all pass

| Class | Tests | Coverage |
|---|---|---|
| `InspectorSnapshotResolutionTests` | 4 | NGS-2.4a: A=0 at ptr 0; A=10 at ptr 2; sequence 0→0→10; null after Continue |
| `HighlightFollowsPointerTests` | 5 | NGS-2.4b: node equals pointer; changes on StepBack; changes on StepInto; StepBack raises event; pointer cleared after Continue |
| `FormatNodePositionTests` | 8 | NGS-2.4c: not paused→empty; no recordings→empty; pointer=0/count=5→"node 1 / 5"; pointer=2→"node 3 / 5"; last node; CF-6 paused+ptr=-1→empty; StepBack records action; single node→"node 1 / 1" |

### Full suite: `Hrot.Blueprints.Tests`
```
Failed: 7, Passed: 1734, Skipped: 8, Total: 1749
```
All 7 failures are the documented pre-existing reds:
- `AiPrimitive_EmitMatchesGoldenSource` ×2
- `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`
- `TickFrame_1000Frames_AllocatesZeroBytes`
- `MoveToAndFire_GeneratedSource_Snapshot`
- `WhenNode_ZeroAllocOnHotPath`

Zero new failures. +18 tests compared to pre-BATCH-04 baseline (1716+1 = 1717 passed without BATCH-04 tests).

### `Hrot.Diagnostics.Breakpoints.Tests`
```
Failed: 0, Passed: 128, Skipped: 0, Total: 128
```

## Seams — unit-tested vs human smoke

| Seam | Tested headlessly | Left for human smoke |
|---|---|---|
| `ResolveInspectorSnapshot` exact values (NGS-2.4a) | ✅ 4 integration tests with real session | — |
| `CurrentlyExecutingNode` tracks pointer (NGS-2.4b) | ✅ 5 tests: equals, changes on StepBack/StepInto, event raised | Canvas highlight color/position — pure ImGui rendering |
| `FormatNodePosition` string values (NGS-2.4c) | ✅ 8 unit tests | Button click (BeginDisabled gate, button render) |
| `StepBack` button rendering | `onStepAction` wiring contract verified via `StepBack_Session_RecordsAction` | Actual ImGui button click path |
| Inspector `"(paused — node X/N)"` header hint | Not tested (private helper, pure display) | Human smoke |

## Developer Insights

1. **No BlueprintDebugSession changes needed.** All three pointer-move methods (`StepBack`, `StepForwardOrCF6`) already call `OnSessionStateChanged?.Invoke()` — added in BATCH-03. The adapter already subscribes to that event. The adapter-StateChanged → canvas-redraw chain was fully wired.

2. **`CaptureLiveState` vs `GetCurrentStateSnapshot` entity mismatch:** The inspector pane receives `entity` (selected entity) and `assetId` (canvas context), while `GetCurrentStateSnapshot()` targets `_pausedOnEntity` (breakpoint entity). When the user selects a different entity than the paused one, `GetCurrentStateSnapshot` returns null and `ResolveInspectorSnapshot` correctly falls back to live state — no special handling needed.

3. **`InspectorSnapshotResolutionTests` are integration tests** (real fixture + compiled blueprint), not pure unit tests. This matches the batch spec requirement for "real BlueprintDebugSession + compiled Sequence A:0→10→20 asset."

4. **Warning CS0618 in `Hrot.Diagnostics.Breakpoints.Tests`:** Pre-existing obsolete `IBlueprintTimeController` usage. Not introduced by BATCH-04.

## Known Issues

- The `FormatPausedHint` in `BlueprintRuntimeInspectorPane.Draw()` is not unit-tested (private, ImGui-gated). This is intentional per the batch spec ("pure-ImGui rendering that can't be headlessly tested is fine to leave for the human smoke").
- Step-past-end tick-bridge (§3.4 in the Addendum) is still not implemented — the batch intentionally excludes it (BATCH-04 scope is UI surfacing only).

## Suggested Commit Message

feat: wire node-granular UI seams — inspector redirect, highlight tracking, Step Back button (NGS-2.4a/b/c)
