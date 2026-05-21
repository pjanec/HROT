# Task Tracker — Blueprint Subsystem

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

<!-- Task sections will be appended below, grouped by design document area -->

---

## Phase 0 -- Infrastructure

**Goal:** Establish build hygiene: all projects compile, asset schema round-trips JSON, one smoke test passes.

- [x] **TASK-P0-001** Project Skeleton & Filesystem Placement -- [details](./TASK-DETAIL.md#task-p0-001----project-skeleton--filesystem-placement)
- [x] **TASK-P0-002** Asset Schema Types -- [details](./TASK-DETAIL.md#task-p0-002----asset-schema-types)
- [x] **TASK-P0-003** Asset JSON Round-Trip Tests -- [details](./TASK-DETAIL.md#task-p0-003----asset-json-round-trip-tests)

---

## Phase 1 -- Test Harness

**Goal:** Build the test infrastructure (mocks, fixture, builder, ALC lifecycle) before any production code; all other phases depend on this harness for running their unit tests.

- [x] **TASK-TH-001** MockSimulationView -- [details](./TASK-DETAIL.md#task-th-001----mocksimulationview)
- [x] **TASK-TH-002** MockEntityCommandBuffer -- [details](./TASK-DETAIL.md#task-th-002----mockentitycommandbuffer)
- [x] **TASK-TH-003** BlueprintTestFixture Core Infrastructure -- [details](./TASK-DETAIL.md#task-th-003----blueprinttestfixture-core-infrastructure)
- [x] **TASK-TH-004** BlueprintAssetBuilder Fluent API -- [details](./TASK-DETAIL.md#task-th-004----blueprintassetbuilder-fluent-api)
- [x] **TASK-TH-005** ALC Lifecycle and Unload Verification -- [details](./TASK-DETAIL.md#task-th-005----alc-lifecycle-and-unload-verification)
- [x] **TASK-TH-006** TickFrame Refinements (Patches 1 + 2 Applied) -- [details](./TASK-DETAIL.md#task-th-006----tickframe-refinements-patches-1--2-applied)
- [x] **TASK-TH-007** Mock Contract Tests -- [details](./TASK-DETAIL.md#task-th-007----mock-contract-tests-8)
- [x] **TASK-TH-008** CapturingDebugSession -- [details](./TASK-DETAIL.md#task-th-008----capturingdebugsession-10)
- [x] **TASK-TH-009** TestData Infrastructure -- [details](./TASK-DETAIL.md#task-th-009----testdata-infrastructure-11)
- [x] **TASK-TH-010** BehaviorRegistry + InvokeBTree/Hsm + MockDispatcherSystem -- [details](./TASK-DETAIL.md#task-th-010----behaviorregistry-wiring--invokebtreehsm-helpers--mockdispatchersystem-12-resolutions--patches-3-q-121-through-q-124)

---

## Phase 2 -- Runtime

**Goal:** Build the engine-side runtime machinery (registry, blackboard tiers, partition allocator, tick and maintenance systems) using hand-crafted fake generated code as the test vehicle, before the compiler exists.

- [x] **TASK-RT-001** BlueprintRegistry -- [details](./TASK-DETAIL.md#task-rt-001----blueprintregistry)
- [x] **TASK-RT-002** BlueprintDefinition, Delegate Types, and BlueprintLatentCursor -- [details](./TASK-DETAIL.md#task-rt-002----blueprintdefinition-delegate-types-and-blueprintlatentcursor)
- [x] **TASK-RT-003** BlueprintBlackboard Components and Slot-Table Types -- [details](./TASK-DETAIL.md#task-rt-003----blueprintblackboard-components-and-slot-table-types)
- [x] **TASK-RT-004** BlueprintBlackboardPartitions (Partition Allocator) -- [details](./TASK-DETAIL.md#task-rt-004----blueprintblackboardpartitions-partition-allocator)
- [x] **TASK-RT-005** BlueprintTickSystem + World-Singleton Dispatch -- [details](./TASK-DETAIL.md#task-rt-005----blueprinttickystem--world-singleton-dispatch)
- [x] **TASK-RT-006** BlueprintMaintenanceSystem -- [details](./TASK-DETAIL.md#task-rt-006----blueprintmaintenancesystem)
- [x] **TASK-RT-007** Runtime Test Suite -- [details](./TASK-DETAIL.md#task-rt-007----runtime-test-suite)

---

## Phase 3 -- Compiler

- [ ] **TASK-CP-000** Implement Static Catalog Stubs (Engine bindings for Demos) -- [details](./TASK-DETAIL.md#task-cp-000----implement-static-catalog-stubs)
- [ ] **TASK-CP-001** Compiler Infrastructure and IR Data Model -- [details](./TASK-DETAIL.md#task-cp-001----compiler-infrastructure-and-ir-data-model)
- [ ] **TASK-CP-002** Pipeline Stages 1-5 (Parse through Schedule) -- [details](./TASK-DETAIL.md#task-cp-002----pipeline-stages-1-5-parse-through-schedule)
- [ ] **TASK-CP-003** Stage 6: Lower (Dispatch-Aware Transformations) -- [details](./TASK-DETAIL.md#task-cp-003----stage-6-lower-dispatch-aware-transformations)
- [ ] **TASK-CP-004** Stage 7: Emit (C# Code Generation) -- [details](./TASK-DETAIL.md#task-cp-004----stage-7-emit-c-code-generation)
- [ ] **TASK-CP-005** Stage 8: Roslyn + Incremental Generator + Debug Map + Determinism + Catalogs -- [details](./TASK-DETAIL.md#task-cp-005----stage-8-roslyn--incremental-generator--debug-map--determinism--catalogs)
- [ ] **TASK-CP-006** Compiler Test Suite -- [details](./TASK-DETAIL.md#task-cp-006----compiler-test-suite)

---

## Phase 4 -- Hot Reload

- [ ] **TASK-HR-001** AiHotReloadCoordinator Core -- [details](./TASK-DETAIL.md#task-hr-001----aihotreloadcoordinator-core)
- [ ] **TASK-HR-002** SimulateReload Test Harness Integration -- [details](./TASK-DETAIL.md#task-hr-002----simulatereload-test-harness-integration)
- [ ] **TASK-HR-003** Hot Reload Test Suite -- [details](./TASK-DETAIL.md#task-hr-003----hot-reload-test-suite)

---

## Phase 5 -- Debug Protocol

- [ ] **TASK-DBG-000** Blueprint Time Controller Adapter (Interface & MasterSyncController wrapper) -- [details](./TASK-DETAIL.md#task-dbg-000----blueprint-time-controller-adapter)
- [ ] **TASK-DBG-001** Debug Session Interface and DebugProbe Dispatcher -- [details](./TASK-DETAIL.md#task-dbg-001----debug-session-interface-and-debugprobe-dispatcher)
- [ ] **TASK-DBG-002** Debug Map Format and Node-ID Resolution -- [details](./TASK-DETAIL.md#task-dbg-002----debug-map-format-and-node-id-resolution)
- [ ] **TASK-DBG-003** Breakpoints and Step Semantics -- [details](./TASK-DETAIL.md#task-dbg-003----breakpoints-and-step-semantics)
- [ ] **TASK-DBG-004** Watch Expressions and Pin-Value Snapshotting -- [details](./TASK-DETAIL.md#task-dbg-004----watch-expressions-and-pin-value-snapshotting)
- [ ] **TASK-DBG-005** Multi-Entity Debugging PDB Integration Hot Reload Interaction -- [details](./TASK-DETAIL.md#task-dbg-005----multi-entity-debugging-pdb-integration-hot-reload-interaction)
- [ ] **TASK-DBG-006** Debug Protocol Test Suite -- [details](./TASK-DETAIL.md#task-dbg-006----debug-protocol-test-suite)

---

## Phase 6 -- Editor

- [ ] **TASK-ED-001** Editor Infrastructure Window Lifecycle IWindowRegistrar Time-Controller Adapter -- [details](./TASK-DETAIL.md#task-ed-001----editor-infrastructure-window-lifecycle-iwindowregistrar-time-controller-adapter)
- [ ] **TASK-ED-002** Asset Browser and Graph Editor Windows -- [details](./TASK-DETAIL.md#task-ed-002----asset-browser-and-graph-editor-windows)
- [ ] **TASK-ED-003** Inspector Window and StructEdit Drawer Infrastructure -- [details](./TASK-DETAIL.md#task-ed-003----inspector-window-and-structedit-drawer-infrastructure)
- [ ] **TASK-ED-004** Debug Panel Watch Panel Callstack Window Hot Reload Log -- [details](./TASK-DETAIL.md#task-ed-004----debug-panel-watch-panel-callstack-window-hot-reload-log)
- [ ] **TASK-ED-005** Quick Reload Full Rebuild Debug Session Lifecycle -- [details](./TASK-DETAIL.md#task-ed-005----quick-reload-full-rebuild-debug-session-lifecycle)
- [ ] **TASK-ED-006** Editor Preferences Configuration and Editor Test Suite -- [details](./TASK-DETAIL.md#task-ed-006----editor-preferences-configuration-and-editor-test-suite)

---

## Phase 7 -- Demos

- [ ] **TASK-DEMO-001** Demo: MathUtilsLib Library Dispatch -- [details](./TASK-DETAIL.md#task-demo-001----demo-mathutilslib-library-dispatch)
- [ ] **TASK-DEMO-002** Demo: HealthRegen Instance Dispatch -- [details](./TASK-DETAIL.md#task-demo-002----demo-healthregen-instance-dispatch)
- [ ] **TASK-DEMO-003** Demo: DoorActor + DoorSensor Multi-Blueprint Peer Calls -- [details](./TASK-DETAIL.md#task-demo-003----demo-dooractor--doorsensor-multi-blueprint-peer-calls)
- [ ] **TASK-DEMO-004** Demo: HasVisibleTarget AiPrimitive Multi-Hosting -- [details](./TASK-DETAIL.md#task-demo-004----demo-hasvisibletarget-aiprimitive-multi-hosting)
- [ ] **TASK-DEMO-005** Demo: MoveToAndFire Headline AiPrimitive Action -- [details](./TASK-DETAIL.md#task-demo-005----demo-movetoandfire-headline-aiprimitive-action)
