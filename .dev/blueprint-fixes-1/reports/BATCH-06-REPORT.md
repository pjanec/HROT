# BATCH-06 Report

**Batch:** BATCH-06  
**Developer:** AI (GitHub Copilot)  
**Date:** 2025-06-03  
**Status:** Complete

---

## Task Completion

| Task ID  | Status | Notes |
|----------|--------|-------|
| BPF-042  | Done   | `AiHotReloadCoordinator` (Fdp.Toolkits) uses a staging `BehaviorRegistry`; only merges on full success |
| BPF-043  | Done   | `AiHotReloadCoordinator` (Hrot.Editor) `DrainPendingCallbacks` processes at most one reload per call |
| BPF-044  | Done   | Background scan exceptions enqueued to `_pendingFailures`; `DrainPendingCallbacks` drains queue and fires `OnReloadFailed` |
| BPF-036  | Done   | `BlueprintDebugSession.OnHotReloadCompleted` only clears `IsStale` when the pin exists in the new debug map |
| BPF-037  | Done   | Mid-move failure test added to `AtomicMultiFileWriterTests` |
| BPF-038  | Done   | `HardReload_ChangedStructureHash_ResetsPayloadAndBumpsVersion` now asserts `versionAfter > versionBefore` |
| BPF-046  | Done   | `TierUpgrade_HappensInBeforeSync_NotInSimulation` uses real `EntityCommandBuffer` (ECB) path |
| BPF-049  | Done   | Pre-existing: `BlueprintRegistry.GetAll` already returns `IReadOnlyList<(int Id, BlueprintDefinition Def)>` (verified) |
| BPF-010  | Done   | Pre-existing: `HsmDebugSession` already uses `DecodeLeaves64`/`DecodeLeaves128` (verified) |
| BPF-011  | Done   | `BuiltInChannelCommandCatalog` comment added; DEBT-003/004/023 updated in `blueprints-1/DEBT-TRACKER.md` |
| BPF-012  | Done   | DEBT D-02 marked RESOLVED in `blueprints-2/DEBT-TRACKER.md` |
| BPF-013  | Done   | DEBT D-BP-01/D-BP-04 notes updated in `breakpoints-1/DEBT-TRACKER.md` |

---

## Testing Results

**Fdp.Toolkits.Tests (BPF-042/044 scope):** 4 passed, 0 failed (filter `FullyQualifiedName~AiHotReload`)  
**Hrot.Editor.Tests (BPF-043 scope):** 116 passed, 0 failed  
**Hrot.Blueprints.Tests (BPF-036/038/046 scope):** 876 passed, 0 failed, 8 skipped  
**Hrot.Editor.AiShared.Tests (BPF-037 scope):** 538 passed, 0 failed  

The 8 skipped tests in `Hrot.Blueprints.Tests` are pre-existing skips (demo tests and one ALC weak-reference test requiring specific compiler phases).

`Fdp.Toolkits.Tests` full run reports 80 pre-existing failures in unrelated subsystems (CarKinem, Combat components, Squad systems, Orchestration path assertions). None of the 4 new BPF-042/044 tests are among the failures.

### New tests added this batch

**BPF-042/044** (`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/AiHotReloadCoordinatorTests.cs`, 4 tests — new file):
- `ApplyReload_ThrowingRegistrar_DoesNotMutateLiveRegistry` — staging rollback: if registrar throws, live `BehaviorRegistry` is not mutated
- `ApplyReload_SuccessfulRegistrar_MergesIntoBehaviorRegistry` — successful reload merges new behavior into live registry
- `DrainPendingCallbacks_BackgroundScanFailure_FiresOnReloadFailed` — background exception queued by `EnqueueFailureForTest` fires `OnReloadFailed`
- `DrainPendingCallbacks_MultipleBackgroundFailures_AllReported` — two queued failures both reported via `OnReloadFailed`

**BPF-043** (`Hrot/Subsystems/Hrot.Editor.Tests/AiHotReloadCoordinatorTests.cs`, 2 tests added):
- `DrainPendingCallbacks_AtMostOneReloadPerCall_WhenTwoEnqueued` — first call applies 1 reload, second call applies the remaining 1
- `DrainPendingCallbacks_DoesNotDrainAllReloadsInOnCall` — 3 queued reloads; single drain applies exactly 1

**BPF-036** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/HotReloadInteractionTests.cs`, 2 tests added):
- `OnHotReloadCompleted_WatchForDeletedPin_RemainsStale` — watch for a pin absent from new map stays stale; watch for surviving pin is cleared
- `OnHotReloadCompleted_NoDebugMapRegistered_AllWatchesRemainStale` — no debug map registered: all watches remain stale after completed

**BPF-037** (`Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/AtomicMultiFileWriterTests.cs`, 1 test added):
- `Write_MidMoveFails_ReturnsFalse_AndLeavesNoTempFiles` — directory placed at final path causes `File.Move` to fail; asserts `Success == false`, `FailureReason != null`, and no `.tmp` files remain

**BPF-038** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs`, existing test updated):
- `HardReload_ChangedStructureHash_ResetsPayloadAndBumpsVersion` now captures `versionBefore` and asserts `versionAfter > versionBefore`

**BPF-046** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockContractTests.cs`, existing test updated):
- `TierUpgrade_HappensInBeforeSync_NotInSimulation` uses real ECB path: `fixture.Ecb.AddEmptyComponent<BlueprintBlackboard4096>(entity)` / `fixture.Ecb.Playback(fixture.World)` then asserts BB4096 present and BB1024 absent

---

## Files Modified / Created

### Production files modified

| File | Task | Change |
|------|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` | BPF-042, BPF-044 | `ApplyReload` creates staging `BehaviorRegistry` + `BlueprintRegistryStaging`; merges only on full success. `_pendingFailures` queue added; `DrainPendingCallbacks` drains failures and fires `OnReloadFailed`. Internal test seams `EnqueueReloadForTest`/`EnqueueFailureForTest` added |
| `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` | BPF-043 | `DrainPendingCallbacks` changed from `while (_pendingReloads.TryDequeue(...))` to a single `if (!_pendingReloads.TryDequeue(...)) return`. Internal test seam `EnqueueReloadForTest` added |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | BPF-036 | `OnHotReloadCompleted` looks up the new `DebugMapIndex` for each asset; only clears `watch.IsStale = false` when the new map contains the watch's `PinId` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs` | BPF-011 | Added 5-line comment before `GetEntries()` explaining short-name intentional deviation from hierarchical design-doc names |
| `.dev/blueprints-1/DEBT-TRACKER.md` | BPF-011 | DEBT-003: added intentional-deviation note; DEBT-004: added "comment present" note; DEBT-023: marked comment added |
| `.dev/blueprints-2/DEBT-TRACKER.md` | BPF-012 | D-02: marked `RESOLVED (BPF-018, BATCH-04)`; added Status column to all rows |
| `.dev/breakpoints-1/DEBT-TRACKER.md` | BPF-013 | D-BP-01: added "(not yet implemented; deferred)" note; D-BP-04: updated status with SetBreakpointManager infrastructure note |

### Production files created

| File | Task | Purpose |
|------|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/AssemblyInfo.cs` | BPF-042 | `[assembly: InternalsVisibleTo("Fdp.Toolkits.Tests")]` to allow test access to internal seams |

### Test files modified

| File | Task | Change |
|------|------|--------|
| `Hrot/Subsystems/Hrot.Editor.Tests/AiHotReloadCoordinatorTests.cs` | BPF-043 | Added 2 new tests for at-most-one-reload-per-call behavior |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/HotReloadInteractionTests.cs` | BPF-036 | Added `MakeMapWithPin` helper; updated SC3 test to register map with pin before `OnHotReloadCompleted`; added 2 new tests for deleted-pin stale behavior |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/MultiEntityTests.cs` | BPF-036 | Added `MakeMapWithPin` helper; updated `OnHotReloadCompleted_ClearsStalWatchesAsValid` to register pin-bearing map before `OnHotReloadCompleted` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs` | BPF-038 | Updated `HardReload_ChangedStructureHash_ResetsPayloadAndBumpsVersion` to assert version bump |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockContractTests.cs` | BPF-046 | Updated `TierUpgrade_HappensInBeforeSync_NotInSimulation` to use real ECB |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/AtomicMultiFileWriterTests.cs` | BPF-037 | Added `Write_MidMoveFails_ReturnsFalse_AndLeavesNoTempFiles` test |

### Test files created

| File | Task | Tests |
|------|------|-------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/AiHotReloadCoordinatorTests.cs` | BPF-042, BPF-044 | 4 tests for staging rollback and failure propagation |

---

## Issues Encountered

### 1. AssemblyLoadContext is not IDisposable (CS1674)

`AssemblyLoadContext` does not implement `IDisposable`; using `using var alc = new AssemblyLoadContext(...)` causes CS1674. Fixed in both `Fdp.Toolkits.Tests/AiHotReloadCoordinatorTests.cs` and `Hrot.Editor.Tests/AiHotReloadCoordinatorTests.cs` by removing the `using` keyword (ALCs are unloaded by GC when collectible).

### 2. BPF-036 broke pre-existing MultiEntityTests SC4 test

`MultiEntityTests.OnHotReloadCompleted_ClearsStalWatchesAsValid` was written before the BPF-036 fix. It registered maps with `Entries = Array.Empty<DebugMapEntry>()` (no pins) then called `OnHotReloadCompleted`, expecting stale to be cleared. With BPF-036, the fix requires the pin to exist in the new map. Fixed by adding `MakeMapWithPin` helper to `MultiEntityTests.cs` and calling it before `OnHotReloadCompleted`.

### 3. BPF-049 and BPF-010 were pre-existing

Inspection of the codebase confirmed both `BlueprintRegistry.GetAll` (returning `IReadOnlyList<(int, BlueprintDefinition)>`) and `HsmDebugSession` using `DecodeLeaves64`/`DecodeLeaves128` were already implemented. These tasks required no production code changes.
