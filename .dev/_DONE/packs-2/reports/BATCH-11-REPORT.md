# BATCH-11 REPORT

**Batch:** BATCH-11  
**Tasks:** PACK2-R005 (EditorFileIO + FeatureSwitchRcu), PACK2-R006 (DistributedBrainMuscle)  
**Status:** COMPLETE

---

## Implementation Summary

### Task 1 — `ScenarioFileService` bus publish
`ScenarioFileService` updated with optional `FdpEventBus? bus` constructor parameter.  
**Critical discovery:** `SoftClear()` calls `Bus.ClearAll()`, which would wipe a `WorldResetEvent` published *before* the clear. Fixed by restructuring `NewScenario` and `LoadScenario` to call `_worldResetObservers?.Invoke()` (synchronous callbacks) **before** `SoftClear()`, and `_bus?.PublishManaged(new WorldResetEvent())` **after** `SoftClear()`. This preserves semantic ordering (callbacks fire before entity destruction) while ensuring the bus event survives the buffer clear.

### Task 2 — `EditorHarness` updates
- Added `_fileService` and `_logicPacks` fields.  
- Changed `Editor` to `{ get; private set; }`.  
- Exposed `FileService` property.  
- Added `SetTranslatorPacks(IReadOnlyList<IEcsModule>)` method.  
- `SimHostModule` (containing `NetworkSpawningSystem`) added to `_logicPacks` so it is also ejected on `SwitchToExternalAsync`. Without this, spawning still worked in External mode because `NetworkSpawningSystem` remained active.

### Task 3 — `CgfSubsystem.GhostEntityMap`
`_entityMap` promoted to a field; `internal NetworkEntityMap? GhostEntityMap => _entityMap` property added.

### Task 4 — `InternalsVisibleTo`
`Hrot.Map.Common.csproj` now includes `Hrot.ClusterRunner.Integration.Tests` in the `InternalsVisibleTo` list, granting access to the `SpawnEntityCommandEgressTranslator` internal testable constructor.

### Task 5 — `EditorFileIOIntegrationTests` (R005-A)
4 tests added: IT-2a, IT-2b, IT-2c, IT-2d. All pass.

### Task 6 — `FeatureSwitchRcuIntegrationTests` (R005-B)
4 tests added: IT-3a, IT-3b, IT-3c, IT-3d. All pass.  
`SpyEgressPack` uses the `Tick()` hook (runs after `SwapBuffers`) to call `_translator.PollIngress(view.GetCommandBuffer(), view)`.  
**Parallel interference fix:** `ModuleHostKernel.UninstallModulesAsync` and `InstallModulesAsync` use `Task.Run` for topology rebuild and background drain disposal. Under heavy parallel test load the thread-pool tasks can be delayed beyond the pump-loop timeout. Fixed with: (a) `Thread.Sleep(1)` between pump frames to yield CPU, (b) `SwitchMs = 30_000` timeout for switch operations, (c) `[Collection("EditorOfflineTests")]` attribute to serialise `OfflineEditorIntegrationTests`, `EditorFileIOIntegrationTests`, and `FeatureSwitchRcuIntegrationTests` onto a single thread, eliminating nested async contention.

### Task 7 — `DistributedBrainMuscleIntegrationTests` (R006)
3 tests added. IT-4c (`CgfAiIntent_ReachesSimHost_ViaDds`) is skipped.

---

## Answer to Report Questions

### Q1: Was IT-3d (DDS spy test) implementable?
**Yes.** `InternalsVisibleTo` granted access to the `SpawnEntityCommandEgressTranslator(IDdsWriter, FdpEventBus, IGeographicTransform)` constructor. `SpyEgressPack.Tick` calls `_translator.PollIngress(view.GetCommandBuffer(), view)` each frame, which drains `SpawnEntityCommand` events from the bus and calls `RecordingDdsWriter.Write`. After pumping 3 frames post-publish, `spy.CallCount == 1`. The `CycloneEgressSystem` approach was not needed.

### Q2: Did `WorldResetEvent` bus publish work?
Initially **no** — `SoftClear()` calls `Bus.ClearAll()`, wiping the event published in `FireWorldReset`. Fixed by publishing the bus event *after* `SoftClear()` (while keeping the synchronous `_worldResetObservers` callback *before*). After this fix `harness.Bus.ConsumeManaged<WorldResetEvent>().Count > 0` returns `true` after a single pump frame.

### Q3: Was IT-3c (CGF AI intent) implementable?
**No — skipped.** `NavigationIntent` exists in `FDP.Toolkit.Navigation` but CGF does not auto-assign navigation doctrines without an ExCon `MissionControlRequest` DDS message. The round-trip would require the full ExCon/MissionControl chain which is outside the scope of the SimHost-only harness. Marked `[Fact(Skip="...")]`.

### Q4: Did R006 DDS tests pass or skip?
**Failed with `CycloneDDS.Runtime.DdsException: Failed to create participant`** — this machine does not have a CycloneDDS native library installed. IT-4a and IT-4b fail immediately; IT-4c is skipped. This matches the existing pattern for all DDS-dependent tests in the suite (`HarnessSmokeTests`, `MiniExCon`, `ClusterOpE2e`). Domain counter starts at 300 (above HrotRunnerHarness range 100–199 and CgfHarness range 200–299).

### Q5: Suggested commit message

```
feat(editor): BATCH-11 R005/R006 — FileIO tests + FeatureSwitch RCU + Distributed Brain-Muscle stubs

Production changes:
- ScenarioFileService: add optional FdpEventBus param; publish WorldResetEvent on bus AFTER SoftClear
- CgfSubsystem: promote entityMap to _entityMap field; expose internal GhostEntityMap property
- Hrot.Map.Common.csproj: add InternalsVisibleTo for Hrot.ClusterRunner.Integration.Tests

Harness changes:
- EditorHarness: pass Bus to ScenarioFileService; expose FileService; add SetTranslatorPacks
- Include SimHostModule in logicPacks so NetworkSpawningSystem is ejected in External mode
- EditorFileIO + FeatureSwitchRcu + OfflineEditor grouped in [Collection("EditorOfflineTests")]

New tests (11 total):
- EditorFileIOIntegrationTests: IT-2a/2b/2c/2d (4/4 pass)
- FeatureSwitchRcuIntegrationTests: IT-3a/3b/3c/3d (4/4 pass)
- DistributedBrainMuscleIntegrationTests: IT-4a/4b fail without DDS; IT-4c skipped

Fix: SwitchMs=30_000 + Thread.Sleep(1) in RCU pump loop for thread-pool starvation
Tests: Editor.Tests 20/20; ClusterRunner.Integration.Tests 60 pass, 4 skip, 4 DDS-fail
```

---

## Test Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.Editor.Tests` | 20/20 ✅ | 20/20 ✅ | 0 |
| `Hrot.ClusterRunner.Integration.Tests` (full) | ~10 pass | 60 pass / 4 skip / 4 DDS-fail | +50 |
| EditorFileIO (filter) | — | 4/4 ✅ | +4 |
| FeatureSwitch (filter) | — | 4/4 ✅ | +4 |

DDS failures (4): `DistributedBrainMuscle` IT-4a/IT-4b (expected without DDS), `MiniExCon`, `ClusterOpE2e.LiveFromReplay` — all pre-existing DDS dependency failures or expected new ones.

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | UPDATE — bus param; publish after SoftClear |
| `Hrot.ClusterRunner/Services/CgfSubsystem.cs` | UPDATE — _entityMap field + GhostEntityMap property |
| `Hrot.Map.Common/Hrot.Map.Common.csproj` | UPDATE — InternalsVisibleTo for Integration.Tests |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | UPDATE — FileService, SetTranslatorPacks, SimHostModule in logicPacks |
| `Hrot.ClusterRunner.Integration.Tests/OfflineEditorIntegrationTests.cs` | UPDATE — [Collection] attribute |
| `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs` | NEW — IT-2a/2b/2c/2d |
| `Hrot.ClusterRunner.Integration.Tests/FeatureSwitchRcuIntegrationTests.cs` | NEW — IT-3a/3b/3c/3d |
| `Hrot.ClusterRunner.Integration.Tests/DistributedBrainMuscleIntegrationTests.cs` | NEW — IT-4a/4b/4c(skip) |
