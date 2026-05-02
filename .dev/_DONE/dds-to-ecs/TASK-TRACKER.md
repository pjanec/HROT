# Task Tracker: DDS-to-ECS Architectural Cleanup

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and
success conditions.  
**Design:** See [DESIGN.md](./DESIGN.md) for the full architectural context.

---

## Phase 1: Purify DDS Data Model

**Goal:** Strip all ECS kernel attributes (`[ComponentId]`) from DDS descriptor types so they are
pure network DTOs with zero engine coupling.

- [x] **DDS2ECS-S1T1** Remove `[ComponentId]` from `EntityMaster` — [details](./TASK-DETAIL.md#dds2ecs-s1t1--remove-componentid-from-entitymaster)
- [x] **DDS2ECS-S1T2** Remove `[ComponentId]` from `EntityDamage` — [details](./TASK-DETAIL.md#dds2ecs-s1t2--remove-componentid-from-entitydamage)

---

## Phase 2: SimHost — Fix `DescriptorMapper`

**Goal:** `DescriptorMapper.MapToComponents` produces only pure ECS components; no raw DDS
structs appear in `SpawnEntityCommand.InitialComponents`.

- [x] **DDS2ECS-S2T1** `dtEntityMaster` case produces nothing — [details](./TASK-DETAIL.md#dds2ecs-s2t1--dtentitymaster-case-produces-nothing)
- [x] **DDS2ECS-S2T2** `dtEntityInfo` case produces nothing — [details](./TASK-DETAIL.md#dds2ecs-s2t2--dtentityinfo-case-produces-nothing)
- [x] **DDS2ECS-S2T3** `dtWorldPos` adds `GeoTransform`, removes raw DTO — [details](./TASK-DETAIL.md#dds2ecs-s2t3--dtgeospatial-case-remove-raw-dto-add-geotransform)
- [x] **DDS2ECS-S2T4** `dtWorldPos` translates to `GeoVelocity` — [details](./TASK-DETAIL.md#dds2ecs-s2t4--dtgeospatialdr-case-translate-to-geovelosity)

---

## Phase 3: SimHost — Replace `AutoCycloneTranslator<EntityMaster>`

**Goal:** SimHost publishes `EntityMaster` via a proper egress translator that reads
FDP-internal ECS components, never via auto-magic relying on `[ComponentId]` on a DDS type.

- [x] **DDS2ECS-S3T1** Create `EntityMasterEgressTranslator` — [details](./TASK-DETAIL.md#dds2ecs-s3t1--create-entitymastereresstranslator)
- [x] **DDS2ECS-S3T2** `SimHostApp`: replace `AutoCycloneTranslator<EntityMaster>` — [details](./TASK-DETAIL.md#dds2ecs-s3t2--simhostapp-replace-autocyclonetranslatorentitymaster)
- [x] **DDS2ECS-S3T3** `SimHostApp`: remove `RegisterComponent<EntityMaster>` — [details](./TASK-DETAIL.md#dds2ecs-s3t3--simhostapp-remove-registercomponententitymaster)
- [x] **DDS2ECS-S3T4** `SimHostApp`: fix `onEntitySpawned` callback — [details](./TASK-DETAIL.md#dds2ecs-s3t4--simhostapp-fix-onentityspawned-callback)

---

## Phase 4: IG — Fix `EntityMasterTranslator`

**Goal:** IG's translator no longer writes the `EntityMaster` DDS struct into the ECS; it only
drives `SpawnEntityCommand` / `DestroyEntityCommand` with the minimum required data.

- [x] **DDS2ECS-S4T1** Spawn path: `InitialComponents` is empty — [details](./TASK-DETAIL.md#dds2ecs-s4t1--spawn-path-empty-initialcomponents)
- [x] **DDS2ECS-S4T2** Update path: remove `cmd.SetComponent(existing, master)` — [details](./TASK-DETAIL.md#dds2ecs-s4t2--update-path-remove-cmdsetcomponentexisting-master)
- [x] **DDS2ECS-S4T3** `ApplyToEntity` becomes a no-op — [details](./TASK-DETAIL.md#dds2ecs-s4t3--applytoentity-become-a-no-op)

---

## Phase 5: IG — `IgEntityData` + Fix `EntityInfoTranslator`

**Goal:** `EntityInfo` DDS data is translated into the IG-internal `IgEntityData` ECS component;
no raw `EntityInfo` struct ever reaches the ECS.

- [x] **DDS2ECS-S5T1** Create `IgEntityData` component — [details](./TASK-DETAIL.md#dds2ecs-s5t1--create-igentitydata-component)
- [x] **DDS2ECS-S5T2** `EntityInfoTranslator.PollIngress` → `IgEntityData` — [details](./TASK-DETAIL.md#dds2ecs-s5t2--entityinfotranslator-translate-to-igentitydata)
- [x] **DDS2ECS-S5T3** `EntityInfoTranslator.ApplyToEntity` → `IgEntityData` — [details](./TASK-DETAIL.md#dds2ecs-s5t3--entityinfotranslatorapplytoentity-use-igentitydata)
- [x] **DDS2ECS-S5T4** `IgApplication`: register `IgEntityData` — [details](./TASK-DETAIL.md#dds2ecs-s5t4--igapplication-register-igentitydata)

---

## Phase 6: IG — `IgHealthState` + `EntityDamageTranslator`

**Goal:** `EntityDamage` DDS data is translated into the IG-internal `IgHealthState` ECS
component via an explicit translator; the `[ComponentId]` anti-pattern is eliminated.

- [x] **DDS2ECS-S6T1** Create `IgHealthState` component — [details](./TASK-DETAIL.md#dds2ecs-s6t1--create-ighealthstate-component)
- [x] **DDS2ECS-S6T2** Create `EntityDamageTranslator` — [details](./TASK-DETAIL.md#dds2ecs-s6t2--create-entitydamagetranslator)
- [x] **DDS2ECS-S6T3** `IgApplication`: register translator — [details](./TASK-DETAIL.md#dds2ecs-s6t3--igapplication-register-entitydamagetranslator)
- [x] **DDS2ECS-S6T4** `IgApplication`: register `IgHealthState` — [details](./TASK-DETAIL.md#dds2ecs-s6t4--igapplication-register-ighealthstate)

---

## Phase 7: IG — `MapEntitySymbolTranslator`

**Goal:** `MapEntitySymbol` DDS data is translated into the existing `IgSymbolOverride` ECS
component via an explicit translator.

- [x] **DDS2ECS-S7T1** Create `MapEntitySymbolTranslator` — [details](./TASK-DETAIL.md#dds2ecs-s7t1--create-mapentitysymboltranslator)
- [x] **DDS2ECS-S7T2** `IgApplication`: register `MapEntitySymbolTranslator` — [details](./TASK-DETAIL.md#dds2ecs-s7t2--igapplication-register-mapentitysymboltranslator)

---

## Phase 8: IG — Fix `IgApplication` Registrations and Queries

**Goal:** Remove all remaining `EntityMaster` DDS type references from the IG application
shell (registrations, queries, extractors).

- [x] **DDS2ECS-S8T1** Remove `RegisterComponent<EntityMaster>()` — [details](./TASK-DETAIL.md#dds2ecs-s8t1--remove-registercomponententitymaster-from-initializeecs)
- [x] **DDS2ECS-S8T2** Render query: `.With<NetworkIdentity>()` — [details](./TASK-DETAIL.md#dds2ecs-s8t2--render-query-replace-withentitymaster-with-withnetworkidentity)
- [x] **DDS2ECS-S8T3** `DisTypeExtractor`: use `NetworkSpawnRequest` — [details](./TASK-DETAIL.md#dds2ecs-s8t3--distypeextractor-use-networkspawnrequest-instead-of-entitymaster)

---

## Phase 9: Network Cleanup System

**Goal:** Destroyed SimHost entities send DDS dispose messages; IG ghost cleanup is automatic
(no zombie entities).

- [x] **DDS2ECS-S9T1** `SimHostApp`: register `CycloneNetworkCleanupSystem` — [details](./TASK-DETAIL.md#dds2ecs-s9t1--simhostapp-register-cyclonenetworkcleanupsystem)
- [x] **DDS2ECS-S9T2** `SimHostSubsystem`: same registration — [details](./TASK-DETAIL.md#dds2ecs-s9t2--simhostsubsystem-same-registration)

---

## Phase 10: Dead Reckoning

**Goal:** IG ghost movement is smooth and predictive; no hard-snapping on packet arrival;
`WorldPos` is fully utilised.

- [x] **DDS2ECS-S10T1** Fix `WorldPosTranslator.Decode`: write `NetworkPosition` — [details](./TASK-DETAIL.md#dds2ecs-s10t1--fix-geospatialtranslatordecode-ig-write-networkposition)
- [x] **DDS2ECS-S10T2** Create `WorldPosTranslator` (IG) — [details](./TASK-DETAIL.md#dds2ecs-s10t2--create-geospatialdrtranlator-ig)
- [x] **DDS2ECS-S10T3** Create `DeadReckoningSyncSystem` (IG) — [details](./TASK-DETAIL.md#dds2ecs-s10t3--create-deadreckoningsyncsystem-ig)
- [x] **DDS2ECS-S10T4** `IgApplication`: register DR translator and system — [details](./TASK-DETAIL.md#dds2ecs-s10t4--igapplication-register-new-dr-translator-and-system)

---

## Phase 11: Time Synchronisation Fix

**Goal:** SimHost broadcasts master clock pulses; IG PLL tracks them for deterministic simulation.

- [x] **DDS2ECS-S11T1** Verify `TimePulseDescriptor` DDS topic registration — [details](./TASK-DETAIL.md#dds2ecs-s11t1--verify-timepulsedescriptor-dds-topic-registration)
- [x] **DDS2ECS-S11T2** `IgApplication`: enable `TimePulseTranslator` — [details](./TASK-DETAIL.md#dds2ecs-s11t2--igapplication-enable-timepulsetranslator)
- [x] **DDS2ECS-S11T3** `SimHostApp` / `SimHostSubsystem`: register time-pulse egress — [details](./TASK-DETAIL.md#dds2ecs-s11t3--simhostapp--simhostsubsystem-register-time-pulse-egress)

---

## Phase 12: Transient Event Translators

**Goal:** `FireInteractionEvent` is distributed over DDS so IG renders combat effects.

- [x] **DDS2ECS-S12T1** Create `FireInteractionEventTranslator` — [details](./TASK-DETAIL.md#dds2ecs-s12t1--create-fireinteractioneventtranslator)
- [x] **DDS2ECS-S12T2** `SimHostApp` / `SimHostSubsystem`: register egress — [details](./TASK-DETAIL.md#dds2ecs-s12t2--simhostapp--simhostsubsystem-register-egress-translator)
- [x] **DDS2ECS-S12T3** `IgApplication`: register ingress — [details](./TASK-DETAIL.md#dds2ecs-s12t3--igapplication-register-ingress-translator)

---

## Phase 13: SimHost Mission Control Reception

**Goal:** SimHost listens for `MissionControlRequest` from IOS and responds with `MissionControlAck`.


## Phase 13: SimHost Mission Control Reception

**Goal:** SimHost listens for `MissionControlRequest` from IOS and responds with `MissionControlAck`.

- [x] **DDS2ECS-S13T1** Create `MissionControlRequestSystem` — [details](./TASK-DETAIL.md#dds2ecs-s13t1--create-missioncontrolrequestsystem)
- [x] **DDS2ECS-S13T2** Register `MissionControlRequestSystem` in SimHostApp / Subsystem — [details](./TASK-DETAIL.md#dds2ecs-s13t2--register-missioncontrolrequestsystem)

---

## Phase 14: IOS Mission Editor UI

**Goal:** `MissionPanel.cs` becomes a full editor: add/delete/reorder tasks and edit parameters.

- [x] **DDS2ECS-S14T1** Task-list editing (Add / Insert / Delete) — [details](./TASK-DETAIL.md#dds2ecs-s14t1--task-list-editing-add--insert--delete)
- [x] **DDS2ECS-S14T2** `BehaviorId` dropdown and `BehaviorParams` JSON editor — [details](./TASK-DETAIL.md#dds2ecs-s14t2--behaviorid-dropdown-and-behaviorparams-json-editor)
- [x] **DDS2ECS-S14T3** "Commit" button wired to `CommitMissionAsync` — [details](./TASK-DETAIL.md#dds2ecs-s14t3--commit-button-wired-to-commitmissionasync)

---

## Phase 15: Integration Test Harness

**Goal:** Automated xUnit end-to-end tests for IOS↔IG↔SimHost flows using the real DDS stack.

- [x] **DDS2ECS-S15T1** Add `internal` test-hook properties/methods to subsystems — [details](./TASK-DETAIL.md#dds2ecs-s15t1--add-internal-test-hook-propertiesmethods)
- [x] **DDS2ECS-S15T2** Create `HrotRunnerHarness` — [details](./TASK-DETAIL.md#dds2ecs-s15t2--create-hrotrunnerharness)
- [x] **DDS2ECS-S15T3** Map Placement integration test — [details](./TASK-DETAIL.md#dds2ecs-s15t3--map-placement-integration-test)
- [x] **DDS2ECS-S15T4** Context Menu Push integration test — [details](./TASK-DETAIL.md#dds2ecs-s15t4--context-menu-push-integration-test)
- [x] **DDS2ECS-S15T5** Entity Destroy integration test — [details](./TASK-DETAIL.md#dds2ecs-s15t5--entity-destroy-integration-test)
- [x] **DDS2ECS-S15T6** Mission Control integration test — [details](./TASK-DETAIL.md#dds2ecs-s15t6--mission-control-integration-test)

---

## Phase 16: SimHost Mission Pipeline (UrbanCombat Alignment)

**Goal:** Align mission execution with `UrbanCombat` golden standard — replace managed DTO holder
with `MissionPlanQueue`, compile real BTree interpreters, replace `MissionAdapterSystem` with
 toolkit-standard `MissionDirectorSystem`. See DESIGN.md §10 for the three-deviation analysis.

- [x] **DDS2ECS-S16T1** Delete `EntityMissionHolder`, register `MissionPlanQueue` — [details](./TASK-DETAIL.md#dds2ecs-s16t1--delete-entitymissionholder)
- [x] **DDS2ECS-S16T2** Rewrite `EntityMissionTranslator` to write `MissionPlanQueue` — [details](./TASK-DETAIL.md#dds2ecs-s16t2--rewrite-entitymissiontranslator-to-write-missionplanqueue)
- [x] **DDS2ECS-S16T3** Delete `MissionAdapterSystem`, register `MissionDirectorSystem` — [details](./TASK-DETAIL.md#dds2ecs-s16t3--delete-missionadaptersystem-register-missiondirectorsystem)
- [x] **DDS2ECS-S16T4** Compile real BTree interpreters for all behaviors — [details](./TASK-DETAIL.md#dds2ecs-s16t4--compile-real-btree-interpreters-for-all-behaviors)
- [x] **DDS2ECS-S16T5** Wire `ParseParams` delegates for param-carrying behaviors — [details](./TASK-DETAIL.md#dds2ecs-s16t5--wire-parseparams-delegates-for-param-carrying-behaviors)

---

## Phase 17: SimHost Combat Readiness (UrbanCombat Alignment)

**Goal:** Elevate SimHost from a driving-only shell to a full FDP simulation node capable of
perception, combat, and damage. Sources: `HeadlessDemoApp.cs`, `DemoTkbSetup.cs`. See DESIGN.md §11.

- [x] **DDS2ECS-S17T1** Add `Perception` and `Combat` project references to `Hrot.SimHost.csproj` — [details](./TASK-DETAIL.md#dds2ecs-s17t1--add-perception-and-combat-project-references)
- [x] **DDS2ECS-S17T2** Register Perception, Combat, Physics, and HSM components in `SimHostApp.RegisterSimComponents()` — [details](./TASK-DETAIL.md#dds2ecs-s17t2--register-perception-combat-physics-and-hsm-components)
- [x] **DDS2ECS-S17T3** Initialize `PhysicsToolkitModule` in `SimHostApp.OnLoad()` — [details](./TASK-DETAIL.md#dds2ecs-s17t3--initialize-physicstoolkitmodule-in-simhostapponload)
- [x] **DDS2ECS-S17T4** Expand `SimulationLogicModule` with Input/Sim/PostSim combat systems — [details](./TASK-DETAIL.md#dds2ecs-s17t4--expand-simulationlogicmodule-with-combat-systems)
- [x] **DDS2ECS-S17T5** Rewrite `BdcTkbBuilder.WithCombat()` to attach real ECS components — [details](./TASK-DETAIL.md#dds2ecs-s17t5--rewrite-bdctkbbuilderwithcombat-to-attach-real-ecs-components)
