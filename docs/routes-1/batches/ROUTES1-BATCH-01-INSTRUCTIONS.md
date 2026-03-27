# ROUTES1-BATCH-01: Foundation & DDS Replication

**Batch Number:** ROUTES1-BATCH-01
**Tasks:** ROUTES1-T001, ROUTES1-T002, ROUTES1-T003, ROUTES1-T004, ROUTES1-T005
**Phase:** Core ECS Data Layer & DDS Replication
**Estimated Effort:** ~13 hours
**Priority:** HIGH
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to ROUTES1-BATCH-01. This batch lays the foundation for the new Route capabilities. You will introduce the core ECS data models and ensure that Route mutations correctly replicate over DDS between SimHost and IG.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Tracker:** `docs/routes-1/ROUTES1-TASK-TRACKER.md` - Overall system progress
3. **Task Details:** `docs/routes-1/ROUTES1-TASK-DETAIL.md` - See T001 to T005 implementation specifications
4. **Design Document:** `docs/routes-1/ROUTES1-DESIGN.md` - Technical design context (Chapters 4, 6, 14, etc.)

### Source Code Location
- **Bagira.Map.Common:** Contains components, structs, translators (`src/` or repository root directories as applicable)
- **Bagira.Map.Definitions:** For TKB wiring (T003)
- **Bagira.SimHost & Bagira.IG:** For ECS registrations and bootstraps
- **Test Projects:** `Bagira.Map.Common.Tests/`, `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ROUTES1-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/ROUTES1-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅
5. **Task 5:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context
This batch implements Phase 1 and Phase 2 from the ROUTES1 design. It introduces the `RoutePlan` component which drives all behavior, the supporting blittable structs needed for event marshaling and fast queries, and the integration of routing data into the TKB blueprints. It also hooks routing data into the network via DDS translators.

---

## 🎯 Batch Objectives
- Implement `RoutePlan` and `RouteWaypoint` data structures.
- Implement memory-safe blittable structures `PersonalRouteRef`, `RouteTrajectoryCache`, `CmdAppendPersonalWaypoint`.
- Extend the `TacGraphic_Route` TKB Blueprint.
- Configure DDS replication egress (SimHost to IG or Vice Versa).
- Configure DDS replication ingress to ingest map samples into ECS entities.

---

## ✅ Tasks

### Task 1: RoutePlan Managed Component (ROUTES1-T001)

**Files:** `Bagira.Map.Common/RoutePlan.cs` (or suitable file), Component Registrations in `Bagira.SimHost` and `Bagira.IG`
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t001--routeplan-managed-component)

**Description:** Implement the `RoutePlan` managed component and `RouteWaypoint` struct. Please refer directly to the task detail document for the exact structural requirements rather than duplicating them here.

**Design Reference:** [ROUTES1-DESIGN.md, §4 Core Data Model](../../docs/routes-1/ROUTES1-DESIGN.md#4-core-data-model--routeplan-ecs-component)

**Tests Required:**
- Component instantiation and mutation checking version increments.
- Value-type structure validation.
- Serialisation/equality tests.

### Task 2: Supporting Components and Events (ROUTES1-T002)

**Files:** `Bagira.Map.Common/PersonalRouteRef.cs`, `Bagira.Map.Common/RouteTrajectoryCache.cs`, event layer struct definitions.
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t002--supporting-components-and-events)

**Description:** Add blittable structs to handle references, caching and events. Ensure they are correctly attributed with `[ComponentId]`.

**Tests Required:**
- Blittable validation tests (`IsBlittable<T>()`).
- Buffer packing assertions and correctness of default values.

### Task 3: TKB Blueprint for TacGraphic_Route (ROUTES1-T003)

**Files:** `Bagira.Map.Definitions/TkbEntityTypes.cs` and Entity Bootstrappers.
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t003--tkb-blueprint-for-tacgraphic_route)

**Description:** Expand the TKB definition data wiring to ensure route entities are crafted natively without unused kinematic states.

**Tests Required:**
- `SpawnEntityCommand` validation against created entity components.
- Confirm `road_graphs` TKB layer predicate correctly filters `TkbType == 8802`.

### Task 4: MapRouteEgressTranslator (ROUTES1-T004)

**Files:** `Bagira.Map.Common/MapRouteEgressTranslator.cs`
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t004--maprouteegresstranslator)

**Description:** Publish mutable ECS state (waypoints, loop status) effectively onto the DDS network tracking mutations correctly using `SmartEgressUtil`.

**Tests Required:**
- MapRoute translation count assertions based on waypoint data.
- GeoPosition precision assertions round trip checking 1 mm tolerance.
- Dirty flag functionality.

### Task 5: MapRouteIngressTranslator (ROUTES1-T005)

**Files:** `Bagira.Map.Common/MapRouteIngressTranslator.cs`
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t005--maprouteingresstranslator)

**Description:** Accept external DDS route messages to apply against internal ECS component. Handle deferred/queued entities natively using retry mechanisms.

**Tests Required:**
- ECS component ingestion correctness mapping tests.
- Retry queue test for un-spawned entities natively.

---

## 🧪 Testing Requirements
- **Quantity minimum:** Minimum test scenarios specified in `ROUTES1-TASK-DETAIL.md` must be completed (likely 20+ tests combined).
- **Style:** Avoid string-matching behaviour assertions ("does code have string X"). Verify exact memory layouts, buffer sizes, and correctly instantiated ECS values validating ACTUAL behavior.
- **Coverage:** Cover all boundary and null-handling cases. Ensure precision checks match specific numeric criteria constraints described in Task Detail.

---

## 📊 Report Requirements

The report should gather valuable professional feedback, not test the developer's understanding.

**Developer Insights Required:**
- **Q1:** What issues did you encounter during implementation, particularly with blittable memory configurations and DDS network propagation? How did you resolve them?
- **Q2:** Did you spot any weak points in the existing Map Layer mapping architecture or ECS definitions? What would you improve?
- **Q3:** What design decisions did you make regarding spatial anchoring, conversion handling or component configuration?
- **Q4:** What edge cases did you discover mapping local floating point spaces into DDS packets that weren't mentioned in the spec?
- **Q5:** Are there any performance concerns or optimization opportunities you noticed while reading the ECS components and iterating over Lists?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] ROUTES1-T001 completed
- [ ] ROUTES1-T002 completed
- [ ] ROUTES1-T003 completed
- [ ] ROUTES1-T004 completed
- [ ] ROUTES1-T005 completed
- [ ] All tests passing
- [ ] Report submitted answering all 5 insights questions

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value" or use `Assert.Contains` for logic checks.
- **REQUIRED:** Tests that verify actual behavior, memory correctness, precision limits, and edge cases.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them.
- **REQUIRED:** Document design decisions YOU made beyond the spec.
- **REQUIRED:** Share insights on code quality and improvement opportunities.

---

## 📚 Reference Materials
- **Task Defs:** `docs/routes-1/ROUTES1-TASK-DETAIL.md`
- **Design:** `docs/routes-1/ROUTES1-DESIGN.md` (Chapters 4, 6, 14)
- **Tracker:** `docs/routes-1/ROUTES1-TASK-TRACKER.md`
