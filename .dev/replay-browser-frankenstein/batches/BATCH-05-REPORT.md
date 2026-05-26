# BATCH-05 Report — Replay Browser Frankenstein

**Batch:** BATCH-05
**Status:** COMPLETE
**Tasks completed:** D05, D06, D07, RBF-P5T1, RBF-P5T2, RBF-P5T3, RBF-P5T4

---

## Summary

All seven tasks from BATCH-05 were implemented and verified. The solution compiles clean (0 errors, 0 warnings). All RBF-P5 tests pass. Two pre-existing regressions introduced during BATCH-05 production-code work were detected and fixed before reporting.

---

## Corrective tasks

### D05 — Harden `OnLoadGroup` exception handling

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

Extended the `OnLoadGroup` catch block to cover `IOException`, `UnauthorizedAccessException`, and `System.Text.Json.JsonException` in addition to `LoadGroupException`. Each exception class returns a user-readable rejection string surfaced through the modal.

Status: RESOLVED.

---

### D06 — Fix `_searchPanel.CurrentFilePath` sourcing in federated mode

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

Replaced the old `_context.CurrentFdpPath` assignment with a manager-driven read:
- In Merged View or when no manager is loaded, `CurrentFilePath = null` (search is disabled, no stale path leaks).
- Otherwise reads `_manager.Contexts[LocalEntitiesProviderNodeId].CurrentFdpPath`.

Status: RESOLVED.

---

### D07 — Cancel seek-to-change task on mode switch to Merged

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

In `SetViewMode`, when switching to `ViewMode.Merged`, `_diffPanel.IsSearching` is set to `false`. The `SeekToNextChangeAsync` loop checks this flag at every frame step and exits on the next iteration, stopping the in-flight search.

Status: RESOLVED.

---

## Feature tasks

### RBF-P5T1 — Excise `ReplayBrowserContext _context` from `ReplayBrowserSubsystem`

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

The `private ReplayBrowserContext _context = null!;` field was removed. Every reference was replaced:

| Old reference | Replacement |
|---|---|
| `_context.SandboxRepo` (gizmo setup) | `_activeRepo` with deferred-init pattern; `RebindGizmoSystems(EntityRepository)` helper |
| `_context.HistoryService` | Lambda: `() => _manager?.Contexts.TryGetValue(nodeId, out c) == true ? c.HistoryService : null` |
| `_context.CurrentFrame` | `PrimaryNodeCurrentFrame()` helper |
| `_context.SeekToFrame(f)` | `SeekFrameViaManager(f)` helper |
| `_context.StepForward()` | `TryStepForwardViaManager()` helper |
| `_context.CurrentFdpPath` | `PrimaryNodeCurrentFdpPath()` helper |
| `_context.Dispose()` | Removed (covered by `_manager?.Dispose()`) |
| `_context.SandboxRepo` in gizmo/selection ticks | `_activeRepo` |

Test seam `WireDelegatesForTest` signature updated to accept `ReplayBrowserContext` as a helper parameter (for tests that still need to construct an `EventBrowserPanel` with a `HistoryService`); the subsystem itself holds zero context fields.

**Tests added to `ReplayBrowserSubsystemTests.cs`:**
- `RBF_P5T1_Subsystem_NoContextField` — reflection asserts no `ReplayBrowserContext` field on the type
- `RBF_P5T1_Subsystem_EmptyManager_NoNullRef` — headless Initialize + Update(0.016f) does not throw
- `RBF_P5T1_SingleNode_SeekViaManager` — load one file, seek via manager, verify `BaseWallTicks` and `ActiveRepo`
- `RBF_P5T1_Merged_SeekRebuildsTransientMaster` — merged view, seek, `TransientBuildOverride` fires, `_activeRepo` changes reference
- `RBF_P5T1_EventBrowser_CurrentFrameProvider_UsesActiveContext` — advance one frame, provider returns updated frame index

**Test results:** 5/5 pass.

---

### RBF-P5T2 — `ReplayTimelinePanel` drives `FederatedReplayManager` directly

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs`

The `private readonly ReplayBrowserContext _context;` field was replaced. Constructor signature changed to:

```csharp
public ReplayTimelinePanel(
    FederatedReplayManager? manager,
    Func<int> getSelectedNodeId,
    IRecordingExportService exportService,
    IFileDialogService fileDialogService,
    PlaybackHistoryTracker playbackHistory,
    InspectorState inspectorState)
```

A private `ActiveContext` property resolves the current node's context from `_manager`. All `_context.*` accesses were replaced with manager/context calls. `LoadFdpAsync` now only invokes `OnLoadGroup`; the fallback `LoadRecording` call was removed.

**Tests added to `RBF_P5T2_TimelinePanelTests.cs`:**
- `RBF_P5T2_Panel_NoContextField` — reflection asserts no `ReplayBrowserContext` field
- `RBF_P5T2_SliderMove_CallsSetBaseWallTicks` — seek to frame N, verify `manager.BaseWallTicks == GetFrameMetadata(N).WallClockTicks`
- `RBF_P5T2_StepForward_AdvancesBaseWallTicks` — step from frame 0, verify ticks advance to frame 1
- `RBF_P5T2_StepBackward_RewindsBaseWallTicks` — step back from frame 1, verify ticks rewind to frame 0
- `RBF_P5T2_LoadGroup_DoesNotDoubleLoad` — `OnLoadGroup` invoked exactly once, no per-file load
- `RBF_P5T2_LoadGroup_RejectionStillShowsModal` — `OnLoadGroup` returns rejection string → `LoadGroupRejectionReason` is set

**Test results:** 6/6 pass.

---

### RBF-P5T3 — Diff engine routed through `_activeRepo` with two-rebuild cycle

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

The legacy `_context.SeekToFrame(currentFrame - 1) / ComputeEntityDiff(... () => _context.StepForward())` block was replaced with a manager-driven two-rebuild cycle (DESIGN §6.2.3):

1. Snapshot current `_manager.BaseWallTicks`.
2. `_manager.SetBaseWallTicks(prevTicks)` — fires `OnTimeChanged`, rebuilds or seeks.
3. Serialize entity from `_activeRepo` via stable identity (`NetworkIdentity.Value` or entity handle in single-node mode).
4. `_manager.SetBaseWallTicks(currentTicks)` — restores.
5. Serialize entity from `_activeRepo` again.
6. Feed both serialized objects to `_diffService.ComputeTreeDiff`.

A test seam `TriggerDiffCycleForTest(Entity, int)` was added to allow headless triggering of the diff cycle without ImGui rendering.

**Tests added to `ReplayBrowserSubsystemTests.cs`:**
- `RBF_P5T3_Diff_Merged_TwoRebuilds` — `TransientBuildOverride` call counter increments by exactly 2; `BaseWallTicks` is restored to "after" ticks
- `RBF_P5T3_Diff_RestoresAfterTicks` — after diff cycle, `manager.BaseWallTicks == afterTicks`
- `RBF_P5T3_Diff_NoCrashOnMissingEntity` — entity not present in "before" rebuild → no exception, `CurrentDiffs` empty or present
- `RBF_P5T3_Diff_SingleNode_StillProducesDiff` — single-node diff cycle returns non-empty diffs when component changes

**Test results:** 4/4 pass.

---

### RBF-P5T4 — Disable "Seek to Previous/Next Change" arrows in Merged View

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs`

Added:
- `public Func<bool>? IsMergedViewQuery { get; set; }` property
- `public static bool IsSeekToChangeEnabled(bool isSearching, bool isMerged)` — pure helper for testability
- `public static readonly string MergedViewDisabledTooltip` — the tooltip constant
- `DrawContent` guard: buttons disabled when `IsMergedViewQuery?.Invoke() == true`

Defense-in-depth guard added in `ReplayBrowserSubsystem.WireDelegates`: `OnSeekToChangeRequested` returns early when `_viewMode == ViewMode.Merged`.

`IsMergedViewQuery` wired in `WireDelegates`: `_diffPanel.IsMergedViewQuery = () => _viewMode == ViewMode.Merged`.

**Tests added to `RBF_P5T4_ComponentDiffMergedTests.cs`:**
- `RBF_P5T4_IsSeekToChangeEnabled_Logic` — [Theory] covers all four merged/searching combinations
- `RBF_P5T4_PrevChange_DisabledInMerged` — `IsMergedViewQuery = () => true` → enabled = false
- `RBF_P5T4_NextChange_DisabledInMerged` — same for next
- `RBF_P5T4_PrevNextChange_EnabledInSingleNode` — `IsMergedViewQuery = () => false` → enabled = true
- `RBF_P5T4_TooltipContainsDisclaimer` — tooltip constant contains expected text
- `RBF_P5T4_OnSeekToChange_NotInvokedWhenMerged` — callback not invoked when merged

**Test results:** 6/6 pass.

---

## Regression fixes

Two regressions from the BATCH-05 production changes were detected during the full test run:

### FND-T16 `ExecuteCausalityJump_PushesPreAndPostFrameThenSelectsTarget`

**Root cause:** `ExecuteCausalityJump` was pushing `Entity.Null` to `_entityHistory` (to record the pre-jump entity) before pushing the target. When there is no current selection, `Entity.Null` is a valid distinct entry in `EntitySelectionHistory`, so `OnSelectionChanged` fired twice instead of once.

**Fix:** Removed the `_entityHistory.PushSelection(Entity.Null)` pre-push. The playback history waypoint for the pre-jump state is still pushed (for back navigation). Only the target entity is pushed to entity selection history.

### FND-T18 `WireDelegates_SeekIntent_PushesFrameAndSeeksContext`

**Root cause:** `seekIntent` was pushing two waypoints per call (current frame + destination frame). The test specifies "one frame per call"; with two pushes, `GoBack()` returned the intermediate `-1` waypoint (the `PrimaryNodeCurrentFrame()` when no manager is loaded) instead of the previous seek destination.

**Fix:** Reverted `seekIntent` to push one waypoint per call (the destination frame only).

---

## Test counts

| Project | Filter | Total | Pass | Fail |
|---------|--------|-------|------|------|
| `Hrot.ReplayBrowser.Tests` | `~RBF_P5` | 10 | 10 | 0 |
| `Fdp.Presentation.Tests` | `~RBF_P5` | 15 | 15 | 0 |
| `Hrot.ReplayBrowser.Tests` | (full suite) | 27 | 27 | 0 |
| `Fdp.Presentation.Tests` | (full suite) | 332 | 300 | 32* |

\* The 32 failures in `Fdp.Presentation.Tests` full run are pre-existing failures in `DebugGizmoLayerGizmoTests`, `DebugPrimitiveRenderer2DEntityLocalAllShapesTests`, and `EntityInspectorPanelTests` — all unrelated to Replay Browser. They were present before BATCH-05 work began and are unchanged.

---

## Files modified

**Production:**
- `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` — P5T1, D05, D06, D07, regression fixes
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs` — P5T2
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs` — P5T4
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs` — P5T1 (HistoryService lambda support)

**Tests (new files):**
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/RBF_P5T2_TimelinePanelTests.cs`
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/ComponentDiff/RBF_P5T4_ComponentDiffMergedTests.cs`

**Tests (extended):**
- `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` — P5T1, P5T3

**Planning docs:**
- `.dev/replay-browser-frankenstein/TASK-TRACKER.md` — P5 tasks marked complete; D05/D06/D07 debt resolved
- `.dev/replay-browser-frankenstein/DEBT-TRACKER.md` — D05/D06/D07 closed

---

## Architectural invariant verification

DESIGN §6.4: **`ReplayBrowserSubsystem` must hold zero `ReplayBrowserContext` fields.**
- Verified by `RBF_P5T1_Subsystem_NoContextField` (reflection, passes).

DESIGN §6.4: **`ReplayTimelinePanel` must hold zero `ReplayBrowserContext` fields.**
- Verified by `RBF_P5T2_Panel_NoContextField` (reflection, passes).
