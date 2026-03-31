# INTS-BATCH-01: Integration Bug Fixes

**Batch Number:** INTS-BATCH-01  
**Tasks:** INTS-P1-001, INTS-P1-002, INTS-P1-003, INTS-P1-004, INTS-P1-005  
**Phase:** Phase 1 - Integration Bug Fixes  
**Estimated Effort:** 8-10 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch focuses on fixing critical integration bugs across SimHost, IG, and IOS to achieve basic end-to-end operation. 

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs\design\TASK-DETAILS-Integration-Troubleshooting.md` - See detailed task specifications
3. **Design Document:** `docs\design\DESIGN-Integration-Troubleshooting.md` - Architectural overview
4. **Developer Guidance (Project Rules):** `.dev-workstream/guides/CODE-STANDARDS.md`

### Source Code Location
- **Primary Work Areas:** 
  - `Hrot.SimHost`
  - `Hrot.IG`
  - `Hrot.ExCon`
  - `Hrot.ClusterRunner`
  - `Hrot.Map.Common`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/INTS-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/INTS-BATCH-01-QUESTIONS.md`

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

## Context

We are addressing root causes preventing basic end-to-end operation across SimHost, IG, and IOS. This involves TKB registration, network publishing of spawned entities, replacing null DDS writers, fixing ImGui viewport blocking, and wiring IG map events.

**Related Tasks:**
- [INTS-P1-001](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-001--register-tkb-catalog-in-simhost-and-ig) - Register TKB Catalog
- [INTS-P1-002](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-002--fix-simhost-vehicle-spawning-to-use-spawnentitycommand) - Fix SimHost Spawning
- [INTS-P1-003](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-003--replace-nullddswriter-with-ddswriteradapter-in-ios) - Replace NullDdsWriter
- [INTS-P1-004](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-004--add-passthrucentralnode-to-imgui-dockspace) - Fix ImGui Map Input
- [INTS-P1-005](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-005--wire-ig-to-ios-map-event-translators) - Wire Map Event Translators

---

## 🎯 Batch Objectives
To successfully register TKB blueprints, allow cross-subsystem entity spawning over DDS, ensure UI input reaches the map while retaining panel interactivity, and lay the foundation for Phase 2.

---

## ✅ Tasks

### Task 1: Register TKB Catalog in SimHost and IG (INTS-P1-001)
**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.IG/IgApplication.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-001--register-tkb-catalog-in-simhost-and-ig)

**Requirements & Custom Overrides:**
- Follow the requirements in the Task Definition document exactly.
- **Do not** refactor into `HrotEnvironment` yet (that is for Phase 2).

### Task 2: Fix SimHost Vehicle Spawning to Use SpawnEntityCommand (INTS-P1-002)
**Files:** `Hrot.SimHost/UI/SimHostScenarioManager.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-002--fix-simhost-vehicle-spawning-to-use-spawnentitycommand)

**Requirements & Custom Overrides:**
- Replace the direct entity creation with `SpawnEntityCommand`.
- Conform strictly to the entity configurations mapping outlined in the task details.

### Task 3: Replace NullDdsWriter with DdsWriterAdapter in IOS (INTS-P1-003)
**Files:** `Hrot.Map.Common/Dds/DdsWriterAdapter.cs` (New), `Hrot.ExCon/Program.cs`, `Hrot.ClusterRunner/Services/IosSubsystem.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-003--replace-nullddswriter-with-ddswriteradapter-in-ios)

### Task 4: Add PassthruCentralNode to ImGui DockSpace (INTS-P1-004)
**Files:** `Hrot.ExCon/IosMock.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-004--add-passthrucentralnode-to-imgui-dockspace)

### Task 5: Wire IG-to-IOS Map Event Translators (INTS-P1-005)
**Files:** `Hrot.IG/IgApplication.cs` and related translator files.
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-005--wire-ig-to-ios-map-event-translators)

---

## 🧪 Testing and Technical Requirements

- **Testing Framework Override:** All new unit tests MUST be xUnit (no NUnit or MSTest, overriding standard docs or existing practices).
- **Logging Rule Override:** All debug prints, logging, or tracing MUST use the existing `FdpLog` from the FDP kernel instead of `Console.WriteLine` or `ILogger`. 
- **Quality over Quantity:** Test count does not matter as much as test quality. Tests must verify the required behaviors as outlined in each task's success conditions within the TASK-DETAILS doc. **We will rigorously check that your tests verify actual correctness, behavior, values, and offsets rather than simple compilation or string matching.**

---

## 📊 Report Requirements

The `.dev-workstream/reports/INTS-BATCH-01-REPORT.md` must gather valuable professional feedback.

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] INTS-P1-001 completed (SimHost and IG TKBs resolve)
- [ ] INTS-P1-002 completed (SimHost uses SpawnEntityCommand with proper Identity)
- [ ] INTS-P1-003 completed (IOS uses functional DdsWriterAdapter)
- [ ] INTS-P1-004 completed (Map receives panning input within Runner/IG)
- [ ] INTS-P1-005 completed (IOS/IG talk correctly via DDS events)
- [ ] ALL tests pass
- [ ] Report submitted answering the 5 questions

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md)
- **Design:** [DESIGN-Integration-Troubleshooting.md](../../docs/design/DESIGN-Integration-Troubleshooting.md)
