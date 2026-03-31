# Task Tracker — Integration Troubleshooting & Architecture Hardening

**Version:** 1.0  
**Date:** 2026-02-27

**Reference:** See [TASK-DETAILS-Integration-Troubleshooting.md](./TASK-DETAILS-Integration-Troubleshooting.md) for detailed task descriptions.

**Parent Design:** [DESIGN-Integration-Troubleshooting.md](./DESIGN-Integration-Troubleshooting.md)

---

## Phase 1: Integration Bug Fixes

**Goal:** Achieve basic end-to-end operation — UI buttons produce DDS traffic, map pans, entities appear across apps.

- [x] **INTS-P1-001** Register TKB Catalog in SimHost and IG [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-001--register-tkb-catalog-in-simhost-and-ig)
- [x] **INTS-P1-002** Fix SimHost Vehicle Spawning to Use SpawnEntityCommand [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-002--fix-simhost-vehicle-spawning-to-use-spawnentitycommand)
- [x] **INTS-P1-003** Replace NullDdsWriter with DdsWriterAdapter in IOS [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-003--replace-nullddswriter-with-ddswriteradapter-in-ios)
- [x] **INTS-P1-004** Add PassthruCentralNode to ImGui DockSpace [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-004--add-passthrucentralnode-to-imgui-dockspace)
- [x] **INTS-P1-005** Wire IG-to-IOS Map Event Translators [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-005--wire-ig-to-ios-map-event-translators)

---

## Phase 2: Architecture Consolidation

**Goal:** Eliminate initialisation duplication across SimHost, IG, and IOS; prevent future configuration drift; resolve input/display collision in the Runner.

- [x] **INTS-P2-006** Implement HrotEnvironment Bootstrapper [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-006--implement-hrotenvironment-bootstrapper)
- [x] **INTS-P2-007** Fix SubsystemOrchestrator Headless Logic [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-007--fix-subsystemorchestrator-headless-logic)
- [x] **INTS-P2-008** Refactor IgApplication to Use HrotEnvironment [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-008--refactor-igapplication-to-use-hrotenvironment)
- [x] **INTS-P2-009** Refactor SimHostApp to Use HrotEnvironment [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-009--refactor-simhostapp-to-use-hrotenvironment)
- [x] **INTS-P2-010** Refactor IosSubsystem to Use HrotEnvironment [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-010--refactor-iossubsystem-to-use-hrotenvironment)

---

## Phase 3: Debug Instrumentation & End-to-End Validation [COMPLETED]

**Goal:** Add structured trace logging at all major data-flow boundaries; validate the complete fixed stack with an automated integration test.

- [x] **INTS-P3-011** Trace Logging: SimHost Entity Spawn (Flow 1) [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-011--trace-logging-simhost-entity-spawn-flow-1)
- [x] **INTS-P3-012** Trace Logging: IG Entity Ingress & Render (Flow 2) [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-012--trace-logging-ig-entity-ingress--render-flow-2)
- [x] **INTS-P3-013** Trace Logging: IG Map Drawings & IOS Interactions (Flows 3–6) [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-013--trace-logging-ig-map-drawings--ios-interactions-flows-36)
- [x] **INTS-P3-014** Integration Test: End-to-End Entity Lifecycle [details](./TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-014--integration-test-end-to-end-entity-lifecycle)
