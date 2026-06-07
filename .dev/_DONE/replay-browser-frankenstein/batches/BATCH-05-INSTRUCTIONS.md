# BATCH-05 Instructions — Replay Browser Frankenstein

**Batch:** BATCH-05
**Target tasks:** D05 (corrective), D06 (corrective), D07 (corrective), RBF-P5T1, RBF-P5T2, RBF-P5T3, RBF-P5T4
**Design reference:** `.dev/replay-browser-frankenstein/DESIGN.md` §6.2, §6.2.3, §6.4
**Task details:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md` (Phase P5)
**Debt tracker:** `.dev/replay-browser-frankenstein/DEBT-TRACKER.md`
**Workspace root:** `D:\Work\IOS-IG-SimHost-FDP-2`

---

## Onboarding

You are implementing the **final phase** of the Replay Browser Frankenstein feature.
Read these design documents before touching code:

1. `.dev/replay-browser-frankenstein/DESIGN.md` — especially §6.4 ("no legacy `_context`"), §6.2.3 (diff policy), §6.2.2 (search policy), §6.3 (mode switching).
2. `.dev/replay-browser-frankenstein/TASK-DETAILS.md` — Phase P5 tasks (RBF-P5T1 through RBF-P5T4) define every success condition.
3. `.dev/replay-browser-frankenstein/reviews/BATCH-04-REVIEW.md` — explains the root cause of the P5 corrective work and lists the three open debt items D05/D06/D07.

Key architecture constraint (DESIGN §6.4): **`ReplayBrowserSubsystem` must hold zero `ReplayBrowserContext` fields**. The single-node case is `_manager.Contexts[selectedNodeId]`; all UI panels communicate exclusively through `_manager` or through `_activeRepo`.

The prior batch (BATCH-04) established:
- `FederatedReplayManager` with `LoadGroup`, `SetBaseWallTicks`, `SeekAll`, `OnTimeChanged`, `LocalEntitiesProviderNodeId`.
- `TransientMasterBuilder.Build(manager)` synthesizes the merged `EntityRepository`.
- `ReplayBrowserSubsystem` has `_manager`, `_activeRepo`, `_viewMode`, `SetViewMode`, `OnManagerTimeChanged`, `BuildAndBindTransientMaster`, `TransientBuildOverride`.
- `ReplayTimelinePanel` already has `OnLoadGroup` and `IsMergedViewQuery` properties but **still holds `_context`**.
- `ComponentDiffPanel` has `OnSeekToChangeRequested` but no merged-view gate yet.

Your job is to complete the wiring so the merged view actually updates on scrub.

---

## Build and test commands

```powershell
# Full solution build (run after every significant change)
dotnet build IOS-IG-SimHost.sln

# Run only the tests relevant to this batch
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --filter "FullyQualifiedName~RBF_P5"
dotnet test Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj --filter "FullyQualifiedName~RBF_P5"

# Full test run for affected projects
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --no-build --nologo
dotnet test Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj --no-build --nologo
```

---

## Corrective tasks (C0) — fix before new tasks

### C0-D05 — Harden `OnLoadGroup` exception handling (P2)

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

In the `Initialize` method, the lambda assigned to `_timelinePanel.OnLoadGroup` currently only catches `LoadGroupException`. The call to `FederatedReplayManager.LoadGroup` internally calls `File.ReadAllText` (can throw `IOException`, `UnauthorizedAccessException`) and `MetadataSerializer.Deserialize` (can throw `JsonException` on corrupt files). These exceptions propagate to the render thread.

**Fix:** Extend the catch clause to also catch `IOException`, `UnauthorizedAccessException`, and `System.Text.Json.JsonException`, and return a user-readable rejection string for each. Example pattern:

```csharp
catch (LoadGroupException ex)
{
    return ex.Message;
}
catch (System.IO.IOException ex)
{
    return $"Failed to read recording file: {ex.Message}";
}
catch (UnauthorizedAccessException ex)
{
    return $"Access denied reading recording file: {ex.Message}";
}
catch (System.Text.Json.JsonException ex)
{
    return $"Recording metadata is corrupt: {ex.Message}";
}
```

**No new tests required** for D05 (unit-testing file I/O exceptions requires extensive mocking; this is a defensive guard). Mark D05 RESOLVED in DEBT-TRACKER.md.

---

### C0-D06 — Fix `_searchPanel.CurrentFilePath` sourcing in federated mode (P3)

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

In `Update`, the line:
```csharp
_searchPanel.CurrentFilePath = _context.CurrentFdpPath;
```
…must be replaced. After P5T1 removes `_context`, it must read from the manager. Additionally, per DESIGN §6.2.2, in Merged View the search panel must receive `null` (not the provider node's path — the merged view has no single on-disk file):

```csharp
if (_searchPanel != null)
{
    if (_viewMode == ViewMode.Merged || _manager == null || _manager.Contexts.Count == 0)
    {
        // Merged View: search is disabled (panel already has IsMergedViewActive=true from SetViewMode)
        // but CurrentFilePath must also be null so no stale path leaks through.
        _searchPanel.CurrentFilePath = null;
    }
    else
    {
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        _searchPanel.CurrentFilePath = _manager.Contexts.TryGetValue(nodeId, out var ctx)
            ? ctx.CurrentFdpPath
            : null;
    }
}
```

**No new tests required** for D06. Mark D06 RESOLVED in DEBT-TRACKER.md.

---

### C0-D07 — Cancel search CTS on mode switch to Merged (P3)

**File:** `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

In `SetViewMode`, when switching to `ViewMode.Merged`, any in-flight `_seekToChangeTask` should be cancelled so it stops consuming CPU. Look for the `_searchCts` field on `ReplaySearchPanel` or the `_seekToChangeTask` on the subsystem. The current `WireDelegates` implementation uses an async `SeekToNextChangeAsync` task stored in `_seekToChangeTask`. The cancellation token logic lives in `ReplaySearchPanel` (check `_searchCts` or equivalent).

Look at the existing `ReplaySearchPanel` to find the correct cancellation mechanism. At a minimum, in `SetViewMode` when `mode == ViewMode.Merged`:

```csharp
// Cancel any in-progress seek-to-change search
if (_seekToChangeTask != null && !_seekToChangeTask.IsCompleted)
{
    // The async task in SeekToNextChangeAsync checks _diffPanel.IsSearching.
    // Set it to false so the running task exits at its next check point.
    if (_diffPanel != null)
        _diffPanel.IsSearching = false;
}
```

**No new tests required** for D07. Mark D07 RESOLVED in DEBT-TRACKER.md.

---

## Task RBF-P5T1 — Excise `ReplayBrowserContext _context` from `ReplayBrowserSubsystem`

**Full spec:** `TASK-DETAILS.md#rbf-p5t1`

### What exists now

`ReplayBrowserSubsystem` has both `private ReplayBrowserContext _context = null!;` and `private FederatedReplayManager? _manager;`. Code throughout `Initialize`, `Update`, `WireDelegates`, and `SeekToNextChangeAsync` reads `_context.*`.

### Changes required

**1. Delete the `_context` field** (line 45) and its construction in `Initialize` (line 128 `_context = new ReplayBrowserContext();`).

**2. Replace every `_context.*` reference.** The subsystem currently uses `_context` in these ways — replace each as shown:

| Original | Replacement |
|---|---|
| `_context.SandboxRepo` (in `Initialize` gizmo setup, `_session`, `_selectionSystem`, `_gizmoLayer`) | `_activeRepo ?? EmptyRepo` — but at init time no file is loaded, so these setup calls either need to be deferred until first load, OR you introduce a lazy empty `EntityRepository` placeholder. **Preferred approach:** move the gizmo-system construction that takes `_context.SandboxRepo` into a new private helper `RebindGizmoSystems(EntityRepository repo)` that is called by `RebindActiveRepo`. Pass the initial sandbox using an empty/primed repo or `null`-safe guards. |
| `_context.HistoryService` (line 284: `_eventPanel = new EventBrowserPanel(_context.HistoryService)`) | `_manager?.Contexts[_manager.LocalEntitiesProviderNodeId].HistoryService` — but at panel-construction time during `Initialize` no manager exists yet. **Solution:** make `EventBrowserPanel.HistoryService` settable and update it inside `OnManagerTimeChanged` when contexts are available. Or supply a lambda: `new EventBrowserPanel(() => _manager?.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var c) == true ? c.HistoryService : null)` — check the `EventBrowserPanel` constructor signature to see which pattern it supports. |
| `_context.CurrentFrame` (line 287: `CurrentFrameProvider`) | `() => (uint)Math.Max(0, _manager?.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var c) == true ? c.CurrentFrame : 0)` |
| `_pendingChangeSeekFrame` handling: `_context.SeekToFrame(pendingSeek)` | Translate to `_manager.SetBaseWallTicks(wallTicks)` — resolve `pendingSeek` to a wall-tick from the primary node's `Playback.GetFrameMetadata(pendingSeek).WallClockTicks`, then call `_manager.SetBaseWallTicks(...)`. |
| `_searchPanel.CurrentFilePath = _context.CurrentFdpPath` | Already replaced by C0-D06 above. |
| `if (!_context.StepForward())` in playback accumulator | Use manager-based step: advance by the wall-tick delta of one primary-node frame. Helper: `bool TryStepForwardViaManager()` — finds primary node's context, checks if a next frame exists, computes `nextFrameTicks`, calls `_manager.SetBaseWallTicks(nextFrameTicks)`, returns true/false. |
| `int currentFrame = _context.CurrentFrame` in diff engine | `int currentFrame = PrimaryNodeCurrentFrame()` — private helper returning `_manager?.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var c) == true ? c.CurrentFrame : -1` or similar. |
| `_context.SeekToFrame(currentFrame - 1, suppressHistory: true)` in diff engine | Will be replaced entirely by RBF-P5T3. |
| `_context.SandboxRepo` passed to `ComputeEntityDiff` | Will be replaced by RBF-P5T3. |
| `() => _context.StepForward(suppressHistory: true)` in diff engine | Will be replaced by RBF-P5T3. |
| `_context.SandboxRepo` in `DrawUI` / `Update` gizmo ticks | `_activeRepo` (already the active repo by that point in the method). |
| `_context?.Dispose()` in `Shutdown` | Remove — `_manager?.Dispose()` already covers all contexts. |
| `_context.SandboxRepo` in `WireDelegates` for `getSelectedNetworkId` lambda | Use `_activeRepo` instead. |
| `_context.CurrentFdpPath` in `SeekToNextChangeAsync` | `_manager?.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var c) == true ? c.CurrentFdpPath : null` |
| `_context.CurrentFrame` in `SeekToNextChangeAsync` | `PrimaryNodeCurrentFrame()` helper. |

**3. Null-guard all `_manager` accesses.** The subsystem must survive `Initialize` → `Update` → `DrawUI` with zero loaded files.

**4. `ReplayBrowserContext` in `_selectionSystem` constructor and `_gizmoLayer`.**
These take an `EntityRepository` argument. At `Initialize` time there is no loaded repository. Two options:
- Option A (preferred): Allocate a minimal empty `EntityRepository` as a placeholder (`_activeRepo = new EntityRepository(); RepositoryPriming.RegisterDiscoveredComponents(_activeRepo, null!);`) and pass it; then update the gizmo systems when a file loads via the `RebindActiveRepo` / `RebindGizmoSystems` pattern.
- Option B: Null-guard every `_selectionSystem?.Tick` and `_gizmoLayer?.Draw` call (already done in much of the code via `?.`).

Check the constructor signatures to decide which is simpler. The key invariant: **no `_context` field**.

**5. The `WireDelegatesForTest` static helper** (if present) may also pass `_context` as a parameter. Update its signature to pass the manager or a lambda instead.

### Tests for RBF-P5T1

Add to `Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs`:

```
RBF_P5T1_Subsystem_NoContextField
```
Use reflection to assert `typeof(ReplayBrowserSubsystem)` has no field of type `ReplayBrowserContext`:
```csharp
var fields = typeof(ReplayBrowserSubsystem)
    .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
Assert.That(fields.Any(f => f.FieldType == typeof(ReplayBrowserContext)), Is.False,
    "ReplayBrowserSubsystem must not hold a ReplayBrowserContext field (DESIGN §6.4)");
```

```
RBF_P5T1_Subsystem_EmptyManager_NoNullRef
```
Construct `new ReplayBrowserSubsystem()`, call `Initialize(new SubsystemConfig { Headless = true })`, call `Update(0.016f)` — assert no exception. No files loaded.

```
RBF_P5T1_SingleNode_SeekViaManager
```
Load one real `.fdp` file through the test helper. Get the wall ticks of frame N via the manager's context `Playback.GetFrameMetadata(N).WallClockTicks`. Call `_manager.SetBaseWallTicks(...)` and verify `_manager.BaseWallTicks` equals the target ticks and `_activeRepo == _manager.Contexts[nodeId].SandboxRepo`.

```
RBF_P5T1_Merged_SeekRebuildsTransientMaster
```
Load two files, switch to Merged view using `LoadFdpGroupForTest` + `SetViewMode(ViewMode.Merged)` with a `TransientBuildOverride` that returns a new `EntityRepository()` each time. Capture the `_activeRepo` before a seek. Trigger `_manager.SetBaseWallTicks(newTicks)`. Assert `_activeRepo` is a different reference (new transient master).

```
RBF_P5T1_EventBrowser_CurrentFrameProvider_UsesActiveContext
```
Load one file. Get the `CurrentFrameProvider` from the event panel (if accessible via internal property). Advance the manager by one frame. Verify the provider returns the correct frame number matching `_manager.Contexts[nodeId].CurrentFrame`.

---

## Task RBF-P5T2 — `ReplayTimelinePanel` drives `FederatedReplayManager` directly

**Full spec:** `TASK-DETAILS.md#rbf-p5t2`

### What exists now

`ReplayTimelinePanel` holds `private readonly ReplayBrowserContext _context`. It uses `_context.*` throughout: `_context.Playback`, `_context.CurrentFrame`, `_context.SeekToFrame(n)`, `_context.StepForward()`, `_context.StepBackward()`, `_context.SandboxRepo`, `_context.CurrentFdpPath`.

### Changes required

**1. Replace the constructor.** The new constructor signature:

```csharp
public ReplayTimelinePanel(
    FederatedReplayManager? manager,
    Func<int> getSelectedNodeId,
    IRecordingExportService exportService,
    IFileDialogService fileDialogService,
    PlaybackHistoryTracker playbackHistory,
    InspectorState inspectorState)
```

Remove the `ReplayBrowserContext context` parameter. Store `_manager` and `_getSelectedNodeId` as fields (both nullable/func-based).

**2. Add a private property `ActiveContext`** that returns `ReplayBrowserContext?` for the current selected node:
```csharp
private ReplayBrowserContext? ActiveContext =>
    _manager != null && _manager.Contexts.TryGetValue(_getSelectedNodeId(), out var ctx) ? ctx : null;
```

**3. Replace `_context.*` usages:**

| Original | Replacement |
|---|---|
| `_context.Playback != null` | `ActiveContext?.Playback != null` |
| `_context.CurrentFrame` | `ActiveContext?.CurrentFrame ?? -1` |
| `_context.Playback.GetFrameMetadata(n)` | `ActiveContext!.Playback!.GetFrameMetadata(n)` |
| `_context.SandboxRepo.HasSingletonUnmanaged<GlobalTime>()` | `ActiveContext?.SandboxRepo?.HasSingletonUnmanaged<GlobalTime>() ?? false` |
| `_context.SandboxRepo.GetSingletonUnmanaged<GlobalTime>()` | `ActiveContext!.SandboxRepo!.GetSingletonUnmanaged<GlobalTime>()` |
| `_context.SeekToFrame(targetFrame)` | Translate frame `targetFrame` of `ActiveContext` to wall ticks; call `_manager!.SetBaseWallTicks(wallTicks)` |
| `_context.StepForward()` | Compute next-frame wall ticks on `ActiveContext`; call `_manager!.SetBaseWallTicks(nextTicks)` |
| `_context.StepBackward()` | Same in reverse |
| `_context.CurrentFdpPath` (for JSON export) | `ActiveContext?.CurrentFdpPath` |

**4. `LoadFdpAsync` / `OnLoadGroup` cleanup.** The method currently calls `_context.LoadRecording(paths[0])` as a fallback when `OnLoadGroup` is null. Remove this fallback. The method must only invoke `OnLoadGroup` if not null, and return early (clearing UI state on success, setting rejection modal on failure). No per-file `LoadRecording` call from the panel.

**5. Update `ReplayBrowserSubsystem.Initialize`** to construct the panel with the new signature:
```csharp
_timelinePanel = new ReplayTimelinePanel(
    _manager,                            // null at init time — will be set after LoadGroup
    () => _manager?.LocalEntitiesProviderNodeId ?? 0,
    _exportService,
    _fileDialogService,
    _playbackHistory,
    _inspectorState);
```

Note: `_manager` is `null` at `Initialize` time. The panel must accept a null manager and render in a "no recording" state (all transport buttons disabled). Wire up a `SetManager(FederatedReplayManager)` method or make the field non-readonly so the subsystem can set it after `LoadGroup`. The preferred approach: add `internal void SetManager(FederatedReplayManager manager)` and call it inside the `OnLoadGroup` success path.

### Tests for RBF-P5T2

Add to `FDP/Engine/Fdp.Presentation.Tests/` (new or existing file for P5 timeline tests):

```
RBF_P5T2_Panel_NoContextField
```
Reflection: `typeof(ReplayTimelinePanel)` has no field of type `ReplayBrowserContext`.

```
RBF_P5T2_SliderMove_CallsSetBaseWallTicks
```
Construct panel with a spy `FakeManager` (implements just `SetBaseWallTicks` tracking). Simulate slider change to frame N on the active context. Assert `manager.SetBaseWallTicks` was called with the wall-tick of frame N from `GetFrameMetadata(N).WallClockTicks`.

```
RBF_P5T2_StepForward_AdvancesBaseWallTicks
```
Single-node context at frame 0. Call the internal step-forward path (simulate the transport button press or call the method directly via test seam). Assert `_manager.BaseWallTicks` equals the wall-tick of frame 1.

```
RBF_P5T2_StepBackward_RewindsBaseWallTicks
```
Context at frame 2. Step backward. Assert `_manager.BaseWallTicks` equals wall-tick of frame 1.

```
RBF_P5T2_LoadGroup_DoesNotDoubleLoad
```
Construct panel with a spy `OnLoadGroup` callback returning `null` (success). Call `LoadFdpAsync` (or the synchronous equivalent via test seam). Assert `OnLoadGroup` was called exactly once. Assert no `ReplayBrowserContext.LoadRecording` call happened (verify by checking that manager `Contexts` count matches only what `OnLoadGroup` would have produced — or by using a mock context).

```
RBF_P5T2_LoadGroup_RejectionStillShowsModal
```
`OnLoadGroup` returns `"exercise mismatch"`. Assert `LoadGroupRejectionReason == "exercise mismatch"`.

---

## Task RBF-P5T3 — Diff engine routed through `_activeRepo` with two-rebuild cycle

**Full spec:** `TASK-DETAILS.md#rbf-p5t3`
**Design ref:** DESIGN §6.2.3

### What exists now

In `ReplayBrowserSubsystem.Update`, the reactive diff block:
```csharp
_context.SeekToFrame(currentFrame - 1, suppressHistory: true);
_diffPanel.CurrentDiffs = _diffService.ComputeEntityDiff(
    currentEntity.Value,
    _context.SandboxRepo,
    _scenarioSerializer,
    () => _context.StepForward(suppressHistory: true));
```

This bypasses the manager and uses the now-deleted `_context`.

### Changes required

Replace the diff block with a manager-driven two-rebuild cycle. The core algorithm (per DESIGN §6.2.3):

1. **Guard:** only run if `_manager != null` and at least one context is loaded and `currentFrame > 0`.
2. **Stable identity:** Track the selected entity's stable identity (not the transient `Entity` handle, which changes on rebuild). In Single-Node mode this can be the `Entity` handle itself (it's stable across `SeekToFrame`). In Merged mode use `NetworkIdentity.Value` (a `long`) from `_activeRepo`, or the synthetic local Guid key (a `string`) looked up in the resolver.
   - **Simpler approach for implementation:** always track `NetworkIdentity.Value` when the entity has `NetworkIdentity`; otherwise track the `Entity` handle (which only works in Single-Node where the repo is stable). This avoids requiring the resolver from outside `TransientMasterBuilder`.
3. **Compute "before" state:**
   - Get `primaryCtx = _manager.Contexts[_manager.LocalEntitiesProviderNodeId]`
   - `prevTicks = primaryCtx.Playback!.GetFrameMetadata(currentFrame - 1).WallClockTicks`
   - `_manager.SetBaseWallTicks(prevTicks)` — fires `OnTimeChanged`, which rebuilds in Merged or seeks in Single-Node
   - Locate the entity in `_activeRepo` using stable identity
   - Serialize: `before = _scenarioSerializer.SerializeEntity(_activeRepo, entityHandle, resolver, mask)`
   - For the resolver and mask in Single-Node mode: use `DiagnosticGuidResolver` and `entityIndex.GetComponentMask(entity)`. In Merged mode these are also fine — the transient master contains the merged components.
4. **Compute "after" state:**
   - `_manager.SetBaseWallTicks(currentTicks)` — restores the "after" state
   - Locate entity again using stable identity
   - Serialize: `after = _scenarioSerializer.SerializeEntity(_activeRepo, entityHandle, resolver, mask)`
5. **Feed to diff service:** `_diffPanel.CurrentDiffs = _diffService.ComputeTreeDiff(before, after, epsilon)`.
6. **Null-guard:** if the entity is missing in "before" or "after" (it may not exist at that frame), treat the missing side's `JsonObject` as `null`. The existing `ComputeTreeDiff` overload should handle this (check its signature; if it doesn't, produce an empty diff rather than throwing).

**Implementation note on finding the entity in `_activeRepo` by stable identity:**
```csharp
// Find entity in the (possibly rebuilt) _activeRepo by NetworkIdentity
private Entity FindEntityByNetworkId(long networkId)
{
    foreach (var e in _activeRepo.GetAllEntities())
    {
        if (_activeRepo.HasComponent<NetworkIdentity>(e) &&
            _activeRepo.GetComponentRO<NetworkIdentity>(e).Value == networkId)
            return e;
    }
    return Entity.Null;
}
```

For provider-local entities (no `NetworkIdentity`), the entity handle is stable in Single-Node mode (no rebuild), so the handle can be used directly. In Merged mode, if the entity is a local provider entity, finding it by handle won't work across rebuilds — use the synthetic Guid key if accessible, otherwise skip diff for local entities in Merged mode (acceptable per SC-6).

**Do not use `ComputeEntityDiff`** — that overload steps the context forward internally; it is incompatible with the manager-driven two-rebuild design.

### Extracting `before` and `after` from `_activeRepo`

The `ScenarioSerializer` has `SerializeEntity(EntityRepository, Entity, IGuidResolver, BitMask512)`. You need a resolver and mask:
- **Resolver:** `new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver()` — appropriate for both modes; it serializes `Entity` handles as Guid strings the diff service can compare as strings.
- **Mask:** `_activeRepo.GetEntityComponentMask(entityHandle)` or equivalent.

### Tests for RBF-P5T3

Add to `Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs`:

```
RBF_P5T3_Diff_SingleNode_StillProducesDiff
```
Load one `.fdp` file. Seek to a known frame where a component changes. Select an entity. Trigger the diff cycle (call `Update(0)` twice with the same entity selected but different frames). Assert `_diffPanel.CurrentDiffs` has at least one entry.

```
RBF_P5T3_Diff_Merged_ProducesDiff
```
Load two synthetic `.fdp` files with `TransientBuildOverride`. The override returns a repo with a known entity. After mode switch to Merged, select that entity, advance frame. Assert `_diffPanel.CurrentDiffs` is non-empty and `CurrentDiffs.Count > 0`.

```
RBF_P5T3_Diff_Merged_TwoRebuilds
```
Use `TransientBuildOverride` that increments a counter on each call. Set to Merged mode. Trigger one diff cycle. Assert counter incremented by exactly 2. Assert `_manager.BaseWallTicks` is restored to the "after" ticks (not left at "before" ticks) after the diff.

```
RBF_P5T3_Diff_StableIdentityAcrossRebuilds
```
In Merged mode with `TransientBuildOverride`, the override each time returns a fresh repo with a different `Entity` handle for the same `NetworkIdentity.Value`. Run one diff cycle. Assert no exception thrown and `CurrentDiffs` is populated (proving the entity was found by `NetworkIdentity` in both repos, not by stale handle).

```
RBF_P5T3_Diff_NoCrashOnMissingEntity
```
In Merged mode where the "before" rebuild returns an empty repo (entity does not exist at `frame - 1`). Assert no exception. `CurrentDiffs` may be empty or have a "wholly added" representation — the important thing is no crash.

---

## Task RBF-P5T4 — Disable "Seek to Previous/Next Change" arrows in Merged View

**Full spec:** `TASK-DETAILS.md#rbf-p5t4`
**Design ref:** DESIGN §6.2.3

### What exists now

`ComponentDiffPanel` renders `##prev_change` and `##next_change` buttons enabled whenever `!IsSearching`. There is no gate for merged view.

`ReplayBrowserSubsystem.WireDelegates` wires:
```csharp
_diffPanel!.OnSeekToChangeRequested = direction =>
{
    if (_inspectorState?.SelectedEntity != null)
        _seekToChangeTask = SeekToNextChangeAsync(_inspectorState.SelectedEntity.Value, direction);
};
```

### Changes required

**1. Add `IsMergedViewQuery` to `ComponentDiffPanel`:**
```csharp
/// <summary>Returns true when the active view is Merged View; used to disable step-change search.</summary>
public Func<bool>? IsMergedViewQuery { get; set; }
```

**2. In `ComponentDiffPanel.DrawContent`, add the merged-view gate.** Replace the button-enabled condition from `!IsSearching` to `!IsSearching && !(IsMergedViewQuery?.Invoke() ?? false)`. Add specific tooltip text when in merged view:

```csharp
bool isMerged = IsMergedViewQuery?.Invoke() ?? false;
bool prevNextEnabled = !IsSearching && !isMerged;

TransportIconRenderer.DrawButton("##prev_change", 20f, TransportShape.StepBack, prevNextEnabled, out _, out bool prevClicked);
if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
{
    Gui.SetTooltip(isMerged
        ? "Step-change search is disabled in Merged View. Switch to Single-Node View to seek to the next change."
        : "Seek to previous frame with changes");
}
if (prevClicked && prevNextEnabled)
    OnSeekToChangeRequested?.Invoke(-1);

Gui.SameLine();
TransportIconRenderer.DrawButton("##next_change", 20f, TransportShape.StepFwd, prevNextEnabled, out _, out bool nextClicked);
if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
{
    Gui.SetTooltip(isMerged
        ? "Step-change search is disabled in Merged View. Switch to Single-Node View to seek to the next change."
        : "Seek to next frame with changes");
}
if (nextClicked && prevNextEnabled)
    OnSeekToChangeRequested?.Invoke(1);
```

**3. Defense-in-depth in subsystem.** In `WireDelegates`, add a guard in the `OnSeekToChangeRequested` callback:
```csharp
_diffPanel!.OnSeekToChangeRequested = direction =>
{
    if (_viewMode == ViewMode.Merged) return; // defense-in-depth per DESIGN §6.2.3
    if (_inspectorState?.SelectedEntity != null)
        _seekToChangeTask = SeekToNextChangeAsync(_inspectorState.SelectedEntity.Value, direction);
};
```

**4. Wire `IsMergedViewQuery` in `SetViewMode` or `WireDelegates`:**
In `WireDelegates` or after `_diffPanel` is constructed:
```csharp
_diffPanel!.IsMergedViewQuery = () => _viewMode == ViewMode.Merged;
```

### Tests for RBF-P5T4

Add to `FDP/Engine/Fdp.Presentation.Tests/` (existing or new ComponentDiffPanel test file):

```
RBF_P5T4_PrevChange_DisabledInMerged
```
Construct `ComponentDiffPanel` with `IsMergedViewQuery = () => true`. Call `DrawContent()` (headless via Gui stubs). The `##prev_change` button must render with `enabled = false`. Assert `OnSeekToChangeRequested` is NOT invoked even if `prevClicked` is simulated.

```
RBF_P5T4_NextChange_DisabledInMerged
```
Same for `##next_change`.

```
RBF_P5T4_PrevNextChange_EnabledInSingleNode
```
`IsMergedViewQuery = () => false`, `IsSearching = false`. Buttons render with `enabled = true`.

```
RBF_P5T4_SubsystemShortCircuit_NoSeekInMerged
```
In `ReplayBrowserSubsystemTests`: set up subsystem in Merged view. Directly invoke `_diffPanel.OnSeekToChangeRequested(1)` via the wired delegate. Assert `_seekToChangeTask == null` (no seek task was started).

```
RBF_P5T4_TooltipContainsDisclaimer
```
With `IsMergedViewQuery = () => true`, verify the tooltip string contains `"Step-change search is disabled in Merged View"` (inspect the string constant rather than rendering — expose it as a `public const` or verify via test of the merged condition logic).

---

## Mandatory Workflow: Test-Driven Task Progression

Follow this workflow for each task above in order (C0 corrections first, then P5T1, P5T2, P5T3, P5T4):

1. **Read** the TASK-DETAILS.md entry for the task.
2. **Write failing test stubs** for the task's success conditions first.
3. **Implement** production code until all tests for this task pass.
4. **Run** the test suite: `dotnet test ... --filter "FullyQualifiedName~RBF_P5T<N>"` — all must be green.
5. **Run full project test suite** to verify no regressions.
6. **Move to next task.**

Do not implement multiple tasks simultaneously. C0 corrections first (they fix the foundation), then P5T1 (removes `_context`), then P5T2 (panel drives manager), then P5T3 (diff engine), then P5T4 (disable arrows).

---

## Developer Insights Section

In your report, answer all five questions:

**Q1.** What was the most complex part of excising `_context` from the subsystem? Which `_context.*` usages required the most careful reasoning to replace correctly?

**Q2.** `ReplayTimelinePanel` previously used `_context.Playback` and `_context.CurrentFrame` directly. How does the refactored panel handle the "no files loaded" state (null manager, null contexts)?

**Q3.** The diff engine's two-rebuild cycle must leave `_manager.BaseWallTicks` at the "after" ticks after the diff completes. Describe any edge case you encountered (e.g., exception mid-rebuild leaving the manager at the wrong tick) and how you handled it.

**Q4.** For RBF-P5T4, did you add `IsMergedViewQuery` to `ComponentDiffPanel` itself, or gate it in the subsystem's `OnSeekToChangeRequested` callback only? What is the trade-off?

**Q5.** What new weak points or technical debt did you spot during implementation? List anything you noticed but did not fix (to be captured in DEBT-TRACKER.md).

---

## Report format

Write your completion report to:
`.dev/replay-browser-frankenstein/reports/BATCH-05-REPORT.md`

Structure:

```
# BATCH-05 Report

## Tasks completed
- [ ] C0-D05
- [ ] C0-D06
- [ ] C0-D07
- [ ] RBF-P5T1
- [ ] RBF-P5T2
- [ ] RBF-P5T3
- [ ] RBF-P5T4

## Test results
<paste final test run output>

## Build result
<paste build result>

## Developer Insights
### Q1 ...
### Q2 ...
### Q3 ...
### Q4 ...
### Q5 ...

## Issues / deviations from spec
<list any deviations or spec ambiguities encountered>
```

---

## Success criteria for this batch

- `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 warnings.
- All existing tests continue to pass (no regressions).
- All RBF_P5T1 through RBF_P5T4 test methods pass.
- Reflection test confirms `ReplayBrowserSubsystem` has no `ReplayBrowserContext` field.
- Reflection test confirms `ReplayTimelinePanel` has no `ReplayBrowserContext` field.
- Debt items D05, D06, D07 marked RESOLVED in DEBT-TRACKER.md.
- TASK-TRACKER.md tasks RBF-P5T1 through RBF-P5T4 marked `[x]`.
