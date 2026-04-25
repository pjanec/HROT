# Task Tracker: Replay Isolation and Modern Module System

## Phase 1 — Togglable Group Foundation

- [ ] T-RMF-01  Create `TogglableSimulationGroup` in `Fdp.ModuleHost.Scheduling` (implements `ISystemGroup`)
- [ ] T-RMF-02  Create `TogglableInputGroup` in `Fdp.ModuleHost.Scheduling` (implements `ISystemGroup`)
- [ ] T-RMF-03  Create `TogglablePostSimulationGroup` in `Fdp.ModuleHost.Scheduling` (implements `ISystemGroup`)
- [ ] T-RMF-04  Update `ReferenceReplayLoadHandler` — add all three new togglable group types, remove legacy `SimulationSystemGroup`
- [ ] T-RMF-05  Update `NodeBootstrapper.BuildOrchestration` — add `TogglableInputGroup`, `TogglableSimulationGroup`, `TogglablePostSimulationGroup` parameters

## Phase 2 — System Migration (ComponentSystem to IEcsModuleSystem)

- [ ] T-RMF-06  Convert `CombatModule` systems (FireProcessingSystem, RaycastSolverSystem, HitResolutionSystem, BallisticsSystem) — expose phase arrays
- [ ] T-RMF-07  Convert `GroundKinematicsModule` systems (SpatialHashSystem, CarKinematicsSystem, LinearKinematicsSystem, NavigationExecutionSystem, FormationTargetSystem, VehicleCommandSystem) — expose phase arrays
- [ ] T-RMF-08  Convert navigation bridge systems (PersonalRouteAuthoringSystem, NavigationIntentBridgeSystem, RouteTrajectorySyncSystem)
- [ ] T-RMF-09  Convert `MissionControlModule` systems (DoctrineIngressSystem, MissionDirectorSystem, others)
- [ ] T-RMF-10  Convert `CognitiveRuntimeModule` and `ActionDispatchModule` systems
- [ ] T-RMF-11  Convert standalone CGF systems and `DamageAssessmentModule`
- [ ] T-RMF-12  Convert `GenesisMaterializationSystem` — throw `InvalidOperationException` if view is not `EntityRepository`

## Phase 3 — Composition Roots and Application Wiring

- [ ] T-RMF-13  Rework `SimHostCoreLogicPack` — expose `BuildInputSystems()`, `BuildSimulationSystems()`, `BuildPostSimulationSystems()` arrays; delete legacy overloads
- [ ] T-RMF-14  Rework `CgfLogicPack` — expose `InputSystems` and `SimulationSystems` array properties (same pattern as T-RMF-13)
- [ ] T-RMF-15  Update `SimHostApp` — remove `_kernelGroup`, wire all three togglable groups, fix empty-simGroup bug
- [ ] T-RMF-16  Update `CgfSubsystem` — remove `CgfSimGroupModule`, wire togglable groups, fix `simGroup: null` bug
- [ ] T-RMF-17  Update `CgfApplication` — same as T-RMF-16
- [ ] T-RMF-18  Update `EditorSubsystem` and `EditorSystemsModule` — remove all adapter usage, use `ISystemRegistry` directly
- [ ] T-RMF-19  Update test harnesses — `EditorHarness` and `SimHostInstance` (remove `SystemGroup` usage)

## Phase 4 — Deep Replay Architecture Fixes

- [ ] T-RMF-20  Move `GhostDestructionSystem` + `DeferredTakeoverSystem` inside `NetworkLifecycleSystemGroup` (or new `NetworkIngressSystemGroup`) so they are disabled during replay
- [ ] T-RMF-21  Fix `GlobalTime` tug-of-war — expose `SuspendGlobalTimePush()` / `ResumeGlobalTimePush()` on kernel; call from `ReferenceReplayLoadHandler`
- [ ] T-RMF-22  Fix `SmartEgressSystem` 10-second seek lag — force-dirty all active entities after `SeekToFrame` in `PlaybackTickSystem`
- [ ] T-RMF-23  Fix `CycloneNetworkCleanupSystem` scrub flood on seek — expose `ResetTracking()` and call it from `PlaybackTickSystem` after seek

## Phase 5 — Legacy Removal

- [ ] T-RMF-24  Delete `ComponentSystem.cs`, `SystemGroup.cs`, `StandardSystemGroups.cs` from `Fdp.Core` — fix all resulting compile errors
- [ ] T-RMF-25  Delete `CgfInputGroupAdapter.cs`, `LegacySystemGroupAdapters.cs` from `Hrot.Common.Infrastructure` — fix all resulting compile errors

## Phase 6 — Verification and Tests

- [ ] T-RMF-26  Write new replay isolation tests (all four groups: Input, Simulation, PostSimulation, NetworkLifecycle toggled during PrepareReplay/FinalizeReplay/PrepareLive)
- [ ] T-RMF-27  Update existing replay tests — replace `SimulationSystemGroup` with `TogglableSimulationGroup`, add other new group types
