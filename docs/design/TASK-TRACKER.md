# IOS-IG-SimHost Project Task Tracker

**Version:** 3.2  
**Date:** 2026-02-13  
**Last Updated:** 2026-03-05

**Parent Documents**: 
- [DESIGN-SHARED.md](./DESIGN-SHARED.md) | [TASK-DETAILS-SHARED.md](./TASK-DETAILS-SHARED.md)
- [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) | [TASK-DETAILS-SIMHOST.md](./TASK-DETAILS-SIMHOST.md)
- [DESIGN-IG.md](./DESIGN-IG.md) | [TASK-DETAILS-IG.md](./TASK-DETAILS-IG.md)
- [DESIGN-IOS.md](./DESIGN-IOS.md) | [TASK-DETAILS-IOS.md](./TASK-DETAILS-IOS.md)
- [DESIGN-RUNNER.md](./DESIGN-RUNNER.md) | [TASK-DETAILS-RUNNER.md](./TASK-DETAILS-RUNNER.md)
- [DESIGN-NetworkSpawning.md](./DESIGN-NetworkSpawning.md) | [TASK-DETAILS-NetworkSpawning.md](./TASK-DETAILS-NetworkSpawning.md)
- [TASK-DETAILS-NetworkDemo-NetworkSpawning.md](./TASK-DETAILS-NetworkDemo-NetworkSpawning.md)
- [EDGE-CASES-AND-MITIGATIONS.md](./EDGE-CASES-AND-MITIGATIONS.md) ⚠️ **READ FIRST**


**⚠️ CRITICAL**: Review [EDGE-CASES-AND-MITIGATIONS.md](./EDGE-CASES-AND-MITIGATIONS.md) before starting any implementation phase.

---

## SHARED COMPONENTS

**Progress:** 15/40 tasks complete (38%)

### Phase 1: Infrastructure Validation

- [x] **P1.1** Build Existing FDP Solution [details](./TASK-DETAILS-SHARED.md#task-p11-build-existing-fdp-solution)
- [x] **P1.2** Run Existing Infrastructure Tests [details](./TASK-DETAILS-SHARED.md#task-p12-run-existing-infrastructure-tests)
- [-] **P1.3** Document Existing API Patterns [details](./TASK-DETAILS-SHARED.md#task-p13-document-existing-api-patterns)

### Phase 2: Data Model Assembly

- [x] **P2.1** Create Bagira.DDS.DataModel Project [details](./TASK-DETAILS-SHARED.md#task-p21-create-bagiraddsdatamodel-project)
- [x] **P2.2** Import FcdCsharp Types [details](./TASK-DETAILS-SHARED.md#task-p22-import-fcdcsharp-types)
- [x] **P2.3** Add DDS Attributes [details](./TASK-DETAILS-SHARED.md#task-p23-add-dds-attributes)
- [-] **P2.4** Create DDS Publisher/Subscriber Test [details](./TASK-DETAILS-SHARED.md#task-p24-create-dds-publishersubscriber-test)
- [-] **P2.5** Document Data Model Assembly [details](./TASK-DETAILS-SHARED.md#task-p25-document-data-model-assembly)

### Phase 3: FDP.Toolkit.DER Implementation

- [x] **P3.1** Create FDP.Toolkit.DER Project [details](./TASK-DETAILS-SHARED.md#task-p31-create-fdptoolkitder-project)
- [x] **P3.2** Implement IDerRepo Interface [details](./TASK-DETAILS-SHARED.md#task-p32-implement-iderrepo-interface)
- [x] **P3.3** Implement IDerEntity Interface [details](./TASK-DETAILS-SHARED.md#task-p33-implement-iderentity-interface)
- [x] **P3.4** Implement DerRepo Class [details](./TASK-DETAILS-SHARED.md#task-p34-implement-derrepo-class)
- [x] **P3.5** Implement DerEntity Class [details](./TASK-DETAILS-SHARED.md#task-p35-implement-derentity-class)
- [x] **P3.6** Write DER Unit Tests [details](./TASK-DETAILS-SHARED.md#task-p36-write-der-unit-tests)
- [x] **P3.7** Create DDS Translator Example [details](./TASK-DETAILS-SHARED.md#task-p37-create-dds-translator-example)
- [x] **P3.8** Document DER Library [details](./TASK-DETAILS-SHARED.md#task-p38-document-der-library)

### Phase 4: FDP.Toolkit.Commands Implementation

- [x] **P4.1** Create FDP.Toolkit.Commands Project [details](./TASK-DETAILS-SHARED.md#task-p41-create-fdptoolkitcommands-project)
- [x] **P4.2** Implement DdsCommandClient<TReq, TAck> [details](./TASK-DETAILS-SHARED.md#task-p42-implement-ddscommandclienttreq-tack)
- [x] **P4.3** Create BdcCommandGateway [details](./TASK-DETAILS-SHARED.md#task-p43-create-bdccommandgateway)
- [x] **P4.4** Write Commands Unit Tests [details](./TASK-DETAILS-SHARED.md#task-p44-write-commands-unit-tests)
- [x] **P4.5** Document Commands Library [details](./TASK-DETAILS-SHARED.md#task-p45-document-commands-library)

### Phase 5: Bagira.Map.Definitions (TKB Extensions)

- [x] **P5.1** Create Bagira.Map.Definitions Project [details](./TASK-DETAILS-SHARED.md#task-p51-create-bagiramapdefinitions-project)
- [x] **P5.2** Implement IG Visual Descriptor [details](./TASK-DETAILS-SHARED.md#task-p52-implement-ig-visual-descriptor)
- [x] **P5.3** Implement SimHost Vehicle Descriptor [details](./TASK-DETAILS-SHARED.md#task-p53-implement-simhost-vehicle-descriptor)
- [x] **P5.4** Implement SimHost Combat Descriptor [details](./TASK-DETAILS-SHARED.md#task-p54-implement-simhost-combat-descriptor)
- [x] **P5.5** Implement TKB Composition Descriptor [details](./TASK-DETAILS-SHARED.md#task-p55-implement-tkb-composition-descriptor)
- [x] **P5.6** Implement BdcTkbBuilder Fluent API [details](./TASK-DETAILS-SHARED.md#task-p56-implement-bdctkbbuilder-fluent-api)
- [x] **P5.7** Register Representative Entity Types [details](./TASK-DETAILS-SHARED.md#task-p57-register-representative-entity-types)
- [-] **P5.8** Write TKB Extensions Tests [details](./TASK-DETAILS-SHARED.md#task-p58-write-tkb-extensions-tests) (Skipped - obsolete architecture)
- [x] **P5.9** Document TKB Extensions Library [details](./TASK-DETAILS-SHARED.md#task-p59-document-tkb-extensions-library)

### Phase 6: Bagira.Map.Common Assembly

- [x] **P6.1** Create Bagira.Map.Common Project [details](./TASK-DETAILS-SHARED.md#task-p61-create-bagiramapcommon-project)
- [x] **P6.2** Add TKB Entity Types Constants [details](./TASK-DETAILS-SHARED.md#task-p62-add-tkb-entity-types-constants)
- [x] **P6.3** Add Map Configuration Constants [details](./TASK-DETAILS-SHARED.md#task-p63-add-map-configuration-constants)
- [x] **P6.4** Create Bagira.Map.Common README [details](./TASK-DETAILS-SHARED.md#task-p64-create-bagiramapcommon-readme)
- [x] **P6.5** Build and Validate Bagira.Map.Common [details](./TASK-DETAILS-SHARED.md#task-p65-build-and-validate-bagiramapcommon)

### Phase 7: Integration Testing (SKIPPED)

- [-] **P7.1** Create Integration Test Project [details](./TASK-DETAILS-SHARED.md#task-p71-create-integration-test-project)
- [-] **P7.2** Implement End-to-End Entity Creation Test [details](./TASK-DETAILS-SHARED.md#task-p72-implement-end-to-end-entity-creation-test)
- [-] **P7.3** Performance Tests [details](./TASK-DETAILS-SHARED.md#task-p73-performance-tests)
- [-] **P7.4** Create Integration Guide [details](./TASK-DETAILS-SHARED.md#task-p74-create-integration-guide)

---

## NETWORK SPAWNING TOOLKIT

**Progress:** 0/6 tasks complete (0%)

**Purpose:** Build `FDP.Toolkit.NetworkSpawning` — the shared library that centralises entity spawning logic for SimHost, IG, and NetworkDemo, eliminating duplicated boilerplate.

**Dependencies:** Requires Shared Components P1-P5 complete

### Phase NS1: FDP.Toolkit.NetworkSpawning Library

- [x] **NS1.1** Create FDP.Toolkit.NetworkSpawning project [details](./TASK-DETAILS-NetworkSpawning.md#task-ns11-create-fdptoolkitnetworkspawning-project)
- [x] **NS1.2** Define SpawnEntityCommand / UpdateEntityCommand / DestroyEntityCommand events [details](./TASK-DETAILS-NetworkSpawning.md#task-ns12-define-event-structs)
- [x] **NS1.3** Implement EntityComponentReflector [details](./TASK-DETAILS-NetworkSpawning.md#task-ns13-implement-entitycomponentreflector)
- [x] **NS1.4** Implement NetworkSpawningSystem spawn path [details](./TASK-DETAILS-NetworkSpawning.md#task-ns14-implement-networkspawningsystem-spawn-path)
- [x] **NS1.5** Implement NetworkSpawningSystem update + destroy paths [details](./TASK-DETAILS-NetworkSpawning.md#task-ns15-implement-update-and-destroy-paths)
- [x] **NS1.6** Integration test: full entity lifecycle [details](./TASK-DETAILS-NetworkSpawning.md#task-ns16-integration-test)

---

## NETWORKDEMO INTEGRATION

**Progress:** 0/5 tasks complete (0%)

**Purpose:** Validate `FDP.Toolkit.NetworkSpawning` in the existing `Fdp.Examples.NetworkDemo` project — replacing manual spawn boilerplate and confirming all lifecycle integration tests still pass.

**Dependencies:** NS1 fully complete

### Phase NS2: NetworkDemo NetworkSpawning Integration

- [x] **NS2.1** Add FDP.Toolkit.NetworkSpawning reference to NetworkDemo [details](./TASK-DETAILS-NetworkDemo-NetworkSpawning.md#task-ns21-add-project-reference)
- [x] **NS2.2** Register NetworkSpawningSystem via SpawningModule [details](./TASK-DETAILS-NetworkDemo-NetworkSpawning.md#task-ns22-register-networkspawningsystem)
- [x] **NS2.3** Refactor SpawnLocalEntities to publish SpawnEntityCommand [details](./TASK-DETAILS-NetworkDemo-NetworkSpawning.md#task-ns23-refactor-spawnlocalentities)
- [x] **NS2.4** Update ingress translator to publish SpawnEntityCommand / DestroyEntityCommand [details](./TASK-DETAILS-NetworkDemo-NetworkSpawning.md#task-ns24-update-ingress-translator)
- [x] **NS2.5** Validate LifecycleIntegrationTests still pass [details](./TASK-DETAILS-NetworkDemo-NetworkSpawning.md#task-ns25-validate-integration-tests)

---

## SIMHOST MOCK

**Progress:** 12/28 tasks complete (42%)

**Dependencies:** Requires Shared Components P1-P6 complete

### Phase S1: Project Setup

- [x] **S1.1** Create SimHost Console Project [details](./TASK-DETAILS-SIMHOST.md#task-s11-create-simhost-console-project)
- [x] **S1.2** Add Project References (incl. FDP.Toolkit.NetworkSpawning) [details](./TASK-DETAILS-SIMHOST.md#task-s12-add-project-references)
- [x] **S1.3** Define ECS Components [details](./TASK-DETAILS-SIMHOST.md#task-s13-define-ecs-components)
- [ ] **S1.3b** Audit TKB Descriptors for SimTransform/SimVelocity [details](./TASK-DETAILS-SIMHOST.md#task-s13b-audit-tkb-descriptors)

### Phase S2: CreateEntityRequestHandler

- [x] **S2.1** Implement Request Handler Skeleton [details](./TASK-DETAILS-SIMHOST.md#task-s21-implement-request-handler-skeleton)
- [x] **S2.2** Implement ID Allocation Logic [details](./TASK-DETAILS-SIMHOST.md#task-s22-implement-id-allocation-logic)
- [x] **S2.3** Implement TKB Template Lookup [details](./TASK-DETAILS-SIMHOST.md#task-s23-implement-tkb-template-lookup)
- [x] **S2.4** Publish SpawnEntityCommand via DescriptorMapper *(uses FDP.Toolkit.NetworkSpawning)* [details](./TASK-DETAILS-SIMHOST.md#task-s24-publish-spawnentitycommand)
- [x] **S2.5** Implement DescriptorMapper *(replaces ApplyInitialDescriptors)* [details](./TASK-DETAILS-SIMHOST.md#task-s25-implement-descriptormapper)
- [x] **S2.6** Implement ACK Response [details](./TASK-DETAILS-SIMHOST.md#task-s26-implement-ack-response)
- [x] **S2.7** Write Request Handler Tests [details](./TASK-DETAILS-SIMHOST.md#task-s27-write-request-handler-tests)

### Phase S3: Geographic Module Integration

- [x] **S3.1** Register GeographicModule and Verify Egress [details](./TASK-DETAILS-SIMHOST.md#task-s31-register-geographicmodule-and-verify-egress)

### Phase S4: Behavior Toolkit Integration

- [x] **S4.1** Register Behavior / Navigation / Physics Systems [details](./TASK-DETAILS-SIMHOST.md#task-s41-register-behavior--navigation--physics-systems)
- [x] **S4.2** Implement EntityMissionTranslator and EntityMissionEgressTranslator [details](./TASK-DETAILS-SIMHOST.md#task-s42-implement-entitymissiontranslator-and-entitymissionegresstranslator)
- [x] **S4.3** Implement MissionAdapterSystem [details](./TASK-DETAILS-SIMHOST.md#task-s43-implement-missionadaptersystem)
- [x] **S4.4** Implement JoinFormationExecutor [details](./TASK-DETAILS-SIMHOST.md#task-s44-implement-joinformationexecutor)

### Phase S5: Main Application Shell

- [x] **S5.1** Implement Program.cs Entry Point [details](./TASK-DETAILS-SIMHOST.md#task-s51-implement-programcs-entry-point)
- [x] **S5.2** Create Configuration System [details](./TASK-DETAILS-SIMHOST.md#task-s52-create-configuration-system)
- [x] **S5.3** Add Logging and Diagnostics [details](./TASK-DETAILS-SIMHOST.md#task-s53-add-logging-and-diagnostics)
- [x] **S5.4** Add Graceful Shutdown [details](./TASK-DETAILS-SIMHOST.md#task-s54-add-graceful-shutdown)

### Phase S6: Integration Testing

- [x] **S6.1** Test Entity Creation Flow [details](./TASK-DETAILS-SIMHOST.md#task-s61-test-entity-creation-flow)
- [x] **S6.2** Test Mission Execution [details](./TASK-DETAILS-SIMHOST.md#task-s62-test-mission-execution)
- [x] **S6.3** Performance Testing [details](./TASK-DETAILS-SIMHOST.md#task-s63-performance-testing)

### Phase S7: Documentation (SKIPPED)

- [~] ~~**S7.1** Create User Guide~~
- [~] ~~**S7.2** Create Configuration Reference~~
- [~] ~~**S7.3** Add Code Documentation~~

---

## IG MOCK

**Progress:** 24/24 tasks complete (100%)

**Dependencies:** Requires Shared Components P1-P2 complete

### Phase IG1: Core Infrastructure

- [x] **IG.1.1** Create Bagira.IG Project [details](./TASK-DETAILS-IG.md#task-ig11-create-bagiraig-project)
- [x] **IG.1.2** Setup MapCanvas with Camera Controls [details](./TASK-DETAILS-IG.md#task-ig12-setup-mapcanvas-with-camera-controls)
- [x] **IG.1.3** Integrate NetworkDemo Network Module (translators publish SpawnEntityCommand) [details](./TASK-DETAILS-IG.md#task-ig13-integrate-networkdemo-network-module)
- [x] **IG.1.3b** Register NetworkSpawningSystem via SpawningModule [details](./TASK-DETAILS-IG.md#task-ig13b-register-networkspawningsystem-in-ig-kernel)
- [x] **IG.1.4** Add EntityRenderLayer with Stub Visualizer [details](./TASK-DETAILS-IG.md#task-ig14-add-entityrenderlayer-with-stub-visualizer)

### Phase IG2: Basic Rendering

- [x] **IG.2.1** Implement ResolvedStyle Component [details](./TASK-DETAILS-IG.md#task-ig21-implement-resolvedstyle-component)
- [x] **IG.2.2** Implement StyleResolutionSystem [details](./TASK-DETAILS-IG.md#task-ig22-implement-styleresolutionsystem)
- [x] **IG.2.3** Create SstVisualizerAdapter [details](./TASK-DETAILS-IG.md#task-ig23-create-sstvisualizeradapter)
- [x] **IG.2.4** Add MapCullingSystem [details](./TASK-DETAILS-IG.md#task-ig24-add-mapcullingsystem)
- [x] **IG.2.5** Integration Test: Render 100 Entities [details](./TASK-DETAILS-IG.md#task-ig25-integration-test-render-100-entities)

### Phase IG3: Interaction Tools

- [x] **IG.3.1** Integrate StandardInteractionTool [details](./TASK-DETAILS-IG.md#task-ig31-integrate-standardinteractiontool)
- [x] **IG.3.2** Add Selection Highlighting [details](./TASK-DETAILS-IG.md#task-ig32-add-selection-highlighting)
- [x] **IG.3.3** Implement CreationTool (Entity Placement) [details](./TASK-DETAILS-IG.md#task-ig33-implement-creationtool-entity-placement)
- [x] **IG.3.4** Implement MeasureTool (Distance) [details](./TASK-DETAILS-IG.md#task-ig34-implement-measuretool-distance)
- [x] **IG.3.5** Integration Test: Create Entity [details](./TASK-DETAILS-IG.md#task-ig35-integration-test-create-entity)

### Phase IG4: Advanced Features

- [x] **IG.4.1** Implement HistoryRecordingSystem [details](./TASK-DETAILS-IG.md#task-ig41-implement-historyrecordingsystem)
- [x] **IG.4.2** Implement EventToEffectSystem [details](./TASK-DETAILS-IG.md#task-ig42-implement-eventtoeffectsystem)
- [x] **IG.4.3** Add Context Menu System [details](./TASK-DETAILS-IG.md#task-ig43-add-context-menu-system)
- [x] **IG.4.4** Implement EditTool (Vertex Manipulation) [details](./TASK-DETAILS-IG.md#task-ig44-implement-edittool-vertex-manipulation)
- [x] **IG.4.5** Integration Test: Advanced Features [details](./TASK-DETAILS-IG.md#task-ig45-integration-test-advanced-features)

### Phase IG5: UI & Polish

- [x] **IG.5.1** Create Debug Panel [details](./TASK-DETAILS-IG.md#task-ig51-create-debug-panel)
- [x] **IG.5.2** Add Entity Inspector Panel [details](./TASK-DETAILS-IG.md#task-ig52-add-entity-inspector-panel)
- [x] **IG.5.3** Add Mini-IOS Panel [details](./TASK-DETAILS-IG.md#task-ig53-add-mini-ios-panel)
- [x] **IG.5.4** Add Performance Metrics Overlay [details](./TASK-DETAILS-IG.md#task-ig54-add-performance-metrics-overlay)

---

## IOS MOCK

**Progress:** 20/20 tasks complete (100%)

**Dependencies:** Requires Shared Components P2, P3, P4

### Phase IOS-P5: Project Setup

- [x] **IOS.P5.1** Create Bagira.IOS Console Project [details](./TASK-DETAILS-IOS.md#p51-create-bagiraios-project)
- [x] **IOS.P5.2** Add Dependencies [details](./TASK-DETAILS-IOS.md#p52-add-dependencies)

### Phase IOS-P6: IOS Services

- [x] **IOS.6.1** Request Transaction Manager [details](./TASK-DETAILS-IOS.md#p61-request-transaction-manager)
- [x] **IOS.6.2** Mission Editor Service [details](./TASK-DETAILS-IOS.md#p62-mission-editor-service)
- [x] **IOS.6.3** Context Menu Logic [details](./TASK-DETAILS-IOS.md#p73-context-menu-logic)

### Phase IOS-P7: IOS UI Panels

- [x] **IOS.7.1** Configuration Panel [details](./TASK-DETAILS-IOS.md#p81-configuration-panel)
- [x] **IOS.7.2** ORBAT Hierarchy Panel [details](./TASK-DETAILS-IOS.md#p82-orbat-hierarchy-panel)
- [x] **IOS.7.3** Mission Panel [details](./TASK-DETAILS-IOS.md#p83-mission-panel)
- [x] **IOS.7.4** Interaction Panel (Event Log) [details](./TASK-DETAILS-IOS.md#p84-interaction-panel-event-log)
- [x] **IOS.7.5** Spawner Panel [details](./TASK-DETAILS-IOS.md#p85-spawner-panel)

### Phase IOS-P8: IOS Application Shell

- [x] **IOS.8.1** IOS Main Logic [details](./TASK-DETAILS-IOS.md#p91-ios-main-logic)
- [x] **IOS.8.2** IOS Program & CLI [details](./TASK-DETAILS-IOS.md#p92-ios-program--cli)

### Phase IOS-P9: Integration Testing

- [x] **IOS.9.1** IOS Standalone Test [details](./TASK-DETAILS-IOS.md#p101-ios-standalone-test)
- [x] **IOS.9.2** IOS + IG Integration Test [details](./TASK-DETAILS-IOS.md#p102-ios--ig-integration-test)
- [x] **IOS.9.3** IOS + SimHost Integration Test [details](./TASK-DETAILS-IOS.md#p103-ios--simhost-integration-test)
- [x] **IOS.9.4** Full Stack Integration Test [details](./TASK-DETAILS-IOS.md#p104-full-stack-integration-test)

### Phase IOS-P10: Advanced Features

- [x] **IOS.10.1** Inspector Panel [details](./TASK-DETAILS-IOS.md#p111-inspector-panel)
- [x] **IOS.10.2** Diagnostics Panel [details](./TASK-DETAILS-IOS.md#p112-diagnostics-panel)
- [x] **IOS.10.3** Conflict Detection UI [details](./TASK-DETAILS-IOS.md#p113-conflict-detection-ui)
- [x] **IOS.10.4** Multi-IOS Testing [details](./TASK-DETAILS-IOS.md#p114-multi-ios-testing)

---

## RUNNER (AGGREGATED APPLICATION)

**Progress:** 8/20 tasks complete (40%)

> **⚠️ Architecture Review (2026-02-26):** DESIGN-RUNNER.md and TASK-DETAILS-RUNNER.md have been updated following architect review. Before starting any Runner tasks, note these corrections:
> - **R1.3 / R1.4**: `ISubsystem` now has `DrawWorld()` and `DrawUI()` phases. The `SubsystemOrchestrator` owns the Raylib window and render loop — subsystems must not call `BeginDrawing`/`rlImGui.Begin` themselves.
> - **R1.5**: `SubsystemStatusAnnounce` uses `[DdsQos(...)]` (single attribute). Old `[DdsReliability]`/`[DdsDurability]` pseudo-attributes do not exist in FDP and will not compile.
> - **R2.1**: `SimHostSubsystem.Initialize()` uses `EntityRepository` + `ModuleHostKernel` (not obsolete `FdpWorld`/`CarKinemModule`).
> - **R2.7**: `DerRepo` takes no constructor arguments — it is a pure storage class. Network wiring happens in `ConnectToDomain()`.
> - **R3.3**: `SpawnEntityHandler` publishes `SpawnEntityCommand` to `EventBus` — there is no `CreateEntityViaFactory()`.
> - **Section 9.3**: `ICameraService`/`HeadlessCamera` have been removed from the design. Headless mode only needs `HeadlessInputProvider` + skipping draw calls.

> **✅ Phase R0 Complete (2026-03-06):** Component ID safety implementation merged. All 23 component structs now have deterministic IDs via `[ComponentId]` attributes. Flight Recorder schema validation active. Zero regressions. See [RUNNER-BATCH-01-REVIEW.md](../../.dev-workstream/reviews/RUNNER-BATCH-01-REVIEW.md).

### Phase R0: ECS Component ID Safety ✅ COMPLETE

- [x] **R0.1** Make Component IDs Deterministic (`[ComponentId]` attribute + `GlobalComponentIds` catalog) [details](./TASK-DETAILS-RUNNER.md#r01-make-component-ids-deterministic)
- [x] **R0.2** Implement Flight Recorder Schema Manifest + Validator [details](./TASK-DETAILS-RUNNER.md#r02-implement-flight-recorder-schema-manifest)

> **✅ Phase R1 Complete (2026-03-07):** Runner Core Infrastructure implemented. `Bagira.Runner` project created with `RunnerConfiguration` (CLI + JSON), `SubsystemOrchestrator` (Raylib window ownership, reverse-shutdown), `ISubsystem` interface, `SubsystemStatusAnnounce` DDS topic, and `WaitingRoomCoordinator`. 39 Runner tests + 2 new DDS DataModel tests. Zero regressions. See [RUNNER-BATCH-02-REPORT.md](../../.dev-workstream/reports/RUNNER-BATCH-02-REPORT.md).

### Phase R1: Runner Core Infrastructure ✅ COMPLETE

- [x] **R1.1** Create `Bagira.Runner` Project Structure [details](./TASK-DETAILS-RUNNER.md#r11-create-bagirarunner-project-structure)
- [x] **R1.2** Implement `RunnerConfiguration` + `RunMode` Enum [details](./TASK-DETAILS-RUNNER.md#r12-implement-runnerconfiguration--runmode-enum)
- [x] **R1.3** Implement `SubsystemOrchestrator` [details](./TASK-DETAILS-RUNNER.md#r13-implement-subsystemorchestrator)
- [x] **R1.4** Implement `ISubsystem` Interface + Stubs [details](./TASK-DETAILS-RUNNER.md#r14-implement-isubsystem-interface--stubs)
- [x] **R1.5** Add `SubsystemStatusAnnounce` DDS Topic [details](./TASK-DETAILS-RUNNER.md#r15-add-subsystemstatusannounce-dds-topic)
- [x] **R1.6** Implement `WaitingRoomCoordinator` [details](./TASK-DETAILS-RUNNER.md#r16-implement-waitingroomcoordinator)

### Phase R2: Subsystem Refactoring (COMPLETE)

- [x] **R2.1** Refactor SimHost to SimHostSubsystem Library [details](./TASK-DETAILS-RUNNER.md#r21-refactor-simhost-to-simhostsubsystem-library)
- [x] **R2.2** Create SimHost Standalone Program.cs [details](./TASK-DETAILS-RUNNER.md#r22-create-simhost-standalone-programcs)
- [x] **R2.3** Test SimHost Embeddability [details](./TASK-DETAILS-RUNNER.md#r23-test-simhost-embeddability)
- [x] **R2.4** Refactor IG to IgSubsystem Library [details](./TASK-DETAILS-RUNNER.md#r24-r29-ig-and-ios-subsystem-refactoring)
- [x] **R2.5** Create IG Standalone Program.cs
- [x] **R2.6** Test IG Embeddability
- [x] **R2.7** Refactor IOS to IosSubsystem Library
- [x] **R2.8** Create IOS Standalone Program.cs
- [x] **R2.9** Test IOS Embeddability

### Phase R0/R3: Technical Debt & Headless Infrastructure (COMPLETED)
- [x] **R0.1 Fix** Complete ComponentTypeRegistry Auto-Assignment Separation
- [x] **R0.2 Fix** Explicitly Attribute All Components
- [x] **R0.3 Fix** Restore `EntityMaster` Translator
- [x] **R3.1** Implement `HeadlessTestExecutor` [details](./TASK-DETAILS-RUNNER.md#r31-implement-headlesstestexecutor)
- [x] **R3.2** Implement Test Script JSON Parser [details](./TASK-DETAILS-RUNNER.md#r32-implement-test-script-json-parser)
- [x] **R3.3** Implement Test Action Handlers [details](./TASK-DETAILS-RUNNER.md#r33-implement-test-action-handlers)
- [x] **R3.4** Implement Metrics Collection [details](./TASK-DETAILS-RUNNER.md#r34-implement-metrics-collection)
- [x] **R3.5** Implement Test Report Generator [details](./TASK-DETAILS-RUNNER.md#r35-implement-test-report-generator)

### Phase R4: Integration Testing (COMPLETED)
- [x] **R4.1-R4.6** Runner Modes Verification & Flaky Tests [details](./TASK-DETAILS-RUNNER.md#r41-r46-integration-testing-tasks)
