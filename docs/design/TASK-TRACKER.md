# IOS-IG-SimHost Project Task Tracker

**Version:** 3.0  
**Date:** 2026-02-13  
**Last Updated:** 2026-02-14

**Parent Documents**: 
- [DESIGN-SHARED.md](./DESIGN-SHARED.md) | [TASK-DETAILS-SHARED.md](./TASK-DETAILS-SHARED.md)
- [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) | [TASK-DETAILS-SIMHOST.md](./TASK-DETAILS-SIMHOST.md)
- [DESIGN-IG.md](./DESIGN-IG.md) | [TASK-DETAILS-IG.md](./TASK-DETAILS-IG.md)
- [DESIGN-IOS.md](./DESIGN-IOS.md) | [TASK-DETAILS-IOS.md](./TASK-DETAILS-IOS.md)
- [DESIGN-RUNNER.md](./DESIGN-RUNNER.md) | [TASK-DETAILS-RUNNER.md](./TASK-DETAILS-RUNNER.md)
- [EDGE-CASES-AND-MITIGATIONS.md](./EDGE-CASES-AND-MITIGATIONS.md) ⚠️ **READ FIRST**

## Overview

This document tracks implementation progress for **all project components**: Shared, SimHost, IG Mock, IOS Mock, and Runner (aggregated app). Each task has binary status (✅ **DONE** or ⬜ **TODO**).

**Overall Progress:** 0/140 tasks complete (0%)

**⚠️ CRITICAL**: Review [EDGE-CASES-AND-MITIGATIONS.md](./EDGE-CASES-AND-MITIGATIONS.md) before starting any implementation phase. This document identifies and resolves critical architectural gaps, race conditions, and integration hazards.

---

# SHARED COMPONENTS

**Progress:** 0/40 tasks complete (0%)

---

## Phase 1: Infrastructure Validation (0/3)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P1.1** | Build Existing FDP Solution | ⬜ TODO | 0.5d | - | Open FDP.sln, build all projects |
| **P1.2** | Run Existing Infrastructure Tests | ⬜ TODO | 1.0d | - | Run tests for ID allocation, TKB, geographic, lifecycle |
| **P1.3** | Document Existing API Patterns | ⬜ TODO | 0.5d | - | Create FDP-API-REFERENCE.md |

**Phase Status:** ⬜ NOT STARTED

---

## Phase 2: Data Model Assembly (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P2.1** | Create Bagira.DDS.DataModel Project | ⬜ TODO | 0.25d | - | Create classlib project, add CycloneDDS.NET |
| **P2.2** | Import FcdCsharp Types | ⬜ TODO | 0.5d | - | Copy docs/FcdCsharp/*.cs to project |
| **P2.3** | Add DDS Attributes | ⬜ TODO | 0.5d | - | Verify [DdsTopic] and [DdsKey] on all types |
| **P2.4** | Create DDS Publisher/Subscriber Test | ⬜ TODO | 1.0d | - | Test EntityMaster, GeoSpatial pub/sub |
| **P2.5** | Document Data Model Assembly | ⬜ TODO | 0.25d | - | Create README.md |

**Phase Status:** ⬜ NOT STARTED

---

## Phase 3: FDP.Toolkit.DER Implementation (0/8)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P3.1** | Create FDP.Toolkit.DER Project | ⬜ TODO | 0.25d | - | Create classlib, no dependencies |
| **P3.2** | Implement IDerRepo Interface | ⬜ TODO | 0.25d | - | Define repository interface |
| **P3.3** | Implement IDerEntity Interface | ⬜ TODO | 0.25d | - | Define entity + descriptor interfaces |
| **P3.4** | Implement DerRepo Class | ⬜ TODO | 0.5d | - | Thread-safe ConcurrentDictionary storage |
| **P3.5** | Implement DerEntity Class | ⬜ TODO | 0.5d | - | Descriptor storage with ConcurrentDictionary |
| **P3.6** | Write DER Unit Tests | ⬜ TODO | 2.0d | - | 15+ tests, >95% coverage |
| **P3.7** | Create DDS Translator Example | ⬜ TODO | 1.0d | - | EntityMaster → DER ingress pattern |
| **P3.8** | Document DER Library | ⬜ TODO | 0.5d | - | Create README.md |

**Phase Status:** ⬜ NOT STARTED

---

## Phase 4: FDP.Toolkit.Commands Implementation (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P4.1** | Create FDP.Toolkit.Commands Project | ⬜ TODO | 0.25d | - | Create classlib, add CycloneDDS.NET |
| **P4.2** | Implement DdsCommandClient<TReq, TAck> | ⬜ TODO | 1.5d | - | Generic RPC with TaskCompletionSource |
| **P4.3** | Create BdcCommandGateway | ⬜ TODO | 0.5d | - | BDC-specific command facade |
| **P4.4** | Write Commands Unit Tests | ⬜ TODO | 1.5d | - | Test correlation, timeout, concurrency |
| **P4.5** | Document Commands Library | ⬜ TODO | 0.25d | - | Create README.md |

**Phase Status:** ⬜ NOT STARTED

---

## Phase 5: Bagira.Map.Definitions (TKB Extensions) (0/9)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P5.1** | Create Bagira.Map.Definitions Project | ⬜ TODO | 0.25d | - | Create classlib, reference FDP.Toolkit.Tkb |
| **P5.2** | Implement IG Visual Descriptor | ⬜ TODO | 0.25d | - | IgVisualDef (symbol, model, color) |
| **P5.3** | Implement SimHost Vehicle Descriptor | ⬜ TODO | 0.25d | - | SimVehicleDef (mass, speed, mobility) |
| **P5.4** | Implement SimHost Combat Descriptor | ⬜ TODO | 0.25d | - | SimCombatDef (armor, weapons, sensors) |
| **P5.5** | Implement TKB Composition Descriptor | ⬜ TODO | 0.25d | - | TkbCompositionDef (ORBAT subordinates) |
| **P5.6** | Implement BdcTkbBuilder Fluent API | ⬜ TODO | 1.0d | - | Fluent builder for template registration |
| **P5.7** | Register Representative Entity Types | ⬜ TODO | 2.0d | - | Register 10-15 entity types (M1, Bradley, etc.) |
| **P5.8** | Write TKB Extensions Tests | ⬜ TODO | 1.0d | - | Test registration, descriptor retrieval |
| **P5.9** | Document TKB Extensions Library | ⬜ TODO | 0.5d | - | Create README.md |

**Phase Status:** ⬜ NOT STARTED

---

## Phase 6: Bagira.Map.Common Assembly (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P6.1** | Create Bagira.Map.Common Project | ⬜ TODO | 0.25d | - | Create classlib, move BdcCommandGateway |
| **P6.2** | Add TKB Entity Types Constants | ⬜ TODO | 0.25d | - | TkbEntityTypes.cs constants |
| **P6.3** | Add Map Configuration Constants | ⬜ TODO | 0.25d | - | MapConfig.cs, ContextKeys.cs |
| **P6.4** | Create Bagira.Map.Common README | ⬜ TODO | 0.25d | - | Document constants and gateway |
| **P6.5** | Build and Validate Bagira.Map.Common | ⬜ TODO | 0.25d | - | Verify no circular dependencies |

**Phase Status:** ⬜ NOT STARTED

---

## Phase 7: Integration Testing (0/4)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **P7.1** | Create Integration Test Project | ⬜ TODO | 0.25d | - | Create mstest project, add all dependencies |
| **P7.2** | Implement End-to-End Entity Creation Test | ⬜ TODO | 1.5d | - | Full workflow: IOS→SimHost→IOS |
| **P7.3** | Performance Tests | ⬜ TODO | 1.0d | - | DER lookup, command RTT benchmarks |
| **P7.4** | Create Integration Guide | ⬜ TODO | 0.5d | - | INTEGRATION-GUIDE-SHARED.md |

**Phase Status:** ⬜ NOT STARTED

---

## Progress by Phase

| Phase | Complete | Total | Progress |
|-------|----------|-------|----------|
| **Phase 1: Infrastructure Validation** | 0 | 3 | ⬜⬜⬜ 0% |
| **Phase 2: Data Model Assembly** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase 3: FDP.Toolkit.DER** | 0 | 8 | ⬜⬜⬜⬜⬜⬜⬜⬜ 0% |
| **Phase 4: FDP.Toolkit.Commands** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase 5: Bagira.Map.Definitions** | 0 | 9 | ⬜⬜⬜⬜⬜⬜⬜⬜⬜ 0% |
| **Phase 6: Bagira.Map.Common** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase 7: Integration Testing** | 0 | 4 | ⬜⬜⬜⬜ 0% |
| **TOTAL** | **0** | **40** | **0%** |

---

## Critical Path

```
P1 (2d) → P2 (3d) → P7 (3d) = 8 days sequential minimum
```

**Parallel Work:**
- P3 (DER) can start immediately (no dependencies)
- P4 (Commands) can start immediately (no dependencies)
- P5 (TKB) requires P1 (infrastructure validation)

**Optimized Timeline (2 developers):**
- Week 1: P1+P2 (Dev1), P3 (Dev2)
- Week 2-3: P4+P6 (Dev1), P5 (Dev2)
- Week 4: P7 (Both)

**Total:** ~3.5 weeks with 2 developers

---

## Blockers & Risks

| Risk | Impact | Mitigation | Status |
|------|--------|------------|--------|
| FDP.sln build failures | HIGH | Resolve dependencies first (P1.1) | ⬜ OPEN |
| Existing tests fail | MEDIUM | Document failures, may need patches | ⬜ OPEN |
| CycloneDDS.NET API changes | MEDIUM | Use stable NuGet version | ⬜ OPEN |
| TkbTemplate interface mismatch | LOW | Use FDP.Interfaces version | ⬜ OPEN |

---

## Deliverables Checklist

### Code Artifacts

- [ ] `Bagira.DDS.DataModel.dll` - Data model types
- [ ] `FDP.Toolkit.DER.dll` - Non-ECS entity repository
- [ ] `FDP.Toolkit.Commands.dll` - RPC framework
- [ ] `Bagira.Map.Definitions.dll` - TKB descriptors
- [ ] `Bagira.Map.Common.dll` - Constants and gateway

### Documentation

- [ ] `FDP-API-REFERENCE.md` - Existing infrastructure API guide
- [ ] `INTEGRATION-GUIDE-SHARED.md` - Integration patterns
- [ ] 5x `README.md` files (one per library)

### Tests

- [ ] Data model pub/sub tests (P2.4)
- [ ] DER unit tests (P3.6)
- [ ] Commands unit tests (P4.4)
- [ ] TKB extensions tests (P5.8)
- [ ] Integration tests (P7.2, P7.3)

---

## Sign-Off

### Phase 1: Infrastructure Validation

- [ ] All FDP projects build successfully
- [ ] All existing tests pass
- [ ] API reference documentation complete

**Sign-Off:** __________________ Date: __________

---

### Phase 2: Data Model Assembly

- [ ] Bagira.DDS.DataModel.dll compiles
- [ ] Pub/sub tests pass for EntityMaster, GeoSpatial, CreateEntityRequest/Ack
- [ ] README complete

**Sign-Off:** __________________ Date: __________

---

### Phase 3: FDP.Toolkit.DER

- [ ] All interfaces and classes implemented
- [ ] Unit tests pass (>95% coverage)
- [ ] DDS translator example working
- [ ] README complete

**Sign-Off:** __________________ Date: __________

---

### Phase 4: FDP.Toolkit.Commands

- [ ] DdsCommandClient<TReq, TAck> implemented
- [ ] BdcCommandGateway implemented
- [ ] Unit tests pass (correlation, timeout, concurrency)
- [ ] README complete

**Sign-Off:** __________________ Date: __________

---

### Phase 5: Bagira.Map.Definitions

- [ ] All 4 descriptor types implemented
- [ ] BdcTkbBuilder fluent API working
- [ ] 10-15 entity types registered
- [ ] Unit tests pass
- [ ] README complete

**Sign-Off:** __________________ Date: __________

---

### Phase 6: Bagira.Map.Common

- [ ] Project compiles without circular dependencies
- [ ] All constants defined
- [ ] README complete

**Sign-Off:** __________________ Date: __________

---

### Phase 7: Integration Testing

- [ ] End-to-end entity creation test passes
- [ ] Performance tests pass (DER <10µs, Commands <100ms RTT)
- [ ] Integration guide complete

**Sign-Off:** __________________ Date: __________

---

## Final Acceptance

**All phases complete:** [ ]

**Total effort:** _______ developer-days

**Actual timeline:** _______ weeks

**Final sign-off:** __________________ Date: __________

---

# SIMHOST MOCK

**Progress:** 0/28 tasks complete (0%)

**Dependencies:** Requires Shared Components P1-P6 complete

---

## Phase S1: Project Setup (0/3)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S1.1** | Create SimHost Console Project | ⬜ TODO | 0.25d | - | Create Bagira.SimHost project |
| **S1.2** | Add Project References | ⬜ TODO | 0.25d | - | Add FDP + Bagira dependencies |
| **S1.3** | Define ECS Components | ⬜ TODO | 0.5d | - | NetworkId, EntityMaster, EntityInfo, GeoSpatial, GeoSpatialDR, EntityMission |

**Phase Status:** ⬜ NOT STARTED

---

## Phase S2: CreateEntityRequestHandler (0/7)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S2.1** | Implement Request Handler Skeleton | ⬜ TODO | 0.5d | - | System class with DDS reader/writer |
| **S2.2** | Implement ID Allocation Logic | ⬜ TODO | 0.5d | - | Call DdsIdAllocator.AllocateAsync() |
| **S2.3** | Implement TKB Template Lookup | ⬜ TODO | 0.5d | - | Extract TkbType, get template |
| **S2.4** | Implement Entity Creation | ⬜ TODO | 0.5d | - | Create entity from template |
| **S2.5** | Implement Initial Descriptor Application | ⬜ TODO | 1.0d | - | Apply EntityMaster, EntityInfo, GeoSpatial |
| **S2.6** | Implement ACK Response | ⬜ TODO | 0.25d | - | Send CreateEntityAck |
| **S2.7** | Write Request Handler Tests | ⬜ TODO | 0.75d | - | Unit test request processing |

**Phase Status:** ⬜ NOT STARTED

---

## Phase S3: GeoSpatialBridgeSystem (0/6)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S3.1** | Implement Bridge System Skeleton | ⬜ TODO | 0.25d | - | Query vehicles with NetworkIdComponent |
| **S3.2** | Implement Position Conversion | ⬜ TODO | 0.25d | - | Vector2 → GeoPosition |
| **S3.3** | Implement Heading Conversion | ⬜ TODO | 0.25d | - | Vector2.Forward → heading degrees |
| **S3.4** | Create GeoSpatial Component | ⬜ TODO | 0.25d | - | Set GeoSpatialComponent on entity |
| **S3.5** | Add GeoSpatialDR (Velocity) | ⬜ TODO | 0.5d | - | Create velocity component for moving vehicles |
| **S3.6** | Write Bridge System Tests | ⬜ TODO | 0.5d | - | Test coordinate conversions |

**Phase Status:** ⬜ NOT STARTED

---

## Phase S4: MissionExecutionSystem (0/7)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S4.1** | Implement Mission System Skeleton | ⬜ TODO | 0.5d | - | Query entities with EntityMissionComponent |
| **S4.2** | Implement MoveToLocation Behavior | ⬜ TODO | 1.0d | - | Navigate to point, detect arrival |
| **S4.3** | Implement FollowRoute Behavior | ⬜ TODO | 1.5d | - | Waypoint sequence with progress tracking |
| **S4.4** | Implement JoinFormation Behavior | ⬜ TODO | 0.75d | - | Join formation via VehicleAPI |
| **S4.5** | Implement Task Completion Detection | ⬜ TODO | 0.5d | - | Detect when tasks complete |
| **S4.6** | Implement Task State Transitions | ⬜ TODO | 0.75d | - | Advance to next task, handle completion |
| **S4.7** | Write Mission Execution Tests | ⬜ TODO | 1.0d | - | Test all behaviors and transitions |

**Phase Status:** ⬜ NOT STARTED

---

## Phase S5: Main Application Shell (0/4)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S5.1** | Implement Program.cs Entry Point | ⬜ TODO | 1.5d | - | Main application with initialization |
| **S5.2** | Create Configuration System | ⬜ TODO | 0.5d | - | JSON config for domain ID, origin, sim rate |
| **S5.3** | Add Logging and Diagnostics | ⬜ TODO | 0.5d | - | Logger class with levels |
| **S5.4** | Add Graceful Shutdown | ⬜ TODO | 0.5d | - | Ctrl+C handler, cleanup |

**Phase Status:** ⬜ NOT STARTED

---

## Phase S6: Integration Testing (0/3)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S6.1** | Test Entity Creation Flow | ⬜ TODO | 1.0d | - | End-to-end with mock IOS client |
| **S6.2** | Test Mission Execution | ⬜ TODO | 1.0d | - | Verify vehicle navigates per mission |
| **S6.3** | Performance Testing | ⬜ TODO | 1.0d | - | 60 Hz sustained with 100 entities |

**Phase Status:** ⬜ NOT STARTED

---

## Phase S7: Documentation (0/3)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **S7.1** | Create User Guide | ⬜ TODO | 0.5d | - | docs/SimHost-User-Guide.md |
| **S7.2** | Create Configuration Reference | ⬜ TODO | 0.25d | - | docs/SimHost-Configuration.md |
| **S7.3** | Add Code Documentation | ⬜ TODO | 0.25d | - | XML docs for all public APIs |

**Phase Status:** ⬜ NOT STARTED

---

## SimHost Progress by Phase

| Phase | Complete | Total | Progress |
|-------|----------|-------|----------|
| **Phase S1: Project Setup** | 0 | 3 | ⬜⬜⬜ 0% |
| **Phase S2: CreateEntityRequestHandler** | 0 | 7 | ⬜⬜⬜⬜⬜⬜⬜ 0% |
| **Phase S3: GeoSpatialBridgeSystem** | 0 | 6 | ⬜⬜⬜⬜⬜⬜ 0% |
| **Phase S4: MissionExecutionSystem** | 0 | 7 | ⬜⬜⬜⬜⬜⬜⬜ 0% |
| **Phase S5: Main Application Shell** | 0 | 4 | ⬜⬜⬜⬜ 0% |
| **Phase S6: Integration Testing** | 0 | 3 | ⬜⬜⬜ 0% |
| **Phase S7: Documentation** | 0 | 3 | ⬜⬜⬜ 0% |
| **TOTAL** | **0** | **28** | **0%** |

---

## SimHost Critical Path

```
Shared P1-P6 (25d) → S1 (1d) → S2 (3d) → S5 (3d) → S6 (3d) → S7 (1d) = 36 days sequential
```

**Parallel Work:**
- S3 (GeoSpatialBridge) can run parallel to S2
- S4 (MissionExecution) can run parallel to S2

**Optimized Timeline (2 developers):**
- Week 1: S1 + S2 (Dev1), S3 + S4 (Dev2)
- Week 2: S4 complete (Dev2), S5 (both)
- Week 3: S6 (integration tests), S7 (documentation)

**Total:** ~2.5 weeks with 2 developers (after Shared Components complete)

---

# IG MOCK

**Progress:** 0/23 tasks complete (0%)

**Dependencies:** Requires Shared Components P1-P2 complete (Data Model)

**Parent Documents**: [DESIGN-IG.md](./DESIGN-IG.md) | [TASK-DETAILS-IG.md](./TASK-DETAILS-IG.md)

---

## Phase IG1: Core Infrastructure (0/4)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IG.1.1** | Create Bagira.IG Project | ⬜ TODO | 0.5d | - | Setup project with all dependencies |
| **IG.1.2** | Setup MapCanvas with Camera Controls | ⬜ TODO | 0.5d | - | Pan/zoom, debug overlay |
| **IG.1.3** | Integrate NetworkDemo Network Module | ⬜ TODO | 0.5d | - | DDS participant, ingress/egress, translators |
| **IG.1.4** | Add EntityRenderLayer with Stub Visualizer | ⬜ TODO | 0.5d | - | Render red circles for entities |

**Phase Status:** ⬜ NOT STARTED

---

## Phase IG2: Basic Rendering (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IG.2.1** | Implement ResolvedStyle Component | ⬜ TODO | 0.25d | - | Define ECS component for visual properties |
| **IG.2.2** | Implement StyleResolutionSystem | ⬜ TODO | 1.0d | - | Merge TKB + network + user config |
| **IG.2.3** | Create SstVisualizerAdapter | ⬜ TODO | 1.0d | - | Production rendering (icons, labels, damage bars) |
| **IG.2.4** | Add MapCullingSystem | ⬜ TODO | 0.5d | - | Frustum culling + LOD |
| **IG.2.5** | Integration Test: Render 100 Entities | ⬜ TODO | 0.25d | - | End-to-end test with SimHost |

**Phase Status:** ⬜ NOT STARTED

---

## Phase IG3: Interaction Tools (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IG.3.1** | Integrate StandardInteractionTool | ⬜ TODO | 0.5d | - | Selection, box select, drag |
| **IG.3.2** | Add Selection Highlighting | ⬜ TODO | 0.5d | - | Yellow outline, green fill |
| **IG.3.3** | Implement CreationTool (Entity Placement) | ⬜ TODO | 1.5d | - | TKB browser + placement workflow |
| **IG.3.4** | Implement MeasureTool (Distance) | ⬜ TODO | 0.5d | - | Distance measurement |
| **IG.3.5** | Integration Test: Create Entity | ⬜ TODO | 0.5d | - | Full creation workflow |

**Phase Status:** ⬜ NOT STARTED

---

## Phase IG4: Advanced Features (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IG.4.1** | Implement HistoryRecordingSystem | ⬜ TODO | 1.0d | - | Record entity trails |
| **IG.4.2** | Implement EventToEffectSystem | ⬜ TODO | 1.5d | - | Spawn explosions/tracers from events |
| **IG.4.3** | Add Context Menu System | ⬜ TODO | 1.0d | - | IOS-driven right-click menus |
| **IG.4.4** | Implement EditTool (Vertex Manipulation) | ⬜ TODO | 1.5d | - | Edit overlay geometry |
| **IG.4.5** | Integration Test: Advanced Features | ⬜ TODO | 1.5d | - | Trails, effects, editing |

**Phase Status:** ⬜ NOT STARTED

---

## Phase IG5: UI & Polish (0/4)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IG.5.1** | Create Debug Panel | ⬜ TODO | 1.0d | - | Time control, recording/replay |
| **IG.5.2** | Add Entity Inspector Panel | ⬜ TODO | 0.5d | - | Detailed entity properties |
| **IG.5.3** | Add Mini-IOS Panel | ⬜ TODO | 0.5d | - | Spawner & TKB browser |
| **IG.5.4** | Add Performance Metrics Overlay | ⬜ TODO | 0.5d | - | FPS, entity count, network stats |

**Phase Status:** ⬜ NOT STARTED

---

## IG Progress by Phase

| Phase | Complete | Total | Progress |
|-------|----------|-------|----------|
| **Phase IG1: Core Infrastructure** | 0 | 4 | ⬜⬜⬜⬜ 0% |
| **Phase IG2: Basic Rendering** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase IG3: Interaction Tools** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase IG4: Advanced Features** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase IG5: UI & Polish** | 0 | 4 | ⬜⬜⬜⬜ 0% |
| **TOTAL** | **0** | **23** | **0%** |

---

## IG Critical Path

```
Shared P1-P2 (3d) → IG1 (2d) → IG2 (3d) → IG3 (3d) → IG4 (4d) → IG5 (2d) = 17 days
```

**Parallel Work:**
- IG1-2 can run parallel to Shared P3-P7
- IG3-4 can run parallel to SimHost work

**Timeline:**
- **Sequential (1 developer):** 14 days (~3 weeks)
- **With SimHost running in parallel:** Can start after Shared P1-P2 (3 days into project)

---

# IOS MOCK

**Progress:** 0/18 tasks complete (0%) [Reduced from 22 - DER moved to SHARED P3]

**Dependencies:** Requires Shared Components P2 (Data Model), P3 (DER Toolkit), P4 (Commands)

**Parent Documents**: [DESIGN-IOS.md](./DESIGN-IOS.md) | [TASK-DETAILS-IOS.md](./TASK-DETAILS-IOS.md)

**Note:** DER toolkit is implemented in SHARED Phase P3. IOS creates DDS-to-DER translators only.

---

## Phase IOS-P6: IOS Services (0/3)

**Services for mission editing, context menus, transaction tracking**

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IOS.6.1** | Request Transaction Manager | ⬜ TODO | 0.5d | - | Track request/ack correlation, timeouts |
| **IOS.6.2** | Mission Editor Service | ⬜ TODO | 1.0d | - | Optimistic locking, async commit |
| **IOS.6.3** | Context Menu Logic | ⬜ TODO | 0.5d | - | Strategy-based menu generation |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** SHARED P3 (D ER), SHARED P4 (Commands)

---

## Phase IOS-P7: IOS UI Panels (0/5)

**ImGui panels for IOS dashboard**

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IOS.7.1** | Configuration Panel | ⬜ TODO | 0.5d | - | Control IG via JSON patches |
| **IOS.7.2** | ORBAT Hierarchy Panel | ⬜ TODO | 1.0d | - | Recursive tree view, CommanderId hierarchy |
| **IOS.7.3** | Mission Panel | ⬜ TODO | 1.0d | - | Display/edit missions, Jump/Abort commands |
| **IOS.7.4** | Interaction Panel (Event Log) | ⬜ TODO | 0.5d | - | Network event monitor, RX/TX log |
| **IOS.7.5** | Spawner Panel | ⬜ TODO | 1.0d | - | TKB browser, placement tool activation |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** IOS-P6 (Services)

---

## Phase IOS-P8: IOS Application Shell (0/2)

**Main application, CLI, integration**

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IOS.8.1** | IOS Main Logic | ⬜ TODO | 1.0d | - | Core state, command handlers, update loop |
| **IOS.8.2** | IOS Program & CLI | ⬜ TODO | 1.0d | - | Entry point, CLI args, ImGui setup |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** IOS-P7 (UI Panels)

---

## Phase IOS-P9: Integration Testing (0/4)

**End-to-end testing with IG and SimHost**

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IOS.9.1** | IOS Standalone Test | ⬜ TODO | 0.5d | - | Verify IOS runs without network |
| **IOS.9.2** | IOS + IG Integration Test | ⬜ TODO | 1.0d | - | Config changes, click events, context menus |
| **IOS.9.3** | IOS + SimHost Integration Test | ⬜ TODO | 1.0d | - | Entity creation, mission control |
| **IOS.9.4** | Full Stack Integration Test | ⬜ TODO | 1.5d | - | IOS + IG + SimHost complete workflow |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** IOS-P8, IG Mock, SimHost Mock

---

## Phase IOS-P10: Advanced Features (0/4)

**Optional enhancements**

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **IOS.10.1** | Inspector Panel | ⬜ TODO | 1.0d | - | Raw descriptor viewer, property grid |
| **IOS.10.2** | Diagnostics Panel | ⬜ TODO | 0.5d | - | Network stats, DER metrics |
| **IOS.10.3** | Conflict Detection UI | ⬜ TODO | 0.5d | - | Mission version conflict warnings |
| **IOS.10.4** | Multi-IOS Testing | ⬜ TODO | 1.0d | - | Test 2+ IOS instances, concurrency |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** IOS-P9

---

## IOS Progress by Phase

| Phase | Complete | Total | Progress |
|-------|----------|-------|----------|
| **Phase IOS-P6: IOS Services** | 0 | 3 | ⬜⬜⬜ 0% |
| **Phase IOS-P7: IOS UI Panels** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase IOS-P8: Application Shell** | 0 | 2 | ⬜⬜ 0% |
| **Phase IOS-P9: Integration Testing** | 0 | 4 | ⬜⬜⬜⬜ 0% |
| **Phase IOS-P10: Advanced Features** | 0 | 4 | ⬜⬜⬜⬜ 0% |
| **TOTAL** | **0** | **18** | **0%** |

**Note:** Phase IOS-P10 (Advanced Features) is optional and can be deferred.

**Note:** DER toolkit (4 tasks, 3 days) moved to SHARED Phase P3. IOS depends on SHARED P3 completion.

---

## IOS Critical Path

```
SHARED P3 DER (5d) → IOS-P6 Services (2d) → IOS-P7 UI (4d) → IOS-P8 App (2d) = 13 days (8 days IOS-specific)
```

**Parallel Work:**
- IOS-P6 can start after SHARED P3 (DER) completes
- IOS-P9 (Integration) requires IG Mock and SimHost Mock complete

**Timeline:**
- **Core IOS (no integration):** 8 days (~2 weeks)
- **With integration testing:** 12 days (~2.5 weeks)
- **With advanced features:** 15 days (~3 weeks)

---

## IOS Deliverables Checklist

### Code Artifacts

- [ ] `Bagira.IOS.exe` - IOS Mock application
- [ ] `Bagira.IOS.Services.dll` - Mission editor, context menu services
- [ ] `Bagira.IOS.Panels.dll` - ImGui UI panels
- [ ] `Bagira.IOS.Descriptors.dll` - DDS-to-DER descriptor wrappers

**Note:** `FDP.Toolkit.DER.dll` is in SHARED deliverables (Phase P3)

### Documentation

- [ ] `DESIGN-IOS.md` - IOS architecture and design ✅
- [ ] `TASK-DETAILS-IOS.md` - IOS task breakdown ✅
- [ ] `FDP.Toolkit.DER/README.md` - DER library documentation
- [ ] `INTEGRATION-GUIDE-IOS.md` - IOS integration patterns

### Tests

- [ ] DER unit tests (lifecycle, descriptors, JSON patch)
- [ ] Service unit tests (mission editor, transaction manager)
- [ ] IOS standalone test
- [ ] IOS + IG integration test
- [ ] IOS + SimHost integration test
- [ ] Full stack integration test

---

## IOS Sign-Off

### Phase IOS-P6: IOS Services

- [ ] All services implemented
- [ ] Async mission commit working
- [ ] Context menu logic tested
- [ ] Transaction manager tracks requests

**Sign-Off:** __________________ Date: __________

---

### Phase IOS-P7: IOS UI Panels

- [ ] All 5 panels implemented
- [ ] ORBAT tree renders correctly
- [ ] Mission panel displays tasks
- [ ] Event log working
- [ ] Spawner TKB browser functional

**Sign-Off:** __________________ Date: __________

---

### Phase IOS-P8: Application Shell

- [ ] Bagira.IOS.exe launches
- [ ] CLI arguments parsed
- [ ] ImGui docking working
- [ ] Update loop stable at 60 FPS

**Sign-Off:** __________________ Date: __________

---

### Phase IOS-P9: Integration Testing

- [ ] IOS + IG: Config changes reflected on IG
- [ ] IOS + IG: Map clicks received by IOS
- [ ] IOS + SimHost: Entity creation working
- [ ] Full stack: Complete workflow end-to-end

**Sign-Off:** __________________ Date: __________

---

# RUNNER (AGGREGATED APPLICATION)

**Progress:** 0/18 tasks complete (0%)

---

## Phase R1: Runner Core (0/6)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **R1.1** | Create Bagira.Runner Project | ⬜ TODO | 0.25d | - | Create console app with CLI parser |
| **R1.2** | Implement RunnerConfiguration with CLI Parsing | ⬜ TODO | 0.5d | - | Parse --mode, --domain, --headless, etc. |
| **R1.3** | Implement SubsystemOrchestrator | ⬜ TODO | 1.0d | - | Manage subsystem lifecycle + main loop |
| **R1.4** | Implement ISubsystem Interface | ⬜ TODO | 0.25d | - | Define interface for embeddable subsystems |
| **R1.5** | Implement SubsystemStatusAnnounce DDS Topic | ⬜ TODO | 0.5d | - | Waiting room protocol |
| **R1.6** | Implement WaitingRoomCoordinator | ⬜ TODO | 1.0d | - | Synchronize subsystem startup |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** R1.1

---

## Phase R2: Subsystem Refactoring (0/9)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **R2.1** | Refactor SimHost to SimHostSubsystem Library | ⬜ TODO | 1.0d | - | Extract logic from Program.cs |
| **R2.2** | Create SimHost Standalone Program.cs | ⬜ TODO | 0.25d | - | Thin wrapper using SimHostSubsystem |
| **R2.3** | Test SimHost Embeddability | ⬜ TODO | 0.5d | - | Verify standalone + embedded modes |
| **R2.4** | Refactor IG to IgSubsystem Library | ⬜ TODO | 1.0d | - | Extract logic, handle Raylib window |
| **R2.5** | Create IG Standalone Program.cs | ⬜ TODO | 0.25d | - | Thin wrapper using IgSubsystem |
| **R2.6** | Test IG Embeddability | ⬜ TODO | 0.5d | - | Verify standalone + embedded modes |
| **R2.7** | Refactor IOS to IosSubsystem Library | ⬜ TODO | 1.0d | - | Extract logic, handle ImGui context |
| **R2.8** | Create IOS Standalone Program.cs | ⬜ TODO | 0.25d | - | Thin wrapper using IosSubsystem |
| **R2.9** | Test IOS Embeddability | ⬜ TODO | 0.5d | - | Verify standalone + embedded modes |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** R1.4, SIMHOST/IG/IOS tasks complete

---

## Phase R3: Headless Testing Infrastructure (0/5)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **R3.1** | Implement HeadlessTestExecutor | ⬜ TODO | 1.5d | - | Execute test scripts, collect metrics |
| **R3.2** | Implement Test Script JSON Parser | ⬜ TODO | 0.5d | - | Parse test script format |
| **R3.3** | Implement Test Action Handlers | ⬜ TODO | 2.0d | - | SimHost/IG/IOS test actions |
| **R3.4** | Implement Metrics Collection | ⬜ TODO | 1.0d | - | Performance metrics (FPS, latency, etc.) |
| **R3.5** | Implement Test Report Generator | ⬜ TODO | 0.5d | - | Generate JSON test reports |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** R2.x complete

---

## Phase R4: Integration Testing (0/6)

| ID | Task | Status | Estimated | Actual | Notes |
|----|------|--------|-----------|--------|-------|
| **R4.1** | Test Single Aggregated Mode | ⬜ TODO | 0.5d | - | Run all subsystems in one app |
| **R4.2** | Test Separate Applications Mode | ⬜ TODO | 0.5d | - | Run as separate executables |
| **R4.3** | Test Waiting Room Synchronization | ⬜ TODO | 1.0d | - | Multiple nodes waiting for each other |
| **R4.4** | Create Headless Latency Test | ⬜ TODO | 1.0d | - | Measure entity creation latency |
| **R4.5** | Create Headless Stress Test | ⬜ TODO | 1.0d | - | 100+ entities, measure FPS |
| **R4.6** | Document Runner Usage | ⬜ TODO | 0.5d | - | CLI reference, test script examples |

**Phase Status:** ⬜ NOT STARTED  
**Dependencies:** R3.x complete

---

## Runner Progress by Phase

| Phase | Complete | Total | Progress |
|-------|----------|-------|----------|
| **Phase R1: Runner Core** | 0 | 6 | ⬜⬜⬜⬜⬜⬜ 0% |
| **Phase R2: Subsystem Refactoring** | 0 | 9 | ⬜⬜⬜⬜⬜⬜⬜⬜⬜ 0% |
| **Phase R3: Headless Testing** | 0 | 5 | ⬜⬜⬜⬜⬜ 0% |
| **Phase R4: Integration Testing** | 0 | 6 | ⬜⬜⬜⬜⬜⬜ 0% |
| **TOTAL** | **0** | **26** | **0%** |

---

## Runner Critical Path

```
SIMHOST/IG/IOS complete → R1 Core (3.5d) → R2 Refactoring (5.25d) → R3 Headless (5.5d) → R4 Integration (4.5d) = 18.75 days (~4 weeks)
```

**Parallel Work:**
- R1 Core can start independently (no dependency on mocks)
- R2 Refactoring requires mocks to exist
- R3 & R4 must be sequential

**Timeline:**
- **Core Runner (no headless):** 8.75 days (~2 weeks)
- **With headless testing:** 14.25 days (~3 weeks)
- **Full integration:** 18.75 days (~4 weeks)

---

## Runner Deliverables Checklist

### Code Artifacts

- [ ] `Bagira.Runner.exe` - Aggregated application
- [ ] `Bagira.SimHost.Standalone.exe` - Standalone SimHost
- [ ] `Bagira.IG.Standalone.exe` - Standalone IG
- [ ] `Bagira.IOS.Standalone.exe` - Standalone IOS
- [ ] `Bagira.Runner.Tests.dll` - Unit tests for runner infrastructure

### Documentation

- [ ] `DESIGN-RUNNER.md` - Runner architecture and design ✅
- [ ] `TASK-DETAILS-RUNNER.md` - Runner task breakdown ✅
- [ ] `RUNNER-CLI-REFERENCE.md` - Command-line argument reference
- [ ] `TEST-SCRIPT-FORMAT.md` - Test script JSON format specification
- [ ] `INTEGRATION-GUIDE-RUNNER.md` - Deployment scenarios

### Tests

- [ ] Runner configuration unit tests
- [ ] Subsystem orchestrator tests
- [ ] Waiting room coordinator tests
- [ ] Test executor integration tests
- [ ] Headless latency test (script)
- [ ] Headless stress test (script)

---

## Runner Sign-Off

### Phase R1: Runner Core

- [ ] Bagira.Runner.exe launches
- [ ] CLI arguments parsed correctly
- [ ] SubsystemOrchestrator lifecycle works
- [ ] Waiting room synchronization tested

**Sign-Off:** __________________ Date: __________

---

### Phase R2: Subsystem Refactoring

- [ ] All three subsystems refactored to ISubsystem
- [ ] Standalone executables work
- [ ] Embedded mode works
- [ ] No functionality lost from refactoring

**Sign-Off:** __________________ Date: __________

---

### Phase R3: Headless Testing Infrastructure

- [ ] Test script parser works
- [ ] All action handlers implemented
- [ ] Metrics collection working
- [ ] Test reports generated

**Sign-Off:** __________________ Date: __________

---

### Phase R4: Integration Testing

- [ ] All deployment modes tested
- [ ] Latency test passes (<100ms p95)
- [ ] Stress test passes (>30 FPS with 100+ entities)
- [ ] Documentation complete

**Sign-Off:** __________________ Date: __________

---

# PROJECT SUMMARY

## Overall Progress

| Component | Tasks Complete | Total Tasks | Progress |
|-----------|---------------|-------------|----------|
| **Shared Components** | 0 | 40 | 0% |
| **SimHost Mock** | 0 | 29 | 0% |
| **IG Mock** | 0 | 25 | 0% |
| **IOS Mock** | 0 | 19 | 0% |
| **Runner (Aggregated App)** | 0 | 27 | 0% |
| **TOTAL** | **0** | **140** | **0%** |

**Note:** +5 tasks added to address critical edge cases identified in design review. See [EDGE-CASES-AND-MITIGATIONS.md](./EDGE-CASES-AND-MITIGATIONS.md) for details.

## Timeline Estimate

**Sequential .5 days (+0.5d for edge case mitigations)
- IG: 14.75 days (+0.75d for edge case mitigations)
- IOS: 12.5 days (+0.5d for edge case mitigations)
- Runner: 19.25 days (+0.5d for edge case mitigations)
- **Total:** ~90 days (~4.5 months)

**Parallel (4 developers):**
- Shared: 3.5 weeks (parallel work on P3/P4/P5)
- SimHost: 2.5 weeks (after Shared P1-P6) | **Dev 1**
- IG: 2 weeks (can start after Shared P1-P2) | **Dev 2**
- IOS: 2.5 weeks (depends on SHARED P3 for DER) | **Dev 3**
- Runner: 4 weeks (after mocks complete) | **Dev 1 or Dev 4**
- **Total:** ~10-12 weeks with 4 developers

**Critical Insight:**
- Runner requires all three mocks (SimHost, IG, IOS) to be refactorable
- IOS now uses DER from SHARED P3 (no duplication)
- +5 tasks added (2.25d) for critical edge case mitigations
- Total project: 140 tasks (was 135 before edge case fixes
- IOS tasks reduced from 22 to 18 (4 fewer DER-related tasks)
- Total project: 135 tasks (was 109 before Runner added)

## Critical Blockers

| Blocker | Component | Impact | Status |
|---------|-----------|--------|--------|
| FDP.sln build failures | Shared | HIGH - Blocks all work | ⬜ OPEN |
| Shared P1-P6 incomplete | SimHost | HIGH - Cannot start S2-S4 | ⬜ OPEN |
| Shared P1-P2 incomplete | IOS/IG | HIGH - No data model | ⬜ OPEN |
| Mocks incomplete | Runner | HIGH - Cannot refactor subsystems | ⬜ OPEN |
| Edge case mitigations not reviewed | All | CRITICAL - Integration hazards | ⚠️ DOCUMENTED |

**⚠️ NEW**: Read [EDGE-CASES-AND-MITIGATIONS.md](./EDGE-CASES-AND-MITIGATIONS.md) before starting implementation!

---

## Navigation

- **[⬆ Back to DESIGN-SHARED.md](./DESIGN-SHARED.md)**
- **[⬆ Back to TASK-DETAILS-SHARED.md](./TASK-DETAILS-SHARED.md)**
- **[⬆ Back to DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md)**
- **[⬆ Back to TASK-DETAILS-SIMHOST.md](./TASK-DETAILS-SIMHOST.md)**
- **[⬆ Back to DESIGN-IG.md](./DESIGN-IG.md)**
- **[⬆ Back to TASK-DETAILS-IG.md](./TASK-DETAILS-IG.md)**
- **[⬆ Back to DESIGN-IOS.md](./DESIGN-IOS.md)**
- **[⬆ Back to TASK-DETAILS-IOS.md](./TASK-DETAILS-IOS.md)**
- **[⬆ Back to DESIGN-RUNNER.md](./DESIGN-RUNNER.md)**
- **[⬆ Back to TASK-DETAILS-RUNNER.md](./TASK-DETAILS-RUNNER.md)**
- **[➜ PHASE-MASTER.md](./PHASE-MASTER.md)** (if exists)
