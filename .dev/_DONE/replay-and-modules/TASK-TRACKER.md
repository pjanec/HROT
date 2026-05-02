# Task Tracker: Replay Isolation and Modern Module System

## Phase 1 — Togglable Group Foundation

- [x] T-RMF-01  Create `TogglableSimulationGroup` in `Fdp.ModuleHost.Scheduling` (implements `ISystemGroup`)
- [x] T-RMF-02  Create `TogglableInputGroup` in `Fdp.ModuleHost.Scheduling` (implements `ISystemGroup`)
- [x] T-RMF-03  Create `TogglablePostSimulationGroup` in `Fdp.ModuleHost.Scheduling` (implements `ISystemGroup`)
- [x] T-RMF-04  Update `ReferenceReplayLoadHandler` — add all three new togglable group types, remove legacy `SimulationSystemGroup`
- [x] T-RMF-05  Update `NodeBootstrapper.BuildOrchestration` — add `TogglableInputGroup`, `TogglableSimulationGroup`, `TogglablePostSimulationGroup` parameters

## Phase 2 — System Migration (ComponentSystem to IEcsModuleSystem)

- [x] T-RMF-06  Convert `CombatModule` systems (FireProcessingSystem, RaycastSolverSystem, HitResolutionSystem, BallisticsSystem) — expose phase arrays
- [x] T-RMF-07  Convert `GroundKinematicsModule` systems (SpatialHashSystem, CarKinematicsSystem, LinearKinematicsSystem, NavigationExecutionSystem, FormationTargetSystem, VehicleCommandSystem) — expose phase arrays
- [x] T-RMF-08  Convert navigation bridge systems (PersonalRouteAuthoringSystem, NavigationIntentBridgeSystem, RouteTrajectorySyncSystem)
- [x] T-RMF-09  Convert `MissionControlModule` systems (BehaviorIngressSystem, MissionDirectorSystem, others)
- [x] T-RMF-10  Convert `CognitiveRuntimeModule` and `ActionDispatchModule` systems
- [x] T-RMF-11  Convert standalone CGF systems and `DamageAssessmentModule`
- [x] T-RMF-12  Convert `GenesisMaterializationSystem` -- throw `InvalidOperationException` if view is not `EntityRepository`

## Phase 3 — Composition Roots and Application Wiring

- [x] T-RMF-13  Rework `SimHostCoreLogicPack` — expose `BuildInputSystems()`, `BuildSimulationSystems()`, `BuildPostSimulationSystems()` arrays; delete legacy overloads
- [x] T-RMF-14  Rework `CgfLogicPack` — expose `InputSystems` and `SimulationSystems` array properties (same pattern as T-RMF-13)
- [x] T-RMF-15  Update `SimHostApp` — remove `_kernelGroup`, wire all three togglable groups, fix empty-simGroup bug
- [x] T-RMF-16  Update `CgfSubsystem` — remove `CgfSimGroupModule`, wire togglable groups, fix `simGroup: null` bug
- [x] T-RMF-17  Update `CgfApplication` — same as T-RMF-16
- [x] T-RMF-18  Update `EditorSubsystem` and `EditorSystemsModule` — remove all adapter usage, use `ISystemRegistry` directly
- [x] T-RMF-19  Update test harnesses — `EditorHarness` and `SimHostInstance` (remove `SystemGroup` usage)

## Phase 4 — Deep Replay Architecture Fixes

- [x] T-RMF-20  Move `GhostDestructionSystem` + `DeferredTakeoverSystem` inside `NetworkLifecycleSystemGroup` (or new `NetworkIngressSystemGroup`) so they are disabled during replay
- [x] T-RMF-21  Fix `GlobalTime` tug-of-war — expose `SuspendGlobalTimePush()` / `ResumeGlobalTimePush()` on kernel; call from `ReferenceReplayLoadHandler`
- [x] T-RMF-22  Fix `SmartEgressSystem` 10-second seek lag — force-dirty all active entities after `SeekToFrame` in `PlaybackTickSystem`
- [x] T-RMF-23  Fix `CycloneNetworkCleanupSystem` scrub flood on seek — expose `ResetTracking()` and call it from `PlaybackTickSystem` after seek

## Phase 5 — Legacy Removal

- [x] T-RMF-24  Delete `ComponentSystem.cs`, `SystemGroup.cs`, `StandardSystemGroups.cs` from `Fdp.Core` — fix all resulting compile errors
- [x] T-RMF-25  Delete `CgfInputGroupAdapter.cs`, `LegacySystemGroupAdapters.cs` from `Hrot.Common.Infrastructure` — fix all resulting compile errors

## Phase 6 — Verification and Tests

- [x] T-RMF-26  Write new replay isolation tests (all four groups: Input, Simulation, PostSimulation, NetworkLifecycle toggled during PrepareReplay/FinalizeReplay/PrepareLive)
- [x] T-RMF-27  Update existing replay tests — replace `SimulationSystemGroup` with `TogglableSimulationGroup`, add other new group types
