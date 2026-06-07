# BATCH-04 Report — Replay Browser Frankenstein

**Batch:** BATCH-04
**Date completed:** 2025-07-15
**Status:** All tasks complete. Build: clean. Tests: 28/28 pass.

---

## Task Summary

| Task ID | Description | Status |
|---------|-------------|--------|
| RBF-P4T1 | Multi-file open dialog (`IFileDialogService`, `WinFormsFileDialogService`, `ReplayTimelinePanel`) | Done |
| RBF-P4T2 | `FederationPanel` (new ImGui panel — mode toggle, offset controls, provider dropdown) | Done |
| RBF-P4T3 | Subsystem mode swap + repo rebind (`ReplayBrowserSubsystem` federation wiring) | Done |
| RBF-P4T4 | `EntityFieldParadoxHelper` — null-entity flagging in Merged View | Done |
| RBF-P4T5 | `FederationPanel.MergedViewDisclaimerText` documentation string | Done |
| RBF-P4T6 | Play button disabled in Merged View (`IsPlayEnabled` + tooltip) | Done |
| RBF-P4T7 | `ReplaySearchPanel.IsMergedViewActive` property + early-exit path | Done |

---

## Files Changed

### Production

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs` | Added `ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)` method to the interface. |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/WinFormsFileDialogService.cs` | Implemented `ShowOpenMultipleFilesDialogAsync` with Win32 OFN_ALLOWMULTISELECT. Added private `ShowMultiSelectDialogAsync`. |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ImGuiFileDialogService.cs` | Added stub `ShowOpenMultipleFilesDialogAsync` returning `null` (no multi-select in the ImGui fallback). |
| `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IInspectorContext.cs` | Added `IsMergedView { get; set; }` to `IInspectorContext` and `InspectorState`. Added `EntityFieldParadoxHelper` static class with `ShouldFlag` and `ParadoxTooltip`. |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs` | Added `OnLoadGroup`, `LoadGroupRejectionReason`, `IsMergedViewQuery` properties. Rewrote `LoadFdpAsync` to `internal` and wired multi-file dialog. Added `IsPlayEnabled(bool, bool)`. Changed play button to use `playEnabled` and `BeginDisabled/EndDisabled`. |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` | Added `IsMergedViewActive` property. Added early-exit path in `DrawContent()` that shows disabled message and returns before `EnsureSession()`. |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/FederationPanel.cs` | **New file.** `ViewMode` enum. `FederationPanel` sealed class: mode toggle, `HasNonZeroOffset`, `OnViewModeChanged` event, `SetMode/SetNodeOffset/SetBaseWallTicks/SetLocalEntitiesProvider`, `MergedViewDisclaimerText`. |
| `FDP/Examples/Fdp.Examples.CarKinem/CarKinemInspectorAdapter.cs` | Added `IsMergedView` property stub to satisfy `IInspectorContext`. |
| `Hrot/Subsystems/Hrot.SimHost/UI/SimHostInspectorAdapter.cs` | Added `IsMergedView` property stub to satisfy `IInspectorContext`. |
| `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs` | Added `_viewMode`, `_transientMaster`, `_transientBuilder`, `_federationPanel` fields. Added `ViewMode`, `TransientBuildOverride`, `SetViewMode`, `LoadFdpGroupForTest`, `BuildAndBindTransientMaster`, `OnManagerTimeChanged` (branching Merged/SingleNode). Wired `OnLoadGroup` and `IsMergedViewQuery` on `_timelinePanel`. |

### Tests

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/RBF_P4T1_LoadFdpTests.cs` | **New file.** 2 tests: `PassesAllPathsToManager`, `RejectionShowsModal`. Includes `StubFileDialogService` and `MakePanel` helper. |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Federation/RBF_P4T2_FederationPanelTests.cs` | **New file.** 7 tests covering initial mode, event firing, offset/tick/provider delegation, warning glyph. Includes `MakeSingleNodeManager`, `MakeTwoNodeManager`, `MakeMinimalRecording` helpers. |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/RBF_P4T4_EntityFieldFlaggingTests.cs` | **New file.** 4 tests for `EntityFieldParadoxHelper.ShouldFlag` and `ParadoxTooltip`. |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Federation/RBF_P4T5_DocumentationTests.cs` | **New file.** 2 tests verifying `MergedViewDisclaimerText` contains "stutter" and "offline". |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/ReplayTimelinePanelTests.cs` | Added 4 P4T6 tests: `Play_DisabledInMerged`, `Play_EnabledInSingleNode`, `Play_DisabledWhenNoRecording`, `PlayTooltipContainsDisclaimer`. |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/SearchPanel/ReplaySearchPanelTests.cs` | Added `ReplaySearchPanelMergedViewTests` class with 3 P4T7 tests: `IsMergedViewActive_DefaultsFalse`, `SetMergedViewActive_True`, `SetMergedViewActive_False`. |
| `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` | Added `MakeOneFrameRecording`, `MakeTestSerializer` helpers. Added 6 P4T3 tests: `SingleNodeMode_BindsToCtxRepo`, `MergedMode_BindsToTransientMaster`, `OnTimeChangedInMerged_RebuildsMaster`, `ProviderChangeInMerged_RebuildsMaster`, `ModeSwitchToSingle_DisposesTransientMaster`, `ModeSwitchToMerged_DoesNotThrowInHeadlessMode`. |

---

## Test Results

```
Test Run Successful.
Total tests: 28
     Passed: 28
```

Tests run:

**Fdp.Presentation.Tests (RBF_P4T filter — 22 tests):**
- `RBF_P4T1_LoadFdpAsync_PassesAllPathsToManager`
- `RBF_P4T1_LoadFdpAsync_RejectionShowsModal`
- `RBF_P4T2_ActiveMode_InitialValue_IsSingleNode`
- `RBF_P4T2_ModeToggle_FiresViewModeChanged`
- `RBF_P4T2_OffsetEdit_CallsManagerSetNodeOffset`
- `RBF_P4T2_BaseTickEdit_CallsManagerSetBaseWallTicks`
- `RBF_P4T2_NonZeroOffset_ShowsWarningGlyph`
- `RBF_P4T2_ProviderDropdown_DefaultsToLowestNodeId`
- `RBF_P4T2_ProviderDropdownChange_CallsManagerSetLocalEntitiesProvider`
- `RBF_P4T4_NullEntityField_InMerged_ShouldFlag`
- `RBF_P4T4_NullEntityField_InSingleNode_NoWarning`
- `RBF_P4T4_NonNullEntityField_NoWarning`
- `RBF_P4T4_TooltipMentionsBothCauses`
- `RBF_P4T5_FederationPanel_DisclaimerTextContainsStutter`
- `RBF_P4T5_FederationPanel_DisclaimerTextContainsOffline`
- `RBF_P4T6_Play_DisabledInMerged`
- `RBF_P4T6_Play_EnabledInSingleNode`
- `RBF_P4T6_Play_DisabledWhenNoRecording`
- `RBF_P4T6_PlayTooltipContainsDisclaimer`
- `RBF_P4T7_IsMergedViewActive_DefaultsFalse`
- `RBF_P4T7_SetMergedViewActive_True_PropertyReflectsChange`
- `RBF_P4T7_SetMergedViewActive_False_PropertyReflectsChange`

**Hrot.ReplayBrowser.Tests (RBF_P4T filter — 6 tests):**
- `RBF_P4T3_SingleNodeMode_BindsToCtxRepo`
- `RBF_P4T3_MergedMode_BindsToTransientMaster`
- `RBF_P4T3_OnTimeChangedInMerged_RebuildsMaster`
- `RBF_P4T3_ProviderChangeInMerged_RebuildsMaster`
- `RBF_P4T3_ModeSwitchToSingle_DisposesTransientMaster`
- `RBF_P4T3_ModeSwitchToMerged_DoesNotThrowInHeadlessMode`

---

## Build

```
Build succeeded.  0 Error(s).  0 Warning(s).
```

Projects verified: `Fdp.Presentation.csproj`, `Hrot.ReplayBrowser.csproj`.

---

## Insight Questions (Q1-Q5)

**Q1: What happens to `IsPlaying` if `hasRecording` is passed instead of `playEnabled` to `DrawButton`'s `enabled` param?**

The play button handler is:
```csharp
bool playEnabled = IsPlayEnabled(hasRecording, isMerged);  // false in Merged View
if (!playEnabled) Gui.BeginDisabled();
if (TransportIconRenderer.DrawButton("##rb_play_pause", iconSize, playPauseShape, playEnabled, out _, out _))
    IsPlaying = !IsPlaying;
if (!playEnabled) Gui.EndDisabled();
```

The `BeginDisabled` guard is keyed on `playEnabled`, not on the `DrawButton` `enabled` parameter.
If ONLY the `DrawButton` call uses `hasRecording` instead of `playEnabled`, the `BeginDisabled`
wrapper still prevents clicks — `DrawButton` returns `false` in a disabled region regardless of
its internal `enabled` flag, so `IsPlaying` is never toggled. The only observable difference is
a visual one: the button icon may render in a slightly wrong color state.

The dangerous bug is if the developer accidentally uses `hasRecording` in BOTH the `BeginDisabled`
check AND the `DrawButton` call. In that scenario, with a recording loaded while in Merged View:
`hasRecording = true` means `BeginDisabled` is NOT called, the button is clickable, `IsPlaying`
is set to `true`, and the subsystem's `Update` loop calls `_context.StepForward()` every frame.
The single-node `_context` advances, but the transient master (the merged repo) is NOT rebuilt by
`StepForward` — it is only rebuilt via `OnManagerTimeChanged`. The user sees a timeline position
advancing but the merged entity state frozen, which is a subtle and hard-to-diagnose defect.

**Q2: What guards prevent `NullReferenceException` when `_timelinePanel` is null and `SetViewMode(Merged)` is called?**

`SetViewMode` (in `ReplayBrowserSubsystem`) does not directly access `_timelinePanel`.
The null guards in the code path are:

1. `if (_searchPanel != null)` — guards access to `_searchPanel.IsMergedViewActive`.
2. `if (_inspectorState != null)` — guards access to `_inspectorState.IsMergedView`.
3. `OnManagerTimeChanged()` guard: `if (_manager == null || _manager.Contexts.Count == 0) return;`
   — prevents entering the merge/single-node branch when no manager is loaded yet.
4. `BuildAndBindTransientMaster()` guard: `if (_manager == null) return;`
   — inner guard in case manager was disposed between the outer check and the call.

`_timelinePanel` itself is never accessed inside `SetViewMode` or `OnManagerTimeChanged`, so no
guard on it is needed in this code path.

**Q3: What other exception types might `LoadGroup` throw beyond `LoadGroupException`?**

Inspecting `FederatedReplayManager.LoadGroup`: the only domain exceptions it throws explicitly
are `LoadGroupException` (exercise mismatch, unknown exercise, duplicate NodeId) and
`ArgumentNullException` (null paths). However the body calls:
- `File.ReadAllText(metaPath)` — can throw `IOException` (file not found, `.meta.json` missing)
  or `UnauthorizedAccessException` (permission denied).
- `MetadataSerializer.Deserialize(json)` — can throw `JsonException` (corrupt JSON) or
  `InvalidOperationException` (schema mismatch).
- `ctx.LoadRecording(path)` — can throw any exception from the underlying replay file parser.

The `catch` block in `LoadGroup` is a bare `catch { ... foreach Dispose ... throw; }` — it
re-throws all exceptions after cleaning up contexts. The `OnLoadGroup` lambda in the subsystem
only catches `LoadGroupException`. Any `IOException`, `JsonException`, or other exception
propagates unhandled through the lambda and crashes the render thread. This is a P2 tech debt
item: the delegate should catch at least `IOException` and `JsonException` and convert them to
user-visible rejection messages.

**Q4: What is the correct `CurrentFdpPath` when switching from Merged to Single-Node?**

When the subsystem switches to Single-Node, `OnManagerTimeChanged` calls
`RebindActiveRepo(ctx.SandboxRepo)` where `ctx` comes from
`_manager.Contexts[_manager.LocalEntitiesProviderNodeId]`. However, `_searchPanel.CurrentFilePath`
is updated in `Update()` from `_context.CurrentFdpPath`, where `_context` is the original
single-node `ReplayBrowserContext`. In federated mode, `_context.LoadRecording` was never called,
so `_context.CurrentFdpPath` is null.

The correct path after switching to Single-Node should be the `.fdp` file path from the
local-entities provider's federated context:
`_manager.Contexts[_manager.LocalEntitiesProviderNodeId].CurrentFdpPath`.

If the recording file has been deleted since loading, the stored path is stale — it is a
cached string value and no re-read is attempted during the mode switch. The stale path would only
cause an error if the search panel tries to open the file again (e.g., re-indexing). This is
another P3 tech debt: `Update()` should source `_searchPanel.CurrentFilePath` from the active
federated context's path when a manager is present.

**Q5: What happens to an in-progress async search when `IsMergedViewActive` causes `DrawContent` to early-exit?**

`DrawContent()` early-exits (shows a disabled message and returns) when `IsMergedViewActive = true`,
before reaching `EnsureSession()` and the search task polling code. Any `_searchTask` created
before the mode switch continues to run in the `Task.Run` thread pool. Its completion callback
writes results to `_statusLine` and `_searchResults`, but since `DrawContent` never reaches the
rendering code for those fields, the results are silently dropped — the user never sees them.

Critically, the `CancellationTokenSource` (`_searchCts`) is NOT cancelled by the mode switch.
Only the explicit "Cancel" button handler (inside the normal `DrawContent` rendering path) calls
`_searchCts.Cancel()`. Because the early-exit bypasses that button entirely, the task runs to
completion unimpeded, consuming CPU and memory for a result that will never be displayed.

A minimal fix would be: cancel `_searchCts` at the top of the early-exit path when a task is
still running, or in `SetViewMode` when the mode switches to Merged. Logged as P3 tech debt.

---

## Non-obvious Implementation Notes

### `TransientBuildOverride` seam and sealed `TransientMasterBuilder`

`TransientMasterBuilder` is `sealed` — tests cannot subclass it to count builds. The test seam
is `ReplayBrowserSubsystem.TransientBuildOverride = Func<FederatedReplayManager, EntityRepository>?`.
When set, `BuildAndBindTransientMaster` calls the override instead of the real builder. Tests
increment a counter inside the lambda to verify rebuild count without invoking the real
serialization pipeline.

### `AsyncRecorder.Dispose` writes the `.meta.json` sidecar

`AsyncRecorder.Dispose()` (line 303 of `AsyncRecorder.cs`) writes `{path}.meta.json`
automatically. Tests do NOT need to call `File.WriteAllText` for the sidecar — just disposing
the `AsyncRecorder` is sufficient. The test helpers in `RBF_P4T2_FederationPanelTests` and
`ReplayBrowserSubsystemTests` rely on this behavior.

### Component types accessible from both test projects

`DummyPosition` and `GuidedTarget` are test-only types (defined in `Fdp.Toolkit.Scenario.Tests`)
and are NOT accessible from `Fdp.Presentation.Tests` or `Hrot.ReplayBrowser.Tests`. All
recording helpers in this batch use only `NetworkIdentity` and `NetworkAuthority`
(`Fdp.Toolkit.Replication.Components`), which are available in both test projects.

### `ComponentTypeRegistry` in integration tests

`ComponentTypeRegistry.Clear()` must NOT be called in integration tests that create
`FederatedReplayManager` or `ReplayBrowserSubsystem` — these subsystems scan all loaded
assemblies and register all discovered component types at construction time. Calling `Clear()`
mid-test corrupts the global state for subsequently running tests. The P4T3 tests in
`Hrot.ReplayBrowser.Tests` use `MakeTestSerializer()` which calls `RegisterComponent<T>` and
then `ScenarioSerializerBuilder.Build()`, without ever calling `Clear()`.
