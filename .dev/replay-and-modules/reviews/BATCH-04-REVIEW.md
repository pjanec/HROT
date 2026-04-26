# BATCH-04 Review

**Tasks**: T-RMF-20, T-RMF-21, T-RMF-22, T-RMF-23  
**Status**: APPROVED  
**Build**: 0 errors  
**Tests**: SimHost.Tests 458/461 (3 skipped), ClusterRunner.Tests 219/219, NED.Tests 57/57  

---

## Changes Reviewed

### T-RMF-20 — GhostDestructionSystem + DeferredTakeoverSystem moved into NetworkLifecycleSystemGroup ✓

`NedReplicationModule` constructor now builds a `List<IEcsModuleSystem>` with conditional additions before constructing `NetworkLifecycleSystemGroup`. Standalone `registry.RegisterSystem` calls for both systems removed from `RegisterSystems`. `CycloneNetworkCleanupSystem` field + `CleanupSystem` property added.

**Deviation accepted**: Subagent also added `AfterSeekCallback` property to `NedReplicationModule` and `INedReplicationModule` interface. This was necessary because `Hrot.SimHost` and `Hrot.CGF` do not reference `Hrot.Network.NED` directly, so a `NedReplicationModule` concrete cast would have been a compile error. The `INedReplicationModule` interface in `Hrot.Core` is the correct seam. The property is clean: `=> _cleanupSystem != null ? () => _cleanupSystem.ResetTracking() : null`.

### T-RMF-21 — GlobalTime tug-of-war fix ✓

`ModuleHostKernel`: `_globalTimePushSuspended` volatile field, `SuspendGlobalTimePush()` / `ResumeGlobalTimePush()` public methods. `UpdateInternal`: `_liveWorld.Tick()` runs unconditionally; `SetSimulationTime` + `SetSingletonUnmanaged` guarded by `!_globalTimePushSuspended`.

`ReferenceReplayLoadHandler`: two optional `Action?` constructor params; called at `PrepareReplay` (suspend), `FinalizeReplay` (resume), `PrepareLive` (resume).

Wired in: `NodeBootstrapper` (uses `kernel.SuspendGlobalTimePush/ResumeGlobalTimePush`), `CgfApplication` (`_kernel`), `CgfSubsystem` (`_context.Kernel`).

### T-RMF-22 — SmartEgressUtil.ForceMarkAllDirty ✓

Added `ForceMarkAllDirty(EntityRepository repo)` static method. Deviation from instructions: used `view.Query().Build()` + per-entity `HasManagedComponent` check rather than `.WithManagedComponent<>()` which doesn't exist in this codebase's query builder API. Same semantics, correct implementation.

`PlaybackTickSystem` calls `SmartEgressUtil.ForceMarkAllDirty(repo)` after `SeekToFrame` in Strategy B.

### T-RMF-23 — CycloneNetworkCleanupSystem.ResetTracking + afterSeek cascade ✓

`CycloneNetworkCleanupSystem.ResetTracking()` added.

`afterSeek: Action?` parameter threaded through: `PlaybackTickSystem` → `ReplayModule` → `EcsRecordReplayController`.

Wired in `NodeBootstrapper` (added optional `Action? afterSeek` param to `BuildOrchestration`), `SimHostApp` (passes `nedModule?.AfterSeekCallback`), `CgfSubsystem` (passes `afterSeekAction` from `INedReplicationModule` cast). `CgfApplication` has no wired replication module — `afterSeek` defaults to null.

---

## Notes

- Pre-existing `EntityMission_MovesEntity` failure (present since SHA 0ce69f5) still shows as skipped/failing in SimHost.Tests — NOT a regression.
- All deviations from BATCH-04-INSTRUCTIONS.md are justified by actual dependency constraints in the project graph.
