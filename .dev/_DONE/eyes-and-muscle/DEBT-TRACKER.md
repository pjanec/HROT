# Technical Debt Tracker — EyesAndMuscle Workstream

This document tracks P2 and P3 technical debt, refactoring opportunities, and deferred minor issues discovered during development and reviews.

- **P1 (critical):** Fixed immediately as Corrective Task 0 in the very next batch.
- **P2 (important):** Added here; scheduled explicitly in a near-future batch.
- **P3 (low priority):** Added here; resolved opportunistically or in a dedicated cleanup batch.

When resolved → mark ✅. Do not delete rows.

| Status | Priority | Category | Source Batch | Description | Target Fix |
|---|---|---|---|---|---|
| ✅ | P2 | Architecture | EAM-BATCH-01 | `HrotNodeBuilder.WithRole` accepts `Hrot.SimHost.NodeRole` param (unused by builder). Prevents future extraction to shared project. Consider dropping `role` param or moving `NodeRole` to `Hrot.Common`. | ✅ BATCH-03 PM-2 |
| ✅ | P2 | DRY | EAM-BATCH-01 | `SimHostApp.EnsureIdAllocatorRouting` private method still exists — circular dependency prevents calling `DdsIdAllocatorHelper` from `Hrot.SimHost`. Move `DdsIdAllocatorHelper` to `Hrot.Common` (or inline in builder, delete from SimHostApp) during EAM-M001 migration. | ✅ BATCH-03 EAM-M001 |
| ✅ | P2 | Correctness | EAM-BATCH-01 | `NedReplicationModule.RegisterSystems` does NOT register `NetworkLifecycleSystemGroup(ghostCreationSystem)`. Required for replay lifecycle gating during Phase 4 SimHostApp migration. Add before EAM-M001 is executed. | BATCH-02 Corrective-0 |
| ✅ | P2 | Architecture | EAM-BATCH-02 | `SimulationLogicModule` omitted from `EyesAndMuscleSubsystem` — old SystemGroup API incompatible with `kernel.RegisterModule(IEcsModule)`. Muscle path handled by `EyesAndMuscleModule.Tick()` PoC instead. Accepted: EyesAndMuscleModule.Tick is PoC muscle path. EAM-M001 uses NodeBootstrapper.BuildSimulationLogic via existing legacy path. | BATCH-03 Corrective-0 |
|   | P2 | Architecture | EAM-BATCH-03 | `SimHostApp._nedReplicationModule` always null — `NedReplicationModule` lives in `Hrot.ClusterRunner.Replication`; `Hrot.SimHost` cannot reference `Hrot.ClusterRunner` (circular dep). Move `NedReplicationModule` to `Hrot.Common` to unblock. | Future batch |
|   | P3 | Architecture | EAM-BATCH-03 | `IgApplication.InitializeNetwork` still calls `HrotEnvironment.CreateParticipant()` directly (EAM-M002 SC2 not satisfied). Full IG participant migration deferred: `Headless=true` in `InitializeEcs` makes `_context.Participant` null, requiring the participant to be created inline in `InitializeNetwork`. | Future batch |
|   | P3 | Architecture | EAM-BATCH-03 | `IgApplication` keeps `ReplicationLogicModule` — removing caused ghost promotion failure (7 integration test failures). Cannot remove without understanding full IG ghost lifecycle path. Deferred. | Future batch |
|   | P3 | Design | EAM-BATCH-03 | `NedReplicationModule` receives `_context.World.Bus` for CGF (Brain) but `_context.EventBus` for EyesAndMuscle (MuscleGround). Semantically inconsistent: `world.Bus` is needed so `GhostDestructionSystem.Execute` can `ConsumeManagedEvents`; `_context.EventBus` is only correct for ClusterSlave events. Consider unifying. | Future batch |
