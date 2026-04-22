# ROUTES1-BATCH-02: Authoring & Trajectories

**Batch Number:** ROUTES1-BATCH-02
**Tasks:** ROUTES1-T006, ROUTES1-T007, ROUTES1-T008, ROUTES1-T009
**Phase:** Trajectory Pool Integration & Authoring Flows
**Estimated Effort:** ~13 hours
**Priority:** HIGH
**Dependencies:** ROUTES1-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to ROUTES1-BATCH-02. In this batch, we integrate the previously implemented `RoutePlan` component into the simulation's Trajectory Pool logic, allowing vehicles to traverse the routes. Additionally, you will implement the Shared Route Authoring flow from the IG UI, and the Personal Route Authoring flow triggered by Shift+Right-Click.  

We also have a pair of Corrective Tasks based on technical debt from Batch 1. **Complete Corrective Tasks First.**

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/routes-1/ROUTES1-TASK-TRACKER.md`
3. **Task Details:** `docs/routes-1/ROUTES1-TASK-DETAIL.md` (See Phase 3, 4, 5)
4. **Design Document:** `docs/routes-1/ROUTES1-DESIGN.md`
5. **Previous Review:** `.dev-workstream/reviews/ROUTES1-BATCH-01-REVIEW.md`

### Source Code Location
- **Hrot.SimHost:** ECS systems and trajectory pool integration.
- **Hrot.IG:** Map command hooks and `PointSequenceTool`.
- **Test Projects:** `Hrot.SimHost.Tests/`, `Hrot.IG.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ROUTES1-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/ROUTES1-BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **CT-0:** Implement → Write tests → **ALL tests pass** ✅
2. **CT-1:** Implement → Write tests → **ALL tests pass** ✅
3. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 2:** Implement → Write tests → **ALL tests pass** ✅
5. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
6. **Task 4:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Corrective Tasks (P1 / P2 Debt)

*These must be implemented and verified before proceeding to the feature work.*

### Corrective Task 0 (CT-0): Guard `EDescriptorType` Enum
**Description:** The developer report for BATCH-01 noted that the `EDescriptorType` enum relies on implicit ordinals, creating a risk of silent data corruption if properties are inserted upstream.
**Requirements:** Locate the `EDescriptorType` declaration, find `dtMapRoute`, and explicitly assign its int/long value (e.g., `dtMapRoute = 5` or whatever it evaluates to currently), so that its network identifier is preserved permanently. Do this for all other existing descriptors if they are implicitly defined.

### Corrective Task 1 (CT-1): RoutePlan Mutability Enforcement
**Description:** Maintainers must manually increment `RoutePlan.Version` to trigger sync logic. 
**Requirements:** Refactor `RoutePlan` so that `Waypoints` and `Version` are updated intrinsically (e.g. by making `Waypoints` private or readonly, exposing a `Mutate(Action<List<RouteWaypoint>>)` method which automatically increments `Version`, or equivalent API wrappers). Update tests and translators using it.

---

## 🎯 Batch Objectives
- Integrate routes into kinematic trajectories using `TrajectoryPoolManager`.
- Create new shared routes using IG's `CMD_START_AUTHORING`.
- Implement `PersonalRouteAuthoringSystem` for bespoke per-entity routes.
- Implement IG front-end logic emitting the vehicle shift-right-click authoring bindings.

---

## ✅ Feature Tasks

### Task 1: RouteTrajectorySyncSystem (ROUTES1-T006)

**Files:** `Hrot.SimHost/Systems/Routing/RouteTrajectorySyncSystem.cs`
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t006--routetrajectorysyncystem)

**Description:** Register and unregister trajectory instances derived from `RoutePlan` coordinates, bridging the declarative ECS data with the deterministic Trajectory Pool arrays.

**Tests Required:**
- Validation trajectory registration matches `RoutePlan.Version` caching.
- Destructor patterns invoking `RemoveTrajectory` properly.

### Task 2: Shared Route Authoring (ROUTES1-T007)

**Files:** `Hrot.IG/Controllers/MapCommandController.cs`, `Hrot.IG/Tools/*`
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t007--shared-route-authoring-via-cmd_start_authoring)

**Description:** Wire IG `CMD_START_AUTHORING` parameters to the `PointSequenceTool` driving native entity publishing events via the DDS network.

**Tests Required:**
- Confirm the tool is successfully injected onto the stack.
- Simulated termination correctly constructs the DDS schema containing descriptors matching native values.

### Task 3: PersonalRouteAuthoringSystem (ROUTES1-T008)

**Files:** `Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs`
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t008--personalrouteauthoringsystem)

**Description:** Build the ingestion mechanism consuming `CmdAppendPersonalWaypoint` payloads to drive the construction or mutating of `PersonalRouteRef`-guided child route instances attached to individual tracked vehicles. Include the `CmdFollowTrajectory` invocation step appropriately.

**Tests Required:**
- Validating entity buffer generation properly seeds components.
- Subsequent iterations safely stack routes natively rather than appending extraneous entities.

### Task 4: IG Input Wiring for Shift+Right-Click (ROUTES1-T009)

**Files:** `Hrot.IG/Application/IgApplication.cs` (or relevant input tool wrapper).
**Task Definition:** See [ROUTES1-TASK-DETAIL.md](../../docs/routes-1/ROUTES1-TASK-DETAIL.md#routes1-t009--ig-input-wiring-for-shiftright-click)

**Description:** Hook `Shift + Right Click` in the active IG viewport configuration to iterate over selected vehicles mapping them to the newly implemented `CmdAppendPersonalWaypoint` channels natively. 

**Tests Required:**
- Test multiple object payload extraction. 
- Validation ensuring single unmodified destination behaviors are excluded correctly.

---

## 🧪 Testing Requirements
- **Quantity minimum:** 20+ tests combining CT-0/CT-1 and the 4 tasks.
- **Style:** Validate actual logic. NO `Assert.Contains()` unless validating syntax templates. Check precise values produced natively. Test lifecycle hooks. 
- **Quality:** Focus on integration edges (e.g. what happens if a vehicle is dead when Shift+Right Click pushes a waypoint?).

---

## 📊 Report Requirements
1. **Issues Encountered:** Did you find issues migrating `RoutePlan` mutations to explicit calls during CT-1? 
2. **Observation:** What challenges occurred coordinating `TrajectorySyncSystem` phases natively? 
3. **Defensive Choices:** What design safety guards did you place inside the Shift+Right click hooks?
4. **Performance:** Did you observe any bottlenecks regarding mapping multi-entity selections?
5. **Edge Cases:** What edge cases popped up simulating trajectory buffer lifecycle deletions?

---

## 🎯 Success Criteria
- [ ] CT-0 and CT-1 complete & verified.
- [ ] ROUTES1-T006 to T009 completed.
- [ ] All tests passing.
- [ ] Meaningful developer report returned discussing insights.
