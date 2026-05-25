# BATCH-50 Instructions

**Scope:** Debt resolution (D-BP-03, D-BP-05) + correctness hardening P11T3, P11T4, P11T5, P11T6, P11T11, P11T12  
**References:** [DESIGN.md](../DESIGN.md) · [TASK-DETAIL.md](../TASK-DETAIL.md) · [DEBT-TRACKER.md](../DEBT-TRACKER.md)  
**Test projects:** `Hrot.Diagnostics.Breakpoints.Tests`, `Hrot.ClusterRunner.Integration.Tests`

---

## Orientation

Read the following before starting:
- TASK-DETAIL.md §P11T3, §P11T4, §P11T5, §P11T6, §P11T11, §P11T12 (full success conditions)
- DESIGN.md §6.2, §9, §13.5 (for P11T12)
- DEBT-TRACKER.md rows D-BP-03, D-BP-05

Key files:
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` (and related test files)
- `.dev/breakpoints-1/DESIGN.md`

---

## Corrective Tasks (from BATCH-49 review)

### CT-1 — D-BP-03: Null asset guard in gutter renderers

**Problem:** `BTreeEditorHostServices.SetBreakpointManager` and `HsmEditorHostServices.SetBreakpointManager` construct their respective gutter renderers with `asset: null!`. Any call to `Render()` or `CountManagerBreakpoints()`/`CountBreakpoints()` that accesses `_asset` will NRE.

**Fix in `BTreeBreakpointGutterRenderer.cs`:**
- At the very top of `Render(ICanvasRenderContext ctx)`, add:
  ```csharp
  if (_asset is null) return; // sentinel state: canvas not yet opened
  ```
- At the top of `CountManagerBreakpoints()`, add:
  ```csharp
  if (_manager is null || _asset is null) return 0;
  ```

**Fix in `HsmBreakpointGutterRenderer.cs`:**
- At the very top of `Render(ICanvasRenderContext ctx)`, add:
  ```csharp
  if (_asset is null) return; // sentinel state: canvas not yet opened
  ```
- At the top of `CountBreakpoints()`, add:
  ```csharp
  if (_asset is null) return (0, 0);
  ```

No new test file needed — the fix is covered by existing `BTree_GutterRenderer_ManagerWired_IsReady` and `Hsm_GutterRenderer_ManagerWired_IsReady` tests which already call these methods. Add one assertion to each test: after verifying the renderer is non-null, call the count method and assert it does NOT throw (`Assert.Equal(0, renderer.CountManagerBreakpoints())` — manager has 0 breakpoints with SourceElementId, so count is 0 regardless; the important thing is no NRE).

---

### CT-2 — D-BP-05: Coordinator event subscription wiring test

**Problem:** Integration tests 14-16 (BATCH-49) call `mgr.OnHotReloadBegin()` directly. They do not verify that `EditorSubsystem.Initialize()` subscribes to `_aiCoordinator.OnReloadBegin` (line 674 of `EditorSubsystem.cs`). If that subscription line is ever accidentally removed, no existing test will catch it.

**Fix:** Add test 19 to `BreakpointSubsystemWiringTests.cs`:

```
// ── Test 19: coordinator event subscription reaches the manager ──────────
public void HotReload_CoordinatorOnReloadBegin_PropagatesViaSub_ToManager()
```

Steps:
1. `var subsystem = new EditorSubsystem(); subsystem.Initialize(config)`
2. Get `var mgr = subsystem.DataBreakpointManager!`
3. Force a pause by adding a BP and calling `mgr.OnHit(bp, Entity.Null)` (same approach as test 14)
4. Assert `mgr.IsPaused == true`
5. Fire the event via the coordinator: `subsystem.AiCoordinator!.OnReloadBegin?.Invoke()`
   - `AiCoordinator` is the `internal AiHotReloadCoordinator? AiCoordinator` accessor on EditorSubsystem
   - `OnReloadBegin` is the `public event Action? OnReloadBegin` on `AiHotReloadCoordinator`
6. Assert `mgr.IsPaused == false` — proves the subscription wired in `Initialize` is active and correctly calls `mgr.OnHotReloadBegin()`

**Domain counter:** Use domain 163 (first unused after existing tests, which used 162-163 range). Actually verify the current `_domainCounter` value in the file and add to it if needed. This test does NOT use DDS so no domain increment is needed (test 19 is headless only).

Mark D-BP-05 as RESOLVED in DEBT-TRACKER.md.

---

## P11T3 — Enforce DataBreakpointSystem ordering after RecorderTickSystem

See TASK-DETAIL.md §UBP-P11T3.

**Work:**
1. In `DataBreakpointSystem.cs`, add `using Fdp.Toolkit.Replay;` at the top.
2. Add `[UpdateAfter(typeof(RecorderTickSystem))]` attribute to `DataBreakpointSystem` class (below `[UpdateInPhase(SystemPhase.PostSimulation)]`). The `UpdateAfterAttribute` is in `Fdp.Core` which is already imported. The `RecorderTickSystem` is in `Fdp.Toolkit.Replay` which requires the new using.
3. Update the class docstring to mention: "Scheduled after `RecorderTickSystem` (when both are in the same PostSimulation phase) to guarantee the flight recorder captures the natural tick-N state before any rewind is applied."

**Test:** Add to `BreakpointSubsystemWiringTests.cs` as test 20:

```
public void RecorderRunsBeforeBreakpointSystem_InKernel()
```

Steps:
1. Boot `EditorSubsystem` headless
2. Install a recording module into the kernel. The subsystem exposes `Kernel` (check `EditorSubsystem` internals). Use `RecordingModule` or `EpisodeRecorderModule` — look for how existing integration tests in the solution install recording (check `RecordingModuleTests.cs` and `EcsRecordReplayController`). If adding a recorder module requires a file path, use a temp path with `.fdp` extension.
3. Rebuild execution orders: `subsystem.Kernel.BuildExecutionOrders()` (if the scheduler needs to be explicitly rebuilt after new registrations).
4. Get the list of all PostSimulation systems in order from `subsystem.Kernel.Scheduler.GetAllSystems()` (check what's accessible internally).
5. Find the indices of `RecorderTickSystem` and `DataBreakpointSystem` in that list.
6. Assert `recorderIdx < bpSystemIdx`.

**If installing a recorder is non-trivial:** provide an alternative test that skips recorder installation and instead verifies the `[UpdateAfter]` attribute is present on `DataBreakpointSystem` via reflection:
```csharp
var attrs = typeof(DataBreakpointSystem)
    .GetCustomAttributes(typeof(Fdp.Core.UpdateAfterAttribute), inherit: false);
Assert.Contains(attrs, a => ((Fdp.Core.UpdateAfterAttribute)a).Target == typeof(RecorderTickSystem));
```
This is a weaker but valid test. Write the reflection test regardless; write the scheduler order test only if the kernel API makes it straightforward.

---

## P11T4 — OnHit re-entrancy guard

See TASK-DETAIL.md §UBP-P11T4.

**Work in `DataBreakpointManager.OnHit`:**
Add at the very top of `OnHit`, BEFORE the null check and BEFORE the `TryGetValue`:
```csharp
if (_isPaused) return; // already paused: drop same-tick re-entrant hits
```

The first hit wins; later hits within the same tick are silently dropped.

**Tests** in a new file `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ReentrancyTests.cs`:

1. `OnHit_SecondHitInSameTick_DoesNotOverwritePostTickSnapshot`:
   - Create manager (ManagerFactory.Create), add entity with TestHealth in liveRepo
   - Register BP A (PropertyMatchDto on TestHealth)
   - Directly call `mgr.OnHit(bpA, entity)` → triggers pause, captures `PostTickSnapshot`
   - Record `PostTickSnapshot` state (e.g., capture `mgr.PostTickSnapshot.GlobalVersion`)
   - Modify liveRepo to have a different state
   - Call `mgr.OnHit(bpB, entity2)` (a different BP or same, doesn't matter)
   - Assert `mgr.PostTickSnapshot` state is UNCHANGED (same GlobalVersion as before second hit)
   - Assert `mgr.IsPaused == true` still (not double-paused or corrupted)

2. `EvaluateStatefulBreakpoints_MultipleHits_PausesOnce`:
   - Create manager, register 3 structural BPs all watching the same component type
   - Add an entity; let `EvaluateStatefulBreakpoints` call fire (call it directly on the manager with a repo)
   - All 3 BPs will match → all 3 call `OnHit`, but only the first one should actually pause
   - Assert `timeController.PauseRequestCount == 1`
   - Assert `OnPauseStateChanged` fired exactly once (use an event counter)

---

## P11T5 — PausedTick uses GlobalTime.TotalWallTicks

See TASK-DETAIL.md §UBP-P11T5.

**Interface change in `IDataBreakpointManager.cs`:**
Change `uint PausedTick { get; }` to `long PausedTick { get; }`.

**Manager change in `DataBreakpointManager.cs`:**
1. Change `private uint _pausedTick;` to `private long _pausedTick;`
2. Change `public uint PausedTick => _pausedTick;` to `public long PausedTick => _pausedTick;`
3. In `OnHit`, replace `_pausedTick = _preTickSnapshot.GlobalVersion;` with:
   ```csharp
   _pausedTick = _liveRepo.HasSingletonUnmanaged<GlobalTime>()
       ? _liveRepo.GetSingletonUnmanaged<GlobalTime>().TotalWallTicks
       : (long)_preTickSnapshot.GlobalVersion; // fallback when GlobalTime not registered
   ```
   The `using Fdp.Core;` is already at the top of `DataBreakpointManager.cs`.
4. Change both `_pausedTick = 0;` in `RequestStep` and `RequestContinue` to `_pausedTick = 0L;`
5. The `_pausedTick = _preTickSnapshot.GlobalVersion;` in `OnExternalHit` (inside the fallback block at line ~572) will be removed by P11T6. Don't touch it here.

**Mock stubs to update:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreeBreakpointWiringTests.cs` — stub mock: `public uint PausedTick => 0;` → `public long PausedTick => 0L;`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmBreakpointWiringTests.cs` — same change

**Tests** in `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TemporalStatusBannerTests.cs` (add to existing class):

1. `PausedTick_ReflectsGlobalTimeTotalWallTicks`:
   - Create `(manager, liveRepo, _, _) = ManagerFactory.Create()`
   - Register GlobalTime in liveRepo: `liveRepo.RegisterComponent<GlobalTime>(); liveRepo.SetSingletonUnmanaged(new GlobalTime { TotalWallTicks = 0xABCDEFL })`
   - Add a breakpoint and call `OnHit` to trigger pause
   - Assert `manager.PausedTick == 0xABCDEFL`

2. `BannerShowsWallClockTickNotVersionCounter`:
   - Same setup: register GlobalTime with `TotalWallTicks = 12345L` and `GlobalVersion` of repo will be some other value
   - Pause via OnHit
   - `new TemporalStatusBannerState().Refresh(manager)`
   - Assert `state.StatusText.Contains("Tick 12345")` — confirms banner uses wall ticks (12345), not the repo version counter

3. `PausedTick_FallbackToRepoVersion_WhenGlobalTimeNotRegistered`:
   - Create manager WITHOUT registering GlobalTime in liveRepo
   - Pause via OnHit
   - Assert `manager.PausedTick == (long)preTickSnapshot.GlobalVersion` (i.e., the fallback works and doesn't throw)
   - Use the `liveRepo` and `snapshotProvider` from ManagerFactory.Create() to confirm

---

## P11T6 — OnExternalHit fallback removal

See TASK-DETAIL.md §UBP-P11T6.

**Work in `DataBreakpointManager.OnExternalHit`:**
Delete the entire fallback block:
```csharp
// If no universal breakpoint fired, still perform the triple-buffer rewind
// so Slice 1 Blueprint probe-driven hits get pre-execution inspection.
if (!anyFired && !_isPaused)
{
    _postTickSnapshot.SyncFrom(_liveRepo);
    _liveRepo.SyncFrom(_preTickSnapshot);
    _timeController.RequestPause();
    _isPaused = true;
    _pausedTick = _preTickSnapshot.GlobalVersion;
    OnPauseStateChanged?.Invoke(true);
}
```
After removal, `OnExternalHit` is a no-op when no registered tag matches.

Note: The `_pausedTick = _preTickSnapshot.GlobalVersion;` inside this block was the ONLY remaining occurrence of `uint`-style GlobalVersion assignment after P11T5 changes. Removing it here keeps the code consistent.

**Tests** in `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ExternalHitTagTests.cs` (add to existing class):

1. `OnExternalHit_NoTagMatch_DoesNotPause`:
   - Create manager; do NOT register any breakpoints
   - Call `manager.OnExternalHit("nonexistent-tag", Entity.Null)`
   - Assert `manager.IsPaused == false`
   - Assert no `OnPauseStateChanged` event fired (subscribe with a counter; assert count == 0)

2. `OnExternalHit_TagMatch_StillPausesAndRewinds`:
   - Create manager; add BP with `ExternalHitTagPredicateDto { Tag = "hit-me" }`
   - Call `manager.OnExternalHit("hit-me", Entity.Null)`
   - Assert `manager.IsPaused == true`
   - Assert `OnPauseStateChanged` fired once with value `true`

---

## P11T11 — Reusable hits buffer in EvaluateStatefulBreakpoints

See TASK-DETAIL.md §UBP-P11T11.

**Work in `DataBreakpointManager.cs`:**
1. Add a private field: `private readonly List<(Breakpoint bp, Entity entity)> _statefulHitsBuffer = new();`
   Place it near the other private fields (around line 88-95).
2. In `EvaluateStatefulBreakpoints`, replace:
   ```csharp
   var hits = new List<(Breakpoint bp, Entity entity)>();
   ```
   with:
   ```csharp
   _statefulHitsBuffer.Clear();
   var hits = _statefulHitsBuffer;
   ```
   The `hits` variable remains unchanged in the method body; only the allocation moves to the field.

**Tests** in `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointSystemStatefulTests.cs` (add to existing class):

1. `StatefulEvaluation_HitsBuffer_IsReusedAcrossCalls`:
   - Register a structural BP
   - Call `manager.EvaluateStatefulBreakpoints(repo)` twice (with different entity states to ensure at least one hit on first call)
   - Assert the manager still works correctly (not a zero-alloc test, just a correctness smoke test that the field reuse doesn't corrupt state)
   - Essentially: verify that on second call after a hit, `IsPaused` may or may not be true depending on whether the re-entrancy guard (P11T4) drops the second evaluation — test whatever invariant makes sense here

Note: The BenchmarkDotNet test (`StatefulEvaluation_ZeroAllocations`) is out of scope for this batch. Only the functional regression test is required here. Leave a TODO comment in the test file marking where the benchmark should go.

---

## P11T12 — API / DESIGN alignment

See TASK-DETAIL.md §UBP-P11T12.

### P11T12 Work Item A — Remove silent OccurrenceThreshold coercion

In `DataBreakpointManager.AddBreakpoint`, change:
```csharp
OccurrenceThreshold = occurrenceThreshold > 0 ? occurrenceThreshold : 1,
```
to:
```csharp
OccurrenceThreshold = occurrenceThreshold >= 1 ? occurrenceThreshold
    : throw new ArgumentOutOfRangeException(nameof(occurrenceThreshold),
          "Occurrence threshold must be ≥ 1. Pass 1 to pause on first hit."),
```

Add an XML-doc param comment on `AddBreakpoint`'s `occurrenceThreshold` parameter:
```csharp
/// <param name="occurrenceThreshold">
/// Number of hits required before the breakpoint pauses execution.
/// Must be ≥ 1. Pass 1 (default) to pause on the very first hit.
/// </param>
```

### P11T12 Work Item B — Update DESIGN.md

In `.dev/breakpoints-1/DESIGN.md`:

1. §6.2 (Breakpoint record): Change `// pause only on Nth+ hit; 0 = every hit` to `// pause only on Nth+ hit; must be ≥ 1`
2. §9 (Manager API): Change `event Action OnPauseStateChanged;` to `event Action<bool> OnPauseStateChanged;` to match the implementation
3. §13.5 (OccurrenceThreshold description): Remove the claim "Zero (default) = pause on every hit." Replace with "Default 1 = pause on the first hit. Minimum value 1; passing 0 throws `ArgumentOutOfRangeException`."

### P11T12 Work Item C — Test

Add to `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs`:

```
public void AddBreakpoint_ThresholdZero_Throws()
```
- Call `manager.AddBreakpoint(new PropertyMatchDto { ... }, occurrenceThreshold: 0)`
- Assert `throws ArgumentOutOfRangeException`

Also add:
```
public void AddBreakpoint_ThresholdOne_IsDefault_PausesOnFirstHit()
```
- Add BP with `occurrenceThreshold: 1`
- Fire one hit via `OnHit`
- Assert `manager.IsPaused == true`

---

## Task Tracker Updates

After all tasks are complete, update `.dev/breakpoints-1/TASK-TRACKER.md`:
- Mark `[x]` for: P11T3, P11T4, P11T5, P11T6, P11T11, P11T12

Update `.dev/breakpoints-1/DEBT-TRACKER.md`:
- D-BP-03: status → RESOLVED
- D-BP-05: status → RESOLVED

---

## Build & Test Verification

```
dotnet build IOS-IG-SimHost.sln -v quiet
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~BreakpointSubsystemWiring"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

All must pass with 0 errors and 0 test failures.

---

## Report

Write a report at `.dev/breakpoints-1/reports/BATCH-50-REPORT.md` covering:
1. All files changed (list each file and what changed)
2. Any deviations from these instructions and why
3. Test names added / changed / removed
4. Build output (errors, warnings)
5. Test results per project
