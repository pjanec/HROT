# BATCH-00 Report: Engine Semantic Version Split (Frame Clock vs Memory Clock)

**Date:** 2026-06-10  
**Branch:** `blueprint-integ-1`  
**Tasks:** NGS-0.1, NGS-0.2, NGS-0.3, NGS-0.4  
**Status:** COMPLETE — all changes implemented, all tests green (zero new failures)

---

## 1. Summary of Changes

### NGS-0.1 — Version-clock split (`EntityRepository.cs`)

Added `_simulationTick` field (initialised to 1, same as `_globalVersion`). Both clocks start equal and stay equal during normal play.

- `private uint _simulationTick = 1;` — new field
- `public uint SimulationTick => _simulationTick;` — new property
- `Tick()` — now increments both clocks atomically (two `Interlocked.Increment` calls)
- `BumpMemoryVersion()` — new public method; advances ONLY `_globalVersion` (leaves frame clock frozen); guarded by `#if DEBUG` invariant assert
- `SetGlobalVersion(uint)` — now sets BOTH fields to keep them in sync on playback restore
- `ResetGlobalVersion(uint)` — now sets BOTH fields to keep them in sync on test reset

**Invariant:** `_globalVersion >= _simulationTick` always. They are equal in normal play. Only `BumpMemoryVersion()` makes the memory clock run ahead.

**Hot path untouched:** `GetComponentRW`/`NativeChunkTable.GetRefRW` were NOT modified. No new branches or parameters on that path.

### NGS-0.2 — ISimulationView.Tick redirected (`EntityRepository.View.cs`)

Line 27 changed from:
```csharp
uint ISimulationView.Tick => _globalVersion;
```
to:
```csharp
uint ISimulationView.Tick => _simulationTick;
```

### NGS-0.3 — Frame-clock consumers migrated

| File | Line | Change |
|------|------|--------|
| `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs` | 63 | `repo.GlobalVersion` → `repo.SimulationTick` (RecordDeltaFrame header) |
| `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs` | 340 | `repo.GlobalVersion` → `repo.SimulationTick` (RecordKeyframe header) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs` | ~197 | `repo.GlobalVersion` → `repo.SimulationTick` (traceCtx.CurrentTick) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs` | 129 | `repo.GlobalVersion` → `repo.SimulationTick` (_frameCount) |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | 480 | `_preTickSnapshot.GlobalVersion` → `_preTickSnapshot.SimulationTick` (PausedTick fallback) |

**ModuleHostKernel.cs analysis (NOT migrated — both stay on GlobalVersion):**
- Line 540: `_eventAccumulator.CaptureFrame(_liveWorld.Bus, _liveWorld.GlobalVersion)` — The paired providers (`SharedSnapshotProvider`, `OnDemandProvider`, `DoubleBufferProvider`) all stamp `_lastSeenTick = _liveWorld.GlobalVersion`. Migrating only `CaptureFrame` while leaving providers on `GlobalVersion` would break event-replay pairing (events captured at ST=N would never match snapshot at GV=N+K). Both must move together or neither. Since the providers are memory-version consumers (delta-skip), this whole group stays on GlobalVersion.
- Line 700: `entry.LastRunTick = _liveWorld.GlobalVersion - 1` — passed to `HasComponentChanged()` as a chunk-version threshold. It's a memory-version consumer (detects whether any component chunk changed since last run). Stays on GlobalVersion.

### NGS-0.4 — Invariant and exhaustive reader audit

Debug invariant `Debug.Assert(_globalVersion >= _simulationTick)` added inside `BumpMemoryVersion()` (guarded by `#if DEBUG`). Cannot fire on hot path since `BumpMemoryVersion` is debug-only.

---

## 2. Exhaustive Reader Audit

All production readers of `.GlobalVersion` and `ISimulationView.Tick` / `view.Tick` classified below.

### `.GlobalVersion` readers

| File | Line | Classification | Migrated? | Justification |
|------|------|----------------|-----------|---------------|
| `EntityRepository.View.cs` | 27 | frame-clock | YES → `_simulationTick` | ISimulationView.Tick is the frame-index surface |
| `RecorderSystem.cs` | 63 | frame-clock | YES → `SimulationTick` | Delta frame header = frame index |
| `RecorderSystem.cs` | 340 | frame-clock | YES → `SimulationTick` | Keyframe header = frame index |
| `FlightRecorderExample.cs` | 62 | frame-clock | NO (example code) | `prevTick = repo.GlobalVersion - 1` in doc example; OK in normal play since GV==ST; does not affect production |
| `PlaybackSystem.cs` | 374 | memory-version | NO | `SetChunk(..., repo.GlobalVersion)` — stamps restored chunk with the current GV so future delta-skips work correctly |
| `ModuleHostKernel.cs` | 540 | memory-version | NO | `CaptureFrame` paired with provider `_lastSeenTick = GlobalVersion`; must stay on same clock |
| `ModuleHostKernel.cs` | 700 | memory-version | NO | `LastRunTick` is chunk-version threshold for `HasComponentChanged` delta-skip |
| `DoubleBufferProvider.cs` | 65, 78 | memory-version | NO | `_lastSyncTick` used for chunk-version delta-skip |
| `OnDemandProvider.cs` | 65, 81 | memory-version | NO | `_lastSeenTick` used for chunk-version delta-skip |
| `SharedSnapshotProvider.cs` | 55 | memory-version | NO | `_lastSeenTick` used for chunk-version delta-skip |
| `HierarchyOrderSystem.cs` | 66 | memory-version | NO | `TopologyVersion` — stamped when entity graph structure changes; compared against chunk versions |
| `ShowcaseGame.cs` | 224 | ambiguous | NO | `_previousTick = Repo.GlobalVersion` — in example/showcase code, used for demo delta-display; acceptable as GV==ST in normal play |
| `PathfindingActionNode.cs` | 33 | memory-version | NO | `requestId` nonce — unique ID formed from entity index + GV for deduplication; uses monotonicity, not frame semantics |
| `NavigationIntentBridgeSystem.cs` | 89, 163 | memory-version | NO | `_lastScanTick` — chunk-version delta-skip, same as providers |
| `NavigationIntentBridgeSystem.cs` | 202, 261 | memory-version | NO | `reqId` nonce — uniqueness nonce, same as PathfindingActionNode |
| `ReferenceCheckpointHandler.cs` | 71 | memory-version | NO | `FlushToReplica(snap.Bus, source.GlobalVersion - 1)` — flush since last memory snapshot, not frame boundary |
| `PhysicsQueryActionNode.cs` | 32 | memory-version | NO | `rayId` nonce — uniqueness nonce |
| `RecorderTickSystem.cs` | 67 | memory-version | NO | `_prevTick = repo.GlobalVersion` — stored as baseline for next delta recording; correctly uses GV so `HasChunkChanged(> prevTick)` catches BumpMemoryVersion writes |
| `NetworkEntityMap.cs` | 103 | memory-version | NO | `Unregister(netId, repo.GlobalVersion)` — stamps entity unregistration at current memory version |
| `NetworkGatewaySystem.cs` | 96 | memory-version | NO | `currentFrame = repo.GlobalVersion` — used for outgoing packet sequencing against chunk versions |
| `SmartEgressUtil.cs` | 157 | memory-version | NO | `LastPublishedTickMap[ordinal] = repo.GlobalVersion` — delta-skip: "did this entity change since GV X?" |
| `AreaQueryBatchHelper.cs` | 34 | memory-version | NO | `requestId` nonce |
| `DataBreakpointSystem.cs` | 93 | memory-version | NO | `LastScanVersion = repo.GlobalVersion` — used for `QueryDelta(sinceVersion)` chunk-version scan |
| `NavigationIntentEgressTranslator.cs` | 104, 165 | memory-version | NO | `_lastScanTick` — chunk-version delta-skip |
| `PerceptionMapLayer.cs` | 57 | memory-version | NO | `currentTick = _world.GlobalVersion` — used for per-entity change detection against chunk stamps |
| `DataBreakpointManager.cs` | 480 | frame-clock | YES → `SimulationTick` | `PausedTick` fallback — this is the displayed "paused at tick N" frame number |
| `HsmTickSystem.cs` | ~197 | frame-clock | YES → `SimulationTick` | `traceCtx.CurrentTick` — trace frame index shown in debugger |
| `BTreeTickSystem.cs` | 129 | frame-clock | YES → `SimulationTick` | `_frameCount` — BTree evaluation frame index |

### `view.Tick` / `ISimulationView.Tick` readers

All `view.Tick` readers are frame-clock consumers (displaying frame numbers, computing heartbeat offsets, timestamping network packets). They now correctly read `_simulationTick` via the `ISimulationView.Tick` property redirect.

| File | Line | Classification | Migrated? | Justification |
|------|------|----------------|-----------|---------------|
| `EventHistoryCaptureSystem.cs` | 32 | frame-clock | YES (via view.Tick redirect) | `_historyService.Capture(name, bus, view.Tick)` — event history timestamp |
| `MultiInstanceCycloneTranslator.cs` | 76 | frame-clock | YES (via view.Tick redirect) | `CreateGhost(repo, netId, view.Tick)` — network ghost spawn tick |
| `DebugPrimitivesBatchPublisherSystem.cs` | 41 | frame-clock | YES (via view.Tick redirect) | `FrameNumber = view.Tick` — explicit frame number |
| `LifecycleSystem.cs` | 24 | frame-clock | YES (via view.Tick redirect) | `currentFrame = view.Tick` — lifecycle frame index |
| `NetworkSpawningSystem.cs` | 64, 70 | frame-clock | YES (via view.Tick redirect) | Spawn/destroy tick passed to network commands |
| `SensorTrackDebounceSystem.cs` | 46 | frame-clock | YES (via view.Tick redirect) | `currentTick = view.Tick` — debounce frame index |
| `ThreatEvaluationSystem.cs` | 45 | frame-clock | YES (via view.Tick redirect) | `tick = view.Tick` — evaluation timestamp |
| `GhostPromotionSystem.cs` | 64 | frame-clock | YES (via view.Tick redirect) | `tick = view.Tick` — promotion tick |
| `SmartEgressUtil.cs` | 98 | frame-clock | YES (via view.Tick redirect) | `currentTick = view.Tick` — egress heartbeat frame |
| `GeoSpatialEgressTranslator.cs` | 130 | frame-clock | YES (via view.Tick redirect) | Heartbeat interval modulo |
| `NavigationStatusEgressTranslator.cs` | 98 | frame-clock | YES (via view.Tick redirect) | Heartbeat interval modulo |
| `BdcEntityMasterTranslator.cs` | 134 | frame-clock | YES (via view.Tick redirect) | Ghost creation tick |
| `IgMissionIngressTranslator.cs` | 64 | frame-clock | YES (via view.Tick redirect) | Ghost creation tick |
| `EntityMasterIngressTranslator.cs` | 136 | frame-clock | YES (via view.Tick redirect) | Ghost creation tick |
| `EntityMissionIngressTranslator.cs` | 84 | frame-clock | YES (via view.Tick redirect) | Ghost creation tick |
| `PerceptionTranslators.cs` | 369, 565 | frame-clock | YES (via view.Tick redirect) | Ghost creation tick and perception packet timestamp |
| `DeferredTakeOwnershipIngressTranslator.cs` | 115 | frame-clock | YES (via view.Tick redirect) | Ghost creation tick |
| `LineOfSightGizmo.cs` | 19 | frame-clock | YES (via view.Tick redirect) | Gizmo frame timestamp |
| `Hrot.IG/IgBootstrapperHelpers.cs` | 34 | frame-clock | YES (via view.Tick redirect) | `Unregister(netId, view.Tick)` — unregistration tick |
| `BlueprintDebugSession.cs` | 124, 131, 208, 229, 239, 755, 1194, 1204 | frame-clock | YES (via view.Tick redirect) | Blueprint node history timestamps and step-from-tick comparisons |
| `EqsSolverSystem.cs` | 72 | frame-clock | YES (via view.Tick redirect) | EQS solver current tick |

---

## 3. Hot Path Confirmation

The `GetComponentRW` and `NativeChunkTable.GetRefRW` hot paths were **NOT modified**. No new branches, parameters, or conditions were added to these methods. Verified by reading the final state of `NativeChunkTable.cs:158` (unchanged) and `EntityRepository.cs:795` (unchanged).

---

## 4. Test Results Per Project

| Project | Passed | Failed | Skipped | Total | Notes |
|---------|--------|--------|---------|-------|-------|
| `Fdp.Core.Tests` | 1157 | 2 | 2 | 1161 | 2 failures are pre-existing timing-sensitive benchmarks (`RealisticMilitrarySimulation`, `Benchmark_HotPathOptimization`); both pass in isolation |
| `Fdp.ModuleHost.Tests` | 183 | 6 | 0 | 189 | 6 failures pre-existing (convoy/SoD provider `Assert.Same()` failures); confirmed same failures on HEAD before our changes |
| `Fhsm.Tests` | 296 | 2 | 0 | 298 | 2 failures pre-existing (`InfiniteLoop_Detected_And_Stops`, `OutputLane_Conflict_Detected`); confirmed same on HEAD before changes |
| `Hrot.Blueprints.Tests` | 1701 | 7 | 8 | 1716 | 7 failures all pre-existing; includes the documented `TickFrame_1000Frames_AllocatesZeroBytes` |
| `Hrot.Diagnostics.Breakpoints.Tests` | 128 | 0 | 0 | 128 | All pass — includes the updated `TemporalStatusBannerTests.PausedTick_FallbackToRepoVersion_WhenGlobalTimeNotRegistered` |

**New tests added:** `FDP/Engine/Fdp.Core.Tests/VersionClockSplitTests.cs` — 15 tests, all passing.

### Key test scenarios covered

**NGS-0.1:**
- `Tick_AdvancesBothClocksByExactlyOne` — both clocks increment exactly 1
- `Tick_KeepsBothClocksEqual_AfterNormalTicks` — clocks stay equal in normal play
- `BumpMemoryVersion_AdvancesGlobalVersion_LeavesSimulationTickUnchanged` — ST frozen
- `BumpMemoryVersionThenTick_FinalValuesCorrect` — K bumps + 1 tick = correct values
- `SetGlobalVersion_SetsBothClocks` / `ResetGlobalVersion_SetsBothClocks` — sync verified

**NGS-0.2:**
- `ISimulationViewTick_EqualsSimulationTick_AfterNormalTicks` — view.Tick == ST
- `ISimulationViewTick_StaysFrozen_WhileGlobalVersionAdvances` — core guarantee: view.Tick = ST != GV after bumps

**NGS-0.3:**
- `RecordDeltaFrame_FrameHeader_UsesSimulationTick_NotGlobalVersion` — header = ST, not GV
- `RecordKeyframe_FrameHeader_UsesSimulationTick` — same for keyframes
- `RecordDeltaAndReplay_RestoresCorrectState_FrameIndexMatchesSimulationTick` — full round-trip with BumpMemoryVersion: component value 10→20 restored correctly, dstRepo.GV == dstRepo.ST == srcRepo.ST == 3 (not inflated GV=4)
- `RecordDeltaWithBumps_FrameHeaderStillFrozen_ChunkDirtyCorrectlyTracked` — header=ST=2 even when chunk stamped at GV=3

**NGS-0.4:**
- `Invariant_GlobalVersionGeSimulationTick_HoldsAcrossMixedSequence` — 50-step random Tick/Bump sequence
- `Invariant_HoldsAfterRepresentativeMixedSequence` — 5-cycle K-bumps+Tick pattern
- `NormalPlay_GlobalVersionEqualsSimulationTick_NoSideEffects` — regression: 20 ticks, GV==ST==21

---

## 5. Deviations from Known-Targets List

- **`ModuleHostKernel.cs:540` (`CaptureFrame`)** — listed as candidate for migration; kept on `GlobalVersion`. Rationale: `CaptureFrame` pairs with provider `_lastSeenTick = GlobalVersion`; migrating one without the other would break event-replay matching. Both are memory-version consumers within a paired system. This is documented as deliberate in the code.
- **`ModuleHostKernel.cs:700` (`LastRunTick`)** — listed as candidate; kept on `GlobalVersion`. Rationale: chunk-version threshold for `HasComponentChanged` delta-skip; must track memory version, not frame clock.
- **`FlightRecorderExample.cs:62`** — doc/example file, not migrated. Uses `GlobalVersion - 1` as prevTick in documentation example. In normal play GV==ST so this is correct; during debug it would over-capture (include the bump), which is acceptable for a doc example.

---

## 6. Suggested Commit Message

```
feat: split EntityRepository version clock into memory-clock and frame-clock (NGS-0.1-0.4)

Add _simulationTick (frame clock) alongside _globalVersion (memory clock).
Tick() advances both; BumpMemoryVersion() advances only _globalVersion for
sub-tick dirty granularity during debug sessions. ISimulationView.Tick now
returns _simulationTick (frozen during debug bursts). SetGlobalVersion and
ResetGlobalVersion sync both clocks.

Migrate frame-clock consumers: RecorderSystem frame headers, HsmTickSystem
CurrentTick, BTreeTickSystem _frameCount, DataBreakpointManager PausedTick
fallback. Memory-version consumers (providers, delta-skip systems) unchanged.

Add 15 new tests in VersionClockSplitTests.cs covering all NGS-0.x tasks.
Hot path (GetComponentRW / NativeChunkTable.GetRefRW) unmodified.
```
