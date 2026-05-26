# BATCH-04 Review

**Batch:** BATCH-04
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Checklist

| Area | Status | Notes |
|------|--------|-------|
| Build clean | PASS | 0 errors, 0 warnings — both `Fdp.Presentation.csproj` and `Hrot.ReplayBrowser.csproj` |
| 28/28 tests pass | PASS | 22 Fdp.Presentation.Tests + 6 Hrot.ReplayBrowser.Tests |
| RBF-P4T1 multi-file dialog | PASS | Interface + WinForms impl + stub; `LoadFdpAsync` wired |
| RBF-P4T2 FederationPanel | PASS | Mode toggle, offset/tick/provider controls, disclaimer text |
| RBF-P4T3 subsystem mode swap | PASS | `SetViewMode`, `BuildAndBindTransientMaster`, `OnManagerTimeChanged` branching |
| RBF-P4T4 entity paradox helper | PASS | `EntityFieldParadoxHelper.ShouldFlag` / `ParadoxTooltip` |
| RBF-P4T5 disclaimer documentation | PASS | `MergedViewDisclaimerText` contains "stutter" and "offline" |
| RBF-P4T6 play button disabled in Merged | PASS | `IsPlayEnabled` + `BeginDisabled/EndDisabled` guard + tooltip |
| RBF-P4T7 search disabled in Merged | PASS | `IsMergedViewActive` early-exit with disabled message |
| Q1-Q5 insight answers | PASS | All five answered with accurate code references |
| `EntityInspectorPanelTests` stub | PASS | `IsMergedView` property added to `MockInspectorContext` |

---

## Critical Path Analysis

### Play Button Disabled in Merged View

`IsPlayEnabled(hasRecording, isMergedView)` = `hasRecording && !isMergedView`.
The production code wraps the play button in `BeginDisabled/EndDisabled` keyed on `playEnabled`,
not on `hasRecording`. This is the correct double-guard: the ImGui region is fully disabled AND
the `DrawButton` `enabled` parameter is `false`. Q1 in the report correctly identifies that
passing `hasRecording` to ONLY `DrawButton` (while keeping `BeginDisabled` correct) would be a
cosmetic-only defect, but passing it to BOTH is the dangerous scenario that allows
`IsPlaying = true` to fire in Merged View.

### FederationPanel Null Safety

`SetViewMode` in the subsystem does NOT access `_timelinePanel` directly; it only touches
`_searchPanel` and `_inspectorState` (both null-guarded). `OnManagerTimeChanged` (called at the
end of `SetViewMode`) guards on `_manager == null`. The headless test
`ModeSwitchToMerged_DoesNotThrowInHeadlessMode` confirms no NPE when all optional panels are null.

### Test Quality

Tests in all three suites verify behavior values rather than structure:

**RBF_P4T1** — `PassesAllPathsToManager`: `StubFileDialogService` returns two paths; asserts
`OnLoadGroup` received exactly those two paths. `RejectionShowsModal`: `OnLoadGroup` returns a
rejection string; asserts `LoadGroupRejectionReason` is populated.

**RBF_P4T2** — `NonZeroOffset_ShowsWarningGlyph`: sets offset to 5, verifies `HasNonZeroOffset`
is `true`. `ProviderDropdownChange_CallsManagerSetLocalEntitiesProvider`: verifies the panel
delegates provider selection to the manager by checking a captured nodeId.

**RBF_P4T3** — `MergedMode_BindsToTransientMaster`: loads two recordings, switches to Merged,
calls `OnManagerTimeChanged`; verifies `_activeRepo` is the transient-master instance (not either
raw sandbox). `ModeSwitchToSingle_DisposesTransientMaster`: verifies transient master is disposed
when switching back to SingleNode.

**RBF_P4T4** — `ShouldFlag` and `ParadoxTooltip` verified with both `Entity.Null` and non-null
inputs in both Merged and SingleNode view modes.

**RBF_P4T6** — `Play_DisabledInMerged`: directly asserts `IsPlayEnabled(true, true) == false`.
`Play_EnabledInSingleNode`: asserts `IsPlayEnabled(true, false) == true`.

No fake assertions, no empty loops.

### `EntityInspectorPanelTests.cs` Change

The `IsMergedView` stub was added to `MockInspectorContext` to satisfy the updated
`IInspectorContext` interface. This is mechanical and correct — the stub defaults to `false`
which is the right behavior for existing inspector tests (they are all single-node scenarios).

---

## New Debt Items

Three new items extracted from Q3-Q5 insight answers:

**D05 (P2):** `OnLoadGroup` lambda only catches `LoadGroupException`. `File.ReadAllText` inside
`FederatedReplayManager.LoadGroup` can throw `IOException` (missing `.meta.json`) or
`UnauthorizedAccessException`; `MetadataSerializer.Deserialize` can throw `JsonException`. These
propagate unhandled through the lambda, crashing the render thread with no user-visible feedback.
Fix: also catch `IOException`, `UnauthorizedAccessException`, and `JsonException` and return a
user-readable rejection string.

**D06 (P3):** `_searchPanel.CurrentFilePath` is always sourced from `_context.CurrentFdpPath`
(the single-node context), which is null when using `FederatedReplayManager`. The search panel
therefore shows a blank/null path in federated mode. Fix: source `CurrentFilePath` from
`_manager.Contexts[_manager.LocalEntitiesProviderNodeId].CurrentFdpPath` when a manager is present.

**D07 (P3):** When `IsMergedViewActive` causes `DrawContent` to early-exit, any in-progress
`_searchTask` continues to run unimpeded (consuming CPU) and its results are silently discarded.
`_searchCts.Cancel()` is never called. Fix: cancel the CTS at the top of the early-exit path
(or in `SetViewMode` when switching to Merged) when `_searchTask != null && !_searchTask.IsCompleted`.

---

## Suggested Commit Message

Already committed:
- `FDP`: `RBF BATCH-04: multi-file dialog, FederationPanel, play-disabled-in-merged, entity paradox helper, search early-exit`
- `Hrot`: `RBF BATCH-04: subsystem mode-swap, transient master build, federated repo rebind`
- Top-level: `RBF BATCH-04: update submodule pointers, add batch instructions and report`

---

## Action Items

- [x] Update TASK-TRACKER.md: tick P4T1-P4T7
- [x] Update DEBT-TRACKER.md: add D05, D06, D07; mark D04 context
- [x] Commit all batch artifacts
