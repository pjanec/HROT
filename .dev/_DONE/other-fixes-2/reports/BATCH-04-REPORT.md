# BATCH-04 Report: BTree Debug Overlay Wiring & Async Badge Rendering

**Batch:** BATCH-04
**Tasks:** FIX2-007, FIX2-013
**Status:** COMPLETE -- all tasks done, all tests pass

---

## Task Status

| Task | Title | Status |
|------|-------|--------|
| FIX2-007 | Wire `BTreeAssetContributor` -> `SetDebugMetadata` | DONE |
| FIX2-013 | Add async-badge render path to `BTreeRuntimeOverlayRenderer` | DONE |

---

## Test Results

### BTree Editor Tests (primary suite for this batch)

```
dotnet test Hrot\Subsystems\AI\Hrot.BTree.Editor.Tests\Hrot.BTree.Editor.Tests.csproj --nologo

Passed!  - Failed: 0, Passed: 319, Skipped: 0, Total: 319, Duration: 204 ms
```

Breakdown:
- 308 pre-existing tests (all still passing)
- 4 new tests for FIX2-007 (`BTreeContributorDebugSessionTests`)
- 7 new tests for FIX2-013 (`BTreeRuntimeOverlayRendererTests`)
- Total new: 11

### Blueprints Tests (regression check)

```
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj
    --filter "FullyQualifiedName!~AllocationFree" --nologo

Passed!  - Failed: 0, Passed: 886, Skipped: 8, Total: 894, Duration: 42 s
```

No regressions. The 8 skipped tests are pre-existing (demo ALC tests that require special conditions).

---

## Changes Made

### FIX2-007 -- `BTreeAssetContributor` -> `SetDebugMetadata` wiring

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs`

Root cause: `LoadFrom()` projected blobs into `BehaviorTreeAsset` objects but never called
`session.SetDebugMetadata(blob.DebugMetadata, assetId)`. The debug session had no metadata,
so `GetCurrentStateSnapshot()` could never symbolicate running node indices into visual GUIDs.

Changes:
- Added `BTreeDebugSession? _debugSession` field.
- Added optional `BTreeDebugSession? debugSession = null` constructor parameter.
- Extracted private `RegisterBlobCore(blob, treeName, assetId, layout, ns)` helper that performs
  both the asset projection and the `_debugSession?.SetDebugMetadata(...)` call.
- Added public `RegisterBlob(blob, treeName, layout, ns)` method that generates the asset ID via
  `AssetIdHasher` and delegates to `RegisterBlobCore` -- used by tests and future callers.
- Refactored `LoadFrom()` to delegate to `RegisterBlobCore` for the same wiring logic.

**Test file:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Debug/BTreeContributorDebugSessionTests.cs`

4 tests:
1. `RegisterBlob_AfterUpdate_RunningElementId_MatchesSymbolicatedVisualId` -- production path
   end-to-end: registers blob, sets up ECS entity with `BrainBTreeState.RunningNodeIndex=0`,
   calls `session.Update(world, entity)`, asserts `RunningElementId` equals the expected GUID.
2. `RegisterBlob_WithoutSession_DoesNotThrow` -- null-session defensive path.
3. `RegisterBlob_WithNullMetadata_SessionSymbolicationIsCleared` -- clearing metadata.
4. `RegisterBlob_TwiceWithDifferentMetadata_SessionUsesLatest` -- second call replaces first.

### FIX2-013 -- Async-badge render path in `BTreeRuntimeOverlayRenderer`

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeRuntimeOverlayRenderer.cs`

Root cause: `Render()` had three sections but design SS12.4 step 4 (async-pending clock-icon
badges) was entirely absent. No async events were ever rendered.

Changes:
- Added `using System.Collections.Generic` and `using System.Runtime.CompilerServices`.
- Added `private static readonly Vector4 AsyncBadgeColor` field (cyan-blue).
- Added `internal List<Guid> LastRenderedAsyncBadgeNodeIds { get; } = new()` observable property.
  Used by headless tests to assert which nodes received badges without a live ImGui context.
- Moved `LastRenderedAsyncBadgeNodeIds.Clear()` to the very start of `Render()`, BEFORE the
  null-snapshot early-return, so the list is always cleared on each call regardless of session
  state (fixes `Render_ResetsAsyncBadgeList_OnEachCall`).
- Added section 4: iterates `GetRecentAsyncHistory()`, filters by `Phase == Issued` and
  `AssetId == snapshot.AssetId`, adds matching node IDs to `LastRenderedAsyncBadgeNodeIds`,
  calls `DrawAsyncBadge(ctx, node.Position)`.
- Added `DrawAsyncBadge()` private static method: computes screen position from
  `DefaultNodeSize` offset, guards null DrawList via `Unsafe.As<ImDrawListPtr, nint>`,
  calls `dl.AddText(...)` with the "o" badge glyph.
- Added null-DrawList guard to `DrawNodeOutline()` (same pattern as `DrawAsyncBadge`) to
  prevent `AccessViolationException` in headless tests that provide a running-node snapshot.

**Test file:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Renderers/BTreeRuntimeOverlayRendererTests.cs`

7 tests:
1. `Render_WithNoSession_DoesNotThrow` -- null session defensive path.
2. `Render_WithNullSnapshot_DoesNotPopulateBadges` -- null snapshot early-return.
3. `Render_WithIssuedAsyncEvent_DrawsAsyncBadge_ForMatchingNode` -- main production-path test.
4. `Render_WithResolvedAsyncEvent_DoesNotDrawAsyncBadge` -- only `Issued` phase earns a badge.
5. `Render_WithMixedAsyncEvents_DrawsBadgesOnlyForIssued` -- phase filtering.
6. `Render_AsyncEventForDifferentAsset_IsFiltered` -- asset ID filtering.
7. `Render_ResetsAsyncBadgeList_OnEachCall` -- observable cleared at every `Render()` call.

---

## Issues Encountered and How Resolved

### 1. Missing `using Fdp.Toolkit.Behavior.Diagnostics` in test file

`BTreeTraceWorkingMemory1024` lives in `Fdp.Toolkit.Behavior.Diagnostics` (not
`Fdp.Toolkit.Behavior.Components`). Added the missing using directive.

### 2. `AccessViolationException` in `DrawNodeOutline` during headless tests

The test `Render_WithIssuedAsyncEvent_DrawsAsyncBadge_ForMatchingNode` passed a snapshot with
`RunningElementId` set, triggering section 1 which called `DrawNodeOutline`. That method called
`ImGui.GetColorU32()` via native P/Invoke without an initialized ImGui context.

Fix: Added the same null-DrawList guard (`Unsafe.As<ImDrawListPtr, nint>(ref dl) == 0`) to
`DrawNodeOutline` that `DrawAsyncBadge` already had. This is safe -- the production renderer
always runs inside a live ImGui frame where the DrawList is non-null.

### 3. `LastRenderedAsyncBadgeNodeIds` not cleared on null-snapshot early return

`Render_ResetsAsyncBadgeList_OnEachCall` verified that the list is cleared even when the second
render hits the null-snapshot early-return path. The initial code placed `Clear()` after the
null check. Moved `Clear()` to before the null check so it executes on every `Render()` call.

---

## Design Decisions Beyond Spec

1. **Concrete type `BTreeDebugSession?` (not interface) for the contributor constructor.**
   `SetDebugMetadata` is declared only on the concrete `BTreeDebugSession` class, not on
   `IBTreeDebugSession`. Using the concrete type avoids casting at the call site and is
   consistent with the existing `BTreeDebugSession`-typed usage elsewhere in the editor.

2. **`RegisterBlob()` public method added for testability.**
   The spec required testing via the production contributor path. `LoadFrom()` uses reflection
   over assemblies, which is unsuitable for unit tests. `RegisterBlob()` allows a test to hand
   a pre-built `BehaviorTreeBlob` directly to the contributor. Both `LoadFrom()` and
   `RegisterBlob()` share `RegisterBlobCore()` to guarantee identical wiring.

3. **`LastRenderedAsyncBadgeNodeIds` exposes node GUIDs (not a count).**
   A count alone would not let tests distinguish between "node A got a badge" and "node B got a
   badge". The list enables precise assertions for the asset-filter and phase-filter tests.

4. **`DrawNodeOutline` null-DrawList guard added as a production improvement.**
   This was not in the original spec but was required to make headless tests work correctly.
   It also makes the production code more defensive -- if called outside a frame for any reason,
   it silently skips drawing rather than crashing.

---

## Edge Cases Discovered

- Null-snapshot early-return must not skip `LastRenderedAsyncBadgeNodeIds.Clear()`, otherwise
  a render with a live session followed by a render with a dead session leaves stale badge IDs
  in the observable list.

- `ImGui.GetColorU32()` is a native P/Invoke that crashes with
  `System.AccessViolationException` when called without an initialized ImGui context -- even
  when the subsequent `DrawList.AddRect()` would be safely guarded. The guard must be placed
  BEFORE any ImGui native call in the draw helper.

---

## Suggested Commit Message

```
fix: wire BTreeAssetContributor->SetDebugMetadata and add async-badge overlay (FIX2-007, FIX2-013)

FIX2-007: BTreeAssetContributor now accepts an optional BTreeDebugSession and
calls SetDebugMetadata(blob.DebugMetadata, assetId) for each loaded blob.
Added RegisterBlob() public method for programmatic asset registration.
Both LoadFrom() and RegisterBlob() share RegisterBlobCore() which performs
the projection and session wiring.

FIX2-013: BTreeRuntimeOverlayRenderer.Render() now includes a 4th section
(per design SS12.4 step 4) that iterates GetRecentAsyncHistory() and calls
DrawAsyncBadge() for each Issued (pending) async event matching the current
asset. Added LastRenderedAsyncBadgeNodeIds observable list for headless test
assertions. Moved Clear() before null-snapshot early-return so the list is
always reset. Added null-DrawList guard to DrawNodeOutline() for consistency
and headless safety.

Tests: +11 (BTreeContributorDebugSessionTests x4, BTreeRuntimeOverlayRendererTests x7)
BTree Editor suite: 319 passed, 0 failed
Blueprints suite: 886 passed, 0 failed
```
