# IG-BATCH-02: Network Integration & Stub Rendering

**Batch Number:** IG-BATCH-02  
**Tasks:** IG.1.3, IG.1.3b, IG.1.4  
**Phase:** IG1 (Core Infrastructure)  
**Estimated Effort:** ~14 hours (1.75 days)  
**Priority:** HIGH  
**Dependencies:** IG-BATCH-01 completed

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the second batch. We are integrating the FDP Cyclone network logic and establishing the `NetworkSpawningSystem` for real-time entity mapping. Finally, you will connect these entities to the first rendering layer as colored circles (Stub Visualizer).

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - See IG.1.3, IG.1.3b, IG.1.4 details.
3. **Previous Review:** `.dev-workstream/reviews/IG-BATCH-01-REVIEW.md` - See test quality approval.
4. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`

### Source Code Location
- **Primary Work Area:** `Bagira.IG/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 0:** Implement Corrective Fixes → **ALL tests pass** ✅
2. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
3. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
4. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

With the window and camera running, IG needs data. You will configure the Cyclone DDS network module exactly like `NetworkDemo`, feeding `EntityMaster` structures into the ECS. Then you will establish the `StubVisualizerAdapter` mapping those spawned entities to the canvas.

**Related Tasks:**
- [IG.1.3] Integrate NetworkDemo Network Module
- [IG.1.3b] Register NetworkSpawningSystem
- [IG.1.4] Add EntityRenderLayer with Stub Visualizer

---

## 🎯 Batch Objectives
- Connect Bagira.IG to the DDS environment on instance 300.
- Handle networking synchronization through robust ECS system translations.
- Render those new entity coordinates into `MapCanvas` space dynamically.

---

## ✅ Tasks

### Task 0: Corrective Issue Fixes (Docs)

**File:** `README.md` (or workspace equivalent docs) and `docs/design/TASK-DETAILS-IG.md`

**Description:** Solve items identified as IG-DEBT-001 and IG-DEBT-002:
- Update main readme/build scripts notes outlining that native execution `ExtDeps\FastCycloneDds\build\native-win.ps1` must be triggered immediately upon initial project cloning.
- Fix the package ambiguity recorded in `TASK-DETAILS-IG.md` replacing `rlImGui` text with `rlImgui-cs` version `3.2.0`.

---

### Task 1: IG.1.3 Integrate NetworkDemo Network Module

**File:** `Bagira.IG/Program.cs` / `Translators/EntityMasterTranslator.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.1.3)

**Description:** Set up DDS integration using `CycloneNetworkModule` ensuring `SlaveTimeController` and proper bridge translators are populated.
**Requirements:**
- Implement `EntityMasterTranslator`, mapping updates appropriately using the snippet from the design document.
- Register `GeoSpatialTranslator`, `EntityInfoTranslator`, and crucially `AutoCycloneTranslator<TimePulseDescriptor>`.
- Instantiate the `CycloneNetworkModule` and inject translators.
- *CRITICAL ARCHITECTURE REMINDER:* Do NOT create custom network modules. Register translators to the explicit component wrapper.

**Tests Required:**
- ✅ Verify that translating a mock `EntityMaster` DDS payload emits a `SpawnEntityCommand` inside an integration or unit test suite.
- ✅ Ensure Time pulse translator bridges mapping correctly without skipping execution.

---

### Task 2: IG.1.3b Register NetworkSpawningSystem via SpawningModule

**File:** `Bagira.IG/Modules/SpawningModule.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.1.3b)

**Description:** Register `NetworkSpawningSystem` inside IG.
**Requirements:**
- Create the custom wrapper `SpawningModule` allocating node ID 300.
- Execute registration pre-network cycle within `Program.cs`.

**Tests Required:**
- ✅ Integration Test: End-to-end execution where a mock `SpawnEntityCommand` pushes onto ECS logic effectively manifesting as an entity.

---

### Task 3: IG.1.4 Add EntityRenderLayer with Stub Visualizer

**File:** `Bagira.IG/Adapters/StubVisualizerAdapter.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.1.4)

**Description:** Translate ECS coordinates into Raylib visual elements.
**Requirements:**
- Build `StubVisualizerAdapter` checking `SimTransform` presence and placing 10-pixel red circles at entity locations.
- Label the components natively if they own a `NetworkIdentity`.
- Ensure querying operations utilize `With<EntityMasterComponent, SimTransform>`.
- **Note:** Do not redefine `SimTransform`! Utilize the native implementation imported via `Fdp.Kernel`.

**Tests Required:**
- ✅ Validated boundary checks confirming coordinates align logic properly around WorldToScreen matrices constraints.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- Do NOT settle for test names that misalign with logical validation boundaries (e.g. checking if a struct instantiated vs. logic constraints working).
- Integration test for Task IG.1.4 should properly confirm entities manifest and simulate properly onto visual outputs without failing on memory loops.
- Use explicit values when bounding camera variables/render items in unit tests.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights in your report:

## Developer Insights

**Q1:** What issues did you encounter when attempting to wrap the FDP Network systems onto node 300? Was there any structural bleed when allocating external libraries?

**Q2:** Did you spot any weak points inside FDP toolkits when constructing the Translator elements?

**Q3:** What edge cases did you discover during the Stub Visualizer `PickEntity` checks? 

**Q4:** Did you notice any performance drift translating `SimTransform` points over 100 entity counts?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 0 completed (Docs updated covering native build limits and packages).
- [ ] Task IG.1.3 completed (Module translating valid mappings into DDS hooks).
- [ ] Task IG.1.3b completed (Spawning architecture embedded in execution loops).
- [ ] Task IG.1.4 completed (RayLib actively translating entities into circles with UI elements bounding boxes).
- [ ] All code conforms to `CODE-STANDARDS.md`.
- [ ] Tests genuinely evaluate functional limits rather than raw instantiation validations.
- [ ] Developer Report submitted.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md`
- **Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Tests Quality Validation:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md`
