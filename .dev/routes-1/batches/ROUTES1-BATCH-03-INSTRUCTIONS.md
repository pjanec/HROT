# ROUTES1-BATCH-03: Rendering, Editing & Integration Finishing

**Batch Number:** ROUTES1-BATCH-03
**Tasks:** ROUTES1-T010, ROUTES1-T011, ROUTES1-T012, ROUTES1-T013, ROUTES1-T014, ROUTES1-T015
**Phase:** Rendering, Editing, AI Context, Deprecation
**Estimated Effort:** ~14 hours
**Priority:** HIGH
**Dependencies:** ROUTES1-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to ROUTES1-BATCH-03. This is the final batch of the ROUTES1 foundational work. In this batch, you will bring routes to life visibly on the canvas, allow operators to modify waypoints in real-time visually through the ImGui editor panel, inject custom soft-advice onto vehicles relying on `BrainBlackboard` integrations, and officially deprecate the legacy waypoint systems once and for all. 

You must address the P2 and P3 technical debts identified in previous batches *first*.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/routes-1/ROUTES1-TASK-TRACKER.md`
3. **Task Details:** `docs/routes-1/ROUTES1-TASK-DETAIL.md` (See Phase 6, 7, 8, 9)
4. **Design Document:** `docs/routes-1/ROUTES1-DESIGN.md`
5. **Previous Review:** `.dev-workstream/reviews/ROUTES1-BATCH-02-REVIEW.md`

### Source Code Location
- **Hrot.IG:** For Rendering Layers, `RouteEditTool`, and `WaypointEditorPanel`.
- **Hrot.SimHost:** For AI Blackboard routing rules and legacy script purges.
- **Hrot.Map.Common:** For ingestion/egress allocations refactors.
- **Test Projects:** `Hrot.IG.Tests/`, `Hrot.SimHost.Tests/`, `Hrot.Map.Common.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ROUTES1-BATCH-03-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **CT-0:** Implement → Write tests → **ALL tests pass** ✅
2. **CT-1:** Implement → Write tests → **ALL tests pass** ✅
3. **CT-2:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 1 (T010, T011):** Implement → Write tests → **ALL tests pass** ✅
5. **Task 2 (T012, T013):** Implement → Write tests → **ALL tests pass** ✅
6. **Task 3 (T014):** Implement → Write tests → **ALL tests pass** ✅
7. **Task 4 (T015):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Corrective Tasks (P2 / P3 Debt)

*These must be implemented and verified before proceeding to the new feature work.*

### Corrective Task 0 (CT-0): Ingress Registration & Allocations
**Description:** Repeated list allocations and Dictionary tracking currently cause GC pressure during DDS ingest processes. `MapRouteIngressTranslator` scans `_pendingRoutes` repeatedly linearly, and `BuildRoutePlan` initializes a new List unnecessarily.
**Requirements:** Refactor `MapRouteIngressTranslator`. Implement specific `NetworkEntityMap` registration callbacks for evaluating the retry queue gracefully, rather than polling linearly across elements on every system tick. Consolidate list allocations in `BuildRoutePlan` via object pooling/resizing.

### Corrective Task 1 (CT-1): Authoring Geo-transform Handlers
**Description:** `ActivateRouteAuthoringTool` may null-check crash or inappropriately route to (X,Y,0) local Cartesian if `_geoTransform` is missing.
**Requirements:** Catch instances where `_geoTransform` is omitted, returning a clean runtime error notification rather than passing bad layout offsets over the DDS egress layout natively. Add defensive check to IG layer.

### Corrective Task 2 (CT-2): Cache `SelectionState` Queries
**Description:** Inside `OnCanvasWorldClick`, `World.Query().With<SelectionState>().Build()` is continuously querying allocations. 
**Requirements:** Statically cache the selection state query locally or move it to a system-wide cached query instance referenced locally inside the IG controller scope.

---

## 🎯 Batch Objectives
- Render route layers explicitly in the IG using Map elements.
- Form the Imgui logic updating waypoint definitions in real time conditionally checking tool handlers correctly using `RouteEditTool`.
- Plumb `BrainBlackboard` values from `ExtensionJson` AI descriptors natively over physics behaviors statically.
- Scrub legacy systems and deprecate `SimHostScenarioManager._waypointQueues`.

---

## ✅ Feature Tasks

### Task 1: Route Rendering (ROUTES1-T010 & ROUTES1-T011)
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t010--routerenderlayer) and [T011](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t011--simhosttrajectorylay-extension)
**Description:** Introduce `RouteRenderLayer` logic bridging generic waypoints directly via the `MapCanvas`. Augment the existing `SimHostTrajectoryLayer` handling rendering the associated `PersonalRouteRef` components as an overlay natively.

### Task 2: Editing & UX Panels (ROUTES1-T012 & ROUTES1-T013)
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t012--routeediittool) and [T013](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t013--waypointeditorpanel)
**Description:** Create `RouteEditTool` facilitating click-drag selections adjusting vertices inline across nodes correctly. Design `WaypointEditorPanel` exposing data hooks reading Speed parameters explicitly across selection indices safely.

### Task 3: AI Soft Advice Logic (ROUTES1-T014)
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t014--routecontextsystem)
**Description:** Provide `RouteContextSystem` operating selectively parsing Route parameters and modifying targeted byte-offsets mapping to Blackboard memory slots safely via JSON extraction hooks. 

### Task 4: Legacy Waypoint Removal (ROUTES1-T015)
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t015--remove-legacy-waypoint-queue)
**Description:** Carefully scrub `_waypointQueues` logic and invocations entirely. Clean up obsolete hooks now supplanted by your Phase 5 architecture patterns natively. Note: Tests must stay active or undergo corresponding refactoring.

---

## 🧪 Testing Requirements
- **Quantity minimum:** 15+ tests for CT's and all rendering/editing tasks.
- **Style:** Render lists and Canvas draws should ideally mock native contexts and count exact loop/line numbers generated per layout context natively. Verify memory offsets natively for the blackboard values. Verify allocation reductions in Translator natively checking memory snapshots/alloc assertions natively if possible. 

---

## 📊 Report Requirements
1. **Insights Required:** Describe your strategies used mitigating the GC allocations requested in CT-0 natively.
2. **Missing Coverage:** Were any integration edge cases missed or uncovered operating the edit tools while elements are rendering conditionally natively?
3. **Deprecation Hazards:** Talk about what specifically needed sweeping or patching when eliminating the legacy subsystem in T015 safely. 
4. **General Optimization:** What potential design changes would further strengthen these integrations upstream natively? 

---

## 🎯 Success Criteria
- [ ] CT-0, CT-1, CT-2 complete & verified.
- [ ] ROUTES1-T010 through T015 completed.
- [ ] All tests passing.
- [ ] Meaningful developer report returned discussing insights.
