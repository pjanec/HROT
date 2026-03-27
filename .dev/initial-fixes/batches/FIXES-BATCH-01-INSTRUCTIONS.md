# FIXES-BATCH-01: Initial Architecture and UI Fixes

**Batch Number:** FIXES-BATCH-01
**Tasks:** TASK-IF001, TASK-IF002, TASK-IF003, TASK-IF004, TASK-IF005, TASK-IF006, TASK-IF007, TASK-IF008
**Phase:** Initial Fixes
**Estimated Effort:** 4-8 hours
**Priority:** HIGH
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch covers all critical architectural deviations and UI wiring issues in the SimHost, IG, and IOS applications. These are mostly small, targeted fixes, but correctness and proper test coverage are vital to ensuring architecture compliance.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs/initial-fixes/TASK-DETAIL.md` - See TASK-IF001 to TASK-IF008 details
3. **Design Document:** `docs/initial-fixes/DESIGN.md` - Technical specifications
4. **Previous Review:** N/A (Initial batch for fixes)
5. **Architectural Principles:** Ensure you review `docs/design/ios-ig-simhost-initial-fixes.md` for broader context.

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost`, `Bagira.IG`, `Bagira.IOS`
- **Test Project:** Existing test projects corresponding to the modified components. Check `tests/` directories.

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/FIXES-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/FIXES-BATCH-01-QUESTIONS.md`

---

## Context

The SimHost, IG, and IOS applications have some critical deviations from the FDP engine's golden examples on network ownership, topic publication, and doctrine processing. Additionally, the IG and IOS applications have uncommented or un-hooked ImGui UI panels preventing startup visibility. This batch addresses these issues to make the system fully compliant and usable.

**Related Tasks:**
- [TASK-IF001](../../docs/initial-fixes/TASK-DETAIL.md#task-if001-remove-vehiclestate-contamination) - SimHost: Clear DescriptorMapper corruption
- [TASK-IF002](../../docs/initial-fixes/TASK-DETAIL.md#task-if002-fix-doctrine-preemption) - SimHost: Increment doctrine InstanceId
- [TASK-IF003](../../docs/initial-fixes/TASK-DETAIL.md#task-if003-publish-entitymaster-dds-topic) - SimHost: Publish EntityMaster DDS Topic
- [TASK-IF004](../../docs/initial-fixes/TASK-DETAIL.md#task-if004-fix-ghost-ownership-in-entitymastertranslator) - IG: Fix ghost ownership mapping
- [TASK-IF005](../../docs/initial-fixes/TASK-DETAIL.md#task-if005-register-transformsyncsystem) - IG: Interpolate remote entities
- [TASK-IF006](../../docs/initial-fixes/TASK-DETAIL.md#task-if006-fix-rogue-local-spawning-in-creationtool) - IG: Publish spawning over DDS
- [TASK-IF007](../../docs/initial-fixes/TASK-DETAIL.md#task-if007-uncomment-ios-draw-methods) - IOS: Uncomment UI panels
- [TASK-IF008](../../docs/initial-fixes/TASK-DETAIL.md#task-if008-connect-ig-ui-panels-to-app-loop) - IG: Hook panels into app loop

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## 🎯 Batch Objectives
Make SimHost an authoritative node and IG a compliant ghost node by addressing mapping bugs and DDS publication. Additionally, restore the functionality of the ImGui debug panels in IG and IOS to aid further development.

---

## ✅ Tasks

### Task 1: Remove VehicleState Contamination (TASK-IF001)

**File:** `Bagira.SimHost/Util/DescriptorMapper.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF001](../../docs/initial-fixes/TASK-DETAIL.md#task-if001-remove-vehiclestate-contamination)

**Description:** Delete the unconditional addition of `VehicleState` to all entities with a `GeoSpatial` descriptor.
**Requirements:**
- Do not introduce conditionals.
- Ensure only the TKB template dictates `VehicleState` addition.

**Design Reference:** [DESIGN.md § 1.1](../../docs/initial-fixes/DESIGN.md#11-remove-vehiclestate-contamination)

**Tests Required:**
- ✅ Verify a non-vehicle descriptor results in no `VehicleState` component.
- ✅ Ensure existing tests for `DescriptorMapper` pass.

### Task 2: Fix Doctrine Preemption (TASK-IF002)

**File:** `Bagira.SimHost/Systems/MissionAdapterSystem.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF002](../../docs/initial-fixes/TASK-DETAIL.md#task-if002-fix-doctrine-preemption)

**Description:** Add an `unchecked` `InstanceId` increment when doctrine changes to trigger channel preemption.
**Requirements:**
- Must occur inside the hash change branch before `World.SetComponent`.

**Design Reference:** [DESIGN.md § 1.2](../../docs/initial-fixes/DESIGN.md#12-fix-doctrine-preemption)

**Tests Required:**
- ✅ Verify `DoctrineState.InstanceId` increments upon doctrine change.
- ✅ Verify standard byte wrapping (e.g. 255 -> 0) does not throw exceptions.

### Task 3: Publish EntityMaster DDS Topic (TASK-IF003)

**File:** `Bagira.SimHost/Program.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF003](../../docs/initial-fixes/TASK-DETAIL.md#task-if003-publish-entitymaster-dds-topic)

**Description:** Manually construct and register an `AutoCycloneTranslator<EntityMaster>` in SimHost.
**Requirements:**
- Use the shared `ddsParticipant` and `entityMap` appropriately.

**Design Reference:** [DESIGN.md § 1.3](../../docs/initial-fixes/DESIGN.md#13-publish-entitymaster-dds-topic)

**Tests Required:**
- ✅ Program boots without exceptions and registration succeeds.
- ✅ (Integration) Network entity creation events get properly routed to ghost readers.

### Task 4: Fix Ghost Ownership in EntityMasterTranslator (TASK-IF004)

**File:** `Bagira.IG/Translators/EntityMasterTranslator.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF004](../../docs/initial-fixes/TASK-DETAIL.md#task-if004-fix-ghost-ownership-in-entitymastertranslator)

**Description:** Set the `OwnerNodeId` to `0` instead of a local node ID to prevent ghost node ownership theft.
**Requirements:** 
- Applies during replicated entity creation.

**Design Reference:** [DESIGN.md § 2.1](../../docs/initial-fixes/DESIGN.md#21-fix-ghost-ownership-theft)

**Tests Required:**
- ✅ Ensure network translated entities have `HasAuthority = false`.

### Task 5: Register TransformSyncSystem (TASK-IF005)

**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF005](../../docs/initial-fixes/TASK-DETAIL.md#task-if005-register-transformsyncsystem)

**Description:** Register `TransformSyncSystem` driven by the network as a global system.
**Requirements:** 
- Supply `driveFromNetwork: true`.

**Design Reference:** [DESIGN.md § 2.2](../../docs/initial-fixes/DESIGN.md#22-register-transformsyncsystem)

**Tests Required:**
- ✅ Validate the system is correctly bound globally.

### Task 6: Fix Rogue Local Spawning in CreationTool (TASK-IF006)

**File:** `Bagira.IG/Tools/CreationTool.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF006](../../docs/initial-fixes/TASK-DETAIL.md#task-if006-fix-rogue-local-spawning-in-creationtool)

**Description:** Use DDS routing (`IDdsWriter<CreateEntityRequest>`) rather than local bus injection for newly spawned entities.
**Requirements:**
- Payload must be fully constructed including `dtEntityMaster` and `dtGeoSpatial` structures. 
- Map clicking calculates `GeoPosition` fields matching the FDP coordinates.

**Design Reference:** [DESIGN.md § 2.3](../../docs/initial-fixes/DESIGN.md#23-fix-rogue-local-spawning)

**Tests Required:**
- ✅ Spawning sends a proper DDS payload.
- ✅ `FdpEventBus` is NOT called.

### Task 7: Uncomment IOS Draw Methods (TASK-IF007)

**Files:** `Bagira.IOS/IosMock.cs` and all Panels in `Bagira.IOS/Panels/` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF007](../../docs/initial-fixes/TASK-DETAIL.md#task-if007-uncomment-ios-draw-methods)

**Description:** Uncomment ImGui panel implementations and wire `using ImGuiNET;` where necessary.
**Requirements:**
- No functional logic modifications. Merely code activation.

**Design Reference:** [DESIGN.md § 3.1](../../docs/initial-fixes/DESIGN.md#31-uncomment-ios-draw-methods)

**Tests Required:**
- ✅ Solution compiles cleanly.

### Task 8: Connect IG UI Panels to App Loop (TASK-IF008)

**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md - TASK-IF008](../../docs/initial-fixes/TASK-DETAIL.md#task-if008-connect-ig-ui-panels-to-app-loop)

**Description:** Connect panels (`IgDebugPanel`, `EntityInspectorPanel`, `MiniIosPanel`, `PerformanceOverlay`) inside the standard render loop.
**Requirements:**
- Manage proper state updating routines between ECS/engine tick updates and `rlImGui` drawing blocks.
- Setup mouse capture logic via `ImGui.GetIO().WantCaptureMouse`

**Design Reference:** [DESIGN.md § 3.2](../../docs/initial-fixes/DESIGN.md#32-connect-ig-ui-panels-to-app-loop)

**Tests Required:**
- ✅ Panels appear immediately on startup.
- ✅ Mouse interactions over ImGui do not execute 3D camera sweeps/action triggers.

---

## 🧪 Testing Requirements
- Focus on verifying ACTUAL runtime logic as per the DEV-LEAD-GUIDE.md constraints. Don't test string outputs!
- For unit tasks that modify the global state/systems layout (like TASK-IF005 and TASK-IF008), favor structural tests checking that dependencies are correctly mapped or the systems run properly.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed in the IG Application loop when hooking up the UI?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-IF001 completed
- [ ] TASK-IF002 completed
- [ ] TASK-IF003 completed
- [ ] TASK-IF004 completed
- [ ] TASK-IF005 completed
- [ ] TASK-IF006 completed
- [ ] TASK-IF007 completed
- [ ] TASK-IF008 completed
- [ ] All tests passing
- [ ] Correctness criteria covered
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid
- **Skipping Test Assertions:** Merely making sure the tool/compiler doesn't fail isn't sufficient for tests. Assert memory offsets, explicit system array inclusions, or data bindings if possible.
- **Rogue Spawning Tool refactor:** Injecting a dependency incorrectly or using service locators instead of the proper constructor injection limits the maintainability of `CreationTool`. Ensure proper dependency resolution.

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAIL.md](../../docs/initial-fixes/TASK-DETAIL.md)
- **Design:** [DESIGN.md](../../docs/initial-fixes/DESIGN.md)
- **TKB Spec details:** Check existing models if required regarding IF001 or IF006.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value" or Assert.Contains
- **REQUIRED:** Tests that verify actual behavior, memory modifications, state transitions, and edge cases. Make sure to check offsets or value correctness where needed.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them
- **REQUIRED:** Document design decisions YOU made beyond the spec
- **REQUIRED:** Share insights on code quality and improvement opportunities
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation
