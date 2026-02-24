# Behavior Control Subsystem — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for full task descriptions and success criteria.  
**Design:** See [DESIGN.md](./DESIGN.md) for the architecture and component/system reference.

---

## Phase 0: Universal Spatial Primitives

**Goal:** Standardize position/rotation/velocity representation across the entire FDP engine before any behavior toolkit work begins.  
**Scope:** `Fdp.Kernel` (new components), `FDP.Toolkit.CarKinem` (refactor), `Fdp.Examples.CarKinem`, `Fdp.Examples.BattleRoyale`, `Fdp.Examples.NetworkDemo`

- [x] **BCS-P0-T1** `SimTransform`, `SimVelocity` in `Fdp.Kernel` [details](./TASK-DETAIL.md#bcs-p0-t1--simtransform--simvelocity-in-fdpkernel)
- [x] **BCS-P0-T2** Refactor `VehicleState` (remove Position/Forward/Pitch/Roll) + `CarKinematicsSystem` 2D↔3D bridge [details](./TASK-DETAIL.md#bcs-p0-t2--refactor-vehiclestate-and-carkinematicssystem)
- [x] **BCS-P0-T3** Refactor `SpatialHashSystem` to query `SimTransform` (universal grid) [details](./TASK-DETAIL.md#bcs-p0-t3--refactor-spatialhashsystem-to-use-simtransform)
- [x] **BCS-P0-T4** Migrate `Fdp.Examples.CarKinem` to `SimTransform`/`SimVelocity` [details](./TASK-DETAIL.md#bcs-p0-t4--migrate-fdpexamplescarkinem)
- [x] **BCS-P0-T5** Migrate `Fdp.Examples.BattleRoyale` (delete local `Position.cs`/`Velocity.cs`) [details](./TASK-DETAIL.md#bcs-p0-t5--migrate-fdpexamplesbattleroyale)
- [x] **BCS-P0-T6** Migrate `Fdp.Examples.NetworkDemo` (delete `DemoPosition`, remove local `Position`/`Velocity` structs) [details](./TASK-DETAIL.md#bcs-p0-t6--migrate-fdpexamplesnetworkdemo)

---

## Phase 1: FDP.Toolkit.Behavior – Core Infrastructure

**Goal:** Define all behavior components, channels, dispatcher infrastructure, Brain VM adapters, and doctrine lifecycle management.  
**New project:** `Toolkits/FDP.Toolkit.Behavior/`

- [x] **BCS-P1-T1** Behavior component types (DoctrineState, BrainBlackboard, Channels, BrainBTreeState, BrainHsm64/128, SimTier, ActorCapabilityState, IActionExecutor interface) [details](./TASK-DETAIL.md#bcs-p1-t1--behavior-component-types)
- [x] **BCS-P1-T2** ChannelArbitrationSystem [details](./TASK-DETAIL.md#bcs-p1-t2--channelarbitrationsystem)
- [x] **BCS-P1-T3** LocomotionDispatcherSystem [details](./TASK-DETAIL.md#bcs-p1-t3--locomotiondispatchersystem)
- [x] **BCS-P1-T4** WeaponDispatcherSystem + InteractionDispatcherSystem [details](./TASK-DETAIL.md#bcs-p1-t4--weapondispatchersystem--interactiondispatchersystem)
- [x] **BCS-P1-T5** BTreeTickSystem (FastBTree adapter) [details](./TASK-DETAIL.md#bcs-p1-t5--btreeticksystem-fastbtree-adapter)
- [x] **BCS-P1-T6** HsmTickSystem\<T\> (FastHSM adapter) [details](./TASK-DETAIL.md#bcs-p1-t6--hsmticksystemt-fasthsm-adapter)
- [x] **BCS-P1-T7** DoctrineRegistry + DoctrineIngressSystem [details](./TASK-DETAIL.md#bcs-p1-t7--doctrineregistry--doctrineingresssystem)

---

## Phase 2: FDP.Toolkit.Perception

**Goal:** Senses infrastructure — audio perception (main thread sync), async vision broadphase (SoD module), target memory management.  
**New project:** `Toolkits/FDP.Toolkit.Perception/`

- [x] **BCS-P2-T1** Perception component types (Faction, PerceptionReceptor, TargetMemory, perception events) [details](./TASK-DETAIL.md#bcs-p2-t1--perception-component-types)
- [x] **BCS-P2-T2** AudioPerceptionSystem (main thread, ConsumesAudioStimulusEvent → TargetMemory) [details](./TASK-DETAIL.md#bcs-p2-t2--audioperceptionsystem-main-thread)
- [x] **BCS-P2-T3** PerceptionModule (async SoD, VisionBroadphaseSystem + ThreatEvaluationSystem) [details](./TASK-DETAIL.md#bcs-p2-t3--perceptionmodule-async-vision-broadphase)
- [x] **BCS-P2-T4** LosRequestBatchingSystem + TargetMemory integration [details](./TASK-DETAIL.md#bcs-p2-t4--losrequestbatchingsystem--targetmemory-integration)

---

## Phase 3: FDP.Toolkit.Navigation

**Goal:** Translate `LocomotionChannel` intents into `CarKinem.NavState` configurations via stateless executor classes.  
**New project:** `Toolkits/FDP.Toolkit.Navigation/`

- [ ] **BCS-P3-T1** Navigation action IDs + parameter/state structs (MoveToParams, FleeParams, FollowRouteParams, etc.) [details](./TASK-DETAIL.md#bcs-p3-t1--navigation-action-ids--parameter-structs)
- [ ] **BCS-P3-T2** MoveToExecutor [details](./TASK-DETAIL.md#bcs-p3-t2--movetoexecutor)
- [ ] **BCS-P3-T3** FleeExecutor [details](./TASK-DETAIL.md#bcs-p3-t3--fleeexecutor)
- [ ] **BCS-P3-T4** FollowRoadGraphExecutor [details](./TASK-DETAIL.md#bcs-p3-t4--followroadgraphexecutor)
- [ ] **BCS-P3-T5** FollowRouteExecutor [details](./TASK-DETAIL.md#bcs-p3-t5--followrouteexecutor)

---

## Phase 4: FDP.Toolkit.Physics

**Goal:** 2D batch raycast solver (line-segment to circle intersection) running in parallel on the main thread.  
**New project:** `Toolkits/FDP.Toolkit.Physics/`

- [ ] **BCS-P4-T1** PhysicsCollider component + RaycastBatchData singleton + PhysicsToolkitModule [details](./TASK-DETAIL.md#bcs-p4-t1--physicscollider--raycastbatchdata)
- [ ] **BCS-P4-T2** Intersection2D math utilities [details](./TASK-DETAIL.md#bcs-p4-t2--intersection2d-math)
- [ ] **BCS-P4-T3** RaycastSolverSystem (Parallel.For, SpatialHashGrid, LayerMask) [details](./TASK-DETAIL.md#bcs-p4-t3--raycastsolversystem)
- [ ] **BCS-P4-T4** HitResolutionSystem (Physics→Combat + Physics→Perception bridge) [details](./TASK-DETAIL.md#bcs-p4-t4--hitresolutionsystem-physicscombat-bridge)

---

## Phase 5: FDP.Toolkit.Combat

**Goal:** Weapon intent execution, bullet entity lifecycle, deferred ballistics, and damage application.  
**New project:** `Toolkits/FDP.Toolkit.Combat/`

- [ ] **BCS-P5-T1** Combat component types (WeaponState, Health, BallisticProjectile) [details](./TASK-DETAIL.md#bcs-p5-t1--combat-component-types)
- [ ] **BCS-P5-T2** Combat events (FireRequestEvent, HitEvent) [details](./TASK-DETAIL.md#bcs-p5-t2--combat-events)
- [ ] **BCS-P5-T3** AimAndFireExecutor (registered to WeaponDispatcher) [details](./TASK-DETAIL.md#bcs-p5-t3--aimandfireexecutor)
- [ ] **BCS-P5-T4** FireProcessingSystem + BallisticsSystem [details](./TASK-DETAIL.md#bcs-p5-t4--fireprocessingsystem--ballisticssystem)
- [ ] **BCS-P5-T5** DamageSystem [details](./TASK-DETAIL.md#bcs-p5-t5--damagesystem)

---

## Phase 6: FDP.Toolkit.Behavior – Advanced Features

**Goal:** Mission plan sequencing, HSM-damage bridge, and embark/disembark interaction executors.

- [ ] **BCS-P6-T1** MissionPlanQueue component + MissionDirectorSystem [details](./TASK-DETAIL.md#bcs-p6-t1--missionplanqueue--missiondirectorsystem)
- [ ] **BCS-P6-T2** HsmDamageBridgeSystem [details](./TASK-DETAIL.md#bcs-p6-t2--hsmdamagebridgesystem)
- [ ] **BCS-P6-T3** EmbarkExecutor + EjectPassengersExecutor + PassengerBuffer/IsEmbarkedTag components [details](./TASK-DETAIL.md#bcs-p6-t3--embarkexecutor--ejectpassengersexecutor)

---

## Phase 7: Fdp.Examples.UrbanCombat – Demo App

**Goal:** Thin demo application wiring all toolkits; "Urban Ambush" headless scenario runnable in autonomous tests.  
**New project:** `Examples/Fdp.Examples.UrbanCombat/`

- [ ] **BCS-P7-T1** Project scaffold + HeadlessDemoApp shell + Program.cs [details](./TASK-DETAIL.md#bcs-p7-t1--project-scaffold--headlessdemoapp-shell)
- [ ] **BCS-P7-T2** TKB Blueprints (5 entity templates) [details](./TASK-DETAIL.md#bcs-p7-t2--tkb-blueprints-entity-templates)
- [ ] **BCS-P7-T3** DemoEnvironmentSetup (city intersection road graph) [details](./TASK-DETAIL.md#bcs-p7-t3--demoenvironmentsetup-road-graph)
- [ ] **BCS-P7-T4** TrafficBrainSystem (Tier 1 hardcoded) [details](./TASK-DETAIL.md#bcs-p7-t4--trafficbrainsystem-tier-1)
- [ ] **BCS-P7-T5** Insurgent BTree nodes + Ambush.json authoring [details](./TASK-DETAIL.md#bcs-p7-t5--insurgent-btree-nodes--json)
- [ ] **BCS-P7-T6** APC HSM authoring (HsmBuilder + action methods) [details](./TASK-DETAIL.md#bcs-p7-t6--apc-hsm-authoring)
- [ ] **BCS-P7-T7** ScenarioDirector (spawn setup) [details](./TASK-DETAIL.md#bcs-p7-t7--scenariodirector-entity-spawning)
- [ ] **BCS-P7-T8** TelemetryReporterSystem (console debug output) [details](./TASK-DETAIL.md#bcs-p7-t8--telemetryreportersystem)
- [ ] **BCS-P7-T9** End-to-end integration test (10-second simulation timeline validation) [details](./TASK-DETAIL.md#bcs-p7-t9--end-to-end-integration-test-10-second-simulation)

---

## Summary

| Phase | Tasks | Done |
|---|---|---|
| Phase 0 – Universal Spatial Primitives | 6 | 6 ✅ |
| Phase 1 – Behavior Core | 7 | 7 ✅ |
| Phase 2 – Perception | 4 | 4 ✅ |
| Phase 3 – Navigation | 5 | 0 |
| Phase 4 – Physics | 4 | 0 |
| Phase 5 – Combat | 5 | 0 |
| Phase 6 – Behavior Advanced | 3 | 0 |
| Phase 7 – Demo App | 9 | 0 |
| **Total** | **43** | **0** |
