# BATCH-05 Review

**Batch:** BATCH-05
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Checklist

| Area | Status | Notes |
|------|--------|-------|
| Build clean | PASS | 0 errors, 0 warnings — full IOS-IG-SimHost.sln |
| Pre-existing Vis2D test failures | CONFIRMED PRE-EXISTING | DebugGizmoLayerActivation/DebugPrimitiveRenderer2D failures existed before BATCH-05 (confirmed via terminal history) |
| 27/27 Hrot.ReplayBrowser.Tests pass | PASS | 10 new RBF_P5 tests + 17 prior tests |
| 15/15 Fdp.Presentation.Tests (RBF_P5) pass | PASS | 9 P5T2 tests + 6 P5T4 tests |
| C0-D05 exception hardening | PASS | IOException, UnauthorizedAccessException, JsonException all caught in OnLoadGroup lambda |
| C0-D06 search panel path sourcing | PASS | Sourced from manager contexts; null in Merged View per DESIGN §6.2.2 |
| C0-D07 cancel seek CTS on mode switch | PASS | `_diffPanel.IsSearching = false` in SetViewMode; SeekToNextChangeAsync loop exits at next checkpoint |
| RBF-P5T1 Excise `_context` field | PASS | Reflection test confirms zero `ReplayBrowserContext` fields in `ReplayBrowserSubsystem` |
| RBF-P5T2 Timeline panel drives manager | PASS | Reflection confirms zero `ReplayBrowserContext` fields in `ReplayTimelinePanel`; `ActiveContext` property delegates to manager |
| RBF-P5T3 Diff engine two-rebuild cycle | PASS | `ComputeDiffInternal` does SetBaseWallTicks(prevTicks), serialize, SetBaseWallTicks(currentTicks), serialize — 2 builds per diff cycle verified |
| RBF-P5T4 Disable change arrows in Merged | PASS | `IsMergedViewQuery` + `IsSeekToChangeEnabled` + `MergedViewDisabledTooltip` constant; subsystem short-circuit guard |
| TASK-TRACKER.md | PASS | All P5 tasks marked `[x]` |
| DEBT-TRACKER.md | PASS | D05/D06/D07 all marked RESOLVED |

Note: Sub-agent did not create BATCH-05-REPORT.md (process miss — not a code issue).

---

## Critical Path Analysis

### `_context` Excision

`ReplayBrowserSubsystem.Initialize` now creates `_activeRepo = new EntityRepository()` as the empty-state placeholder (passed to `_selectionSystem`, `_gizmoLayer`, `_session`). This satisfies the DESIGN §6.4 constraint; no gizmo system depends on a specific `ReplayBrowserContext` at construction time.

`OnManagerTimeChanged` was extended to update `_eventPanel.HistoryService` after every time change, replacing the one-time assignment that previously required a `_context`. The `EventBrowserPanel` supports this via a settable `HistoryService` property (which was added in this batch as a new parameterless constructor + setter).

All `_context.*` usages have been replaced by private helpers: `PrimaryNodeCurrentFrame()`, `PrimaryNodeCurrentFdpPath()`, `TryStepForwardViaManager()`, `SeekFrameViaManager(int)`, and `ComputeDiffInternal(int, Entity)`.

### Diff Engine (`ComputeDiffInternal`)

The two-rebuild pattern is correctly implemented:
1. `_manager.SetBaseWallTicks(prevTicks)` fires `OnTimeChanged` → rebuilds transient master (Merged) or seeks (Single-Node); `_activeRepo` now holds the before-state.
2. Serialise entity from `_activeRepo` using `DiagnosticGuidResolver` and `GetSnapshotableMask()`.
3. `_manager.SetBaseWallTicks(currentTicks)` fires again; `_activeRepo` now holds the after-state.
4. Serialise again; feed both DOMs into `ComputeTreeDiff`.

The `_manager.BaseWallTicks` is correctly restored to `currentTicks` at the end. The `RBF_P5T3_Diff_RestoresAfterTicks` test confirms this explicitly.

Stable identity is tracked via `NetworkIdentity.Value` (a `long`) across rebuilds. For entities without `NetworkIdentity` the `Entity` handle is used — valid in Single-Node mode (handle is stable); in Merged mode such entities are local-only and the fallback handle lookup returns `Entity.Null` gracefully.

### `ReplayTimelinePanel` Refactor

The constructor now accepts `FederatedReplayManager? manager` and `Func<int> getSelectedNodeId`. The `ActiveContext` computed property resolves the current node context lazily, correctly returning `null` when no manager or no matching node exists. All transport operations (`SeekToFrame`, `StepForward`, `StepBackward`) translate to `_manager.SetBaseWallTicks(...)` calls. The `SetManager(FederatedReplayManager)` seam allows the subsystem's `OnLoadGroup` success path to inject the newly-loaded manager into the already-constructed panel.

### `ComponentDiffPanel` Gating

`IsMergedViewQuery`, `MergedViewDisabledTooltip`, and `IsSeekToChangeEnabled` are cleanly separated. The `IsSeekToChangeEnabled` static helper is testable without rendering; the `RBF_P5T4_IsSeekToChangeEnabled_Logic` theory covers all four combinations of (searching, merged). The tooltip constant is verified by string-contains assertion rather than rendering.

The subsystem's `OnSeekToChangeRequested` callback short-circuits on `_viewMode == ViewMode.Merged` as defense-in-depth per DESIGN §6.2.3.

### Test Quality

**RBF-P5T1** — Reflection test checks field type, not name (robust to renames). Seek test verifies `PrimaryNodeCurrentFrame()` matches `ctx.CurrentFrame` after `SetBaseWallTicks`. Merged rebuild test uses `TransientBuildOverride` counter; verifies reference inequality of `ActiveRepo`.

**RBF-P5T2** — Tests use real 2-frame recordings; `SeekToFrameForTest` and `StepForward/BackwardForTest` test seams verify exact wall-tick values from `GetFrameMetadata`. `RejectionShowsModal` regression confirms modal path still works after panel refactor.

**RBF-P5T3** — `TwoRebuilds` asserts build counter == 2 exactly; `RestoresAfterTicks` asserts manager is at after-state ticks; `NoCrashOnMissingEntity` uses empty override repo; `SingleNode_StillProducesDiff` asserts 0 builds (no transient overhead in single-node).

**RBF-P5T4** — `SubsystemShortCircuit_NoSeekInMerged` wires delegates and invokes callback directly; asserts `BaseWallTicks` unchanged (strongest proof no seek started).

No fake tests. All assertions verify observable state changes.

---

## Minor Issues / Debt

### D08 (P3) — Stale `context` parameter in `WireDelegatesForTest`

`WireDelegatesForTest` retains a `ReplayBrowserContext context` parameter (its 4th argument) that is never referenced in the method body. Production code passes `null!`; tests pass `new ReplayBrowserContext()` to satisfy the type. The parameter is dead code.

**Fix:** Remove the `context` parameter and update all call sites. Assign BATCH-06 if there is follow-up work; otherwise fix opportunistically.

---

## Suggested Git Commit Message

### Submodule: `FDP` (if applicable — check if FDP is a submodule)
```
refactor: excise ReplayBrowserContext from ReplayTimelinePanel (RBF-P5T2)

ReplayTimelinePanel now takes FederatedReplayManager? + Func<int> getSelectedNodeId
instead of ReplayBrowserContext. All transport ops translate to SetBaseWallTicks.
SetManager() seam added for post-load injection. Fixes scrub-disconnect in merged view.
Adds RBF_P5T2 tests (6).
```

### Submodule / project: `Hrot`
```
refactor: excise ReplayBrowserContext _context from ReplayBrowserSubsystem (RBF-P5T1)
fix: diff engine uses two-rebuild cycle via FederatedReplayManager (RBF-P5T3)
fix: disable seek-to-change arrows in Merged View (RBF-P5T4)
fix: harden OnLoadGroup exception handling D05; fix search path D06; cancel seek D07

- _context field removed; replaced by PrimaryNodeCurrentFrame/Fdp/Path helpers
- ComputeDiffInternal: SetBaseWallTicks(prev) -> serialize -> SetBaseWallTicks(cur) -> serialize
- ComponentDiffPanel.IsMergedViewQuery gates prev/next-change buttons + tooltip
- D05: catch IOException, UnauthorizedAccessException, JsonException in OnLoadGroup
- D06: search panel sources path from manager; null in Merged View
- D07: IsSearching=false on SetViewMode(Merged) to stop in-flight seek
Adds RBF_P5T1/T3/T4 tests (10).
```

### Top-level module
```
feat(replay-browser): Phase P5 complete - federated manager is sole timeline source

All tasks RBF-P1T1 through RBF-P5T4 now complete. Replay browser frankenstein
feature is fully implemented: multi-node .fdp loading, ECS synthesis, merged view
with correct scrub, diff engine, and diagnostic UI.
```

---

## All Tasks Status

Every task across P1-P5 is now `[x]`. **Mission accomplished.**
