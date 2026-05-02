# BUG2-BATCH-01: Network Correctness, Mission System & IOS UI Clean-up

**Batch Number:** BUG2-BATCH-01  
**Tasks:** BUG2-N001, BUG2-N002, BUG2-N003, BUG2-M001, BUG2-M002, BUG2-M003, BUG2-M004, BUG2-U001, BUG2-U002  
**Phase:** Phase 1, Phase 2, Phase 3  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the BUG2 workstream! This first batch focuses on addressing critical network issues (duplicate ACKs, missing sender tracking, descriptor leaks), fixing the mission system so vehicles can move properly and triggers can be edited, and performing UI cleanup on the IOS.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Onboarding Guide:** `docs/bugs-2/ONBOARDING.md` - Workstream specific onboarding
3. **Task Tracker:** `docs/bugs-2/BUG2-TASK-TRACKER.md` - Progress checklist
4. **Task Definitions:** `docs/bugs-2/BUG2-TASK-DETAIL.md` - Specific implementation details and success conditions
5. **Design Document:** `docs/bugs-2/BUG2-DESIGN.md` - Architectural context and reasoning

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`, `Hrot.IG/`, `Hrot.ExCon/`, `Hrot.Map.Common/`, `FDP/Examples/Fdp.Examples.NetworkDemo/`
- **Test Projects:** `Hrot.SimHost.Tests/`, `Hrot.Map.Common.Tests/`, `Hrot.ExCon.Tests/`, `Hrot.IG.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BUG2-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BUG2-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task X:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

This batch bundles 9 tasks from phases 1, 2, and 3 of the BUG2 work tracking. The tasks cover network correctness fixes, essential bug fixes in the mission trigger resolution and task UI to make mission tasking actually work in the operator UI, and removal of visual debt in the IOS UI.

**Related Tasks:**
- [BUG2-N001](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-n001-fix-duplicate-updateentitydescriptorrequestsystem-registration) - Duplicate System Registration
- [BUG2-N002](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-n002-add-enablesendertracking-to-all-dds-participant-initializations) - Add EnableSenderTracking
- [BUG2-N003](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-n003-fix-geospatialdr-descriptor-disposal-leak) - Fix WorldPos Leak
- [BUG2-M001](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m001-fix-missing-resolvetrigger-cases) - Fix Missing ResolveTrigger Cases
- [BUG2-M002](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m002-add-trigger-selection-ui-to-missionpanel) - Add Trigger Selection UI
- [BUG2-M003](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m003-fix-unreadable-mission-task-action-buttons) - Fix Unreadable Mission Task Buttons
- [BUG2-M004](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m004-add-inline-version-conflict-resolution-to-missionpanel) - Add Inline Version-Conflict Resolution
- [BUG2-U001](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-u001-remove-legacy-tool-combo-from-configpanel) - Remove Legacy Tool Combo
- [BUG2-U002](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-u002-fix-orbat-tree-indentation) - Fix ORBAT Tree Indentation

---

## 🎯 Batch Objectives
- Ensure network stability by fixing duplicate messages and missing sender tracking.
- Enable full functional control of mission tasks, editing, and conflict resolution from the IOS.
- Ensure proper visual structure in IOS ORBAT and Config panels.

---

## ✅ Tasks

### Task 1: Fix Duplicate UpdateEntityDescriptorRequestSystem Registration (BUG2-N001)

**File:** `Hrot.SimHost/SimHostApp.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-n001-fix-duplicate-updateentitydescriptorrequestsystem-registration)

**Description:** Remove the duplicate registration of `UpdateEntityDescriptorRequestSystem` to prevent double ACKs.
**Requirements:** See task definition for explicit lines to delete.

**Tests Required:**
- ✅ `SimHostAppTests.RegisteredSystemTypes_ContainsNoDuplicates` (new)
- ✅ Integration confirmation of exactly one ACK per request.

### Task 2: Add EnableSenderTracking to All DDS Participant Initializations (BUG2-N002)

**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.IG/IgApplication.cs`, `Hrot.ClusterRunner/Services/IosSubsystem.cs`, `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-n002-add-enablesendertracking-to-all-dds-participant-initializations)

**Description:** Add `EnableSenderTracking` to the participant at initialization.
**Requirements:** Use the appropriate `AppDomainId` and `AppInstanceId` values per context.

**Tests Required:**
- ✅ `EntityMasterIngressTranslatorTests.ProcessSample_WithSenderTracking_SetsOwnerId` (new or updated)

### Task 3: Fix WorldPos Descriptor Disposal Leak (BUG2-N003)

**File:** `Hrot.Map.Common/Replication/Egress/WorldPosEgressTranslator.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-n003-fix-geospatialdr-descriptor-disposal-leak)

**Description:** Override `Dispose(long networkEntityId)` to tombstone both the primary sample and `_drWriter`.

**Tests Required:**
- ✅ `WorldPosEgressTranslatorTests.Dispose_CallsDisposeOnDrWriter` (new)
- ✅ `WorldPosEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` (new)

### Task 4: Fix Missing ResolveTrigger Cases (BUG2-M001)

**Files:** `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`, `Hrot.Map.Common/Translators/EntityMissionIngressTranslator.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m001-fix-missing-resolvetrigger-cases)

**Description:** Add `"BehaviorFinished"` and `"UnderAttack"` cases to the switch in `ResolveTrigger`.

**Tests Required:**
- ✅ `EntityMissionIngressTranslatorTests.ResolveTrigger_BehaviorFinished_ReturnsCorrectEnum` (new)
- ✅ `EntityMissionIngressTranslatorTests.ResolveTrigger_UnderAttack_ReturnsCorrectEnum` (new)
- ✅ `MissionControlRequestSystemTests.ResolveTrigger_BehaviorFinished_ReturnsCorrectEnum` (new)
- ✅ `MissionControlRequestSystemTests.ResolveTrigger_UnderAttack_ReturnsCorrectEnum` (new)

### Task 5: Add Trigger Selection UI to MissionPanel (BUG2-M002)

**File:** `Hrot.ExCon/Panels/MissionPanel.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m002-add-trigger-selection-ui-to-missionpanel)

**Description:** Add comprehensive task trigger selection options directly in the Mission task list UI loop.

**Tests Required:**
- ✅ `MissionPanelTests.HandleEditTriggerType_UpdatesTriggerInDraft` (new)
- ✅ `MissionPanelTests.HandleEditTriggerParams_UpdatesParamsInDraft` (new)
- ✅ `MissionPanelTests.HandleAddTrigger_AddsBehaviorFinishedTrigger` (new)
- ✅ Parameterized test verifying `GetDefaultTriggerParams` behavior.

### Task 6: Fix Unreadable Mission Task Action Buttons (BUG2-M003)

**File:** `Hrot.ExCon/Panels/MissionPanel.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m003-fix-unreadable-mission-task-action-buttons)

**Description:** Replace Unicode text on buttons with the ASCII equivalents: `Up`, `Down`, `Delete`.

**Tests Required:**
- ✅ Manual check. Confirm no non-ASCII characters remain in button label strings.

### Task 7: Add Inline Version-Conflict Resolution to MissionPanel (BUG2-M004)

**File:** `Hrot.ExCon/Panels/MissionPanel.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-m004-add-inline-version-conflict-resolution-to-missionpanel)

**Description:** Add Force Commit handling to bypass OCC checks and conditionally show conflict buttons.

**Tests Required:**
- ✅ `MissionPanelTests.HandleForceCommit_SendsWithBaseVersionZero` (new)
- ✅ `MissionPanelTests.ConflictState_ShowsConflictButtonsNotCommit` (new)
- ✅ `MissionPanelTests.DiscardDraft_ClearsConflictAndDraft` (new)

### Task 8: Remove Legacy Tool Combo from ConfigPanel (BUG2-U001)

**File:** `Hrot.ExCon/Panels/ConfigPanel.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-u001-remove-legacy-tool-combo-from-configpanel)

**Description:** Clean up `ConfigPanel` by deleting legacy tool items and ensuring clean patch outputs.

**Tests Required:**
- ✅ `ConfigPanelTests.BuildPatch_DoesNotContainInteractionKey` (new)
- ✅ `ConfigPanelTests.NoToolsField` (new)

### Task 9: Fix ORBAT Tree Indentation (BUG2-U002)

**File:** `Hrot.ExCon/Panels/OrbatPanel.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-u002-fix-orbat-tree-indentation)

**Description:** Inject programmatic `Indent` and `Unindent` blocks inside the node-rendering process.

**Tests Required:**
- ✅ `OrbatPanelTests.GetVisibleNodes_SubordinateHasGreaterDepth` (verify or new)
- ✅ Manual verification on running IG standalone.

---

## 🧪 Testing Requirements
- **Always verify correctness:** Evaluate whether the test properly captures behavioral goals vs just existing functionality checks.
- Build must run zero compile errors and zero warnings. Ensure `dotnet test IOS-IG-SimHost.sln` completes fully.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights and experience:

**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** Did you spot any weak points in the existing codebase? What would you improve?
**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
**Q4:** What edge cases did you discover that weren't mentioned in the spec?
**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BUG2-N001 completed
- [ ] BUG2-N002 completed
- [ ] BUG2-N003 completed
- [ ] BUG2-M001 completed
- [ ] BUG2-M002 completed
- [ ] BUG2-M003 completed
- [ ] BUG2-M004 completed
- [ ] BUG2-U001 completed
- [ ] BUG2-U002 completed
- [ ] All tests passing
- [ ] Report submitted answering the 5 questions

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value"
- **REQUIRED:** Tests that verify actual behavior and edge cases

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them
- **REQUIRED:** Document design decisions YOU made beyond the spec
- **REQUIRED:** Share insights on code quality and improvement opportunities
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation

---

## 📚 Reference Materials
- **Task Defs:** [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md)
- **Design:** [BUG2-DESIGN.md](../../docs/bugs-2/BUG2-DESIGN.md)
