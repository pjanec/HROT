# BD1-BATCH-03: Miscellaneous Simulator & Inspector Fixes

**Batch Number:** BD1-BATCH-03  
**Tasks:** BD1-P3T1, BD1-P3T2, BD1-P4T1, BD1-P5T1, BD1-P6T1, BD1-P7T1  
**Phase:** Phases 3 through 7  
**Estimated Effort:** ~8 hours  
**Priority:** MEDIUM  
**Dependencies:** BD1-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch cleans up a set of targeted bug fixes and developer-experience upgrades. You will restore collision geometry to local-spawn entities, fix a camera-offset offset display bug in the frontend, enrich the DDS payload inspection types, create a caching change detector in the Entity Inspector, and close a delegate allocation leak.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/brain-death/BD1-TASK-TRACKER.md`
3. **Task Definitions:** `docs/brain-death/BD1-TASK-DETAIL.md` (# Phase 3 through 7)
4. **Design Document:** `docs/brain-death/BD1-DESIGN.md` (Lines 228-367)

### Source Code Location
- **Primary Work Areas:**
  - `Hrot.Map.Definitions/`
  - `Hrot.SimHost/`
  - `Hrot.Map.Common/`
  - `Hrot.NED/`
  - `FDP/Toolkits/FDP.Toolkit.ImGui/`
- **Test Projects:** Respective `.Tests` packages matching paths.

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BD1-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BD1-BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅  
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅  
5. **Task 5:** Implement → Write tests → **ALL tests pass** ✅  
6. **Task 6:** Implement → Write tests → **ALL tests pass** ✅  

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## 🎯 Batch Objectives
- Restore vehicle-to-vehicle collision avoidance by adding the `PhysicsCollider`.
- Center entities cleanly in the SimHost Visualization map.
- Make DIS-Type fields legible to DDS sniffers.
- Enable dirty-byte highlights inside the ImGui ECS Entity Inspector.
- Zero-alloc `CreateEntityRequestSystem` ingress routing.

---

## ✅ Tasks

### Task 1: BdcTkbBuilder — Add PhysicsCollider to WithPhysics (BD1-P3T1)
**File:** `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p3t1-bdctkbbuilder--add-physicscollider-to-withphysics)
**Description:** Add `PhysicsCollider` at the end of `WithPhysics` using `Math.Max(Length, Width)/2f`.
**Tests Required:** See Task Definitions.

### Task 2: SimHostScenarioManager — Add PhysicsCollider to SpawnEntityLocal (BD1-P3T2)
**File:** `Hrot.SimHost/UI/SimHostScenarioManager.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p3t2-simhostscenariomanager--add-physicscollider-to-spawnentitylocal)
**Description:** Add `PhysicsCollider` at the end of `SpawnEntityLocal` similarly to Task 1.
**Tests Required:** See Task Definitions.

### Task 3: SimHostVisualization — Set Camera Offset on Initialize (BD1-P4T1)
**File:** `Hrot.SimHost/SimHostVisualization.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p4t1-simhostvisualization--set-camera-offset-on-initialize)
**Description:** Adjust `_map.Camera.Offset`.
**Tests Required:** See Task Definitions.

### Task 4: EntityMaster — Replace Plain long DisType with DisTypeStruct (BD1-P5T1)
**Files:** `Hrot.NED/GenericDescriptors.cs` and Egress/Ingress Translators
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p5t1-entitymaster--replace-plain-long-distype-with-distypestruct)
**Description:** Overhaul wire structs for DIS decomposition.
**Tests Required:** See Task Definitions.

### Task 5: ComponentReflector — Byte-Cache Change Detection (BD1-P6T1)
**File:** `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p6t1-componentreflector--byte-cache-change-detection)
**Description:** Byte-array diff caching via pinned memory to mark unmanaged mutated structs yellow via `ImGuiCol`.
**Tests Required:** See Task Definitions.

### Task 6: CreateEntityRequestSystem — Cache ProcessRequest Delegate (BD1-P7T1)
**File:** `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p7t1-createentityrequestsystem--cache-processrequest-delegate)
**Description:** Resolve lambda allocation leak.
**Tests Required:** See Task Definitions.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **REQUIRED:** Tests must mock external IO properly if running byte layout comparisons or DDS routing. Ensure robust logic layout without flaky dependencies.
- **REQUIRED:** You must run tests locally. Verify tests can detect broken behavior.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them.
- **REQUIRED:** Document design decisions YOU made beyond the spec.

---

## 📊 Report Requirements

In your `.dev-workstream/reports/BD1-BATCH-03-REPORT.md`, answer:
**Developer Insights**
**Q1:** What issues did you encounter regarding DDS structural changes or byte mapping logic?
**Q2:** Highlight the efficiency considerations made around caching bytes across inspection queries in ImGui? Any observable UI lag?

---

## 🎯 Success Criteria
- [ ] Task BD1-P3T1 completed
- [ ] Task BD1-P3T2 completed
- [ ] Task BD1-P4T1 completed
- [ ] Task BD1-P5T1 completed
- [ ] Task BD1-P6T1 completed
- [ ] Task BD1-P7T1 completed
- [ ] ALL tests passing.
- [ ] Report submitted.
