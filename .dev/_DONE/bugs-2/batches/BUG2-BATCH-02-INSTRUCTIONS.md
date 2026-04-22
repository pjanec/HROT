# BUG2-BATCH-02: Tech Debt, Interactions, Cursors & Network Operations

**Batch Number:** BUG2-BATCH-02  
**Tasks:** BUG2-DEBT-01, BUG2-I001, BUG2-V001, BUG2-T001, BUG2-T002, BUG2-E001, BUG2-E002, BUG2-R001, BUG2-A001  
**Phase:** Tech Debt, Phase 4, Phase 5, Phase 6, Phase 7, Phase 8, Phase 9  
**Estimated Effort:** 11-13 hours  
**Priority:** HIGH  
**Dependencies:** BUG2-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the second batch of the BUG2 workstream! This batch contains the remaining high-priority fixes including immediate technical debt reduction from the last batch, IG interaction updates, visual bug fixes for tool cursors and entity rendering, networking updates for correct contextual deletion, and a clean-out of architecture debt regarding the `HealthData` proxy component.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Onboarding Guide:** `docs/bugs-2/ONBOARDING.md` - Workstream specific onboarding
3. **Task Tracker:** `docs/bugs-2/BUG2-TASK-TRACKER.md` - Progress checklist
4. **Task Definitions:** `docs/bugs-2/BUG2-TASK-DETAIL.md` - Specific implementation details and success conditions
5. **Design Document:** `docs/bugs-2/BUG2-DESIGN.md` - Architectural context and reasoning

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`, `FDP/Toolkits/FDP.Toolkit.Vis2D/`, `Hrot.SimHost/`, `Hrot.Map.Common/`, `Fdp.Kernel/`, `FDP/Toolkits/FDP.Toolkit.Combat/`, `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/`, `FDP/Toolkits/FDP.Toolkit.Behavior/`
- **Test Projects:** `Hrot.IG.Tests/`, `FDP.Toolkit.Vis2D.Tests/`, `Hrot.SimHost.Tests/`, `Hrot.Map.Common.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BUG2-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BUG2-BATCH-02-QUESTIONS.md`

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

This batch completes the BUG2 work tracking and includes an immediate technical debt resolution from BUG2-BATCH-01.

**Related Tasks:**
- **BUG2-DEBT-01** (Tech Debt) - Consolidate duplicate `ResolveTrigger` methods
- **BUG2-I001** (Phase 4) - Add Shift-Key Immediate Drag Mode ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-i001-add-shift-key-immediate-drag-mode))
- **BUG2-V001** (Phase 5) - Enforce Layer Visibility in Selection and Rendering ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-v001-enforce-layer-visibility-in-selection-and-rendering))
- **BUG2-T001** (Phase 6) - Add Crosshair Cursor to MeasureTool ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-t001-add-crosshair-cursor-to-measuretool))
- **BUG2-T002** (Phase 6) - Add Crosshair Cursor to EntityPickerTool ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-t002-add-crosshair-cursor-to-entitypickertool))
- **BUG2-E001** (Phase 7) - Add Delete to Inspector Context Menus ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-e001-add-delete-to-inspector-context-menus))
- **BUG2-E002** (Phase 7) - Wire IOS DELETE Context Action to IG-Side ELM Deletion ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-e002-wire-ios-delete-context-action-to-ig-side-elm-deletion))
- **BUG2-R001** (Phase 8) - Fix SimHost Road Graph Rendering ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-r001-fix-simhost-road-graph-rendering))
- **BUG2-A001** (Phase 9) - Consolidate Health into FDP.Toolkit.Combat.Contracts ([details](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-a001-consolidate-health-into-fdptoolkitcombatcontracts))

---

## 🎯 Batch Objectives
- Remove technical and architectural debt for `ResolveTrigger` and `HealthData`.
- Correct rendering paths where crosshairs were missing or layer visibility was ignored.
- Enable deletion of networked entities correctly using the Entity Lifecycle Module via IG and IOS interactions.
- Provide real-time location streaming over DDS when dragging objects with Shift pressed.
- Fix hardcoded errors preventing map road networks from loading.

---

## ✅ Tasks

### Task 0: Consolidate ResolveTrigger helper (BUG2-DEBT-01)

**Files:** `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`, `Hrot.Map.Common/Translators/EntityMissionIngressTranslator.cs`, `Hrot.Map.Common/Helpers/MissionTriggerHelper.cs` (new)
**Task Definition:** Tech Debt from BUG2-BATCH-01.

**Description:** The duplicate-copy of `ResolveTrigger` logic between `MissionControlRequestSystem` and `EntityMissionIngressTranslator` is a tech-debt item. Consolidate into a shared static helper (`MissionTriggerHelper`) within `Hrot.Map.Common`.

**Tests Required:**
- ✅ Verify existing tests for `ResolveTrigger` in both `Hrot.SimHost.Tests` and `Hrot.Map.Common.Tests` continue to pass correctly by migrating them to test the new utility class.

### Task 1: Add Shift-Key Immediate Drag Mode (BUG2-I001)

**File:** `Hrot.IG/IgApplication.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-i001-add-shift-key-immediate-drag-mode)

**Description:** Unthrottle location updates in the map canvas when the Shift key is held down.

**Tests Required:**
- ✅ `ContinuousDragTests.OnEntityMoved_ShiftHeld_PositionChanged_SendsUpdate` (new)
- ✅ `ContinuousDragTests.OnEntityMoved_ShiftHeld_SamePosition_DoesNotSend` (new)
- ✅ `ContinuousDragTests.OnEntityMoved_ShiftNotHeld_ContinuousDragDisabled_DoesNotSend` (new)

### Task 2: Enforce Layer Visibility in Selection and Rendering (BUG2-V001)

**Files:** `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/BoxSelectionTool.cs`, `Hrot.IG/Systems/SelectionRenderSystem.cs`, `FDP/Toolkits/FDP.Toolkit.Vis2D/Layers/EntityRenderLayer.cs`, `Hrot.IG/IgApplication.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-v001-enforce-layer-visibility-in-selection-and-rendering)

**Description:** Enforce proper masking logic so hidden layers aren't interactable or rendering selection rings.

**Tests Required:**
- ✅ `BoxSelectionToolTests.FinishSelection_HiddenLayerEntities_NotIncluded` (new)
- ✅ `BoxSelectionToolTests.FinishSelection_VisibleLayerEntities_Included` (new)
- ✅ `SelectionRenderSystemTests.Draw_HiddenLayerEntity_DoesNotRenderRing` (new)
- ✅ `EntityRenderLayerTests.Draw_CatchAllMode_HiddenEntities_Skipped` (new)

### Task 3: Add Crosshair Cursor to MeasureTool (BUG2-T001)

**File:** `Hrot.IG/Tools/MeasureTool.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-t001-add-crosshair-cursor-to-measuretool)

**Description:** Provide visual feedback when the operator equips the measure tool prior to clicking on a start point.

**Tests Required:**
- ✅ `MeasureToolTests.Draw_NoStartPoint_DoesNotThrow`
- ✅ `MeasureToolTests.Draw_NoStartPoint_DrawsCrosshair` (new)

### Task 4: Add Crosshair Cursor to EntityPickerTool (BUG2-T002)

**File:** `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/EntityPickerTool.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-t002-add-crosshair-cursor-to-entitypickertool)

**Description:** Inform the operator that the current tool needs action using a crosshair that detects hoverability.

**Tests Required:**
- ✅ `EntityPickerToolTests.Draw_NoHoveredEntity_DrawsAmberCrosshair` (new)
- ✅ `EntityPickerToolTests.Draw_HoveredEntity_DrawsRedCrosshair` (new)

### Task 5: Add Delete to Inspector Context Menus (BUG2-E001)

**Files:** `Hrot.SimHost/SimHostVisualization.cs`, `Hrot.IG/IgApplication.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-e001-add-delete-to-inspector-context-menus)

**Description:** Properly network UI deletion calls across SimHost and IG inspectors via ELM messaging (`DestroyEntityCommand`), or manually clear if local-only.

**Tests Required:**
- ✅ `EntityInspectorContextMenuTests.DeleteNetworkedEntity_PublishesDestroyEntityCommand` (new)
- ✅ `EntityInspectorContextMenuTests.DeleteLocalEntity_CallsDestroyEntity` (new)
- ✅ `EntityInspectorContextMenuTests.DeleteSelectedEntity_ClearsSelection` (new)

### Task 6: Wire IOS DELETE Context Action to IG-Side ELM Deletion (BUG2-E002)

**Files:** `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs`, `Hrot.IG/IgApplication.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-e002-wire-ios-delete-context-action-to-ig-side-elm-deletion)

**Description:** Map numeric IOS action ID `10` -> `IG_DeleteEntity` and invoke the appropriate networked destruct system locally.

**Tests Required:**
- ✅ `ContextActionsUpdateTranslatorTests.ParseActions_Id10_ReturnsIgDeleteEntity` (new)
- ✅ `IgApplicationTests.ExecuteLocalContextAction_IgDeleteEntity_PublishesDestroyCommand` (new)

### Task 7: Fix SimHost Road Graph Rendering (BUG2-R001)

**Files:** `Hrot.SimHost/Modules/SimulationLogicModule.cs`, `Hrot.SimHost/SimHostApp.cs`  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-r001-fix-simhost-road-graph-rendering)

**Description:** Correctly implement node-configurable file path references and fix auto-property errors with `RoadNetworkBlob`.

**Tests Required:**
- ✅ `SimulationLogicModuleTests.Constructor_WithRoadNetwork_SetsProperty` (new)
- ✅ `SimulationLogicModuleTests.RoadNetwork_Default_ReturnsDefaultNotAlwaysDefault` (new)
- ✅ `SimHostAppTests.LoadRoadNetwork_ValidPath_AssignsNetworkToModule` (new)
- ✅ `SimHostAppTests.LoadRoadNetwork_InvalidPath_LogsWarnDoesNotThrow` (new)

### Task 8: Consolidate Health into FDP.Toolkit.Combat.Contracts (BUG2-A001)

**Files:** See task definition specific file list.  
**Task Definition:** See [BUG2-TASK-DETAIL.md](../../docs/bugs-2/BUG2-TASK-DETAIL.md#bug2-a001-consolidate-health-into-fdptoolkitcombatcontracts)

**Description:** Completely eradicate redundant `HealthData` proxy components used structurally, transitioning reads and writes natively through `Health.cs` in the `Contracts` project.

**Tests Required:**
- ✅ `DamageSystemTests.ProcessHit_DoesNotSetHealthDataComponent` (new guard test)
- ✅ `MissionDirectorSystemTests.EvaluateTrigger_HealthCritical_ReadFromHealthComponent` (new)
- ✅ Ensure zero build errors occur solution wide.

---

## 🧪 Testing Requirements
- Unit tests MUST actually invoke behavioral conditions and avoid simple `Assert.Contains()` object-checking. Read test guidelines closely.
- Verify tests in Phase 9 against system build dependencies to ensure solution links are healthy.
- Complete execution of `dotnet test IOS-IG-SimHost.sln` is mandatory.

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
- [ ] BUG2-DEBT-01 completed
- [ ] BUG2-I001 completed
- [ ] BUG2-V001 completed
- [ ] BUG2-T001 completed
- [ ] BUG2-T002 completed
- [ ] BUG2-E001 completed
- [ ] BUG2-E002 completed
- [ ] BUG2-R001 completed
- [ ] BUG2-A001 completed
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
